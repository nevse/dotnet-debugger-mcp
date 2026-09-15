using System.ComponentModel;
using System.Text.Json;

using ModelContextProtocol.Server;

using SharpDbg.MCP.Configuration;
using SharpDbg.MCP.Debugging;
using SharpDbg.MCP.Mobile;

namespace SharpDbg.MCP.Tools;

/// <summary>
/// MCP tools for debugging a .NET MAUI app on a phone, an emulator or this Mac.
///
/// The shape is the same as the desktop one - prepare, set breakpoints, start - because everything
/// after the launch is identical: the debugger connects to the app over a socket and speaks the
/// same protocol it does to a local process, so every other tool in this server works unchanged.
/// What differs is only what it takes to get there, and that is what these three tools cover:
/// finding the device, building an app with the debugging library inside it, and telling the
/// debugger which of the two to put where.
/// </summary>
[McpServerToolType]
public sealed class MobileTools
{
    private readonly ServerConfiguration _configuration;
    private readonly DebugSessionManager _sessionManager;
    private readonly MobileDeviceDiscovery _devices;

    public MobileTools(
        ServerConfiguration configuration,
        DebugSessionManager sessionManager,
        MobileDeviceDiscovery devices)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(devices);

        _configuration = configuration;
        _sessionManager = sessionManager;
        _devices = devices;
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [McpServerTool, Description(
        "List the devices, emulators and simulators a .NET MAUI app can be built for and debugged " +
        "on: Android emulators and phones, iOS simulators and devices, and this Mac as a Mac " +
        "Catalyst target. The id is what build_mobile_app and launch_mobile_app take. An Android " +
        "emulator does not have to be running - the debugger boots a cold one itself - and is " +
        "identified by its AVD name rather than by an adb serial, which it only has once booted.")]
    public string ListMobileDevices()
    {
        try
        {
            var listing = _devices.List();

            var response = new
            {
                success = true,
                count = listing.Devices.Count,
                devices = listing.Devices.Select(d => new
                {
                    id = d.Id,
                    name = d.Name,
                    platform = d.Platform,
                    runtime_identifier = d.RuntimeIdentifier,
                    os_version = d.OsVersion,
                    is_emulator = d.IsEmulator,
                    is_running = d.IsRunning
                }).ToList(),
                // Reported rather than thrown: a machine with no Android SDK still has simulators,
                // and a caller looking for one should not be told about the SDK instead
                warnings = listing.Warnings
            };

            return JsonSerializer.Serialize(response, Indented);
        }
        catch (Exception ex)
        {
            return DebuggerErrors.ErrorResponse(ex);
        }
    }

    [McpServerTool, Description(
        "Build a .NET MAUI project for a device so that it can be debugged, which an ordinary " +
        "'dotnet build' does not produce: the app is built against CoreCLR rather than Mono and " +
        "carries the remote debugging library inside it. Pass the project's .csproj and a device " +
        "id from list_mobile_devices. Needs the remote CoreCLR debugger libraries, which are part " +
        "of Visual Studio and are not shipped with this server - set SHARPDBG_VSDBG_LIBRARIES to " +
        "the directory holding VsdbgRemoteCoreclrHost and VsdbgRemoteCoreclrTarget, or pass it as " +
        "vsdbg_libraries_path. Then call launch_mobile_app.")]
    public string BuildMobileApp(
        string project_path,
        string device_id,
        string configuration = "Debug",
        string? target_framework = null,
        string? vsdbg_libraries_path = null)
    {
        try
        {
            var plan = Plan(project_path, device_id, configuration, target_framework, vsdbg_libraries_path);
            var result = MobileBuild.Build(plan, TimeSpan.FromSeconds(_configuration.BuildTimeoutSeconds));

            if (!result.Success)
            {
                var failure = new
                {
                    success = false,
                    error = $"The build of {Path.GetFileName(plan.ProjectPath)} failed.",
                    command = result.Command,
                    build_output = ShellCommand.Tail(result.Output, 40)
                };

                return JsonSerializer.Serialize(failure, Indented);
            }

            var response = new
            {
                success = true,
                project = plan.ProjectPath,
                device = Describe(plan),
                configuration = plan.Configuration,
                target_framework = plan.TargetFramework,
                runtime_identifier = plan.RuntimeIdentifier,
                program = plan.Program,
                assets_path = plan.AssetsPath,
                // Said plainly because the build can succeed while producing nothing to launch -
                // an unsigned Android build, or a target framework that builds a library
                program_exists = ProgramExists(plan.Program),
                command = result.Command,
                message = "Built. Call launch_mobile_app with the same project and device, then set "
                    + "breakpoints, then start_program."
            };

            return JsonSerializer.Serialize(response, Indented);
        }
        catch (Exception ex)
        {
            return DebuggerErrors.ErrorResponse(ex);
        }
    }

