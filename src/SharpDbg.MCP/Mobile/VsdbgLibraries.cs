using SharpDbg.MCP.Configuration;

namespace SharpDbg.MCP.Mobile;

/// <summary>
/// Where the two halves of the remote CoreCLR debugger live on this machine.
///
/// Debugging a .NET app on a phone takes a native library on each side: a target library that goes
/// inside the app and opens the connection, and a host library the debugger loads to speak to it.
/// Both are part of Visual Studio's debugger, both are licensed in a way that forbids shipping them
/// with anything else, and so neither is in this package. The user points the server at a copy.
///
/// The expected layout is the one they are distributed in - a directory holding
/// VsdbgRemoteCoreclrHost and VsdbgRemoteCoreclrTarget - so the usual case is one path rather than
/// two. The two can still be given separately for a machine that keeps them apart.
/// </summary>
public sealed record VsdbgLibraries(string HostDirectory, string TargetDirectory)
{
    private const string HostFolder = "VsdbgRemoteCoreclrHost";
    private const string TargetFolder = "VsdbgRemoteCoreclrTarget";

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
    /// Resolves the libraries from what the caller passed, falling back to what the server was
    /// configured with. The failure is the interesting case: it is the one thing a user has to
    /// supply by hand, so the message says exactly what is expected and where to put it.
    /// </summary>
    public static VsdbgLibraries Resolve(ServerConfiguration configuration, string? path)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.IsNullOrWhiteSpace(path))
            return FromRoot(path, "The path passed as vsdbg_libraries_path");

        if (!string.IsNullOrWhiteSpace(configuration.RemoteCoreclrHostDirectory)
            && !string.IsNullOrWhiteSpace(configuration.RemoteCoreclrTargetDirectory))
        {
            return new VsdbgLibraries(
                Existing(configuration.RemoteCoreclrHostDirectory, "SHARPDBG_REMOTE_CORECLR_HOST"),
                Existing(configuration.RemoteCoreclrTargetDirectory, "SHARPDBG_REMOTE_CORECLR_TARGET"));
        }

        if (!string.IsNullOrWhiteSpace(configuration.VsdbgLibrariesDirectory))
            return FromRoot(configuration.VsdbgLibrariesDirectory, "SHARPDBG_VSDBG_LIBRARIES");

        throw new InvalidOperationException(
            "Debugging a mobile app needs the remote CoreCLR debugger libraries, which are part of "
            + "Visual Studio and cannot be shipped with this server. Point it at a copy: set "
            + "SHARPDBG_VSDBG_LIBRARIES to a directory containing "
            + $"{HostFolder}/ and {TargetFolder}/, or pass that directory as vsdbg_libraries_path. "
            + "Set SHARPDBG_REMOTE_CORECLR_HOST and SHARPDBG_REMOTE_CORECLR_TARGET instead if the "
            + "two are not kept together.");
    }

    private static VsdbgLibraries FromRoot(string root, string what)
    {
        var full = Path.GetFullPath(root);

        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"{what} points at {full}, which does not exist.");

        var host = Path.Combine(full, HostFolder);
        var target = Path.Combine(full, TargetFolder);

        // A user who has pointed one level too deep - straight at the target libraries - gets told
        // so, since that is the mistake the layout invites
        if (!Directory.Exists(host) || !Directory.Exists(target))
        {
            throw new DirectoryNotFoundException(
                $"{what} points at {full}, which does not contain {HostFolder}/ and {TargetFolder}/. "
                + "It should be the directory holding both.");
        }

        return new VsdbgLibraries(host, target);
    }

    private static string Existing(string path, string what)
    {
        var full = Path.GetFullPath(path);

        return Directory.Exists(full)
            ? full
            : throw new DirectoryNotFoundException($"{what} points at {full}, which does not exist.");
    }
}
