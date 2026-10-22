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
    // see a bearer token or a password was there.
    private static readonly Regex[] SecretPatterns =
    [
        // Authorization header values
        new(@"\b(?:Bearer|Basic)\s+(?<secret>[A-Za-z0-9\-._~+/]{8,}=*)", Options),
        // JWTs: a base64url JSON header, a payload and an optional signature
        new(@"(?<secret>\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*)", Options),
        // key=value in connection strings and query strings, up to the separator
        new(@"\b(?:password|pwd|secret|client_?secret|token|access_?token|api_?key|access_?key|account_?key|sig)\s*=\s*(?<secret>[^;&\s""']+)",
            Options | RegexOptions.IgnoreCase),
        // Provider keys recognisable by their prefix
        new(@"(?<secret>\b(?:sk-[A-Za-z0-9_\-]{16,}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|xox[abprs]-[A-Za-z0-9\-]{10,}|AIza[0-9A-Za-z_\-]{35}))",
            Options),
        // PEM private keys. The body is base64, which holds no dash.
        new(@"(?<secret>-----BEGIN [A-Z ]*PRIVATE KEY-----[^\-]*-----END [A-Z ]*PRIVATE KEY-----)", Options),
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
    public string Text(string text)
    {
        if (!_enabled || string.IsNullOrEmpty(text))
            return text;

        foreach (var pattern in SecretPatterns)
        {
            text = pattern.Replace(text, match =>
            {
                var secret = match.Groups["secret"];
                Count++;

                return string.Concat(
                    match.Value.AsSpan(0, secret.Index - match.Index),
                    Mask(secret.Length),
                    match.Value.AsSpan(secret.Index - match.Index + secret.Length));
            });
        }

        return text;
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