    [McpServerTool, Description(
        "Prepare a built .NET MAUI app for debugging on a device without starting it yet, the way " +
        "launch_program does for a desktop program: set breakpoints now - they will be in place " +
        "before the app's first line runs - and then call start_program. Build it with " +
        "build_mobile_app first, with the same project, device and configuration. start_program is " +
        "where the time goes: it boots the emulator if it is cold, installs the app and launches " +
        "it, which is minutes rather than seconds. Everything after that - breakpoints, stepping, " +
        "variables, expressions - works exactly as it does for a local process.")]
    public string LaunchMobileApp(
        string project_path,
        string device_id,
        string configuration = "Debug",
        string? target_framework = null,
        string? vsdbg_libraries_path = null,
        bool uninstall_app = false,
        int? session_id = null)
    {
        try
        {
            var plan = Plan(project_path, device_id, configuration, target_framework, vsdbg_libraries_path);

            if (!ProgramExists(plan.Program))
            {
                throw new InvalidOperationException(
                    $"There is no app at {plan.Program}. Build it first with build_mobile_app, "
                    + "passing the same project, device and configuration - an app built any other "
                    + "way runs on Mono and carries no debugging library, and cannot be attached to.");
            }

            var session = _sessionManager.AcquireForDebuggee(session_id);

            session
                .Launch(plan.Program, mobile: plan.ToLaunchOptions(uninstall_app))
                .GetAwaiter()
                .GetResult();

            var response = new
            {
                success = true,
                session_id = session.SessionId,
                program = plan.Program,
                device = Describe(plan),
                target_framework = plan.TargetFramework,
                assets_path = plan.AssetsPath,
                started = false,
                message = "Prepared but not started. Set breakpoints now - they will be in place "
                    + "before the app runs - then call start_program, which may take several "
                    + $"minutes (it is bounded at {_configuration.MobileStartTimeoutSeconds}s). "
                    + "The app's output appears in get_program_output."
            };

            return JsonSerializer.Serialize(response, Indented);
        }
        catch (Exception ex)
        {
            return DebuggerErrors.ErrorResponse(ex);
        }
    }

    /// <summary>
    /// Works out everything the build and the launch have to agree on. Both tools do this the same
    /// way from the same arguments, which is what keeps a launch from looking for the app somewhere
    /// other than where the build put it.
    /// </summary>
    private MobileLaunchPlan Plan(
        string projectPath,
        string deviceId,
        string configuration,
        string? targetFramework,
        string? vsdbgLibrariesPath)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            throw new ArgumentException("Configuration cannot be empty", nameof(configuration));

        var device = _devices.Resolve(deviceId);

        if (device.Platform is MobilePlatforms.IOS or MobilePlatforms.MacCatalyst && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(
                $"{device.Platform} apps can only be built and debugged on macOS.");

        var libraries = VsdbgLibraries.Resolve(_configuration, vsdbgLibrariesPath);

        return MobileLaunchPlan.Create(
            projectPath,
            device,
            configuration,
            targetFramework,
            libraries,
            TimeSpan.FromSeconds(_configuration.OperationTimeoutSeconds));
    }

    /// <summary>
    /// An iOS or Mac Catalyst app is a directory, an Android package is a file, so both are
    /// accepted as "there"
    /// </summary>
    private static bool ProgramExists(string program) =>
        File.Exists(program) || Directory.Exists(program);

    private static object Describe(MobileLaunchPlan plan) => new
    {
        id = plan.Device.Id,
        name = plan.Device.Name,
        platform = plan.Device.Platform,
        is_emulator = plan.Device.IsEmulator,
        is_running = plan.Device.IsRunning
    };
}
