using System.Reflection;

using Microsoft.Extensions.Logging;

namespace SharpDbg.MCP.Configuration;

/// <summary>
/// Configuration settings for the SharpDbg MCP Server
/// </summary>
public class ServerConfiguration
{
    /// <summary>
    /// Log level for the server (default: Information)
    /// Environment variable: SHARPDBG_LOG_LEVEL
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Maximum number of debug sessions open at once (default: 1). Raising it lets one server debug
    /// several processes at the same time, each with its own breakpoints and its own stops, selected
    /// by session_id. The default of one keeps that off unless it is asked for: every attach carries
    /// the risk of a native crash inside the debugging shim, and more sessions means more attaches.
    /// Environment variable: SHARPDBG_MAX_SESSIONS
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 1;

    /// <summary>
    /// Timeout in seconds for debugger operations (default: 30)
    /// Environment variable: SHARPDBG_OPERATION_TIMEOUT_SECONDS
    /// </summary>
    public int OperationTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to allow attaching to processes owned by other users (default: false)
    /// Environment variable: SHARPDBG_ALLOW_OTHER_USER_PROCESSES
    /// </summary>
    public bool AllowOtherUserProcesses { get; set; } = false;

    /// <summary>
    /// Expression evaluation timeout in milliseconds (default: 5000)
    /// Environment variable: SHARPDBG_EVAL_TIMEOUT_MS
    /// </summary>
    public int ExpressionEvaluationTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// How long to wait for a breakpoint to bind before reporting it as unverified (default: 2000).
    /// A breakpoint set right after attaching can be answered unverified because the target
    /// module's symbols have not been processed yet; it binds on the module-load callback, which
    /// measures around 30ms. Waiting for that is what keeps callers from seeing a breakpoint that
    /// is about to become active, but the wait has to stay short, because a breakpoint that can
    /// never bind costs exactly this long.
    /// Environment variable: SHARPDBG_BREAKPOINT_BIND_TIMEOUT_MS
    /// </summary>
    public int BreakpointBindTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// Enable detailed diagnostic logging for troubleshooting (default: false)
    /// Environment variable: SHARPDBG_ENABLE_DIAGNOSTICS
    /// </summary>
    public bool EnableDiagnostics { get; set; } = false;

    /// <summary>
    /// Restrict debugging to user code, skipping framework and third-party assemblies (default: true).
    /// Turning it off makes the debugger look for symbols of every module rather than only the ones
    /// built by the user, so a step can surface inside a dependency whose symbols are there. It does
    /// not let a step stop in code with no symbols at all: such a step steps back out either way.
    /// Environment variable: SHARPDBG_JUST_MY_CODE
    /// </summary>
    public bool JustMyCode { get; set; } = true;

    /// <summary>
    /// Where the remote CoreCLR debugger libraries are, as one directory holding
    /// VsdbgRemoteCoreclrHost and VsdbgRemoteCoreclrTarget. They are part of Visual Studio's
    /// debugger and cannot be redistributed, so debugging a mobile app needs this set - there is
    /// nothing sensible to default it to.
    /// Environment variable: SHARPDBG_VSDBG_LIBRARIES
    /// </summary>
    public string? VsdbgLibrariesDirectory { get; set; }

    /// <summary>
    /// The host half of those libraries, for a machine that does not keep the two together. Set
    /// this and the target below instead of VsdbgLibrariesDirectory; setting both of them wins.
    /// Environment variable: SHARPDBG_REMOTE_CORECLR_HOST
    /// </summary>
    public string? RemoteCoreclrHostDirectory { get; set; }

    /// <summary>
    /// The target half: the libraries that go inside the app being debugged, one per platform and
    /// architecture.
    /// Environment variable: SHARPDBG_REMOTE_CORECLR_TARGET
    /// </summary>
    public string? RemoteCoreclrTargetDirectory { get; set; }

    /// <summary>
    /// How long starting a mobile app may take, in seconds (default: 600). Starting one is not the
    /// millisecond affair that starting a local program is: the debugger boots the emulator if it
    /// is cold, uninstalls and installs the package, pushes the assemblies and launches the app,
    /// all within the single request that start_program sends. A cold emulator alone can take
    /// minutes, which is why this is not the operation timeout.
    /// Environment variable: SHARPDBG_MOBILE_START_TIMEOUT_SECONDS
    /// </summary>
    public int MobileStartTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// How long a mobile build may take, in seconds (default: 900). A first build of a MAUI project
    /// - restoring, compiling for the platform, packaging and signing - is minutes rather than
    /// seconds, and a cold NuGet cache makes it longer still.
    /// Environment variable: SHARPDBG_BUILD_TIMEOUT_SECONDS
    /// </summary>
    public int BuildTimeoutSeconds { get; set; } = 900;

    /// <summary>
    /// Server version, as reported to the client over MCP. Read from the assembly rather than
    /// written here, because the package version comes from the release tag at pack time and a
    /// constant would go stale the moment the two disagreed - which, being cosmetic, nobody notices.
    /// The project stamps 0.0.0-dev when no tag supplied one, so an unreleased build says so.
    /// </summary>
    public string Version { get; set; } = ReadAssemblyVersion();

