namespace DotnetDebugger.Mcp.Tests.Integration;

/// <summary>
/// Locates the debuggee assembly and its source. The test app is referenced with
/// ReferenceOutputAssembly=false, so it is built alongside the tests but not copied next to them.
/// </summary>
internal static class TestPaths
{
    private const string TestAppName = "DotnetDebugger.Mcp.TestApp";

    /// <summary>
    /// What the test app builds as. Kept free of "dotnet" so its apphost is a process name that only
    /// a real check, not a match on the name, recognises as .NET - see ProcessDiscoveryTests.
    /// </summary>
    private const string TestAppAssemblyName = "Debuggee";

    /// <summary>
    /// The name the debuggee gives its main thread. Repeated rather than shared, because the test
    /// app is not linked here - if this stops matching Program.cs, the thread name test says so.
    /// </summary>
    public const string DebuggeeMainThreadName = "debuggee-main";

    private static readonly Lazy<string> _repositoryRoot = new(FindRepositoryRoot);

    public static string TestAppAssembly => Path.Combine(
        _repositoryRoot.Value, "tests", TestAppName, "bin", Configuration, TargetFramework, $"{TestAppAssemblyName}.dll");

    public static string TestAppSource => Path.Combine(
        _repositoryRoot.Value, "tests", TestAppName, "Program.cs");

    /// <summary>
    /// The apphost built next to the assembly. Launching it goes down a different path in SharpDbg
    /// than launching the .dll, which is handed to the dotnet muxer instead.
    /// </summary>
    public static string TestAppExecutable => Path.Combine(
        _repositoryRoot.Value, "tests", TestAppName, "bin", Configuration, TargetFramework,
        OperatingSystem.IsWindows() ? $"{TestAppAssemblyName}.exe" : TestAppAssemblyName);

    /// <summary>
    /// Build configuration of the running test assembly (Debug/Release)
    /// </summary>
    private static string Configuration => Path.GetFileName(Path.GetDirectoryName(BaseDirectory)!);

    private static string TargetFramework => Path.GetFileName(BaseDirectory);

    private static string BaseDirectory =>
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Finds the 1-based line number carrying the given marker comment in the debuggee source.
    /// Keeps tests from breaking whenever the test app is edited.
    /// </summary>
    public static int FindMarkerLine(string marker)
    {
        var lines = File.ReadAllLines(TestAppSource);

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(marker, StringComparison.Ordinal))
                return i + 1;
        }

        throw new InvalidOperationException($"Marker '{marker}' not found in {TestAppSource}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DotnetDebugger.Mcp.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate the repository root above {BaseDirectory}");
    }
}
