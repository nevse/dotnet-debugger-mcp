using SharpDbg.MCP.Debugging;

namespace SharpDbg.MCP.Mobile;

/// <summary>
/// Everything needed to build a MAUI app and then debug it, worked out once from a project and a
/// device. Building and launching are separate tool calls but must agree on every one of these -
/// a launch that computed the app bundle's path differently from the build that produced it would
/// fail with a missing file and no indication that the two had disagreed.
/// </summary>
public sealed record MobileLaunchPlan(
    string ProjectPath,
    string Configuration,
    string TargetFramework,
    MobileDevice Device,
    string? RuntimeIdentifier,
    string? AdbSerial,
    string Program,
    string AssetsPath,
    VsdbgLibraries Libraries)
{
    /// <summary>
    /// The plan as the debugger takes it. Mac Catalyst names no device - it runs on this machine -
    /// while Android is given the AVD name of an emulator that may not be booted yet, which the
    /// debugger boots for itself.
    /// </summary>
    public MobileLaunchOptions ToLaunchOptions(bool uninstallApp) => new(
        Platform: Device.DebugTarget,
        RuntimeIdentifier: RuntimeIdentifier,
        Device: Device.Platform == MobilePlatforms.MacCatalyst ? null : Device.Id,
        IsDevice: !Device.IsEmulator && Device.Platform != MobilePlatforms.MacCatalyst,
        AssetsPath: AssetsPath,
        UninstallApp: uninstallApp,
        RemoteCoreclrHost: Libraries.HostDirectory,
        RemoteCoreclrTarget: Libraries.TargetDirectory);

    /// <summary>
    /// The lowest framework version each platform has a CoreCLR runtime for. Below it a MAUI app is
    /// built against Mono, which has its own debugging protocol that this debugger does not speak -
    /// it would build cleanly, start, and then never connect back.
    /// </summary>
    private static readonly Dictionary<string, Version> FirstCoreClrVersion = new(StringComparer.OrdinalIgnoreCase)
    {
        [MobilePlatforms.Android] = new Version(10, 0),
        [MobilePlatforms.IOS] = new Version(11, 0),
        [MobilePlatforms.MacCatalyst] = new Version(11, 0)
    };

    public static MobileLaunchPlan Create(
        string projectPath,
        MobileDevice device,
        string configuration,
        string? targetFramework,
        VsdbgLibraries libraries,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(libraries);

        var project = ValidateProject(projectPath);
        var framework = targetFramework ?? SelectTargetFramework(project, device.Platform, timeout);

        VerifySupported(framework, device.Platform);

        var runtimeIdentifier = device.RuntimeIdentifier;

        var properties = new Dictionary<string, string>
        {
            ["Configuration"] = configuration,
            ["TargetFramework"] = framework,
            // The output layout differs between the two runtimes, so the paths are read for the
            // runtime the app will actually be built with
            ["UseMonoRuntime"] = "false"
        };

        if (!string.IsNullOrEmpty(runtimeIdentifier))
            properties["RuntimeIdentifier"] = runtimeIdentifier;

        var (program, assets) = ResolveOutput(project, device.Platform, properties, timeout);

        return new MobileLaunchPlan(
            project,
            configuration,
            framework,
            device,
            runtimeIdentifier,
            device.AdbSerial,
            program,
            assets,
            libraries);
    }

    private static string ValidateProject(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("A project path is required", nameof(projectPath));

        var full = Path.GetFullPath(projectPath);

        if (Directory.Exists(full))
            throw new ArgumentException(
                $"{full} is a directory. Pass the MAUI project's .csproj inside it.", nameof(projectPath));

        if (!File.Exists(full))
            throw new ArgumentException($"There is no file at {full}", nameof(projectPath));

        // The app is built from its project, unlike launch_program, which takes build output: what
        // has to be produced here - an .apk or an .app - is not something a caller can point at
        // before the build has run
        if (Path.GetExtension(full) is not (".csproj" or ".fsproj" or ".vbproj"))
            throw new ArgumentException(
                $"{full} is not a project file. Pass the MAUI project's .csproj.", nameof(projectPath));

        return full;
    }

    /// <summary>
    /// Picks the project's target framework for the platform being debugged. A MAUI project is
    /// multi-targeted, and building it without saying which one produces every platform at once -
    /// slowly, and with no single app to then launch.
    /// </summary>
    private static string SelectTargetFramework(string projectPath, string platform, TimeSpan timeout)
    {
        var properties = MobileBuild.Evaluate(
            projectPath, ["TargetFrameworks", "TargetFramework"], new Dictionary<string, string>(), timeout);

        var candidates = (properties.GetValueOrDefault("TargetFrameworks") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (candidates.Count == 0)
        {
            var single = properties.GetValueOrDefault("TargetFramework");

            if (!string.IsNullOrEmpty(single))
                candidates.Add(single);
        }

        var match = candidates.FirstOrDefault(tfm => PlatformOf(tfm) == platform);

        if (match != null)
            return match;

        throw new InvalidOperationException(
            $"{Path.GetFileName(projectPath)} has no {platform} target framework"
            + (candidates.Count == 0 ? "." : $". It targets {string.Join(", ", candidates)}.")
            + " Pass target_framework explicitly if it is built another way.");
    }

    /// <summary>
    /// The platform a target framework moniker names, lowercased and without its version:
    /// 'net11.0-ios18.0' is ios. Null for a framework with no platform at all.
    /// </summary>
    private static string? PlatformOf(string targetFramework)
    {
        var dash = targetFramework.IndexOf('-');

        if (dash < 0)
            return null;

        var platform = targetFramework[(dash + 1)..].TrimEnd("0123456789.".ToCharArray()).ToLowerInvariant();

        return platform.Length == 0 ? null : platform;
    }

    /// <summary>
    /// Refuses a framework whose runtime this debugger cannot attach to, before a build that takes
    /// minutes and an app that then silently never connects.
    /// </summary>
    private static void VerifySupported(string targetFramework, string platform)
    {
        var actual = PlatformOf(targetFramework);

        // A framework with no platform at all - plain 'net11.0' - is refused here rather than later:
        // it would evaluate, build a library, and produce no app to launch
        if (actual != platform)
            throw new ArgumentException(
                $"{targetFramework} targets {actual ?? "no platform"}, not {platform}.",
                nameof(targetFramework));

        if (!FirstCoreClrVersion.TryGetValue(platform, out var minimum))
            throw new ArgumentException($"{platform} is not a mobile platform this server debugs.", nameof(platform));

        var version = FrameworkVersionOf(targetFramework);

        if (version != null && version >= minimum)
            return;

        throw new InvalidOperationException(
            $"{targetFramework} runs on Mono, which this debugger cannot attach to - it debugs "
            + "CoreCLR only. CoreCLR on Android needs net10.0-android or later, and on iOS and Mac "
            + $"Catalyst net11.0 or later. Retarget the project to net{minimum.Major}.0-{platform}.");
    }

    /// <summary>The framework version of 'net11.0-ios' is 11.0. Null for anything not spelled that way.</summary>
    private static Version? FrameworkVersionOf(string targetFramework)
    {
        var dash = targetFramework.IndexOf('-');
        var head = dash < 0 ? targetFramework : targetFramework[..dash];

        return head.StartsWith("net", StringComparison.OrdinalIgnoreCase)
            && Version.TryParse(head[3..], out var version)
                ? version
                : null;
    }

    /// <summary>
    /// Where the build puts the app, and where its assemblies are for the debugger to read symbols
    /// from. Both come out of MSBuild rather than being assembled from the project path: the app's
    /// name is a property, and on Android the assemblies the debugger needs are in an intermediate
    /// directory that has nothing to do with the package.
    /// </summary>
    private static (string Program, string AssetsPath) ResolveOutput(
        string projectPath,
        string platform,
        IReadOnlyDictionary<string, string> properties,
        TimeSpan timeout)
    {
        string[] wanted = platform == MobilePlatforms.Android
            ? ["TargetPath", "ApplicationId", "MonoAndroidIntermediateAssemblyDir"]
            : ["TargetPath", "_AppBundleName"];

        var evaluated = MobileBuild.Evaluate(projectPath, wanted, properties, timeout);

        var targetPath = Required(evaluated, "TargetPath", projectPath);
        var outputDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"TargetPath has no directory: {targetPath}");

        if (platform == MobilePlatforms.Android)
        {
            // The signed package is what gets installed; the unsigned one beside it cannot be
            var applicationId = Required(evaluated, "ApplicationId", projectPath);
            var program = Path.Combine(outputDirectory, $"{applicationId}-Signed.apk");

            var assemblies = Required(evaluated, "MonoAndroidIntermediateAssemblyDir", projectPath);

            if (!Path.IsPathRooted(assemblies))
                assemblies = Path.Combine(Path.GetDirectoryName(projectPath)!, assemblies);

            return (program, Path.GetFullPath(assemblies));
        }

        var bundle = Path.Combine(outputDirectory, Required(evaluated, "_AppBundleName", projectPath) + ".app");

        // An iOS bundle is flat, a Mac Catalyst one keeps its managed assemblies in MonoBundle
        return platform == MobilePlatforms.MacCatalyst
            ? (bundle, Path.Combine(bundle, "Contents", "MonoBundle"))
            : (bundle, bundle);
    }

    private static string Required(IReadOnlyDictionary<string, string> properties, string name, string projectPath)
    {
        var value = properties.GetValueOrDefault(name);

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"{Path.GetFileName(projectPath)} does not define {name}, so where its build output "
                + "goes cannot be worked out. Check that it is a MAUI project and that its workloads "
                + "are installed.")
            : value;
    }
}
