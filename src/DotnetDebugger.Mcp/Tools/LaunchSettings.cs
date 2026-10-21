using System.Text;
using System.Text.Json;

namespace DotnetDebugger.Mcp.Tools;

/// <summary>
/// What a launchSettings.json profile asks for, turned into launch_program's own parameters
/// </summary>
public sealed record LaunchProfile(
    string Name,
    IReadOnlyList<string>? Arguments,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>
    /// The launch the caller gets: arguments and working directory given by the caller replace the
    /// profile's, and the caller's variables go on top of the profile's, so one of them can be
    /// changed without restating the rest
    /// </summary>
    public (IReadOnlyList<string>? Arguments, string? WorkingDirectory, Dictionary<string, string> Environment) Apply(
        IReadOnlyList<string>? arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        var merged = new Dictionary<string, string>(Environment);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            merged[name] = value;

        return (arguments ?? Arguments, workingDirectory ?? WorkingDirectory, merged);
    }
}

/// <summary>
/// Reads a project's Properties/launchSettings.json. The debugger could apply a profile itself, but it
/// does so after this server has checked the request and replaces the environment with the
/// profile's, so a repository's profile would get past the refused-variable check. The file comes
/// with the repository and is no more trusted than the agent's own input, so it is read here and its
/// result goes through the same validation as launch_program's parameters.
/// </summary>
public static class LaunchSettings
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Reads the profile to launch with. A name picks that profile; without one the debugger's own
    /// choice is made - the "https" profile, else the first with commandName "Project" - so a profile
    /// means the same thing here as in an editor using the same debugger. Unlike the debugger, a name
    /// that is not in the file fails rather than quietly falling back to another profile. A project
    /// without the file has no profile, which is only a mistake when one was named.
    ///
    /// Values are taken literally: neither $(...) properties nor %VAR% references are expanded,
    /// which the debugger does not do either.
    /// </summary>
    public static LaunchProfile? Read(string projectPath, string? profileName)
    {
        var projectDirectory = ProjectDirectory(projectPath);
        var settingsPath = Path.Combine(projectDirectory, "Properties", "launchSettings.json");

        if (!File.Exists(settingsPath))
        {
            if (string.IsNullOrWhiteSpace(profileName))
                return null;

            throw new ArgumentException(
                $"There is no launch settings file at {settingsPath} to take profile '{profileName}' from",
                nameof(projectPath));
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(settingsPath), DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{settingsPath} is not valid JSON: {ex.Message}", nameof(projectPath));
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("profiles", out var profiles)
                || profiles.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"{settingsPath} has no profiles", nameof(projectPath));

            var (name, profile) = SelectProfile(profiles, profileName, settingsPath);

            return ToLaunchProfile(name, profile, projectDirectory, settingsPath, ProfileNames(profiles));
        }
    }

    /// <summary>
    /// The project's directory, from the project file or the directory itself. A built .dll is not
    /// accepted: walking up from bin/ to find the project breaks on any custom OutputPath.
    /// </summary>
    private static string ProjectDirectory(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("Project path cannot be empty", nameof(projectPath));

        var fullPath = Path.GetFullPath(projectPath);

        if (Directory.Exists(fullPath))
            return fullPath;

        if (File.Exists(fullPath) && Path.GetExtension(fullPath) is ".csproj" or ".fsproj" or ".vbproj")
            return Path.GetDirectoryName(fullPath)!;

        throw new ArgumentException(
            $"{fullPath} is not a project: pass the project file or its directory, the one holding "
            + "Properties/launchSettings.json",
            nameof(projectPath));
    }

    private static (string Name, JsonElement Profile) SelectProfile(
        JsonElement profiles, string? profileName, string settingsPath)
    {
        var all = profiles.EnumerateObject().ToList();

        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var named = all.FirstOrDefault(p => p.Name == profileName);
            if (named.Value.ValueKind == JsonValueKind.Undefined)
                throw new ArgumentException(
                    $"{settingsPath} has no profile named '{profileName}'. Its profiles: "
                    + ProfileNames(profiles),
                    "launchProfile");

            return (named.Name, named.Value);
        }

        var https = all.FirstOrDefault(p => p.Name == "https");
        if (https.Value.ValueKind != JsonValueKind.Undefined)
            return (https.Name, https.Value);

        var project = all.FirstOrDefault(p => CommandName(p.Value) == "Project");
        if (project.Value.ValueKind != JsonValueKind.Undefined)
            return (project.Name, project.Value);

        throw new ArgumentException(
            $"{settingsPath} has no \"https\" profile and none with commandName \"Project\"; name one "
            + "with launch_profile",
            "launchProfile");
    }

    private static string ProfileNames(JsonElement profiles) =>
        string.Join(", ", profiles.EnumerateObject().Select(p => p.Name));

    /// <summary>
    /// Only a Project profile is turned into a launch. An Executable profile names a program of its
    /// own, which would replace the one launch_program has checked, and IIS Express or Docker
    /// profiles do not run the built program at all. executablePath is never read, so the program
    /// stays the one the caller passed.
    /// </summary>
    private static LaunchProfile ToLaunchProfile(
        string name, JsonElement profile, string projectDirectory, string settingsPath, string profileNames)
    {
        if (profile.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"Profile '{name}' in {settingsPath} is not an object", "launchProfile");

        var commandName = CommandName(profile);
        if (commandName != "Project")
            throw new ArgumentException(
                $"Profile '{name}' has commandName \"{commandName}\"; only \"Project\" profiles are used, "
                + $"since the others run something other than the built program. Its profiles: {profileNames}",
                "launchProfile");

        var arguments = String(profile, "commandLineArgs", name) is { } commandLine
            ? SplitCommandLine(commandLine)
            : null;

        // Relative to the project, the way an editor resolves it
        var workingDirectory = String(profile, "workingDirectory", name) is { Length: > 0 } directory
            ? Path.GetFullPath(directory, projectDirectory)
            : null;

        var environment = new Dictionary<string, string>();

        if (profile.TryGetProperty("environmentVariables", out var variables)
            && variables.ValueKind != JsonValueKind.Null)
        {
            if (variables.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"environmentVariables in profile '{name}' is not an object", "launchProfile");

            foreach (var variable in variables.EnumerateObject())
            {
                if (variable.Value.ValueKind != JsonValueKind.String)
                    throw new ArgumentException(
                        $"Environment variable {variable.Name} in profile '{name}' is not a string",
                        "launchProfile");

                environment[variable.Name] = variable.Value.GetString()!;
            }
        }

        // A URL set in the profile's own variables wins, as it does for the debugger
        if (String(profile, "applicationUrl", name) is { Length: > 0 } urls)
            environment.TryAdd("ASPNETCORE_URLS", urls);

        return new LaunchProfile(name, arguments, workingDirectory, environment);
    }

    private static string? CommandName(JsonElement profile) =>
        profile.ValueKind == JsonValueKind.Object
            && profile.TryGetProperty("commandName", out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static string? String(JsonElement profile, string property, string profileName)
    {
        if (!profile.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"{property} in profile '{profileName}' is not a string", "launchProfile");

        return value.GetString();
    }

    /// <summary>
    /// Splits commandLineArgs the way a .NET program's command line is split on Windows: whitespace
    /// separates arguments, double quotes group them, and backslashes escape a quote only when they
    /// come right before one. The debugger splits on single spaces, which breaks any quoted path.
    /// </summary>
    public static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var inArgument = false;
        var quoted = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];

            if (c == '\\')
            {
                var backslashes = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                inArgument = true;

                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    // 2n backslashes and a quote: n backslashes, and the quote opens or closes;
                    // 2n+1: n backslashes and a literal quote
                    current.Append('\\', backslashes / 2);
                    if (backslashes % 2 == 1)
                        current.Append('"');
                    else
                        quoted = !quoted;
                }
                else
                {
                    current.Append('\\', backslashes);
                    i--;
                }
            }
            else if (c == '"')
            {
                inArgument = true;
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (inArgument)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    inArgument = false;
                }
            }
            else
            {
                inArgument = true;
                current.Append(c);
            }
        }

        if (inArgument)
            arguments.Add(current.ToString());

        return arguments;
    }
}
