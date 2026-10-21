using DotnetDebugger.Mcp.Configuration;

namespace DotnetDebugger.Mcp.Mobile;

/// <summary>
/// Where the two halves of the remote CoreCLR debugger live on this machine.
///
/// Debugging a .NET app on a phone takes a native library on each side: a target library that goes
/// inside the app and opens the connection, and a host library the debugger loads to speak to it.
/// Both come from a clrdbg release and ship beside the server, laid out as that release carries
/// them - remote-host/ holding one directory per RID, remote-target/ one per platform - so nothing
/// has to be set to debug a mobile app.
///
/// Either half can still be pointed elsewhere, for a copy built locally - with tracing compiled in,
/// say. The two are overridden separately because they are built separately.
///
/// Wherever a half comes from, it must not hold Visual Studio's own remote debugger. clrdbg can
/// still drive that one - it switches to it when it sees one - but it is Microsoft's, licensed for
/// Visual Studio's products, and this server keeps that path unreachable rather than rely on it.
/// </summary>
public sealed record RemoteCoreclrLibraries(string HostDirectory, string TargetDirectory)
{
    private const string HostFolder = "remote-host";
    private const string TargetFolder = "remote-target";

    /// <summary>The copy that ships with the server, beside its own assembly</summary>
    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "remote-coreclr");

    /// <summary>
    /// The MSBuild file that puts the target library into the app. It travels with this server
    /// rather than being written out at build time, so a user can read what is being added to their
    /// build before it runs.
    /// </summary>
    public static string TargetsFile
    {
        get
        {
            var path = Path.Combine(
                AppContext.BaseDirectory, "Resources", "CopyRemoteCoreclrTargetLibrary.targets");

            return File.Exists(path)
                ? path
                : throw new FileNotFoundException(
                    $"The build support file is missing at {path}. The installation is incomplete.", path);
        }
    }

    /// <summary>
    /// Resolves each half from what the server was configured with, falling back to the copy that
    /// ships with it. <paramref name="bundledDirectory"/> stands in for that copy; it defaults to
    /// <see cref="BundledDirectory"/>.
    /// </summary>
    public static RemoteCoreclrLibraries Resolve(ServerConfiguration configuration, string? bundledDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var bundled = bundledDirectory ?? BundledDirectory;

        var host = Configured(configuration.RemoteCoreclrHostDirectory, "DOTNET_DEBUGGER_MCP_REMOTE_CORECLR_HOST")
            ?? Bundled(bundled, HostFolder);
        var target = Configured(configuration.RemoteCoreclrTargetDirectory, "DOTNET_DEBUGGER_MCP_REMOTE_CORECLR_TARGET")
            ?? Bundled(bundled, TargetFolder);

        RefuseVisualStudioDebugger(host);
        RefuseVisualStudioDebugger(target);

        return new RemoteCoreclrLibraries(host, target);
    }

    /// <summary>
    /// Refuses a directory clrdbg would take for Visual Studio's remote debugger. The rule is
    /// clrdbg's own, copied from MobileDebugAgent: any file anywhere below it with "vsdbg" in its
    /// name, in any case. Checking less than clrdbg does would leave the path open, so when the
    /// submodule moves, this has to be compared with that rule again.
    /// </summary>
    private static void RefuseVisualStudioDebugger(string directory)
    {
        var found = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .FirstOrDefault(it => Path.GetFileName(it).Contains("vsdbg", StringComparison.OrdinalIgnoreCase));

        if (found != null)
            throw new InvalidOperationException(
                $"{directory} holds Visual Studio's remote debugger ({found}), which this server does "
                + "not use. Use the remote-host/ and remote-target/ of a clrdbg release's "
                + "RemoteCoreClrLibraries.zip instead.");
    }

    private static string? Configured(string? path, string what)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var full = Path.GetFullPath(path);

        return Directory.Exists(full)
            ? full
            : throw new DirectoryNotFoundException($"{what} points at {full}, which does not exist.");
    }

    private static string Bundled(string root, string folder)
    {
        var full = Path.Combine(Path.GetFullPath(root), folder);

        return Directory.Exists(full)
            ? full
            : throw new DirectoryNotFoundException(
                $"The remote CoreCLR debugging libraries are missing at {full}. They ship with this "
                + "server, so the installation is incomplete - reinstall it, or point "
                + "DOTNET_DEBUGGER_MCP_REMOTE_CORECLR_HOST and DOTNET_DEBUGGER_MCP_REMOTE_CORECLR_TARGET at a copy of "
                + "remote-host/ and remote-target/ from a clrdbg release's RemoteCoreClrLibraries.zip.");
    }
}