    private static string ReadAssemblyVersion()
    {
        var informational = typeof(ServerConfiguration).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
            return "0.0.0-dev";

        // The SDK appends the source revision as "+<sha>", which is noise to a client
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }

    /// <summary>
    /// Load configuration from environment variables
    /// </summary>
    public static ServerConfiguration LoadFromEnvironment()
    {
        var config = new ServerConfiguration();

        // Log level
        var logLevel = Environment.GetEnvironmentVariable("SHARPDBG_LOG_LEVEL");
        if (Enum.TryParse<LogLevel>(logLevel, true, out var parsedLevel))
        {
            config.LogLevel = parsedLevel;
        }

        // Max sessions
        var maxSessions = Environment.GetEnvironmentVariable("SHARPDBG_MAX_SESSIONS");
        if (int.TryParse(maxSessions, out var parsedMaxSessions) && parsedMaxSessions > 0)
        {
            config.MaxConcurrentSessions = parsedMaxSessions;
        }

        // Operation timeout
        var opTimeout = Environment.GetEnvironmentVariable("SHARPDBG_OPERATION_TIMEOUT_SECONDS");
        if (int.TryParse(opTimeout, out var parsedOpTimeout) && parsedOpTimeout > 0)
        {
            config.OperationTimeoutSeconds = parsedOpTimeout;
        }

        // Allow other user processes
        var allowOther = Environment.GetEnvironmentVariable("SHARPDBG_ALLOW_OTHER_USER_PROCESSES");
        if (bool.TryParse(allowOther, out var parsedAllowOther))
        {
            config.AllowOtherUserProcesses = parsedAllowOther;
        }

        // Expression evaluation timeout
        var evalTimeout = Environment.GetEnvironmentVariable("SHARPDBG_EVAL_TIMEOUT_MS");
        if (int.TryParse(evalTimeout, out var parsedEvalTimeout) && parsedEvalTimeout > 0)
        {
            config.ExpressionEvaluationTimeoutMs = parsedEvalTimeout;
        }

        // Breakpoint bind timeout
        var bindTimeout = Environment.GetEnvironmentVariable("SHARPDBG_BREAKPOINT_BIND_TIMEOUT_MS");
        if (int.TryParse(bindTimeout, out var parsedBindTimeout) && parsedBindTimeout > 0)
        {
            config.BreakpointBindTimeoutMs = parsedBindTimeout;
        }

        // Just my code
        var justMyCode = Environment.GetEnvironmentVariable("SHARPDBG_JUST_MY_CODE");
        if (bool.TryParse(justMyCode, out var parsedJustMyCode))
        {
            config.JustMyCode = parsedJustMyCode;
        }

        // Remote CoreCLR debugger libraries, for mobile debugging. Kept as written rather than
        // resolved here: an unset one is the normal case on a machine that debugs no mobile apps,
        // and a wrong one has to be reported to whoever asked for the app, not at startup.
        config.VsdbgLibrariesDirectory = Environment.GetEnvironmentVariable("SHARPDBG_VSDBG_LIBRARIES");
        config.RemoteCoreclrHostDirectory = Environment.GetEnvironmentVariable("SHARPDBG_REMOTE_CORECLR_HOST");
        config.RemoteCoreclrTargetDirectory = Environment.GetEnvironmentVariable("SHARPDBG_REMOTE_CORECLR_TARGET");

        // Mobile start timeout
        var mobileStart = Environment.GetEnvironmentVariable("SHARPDBG_MOBILE_START_TIMEOUT_SECONDS");
        if (int.TryParse(mobileStart, out var parsedMobileStart) && parsedMobileStart > 0)
        {
            config.MobileStartTimeoutSeconds = parsedMobileStart;
        }

        // Build timeout
        var buildTimeout = Environment.GetEnvironmentVariable("SHARPDBG_BUILD_TIMEOUT_SECONDS");
        if (int.TryParse(buildTimeout, out var parsedBuildTimeout) && parsedBuildTimeout > 0)
        {
            config.BuildTimeoutSeconds = parsedBuildTimeout;
        }

        // Diagnostics
        var diagnostics = Environment.GetEnvironmentVariable("SHARPDBG_ENABLE_DIAGNOSTICS");
        if (bool.TryParse(diagnostics, out var parsedDiagnostics))
        {
            config.EnableDiagnostics = parsedDiagnostics;
        }

        return config;
    }

    /// <summary>
    /// Validate configuration and return error message if invalid
    /// </summary>
    public string? Validate()
    {
        if (MaxConcurrentSessions < 1)
            return "MaxConcurrentSessions must be at least 1";

        if (OperationTimeoutSeconds < 1)
            return "OperationTimeoutSeconds must be at least 1";

        if (ExpressionEvaluationTimeoutMs < 100)
            return "ExpressionEvaluationTimeoutMs must be at least 100ms";

        if (BreakpointBindTimeoutMs < 100)
            return "BreakpointBindTimeoutMs must be at least 100ms";

        if (MobileStartTimeoutSeconds < 1)
            return "MobileStartTimeoutSeconds must be at least 1";

        if (BuildTimeoutSeconds < 1)
            return "BuildTimeoutSeconds must be at least 1";

        return null;
    }
}
