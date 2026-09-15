using System.Text.Json;

using SharpDbg.MCP.Logging;

namespace SharpDbg.MCP.Mobile;

/// <summary>
/// The two things this server asks MSBuild for: what a project's build would produce, and the build
/// itself.
///
/// Where the output lands is a question only MSBuild can answer - an app bundle's name, an Android
/// package's id and the directory its assemblies are staged in are all computed by the platform SDK
/// out of properties a project may or may not set. Guessing them from the project path works until
/// somebody sets OutputPath, and then produces a path that does not exist with nothing to say why.
/// </summary>
internal static class MobileBuild
{
    /// <summary>
    /// Evaluates properties without building. MSBuild answers with JSON when more than one is
    /// asked for, which is why they are always fetched together rather than one call each - and
    /// also why this is cheap enough to do before every launch.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Evaluate(
        string projectPath,
        IReadOnlyList<string> names,
        IReadOnlyDictionary<string, string> properties,
        TimeSpan timeout)
    {
        List<string> arguments = ["msbuild", projectPath];

        foreach (var name in names)
            arguments.Add($"-getProperty:{name}");

        // A single -getProperty prints the bare value instead of JSON. Asking for the project's own
        // path alongside costs nothing and keeps the answer one shape.
        if (names.Count == 1)
            arguments.Add("-getProperty:MSBuildProjectFullPath");

        foreach (var (key, value) in properties)
            arguments.Add($"-p:{key}={value}");

        // Run from the project's own directory, so a global.json beside it picks the SDK. Evaluating
        // from wherever this server happens to have been started would use that directory's
        // global.json instead, and a project needing a newer SDK than the server's would fail to
        // evaluate for a reason nothing in the answer would explain.
        var result = ShellCommand.Run(
            "dotnet", arguments, timeout, workingDirectory: Path.GetDirectoryName(projectPath));

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Could not read the build properties of {Path.GetFileName(projectPath)}. MSBuild said:"
                + Environment.NewLine + ShellCommand.Tail(result.Errors.Count > 0 ? result.Errors : result.Output, 20));
        }

        return Parse(result.OutputText, projectPath);
    }

    /// <summary>
    /// MSBuild writes {"Properties": {...}} to standard output. A banner or a warning can precede
    /// it, so the object is found rather than assumed to start at the beginning.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Parse(string output, string projectPath)
    {
        var start = output.IndexOf('{');

        if (start < 0)
            throw new InvalidOperationException(
                $"MSBuild returned no properties for {Path.GetFileName(projectPath)}: {output.Trim()}");

        using var json = JsonDocument.Parse(output[start..]);

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (json.RootElement.TryGetProperty("Properties", out var values))
        {
            foreach (var value in values.EnumerateObject())
                properties[value.Name] = value.Value.GetString() ?? string.Empty;
        }

        return properties;
    }

    /// <summary>What a build produced, or failed to</summary>
    internal sealed record BuildResult(bool Success, string Command, IReadOnlyList<string> Output);

    /// <summary>
    /// Builds the app with the debugger's libraries in it. The properties are what turn an ordinary
    /// MAUI build into a debuggable one, and every one of them is load-bearing:
    ///
    /// UseMonoRuntime=false picks CoreCLR, which is the only runtime this debugger can attach to.
    /// CustomAfterMicrosoftCommonTargets and RemoteCoreclrTargetDir put the remote debugging library
    /// inside the app. EnableDiagnostics keeps the diagnostic components a debugger needs from being
    /// trimmed out. On Android, AndroidAttachDebugger is what makes the native libraries travel and
    /// stops the build from launching the app itself - the debugger installs and launches it, having
    /// first set the environment the runtime reads at startup.
    /// </summary>
    public static BuildResult Build(MobileLaunchPlan plan, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(plan);

        List<string> arguments = ["build", plan.ProjectPath];

        foreach (var (key, value) in BuildProperties(plan))
            arguments.Add($"-p:{key}={value}");

        var command = "dotnet " + string.Join(" ", arguments);
        McpLogger.LogDebug("Building the mobile app: {Command}", command);

        var result = ShellCommand.Run(
            "dotnet", arguments, timeout,
            workingDirectory: Path.GetDirectoryName(plan.ProjectPath),
            onLine: line => McpLogger.LogDebug("[build] {Line}", line));

        return new BuildResult(result.Success, command, result.Output);
    }

    /// <summary>
    /// The build properties, also used to report what the build was asked to do. Kept separate from
    /// running it so a caller can be shown the command before it takes a minute to fail.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> BuildProperties(MobileLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        yield return new("Configuration", plan.Configuration);
        yield return new("TargetFramework", plan.TargetFramework);

        if (!string.IsNullOrEmpty(plan.RuntimeIdentifier))
            yield return new("RuntimeIdentifier", plan.RuntimeIdentifier);

        yield return new("CustomAfterMicrosoftCommonTargets", VsdbgLibraries.TargetsFile);
        yield return new("RemoteCoreclrTargetDir", plan.Libraries.TargetDirectory);
        yield return new("UseMonoRuntime", "false");
        yield return new("EnableDiagnostics", "true");

        if (plan.Device.Platform != MobilePlatforms.Android)
            yield break;

        yield return new("AndroidAttachDebugger", "true");

        var sdk = AndroidSdk.Directory();

        if (sdk != null)
            yield return new("AndroidSdkDirectory", sdk);

        // Which device the Android targets query for its ABI and API level. %20 is how MSBuild
        // carries the space through a property value; the build only needs it when more than one
        // device is attached, and a cold emulator has no serial to give yet.
        if (plan.AdbSerial != null)
            yield return new("AdbTarget", $"-s%20{plan.AdbSerial}");
    }
}
