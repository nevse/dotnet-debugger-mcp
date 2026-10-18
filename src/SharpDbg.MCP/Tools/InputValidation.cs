using SharpDbg.MCP.Debugging;

namespace SharpDbg.MCP.Tools;

/// <summary>
/// Input validation helpers for MCP tools
/// </summary>
public static class InputValidation
{
    public static void ValidateProcessId(int processId)
    {
        if (processId <= 0)
            throw new ArgumentException($"Process ID must be positive, got: {processId}", nameof(processId));
    }

    public static void ValidateThreadId(int threadId)
    {
        if (threadId < 0)
            throw new ArgumentException($"Thread ID must be non-negative, got: {threadId}", nameof(threadId));
    }

    public static void ValidateFrameId(int frameId)
    {
        if (frameId <= 0)
            throw new ArgumentException($"Frame ID must be positive, got: {frameId}", nameof(frameId));
    }

    public static void ValidateVariablesReference(int variablesReference)
    {
        if (variablesReference <= 0)
            throw new ArgumentException(
                $"Variables reference must be positive, got: {variablesReference}", nameof(variablesReference));
    }

    public static void ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty", nameof(filePath));
    }

    /// <summary>
    /// Validates the program to launch and returns it as an absolute path. A path that does not
    /// exist fails deep inside the debugger with "Process start failed", and a project file is the
    /// mistake worth naming: what runs is build output, not a project.
    /// </summary>
    public static string ValidateProgramPath(string programPath)
    {
        if (string.IsNullOrWhiteSpace(programPath))
            throw new ArgumentException("Program path cannot be empty", nameof(programPath));

        if (Path.GetExtension(programPath) is ".csproj" or ".fsproj" or ".vbproj" or ".sln" or ".slnx")
            throw new ArgumentException(
                $"{programPath} is a project, not a program. Build it and pass the .dll or the " +
                "executable next to it, usually under bin/Debug/<framework>.",
                nameof(programPath));

        var fullPath = Path.GetFullPath(programPath);

        if (!File.Exists(fullPath))
            throw new ArgumentException($"There is no file at {fullPath}", nameof(programPath));

        RequireDotNetProgram(fullPath);

        return fullPath;
    }

    /// <summary>
    /// Only a .NET program is launched. The debugger starts anything it is given - a .dll through
    /// the dotnet muxer, anything else directly - so without this, launch_program would run /bin/sh
    /// or cmd.exe with whatever arguments it was handed, a shell for anyone allowed this tool but
    /// not a shell.
    ///
    /// What marks a .NET program is its build output: an app's .dll always has its
    /// .runtimeconfig.json beside it, and an apphost - framework-dependent or self-contained - has
    /// both of those beside it under its own name. A single-file publish has neither, and is not
    /// debuggable anyway. This is not a sandbox: an agent able to write files can still build or
    /// assemble something that passes. It stops a ready-made executable from being run.
    /// </summary>
    private static void RequireDotNetProgram(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath)!;
        var isAssembly = Path.GetExtension(fullPath).Equals(".dll", StringComparison.OrdinalIgnoreCase);
        // App.dll and App.exe both name the app App; an apphost on macOS and Linux has no extension
        var name = isAssembly || OperatingSystem.IsWindows()
            ? Path.GetFileNameWithoutExtension(fullPath)
            : Path.GetFileName(fullPath);

        var assembly = Path.Combine(directory, name + ".dll");
        var runtimeConfig = Path.Combine(directory, name + ".runtimeconfig.json");

        if (File.Exists(assembly) && File.Exists(runtimeConfig))
            return;

        throw new ArgumentException(
            isAssembly
                ? $"{fullPath} is not a runnable .NET app: there is no {Path.GetFileName(runtimeConfig)} "
                    + "beside it, so it is a library or not build output. Pass the app's own .dll."
                : $"{fullPath} is not a .NET app: only .NET programs are launched, and a .NET app's "
                    + $"executable has {Path.GetFileName(assembly)} and {Path.GetFileName(runtimeConfig)} "
                    + "beside it. A single-file publish has neither and cannot be debugged; build "
                    + "without PublishSingleFile and pass the output.",
            "programPath");
    }

    /// <summary>
    /// Variables that load code of somebody else's choosing into the program before or alongside
    /// it: a startup hook or extra dependencies through the host, a profiler through the runtime,
    /// a different runtime through DOTNET_ROOT, a preloaded library through the dynamic loader, a
    /// hosting startup assembly through ASP.NET Core, which reads its host settings under both the
    /// ASPNETCORE_ and the DOTNET_ prefix.
    /// Any one of them turns launching a legitimate program into running arbitrary native code, so
    /// the program-path check would be decorative without refusing them.
    /// </summary>
    private static readonly string[] RefusedEnvironmentVariables =
    [
        "DOTNET_STARTUP_HOOKS",
        "DOTNET_ADDITIONAL_DEPS",
        "DOTNET_SHARED_STORE",
        "CORECLR_ENABLE_PROFILING",
        "COR_ENABLE_PROFILING",
        "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES",
        "DOTNET_HOSTINGSTARTUPASSEMBLIES",
        "LD_PRELOAD",
        "LD_LIBRARY_PATH",
        "LD_AUDIT"
    ];

    /// <summary>
    /// Prefixes refused for the same reason: the 32- and 64-bit and per-architecture forms of the
    /// profiler and runtime-location variables, and every DYLD_ variable, which macOS reads to
    /// insert or redirect libraries.
    /// </summary>
    private static readonly string[] RefusedEnvironmentPrefixes =
    [
        "CORECLR_PROFILER",
        "COR_PROFILER",
        "DOTNET_ROOT",
        "DYLD_"
    ];

    /// <summary>
    /// Refuses environment variables that inject code into the launched program. Compared without
    /// case, because Windows treats them that way and a variable spelled differently must not slip
    /// past on the platform where it would still take effect. <paramref name="source"/> names where
    /// the variables came from when it is not the caller, so a refusal points at the right place.
    /// </summary>
    public static void ValidateLaunchEnvironment(
        IReadOnlyDictionary<string, string>? environment, string? source = null)
    {
        if (environment is null)
            return;

        var refused = environment.Keys
            .Where(name =>
                RefusedEnvironmentVariables.Contains(name, StringComparer.OrdinalIgnoreCase)
                || RefusedEnvironmentPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (refused.Count > 0)
            throw new ArgumentException(
                $"These environment variables{(source is null ? "" : $" set by {source}")} are refused, "
                + "because they load other code into the "
                + $"program: {string.Join(", ", refused)}.",
                nameof(environment));
    }

    public static void ValidateWorkingDirectory(string? workingDirectory)
    {
        if (workingDirectory is null)
            return;

        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new ArgumentException("Working directory cannot be blank", nameof(workingDirectory));

        if (!Directory.Exists(workingDirectory))
            throw new ArgumentException($"There is no directory at {workingDirectory}", nameof(workingDirectory));
    }

    public static void ValidateOutputLineCount(int maxLines)
    {
        if (maxLines <= 0)
            throw new ArgumentException($"max_lines must be positive, got: {maxLines}", nameof(maxLines));
    }

    /// <summary>
    /// Validates a function breakpoint name. Only emptiness is checked here: the pattern grammar is
    /// the debugger's, and it reports a bad pattern as an unverified breakpoint with the reason,
    /// which is more useful than a second opinion from here.
    /// </summary>
    public static void ValidateFunctionName(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            throw new ArgumentException("Function name cannot be empty", nameof(functionName));
    }

    public static void ValidateLineNumber(int line)
    {
        if (line <= 0)
            throw new ArgumentException($"Line number must be positive, got: {line}", nameof(line));
    }

    public static void ValidateBreakpointId(int breakpointId)
    {
        if (breakpointId <= 0)
            throw new ArgumentException($"Breakpoint ID must be positive, got: {breakpointId}", nameof(breakpointId));
    }

    /// <summary>
    /// Validates a hit-count condition. The debugger silently treats an unparseable one as never
    /// satisfied, which would leave a breakpoint that never fires and no indication why.
    /// </summary>
    public static void ValidateHitCondition(string? hitCondition)
    {
        if (string.IsNullOrWhiteSpace(hitCondition))
            return;

        var text = hitCondition.Trim();

        var operatorLength = text switch
        {
            _ when text.StartsWith("==", StringComparison.Ordinal) => 2,
            _ when text.StartsWith(">=", StringComparison.Ordinal) => 2,
            _ when text.StartsWith("<=", StringComparison.Ordinal) => 2,
            _ when text[0] is '>' or '<' or '%' => 1,
            _ => 0
        };

        if (!int.TryParse(text[operatorLength..], out var count))
            throw new ArgumentException(
                "Hit condition must be a count, optionally prefixed with ==, >=, <=, >, < or %, " +
                $"got: {hitCondition}",
                nameof(hitCondition));

        if (text[0] == '%' && count <= 0)
            throw new ArgumentException(
                $"A '%' hit condition needs a positive interval, got: {hitCondition}",
                nameof(hitCondition));
    }

    /// <summary>
    /// Validates a blocking wait. The upper bound keeps a tool call from outliving the client's
    /// own request timeout, which would leave the caller with no result at all.
    /// </summary>
    public static void ValidateWaitTimeout(int timeoutMs)
    {
        const int MaxTimeoutMs = 300_000;

        if (timeoutMs <= 0)
            throw new ArgumentException($"Timeout must be positive, got: {timeoutMs}", nameof(timeoutMs));

        if (timeoutMs > MaxTimeoutMs)
            throw new ArgumentException(
                $"Timeout must be at most {MaxTimeoutMs}ms, got: {timeoutMs}", nameof(timeoutMs));
    }

    /// <summary>
    /// Parses an exception break mode, naming the accepted values on failure - an unrecognised mode
    /// silently falling back to a default would change whether the debuggee stops.
    /// </summary>
    public static ExceptionBreakMode ParseExceptionBreakMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            throw new ArgumentException("Exception break mode cannot be empty", nameof(mode));

        return mode.Trim().ToLowerInvariant() switch
        {
            "always" => ExceptionBreakMode.Always,
            "user_unhandled" => ExceptionBreakMode.UserUnhandled,
            "unhandled" => ExceptionBreakMode.Unhandled,
            "never" => ExceptionBreakMode.Never,
            _ => throw new ArgumentException(
                "Exception break mode must be 'always', 'user_unhandled', 'unhandled' or 'never', got: "
                + mode, nameof(mode))
        };
    }

    /// <summary>
    /// Checks a type filter before it is turned into the debugger's condition string, which is a
    /// comma-separated list where a leading '!' means the whole list is exclusions. A name carrying
    /// either character would change the meaning of the list rather than be matched, so both are
    /// refused here instead of quietly producing a filter that does something else.
    /// </summary>
    public static void ValidateExceptionTypes(string[]? exceptionTypes, ExceptionBreakMode mode)
    {
        if (exceptionTypes is null || exceptionTypes.Length == 0)
            return;

        if (mode is ExceptionBreakMode.Unhandled or ExceptionBreakMode.Never)
        {
            throw new ArgumentException(
                $"Exception types cannot be used with mode '{mode.ToString().ToLowerInvariant()}', which "
                + "reports no exception the caller could filter. Use 'always' or 'user_unhandled'.",
                nameof(exceptionTypes));
        }

        foreach (var exceptionType in exceptionTypes)
        {
            if (string.IsNullOrWhiteSpace(exceptionType))
                throw new ArgumentException("An exception type cannot be empty", nameof(exceptionTypes));

            if (exceptionType.Contains(',', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Exception types are passed one per entry, not comma-separated: {exceptionType}",
                    nameof(exceptionTypes));
            }

            if (exceptionType.TrimStart().StartsWith('!'))
            {
                throw new ArgumentException(
                    "Use types_are_excluded to invert the list rather than a leading '!': "
                    + exceptionType,
                    nameof(exceptionTypes));
            }
        }
    }

    public static void ValidateExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression cannot be empty", nameof(expression));
    }
}
