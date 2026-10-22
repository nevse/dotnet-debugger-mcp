using System.Text.RegularExpressions;

namespace DotnetDebugger.Mcp.Debugging;

/// <summary>
/// Masks what looks like a secret in values the tools return, before they reach the model. One per
/// tool call: it counts what it masked, so the response can say the agent is not seeing everything.
///
/// Two rules. A string whose variable or member name says it holds a secret - password, token,
/// connection string - is masked whole, whatever its format. Any text, named or not, has the
/// recognisable formats masked where they occur: bearer tokens, JWTs, password= in a connection
/// string, provider key prefixes, PEM private keys. This reduces exposure rather than closing it: a
/// secret in a local called s, or password.Length evaluated, passes. The threat is a secret reaching
/// the model by accident, not an agent set on reading one.
///
/// The patterns run on text the debuggee controls, so they are non-backtracking and cannot be made
/// to take more than linear time.
/// </summary>
public sealed class SecretRedactor
{
    // Matched against the name with its case, underscores and dashes removed, so api_key, apiKey and
    // API-KEY are one entry. A substring match, which is why only strings are masked by name: an int
    // tokenCount or a CancellationToken token is no secret.
    private static readonly string[] SecretNames =
    [
        "password", "passwd", "pwd", "secret", "token", "apikey", "connectionstring", "credential",
        "privatekey", "accesskey", "authorization",
    ];

    private const RegexOptions Options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;

    // Each pattern's group "secret" is what gets masked; anything around it stays, so the reader can
    // see a bearer token or a password was there. A secret shorter than its pattern's minimum is
    // left alone: the length a pattern can demand is limited without lookaround.
    private static readonly (Regex Pattern, int MinLength)[] SecretPatterns =
    [
        // Authorization header values. The credential has to hold a digit or a token character, so
        // "Basic authentication failed" is the English it looks like.
        (new(@"\b(?:Bearer|Basic)\s+(?<secret>[A-Za-z0-9\-._~+/]*[0-9\-._~+/][A-Za-z0-9\-._~+/]*=*)", Options), 8),
        // JWTs: a base64url JSON header, a payload and an optional signature
        (new(@"(?<secret>\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*)", Options), 1),
        // key=value in connection strings and query strings: a quoted value up to its closing quote,
        // an unquoted one up to the separator
        (new(@"\b(?:password|pwd|secret|client_?secret|token|access_?token|api_?key|access_?key|account_?key|sig)\s*=\s*(?:""(?<secret>[^""]+)""|'(?<secret>[^']+)'|(?<secret>[^;&\s""']+))",
            Options | RegexOptions.IgnoreCase), 1),
        // Provider keys recognisable by their prefix
        (new(@"(?<secret>\b(?:sk-[A-Za-z0-9_\-]{16,}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|xox[abprs]-[A-Za-z0-9\-]{10,}|AIza[0-9A-Za-z_\-]{35}))",
            Options), 1),
        // PEM private keys. The body is base64, which holds no dash.
        (new(@"(?<secret>-----BEGIN [A-Z ]*PRIVATE KEY-----[^\-]*-----END [A-Z ]*PRIVATE KEY-----)", Options), 1),
    ];

    private readonly bool _enabled;

    public SecretRedactor(bool enabled)
    {
        _enabled = enabled;
    }

    /// <summary>
    /// How many secrets this redactor has masked so far
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// A variable's or member's value, as the debugger shows it. A string named like a secret is
    /// masked whole; an object, which has members to expand, is left for those members to be checked
    /// when they are read.
    /// </summary>
    public string Value(string? name, string? type, string value, int variablesReference)
    {
        if (!_enabled)
            return value;

        if (variablesReference == 0 && IsString(type) && IsSecretName(name) && Content(value) is { Length: > 0 } content)
        {
            Count++;
            return Mask(content.Length);
        }

        return Text(value);
    }

    /// <summary>
    /// Free text - program output, an exception's message or trace - with every recognisable secret
    /// in it masked
    /// </summary>
    public string Text(string text) => Redact(text, keepLines: false);

    /// <summary>
    /// Lines of output, masked as one text so a secret printed over several lines - a PEM key - is
    /// still seen whole. Every line keeps its place: a masked span that covered several lines leaves
    /// the mask on its first and the others empty.
    /// </summary>
    public IReadOnlyList<string> Lines(IReadOnlyList<string> lines)
    {
        if (!_enabled || lines.Count == 0)
            return lines;

        var masked = Redact(string.Join('\n', lines), keepLines: true).Split('\n');

        // A line can hold newlines of its own, so put back as many pieces as each one had
        var result = new List<string>(lines.Count);
        var next = 0;

        foreach (var line in lines)
        {
            var pieces = line.Count(c => c == '\n') + 1;
            result.Add(string.Join('\n', masked, next, pieces));
            next += pieces;
        }

        return result;
    }

    /// <summary>
    /// Every pattern runs on the original text and the spans they find are merged before anything is
    /// replaced, so a secret two patterns both recognise - a JWT after access_token= - is masked
    /// once, and no pattern ever reads another's mask.
    /// </summary>
    private string Redact(string text, bool keepLines)
    {
        if (!_enabled || string.IsNullOrEmpty(text))
            return text;

        var spans = new List<(int Start, int End)>();

        foreach (var (pattern, minLength) in SecretPatterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var secret = match.Groups["secret"];

                if (secret.Success && secret.Length >= minLength)
                    spans.Add((secret.Index, secret.Index + secret.Length));
            }
        }

        if (spans.Count == 0)
            return text;

        spans.Sort();

        var result = new System.Text.StringBuilder(text.Length);
        var position = 0;
        var index = 0;

        while (index < spans.Count)
        {
            var (start, end) = spans[index++];

            while (index < spans.Count && spans[index].Start < end)
                end = Math.Max(end, spans[index++].End);

            result.Append(text, position, start - position);
            result.Append(Mask(end - start));

            if (keepLines)
                result.Append('\n', text.AsSpan(start, end - start).Count('\n'));

            Count++;
            position = end;
        }

        return result.Append(text, position, text.Length - position).ToString();
    }

    /// <summary>
    /// The name an expression's result goes by, for the name rule: the last member it reads, so
    /// config.Db.Password is Password. Null for anything else - a call or an indexer names no member
    /// whose name can be trusted - which leaves the result to the patterns alone.
    /// </summary>
    public static string? LastIdentifier(string expression)
    {
        var trimmed = expression.Trim();
        var start = trimmed.Length;

        while (start > 0 && (char.IsLetterOrDigit(trimmed[start - 1]) || trimmed[start - 1] == '_'))
            start--;

        if (start == trimmed.Length || char.IsDigit(trimmed[start]))
            return null;

        // An identifier the whole expression, or the member after a dot; anything else before it is
        // an operator or a call, and the name is not the result's
        return start == 0 || trimmed[start - 1] == '.' ? trimmed[start..] : null;
    }

    private static bool IsString(string? type) =>
        type is "string" or "string?" or "System.String";

    private static bool IsSecretName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        var normalized = string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();

        return SecretNames.Any(normalized.Contains);
    }

    /// <summary>
    /// A string value without the quotes the debugger shows it in. Null for null, which is left as it
    /// is: whether a secret is set at all is what a debugging session most often needs to know.
    /// </summary>
    private static string? Content(string value)
    {
        if (value == "null")
            return null;

        return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    }

    private static string Mask(int length) => $"[redacted: {length} chars]";
}
