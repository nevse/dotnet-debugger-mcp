using SharpDbg.MCP.Configuration;
using SharpDbg.MCP.Mobile;

namespace SharpDbg.MCP.Tests.Mobile;

/// <summary>
/// The libraries ship with the server, so the cases worth being precise about are that the bundled
/// copy is really there, that each half can be pointed elsewhere on its own, and what is said when
/// either is missing.
/// </summary>
[TestClass]
public class RemoteCoreclrLibrariesTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), $"remote-coreclr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "remote-host"));
        Directory.CreateDirectory(Path.Combine(_root, "remote-target"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Resolve_NothingConfigured_UsesTheBundledCopy()
    {
        var libraries = RemoteCoreclrLibraries.Resolve(new ServerConfiguration(), _root);

        Assert.AreEqual(Path.Combine(_root, "remote-host"), libraries.HostDirectory);
        Assert.AreEqual(Path.Combine(_root, "remote-target"), libraries.TargetDirectory);
    }

    [TestMethod]
    public void Resolve_HalvesConfigured_UsesThemAsGiven()
    {
        var host = Directory.CreateDirectory(Path.Combine(_root, "my-host")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "my-target")).FullName;

        var configuration = new ServerConfiguration
        {
            RemoteCoreclrHostDirectory = host,
            RemoteCoreclrTargetDirectory = target
        };

        var libraries = RemoteCoreclrLibraries.Resolve(configuration, _root);

        Assert.AreEqual(host, libraries.HostDirectory);
        Assert.AreEqual(target, libraries.TargetDirectory);
    }

    /// <summary>
    /// The two are built separately, so a locally built target library - one with tracing compiled
    /// in - is used with the bundled host rather than requiring a second copy of it
    /// </summary>
    [TestMethod]
    public void Resolve_OneHalfConfigured_TakesTheOtherFromTheBundledCopy()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "my-target")).FullName;

        var configuration = new ServerConfiguration { RemoteCoreclrTargetDirectory = target };

        var libraries = RemoteCoreclrLibraries.Resolve(configuration, _root);

        Assert.AreEqual(Path.Combine(_root, "remote-host"), libraries.HostDirectory);
        Assert.AreEqual(target, libraries.TargetDirectory);
    }

    [TestMethod]
    public void Resolve_ConfiguredHalfMissing_NamesTheVariable()
    {
        var missing = Path.Combine(_root, "nowhere");

        var configuration = new ServerConfiguration { RemoteCoreclrHostDirectory = missing };

        var error = Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => RemoteCoreclrLibraries.Resolve(configuration, _root));

        StringAssert.Contains(error.Message, "SHARPDBG_REMOTE_CORECLR_HOST");
        StringAssert.Contains(error.Message, missing);
    }

    [TestMethod]
    public void Resolve_BundledCopyMissing_SaysTheInstallationIsIncomplete()
    {
        Directory.Delete(Path.Combine(_root, "remote-target"));

        var error = Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => RemoteCoreclrLibraries.Resolve(new ServerConfiguration(), _root));

        StringAssert.Contains(error.Message, Path.Combine(_root, "remote-target"));
        StringAssert.Contains(error.Message, "installation is incomplete");
    }

    /// <summary>
    /// clrdbg switches to Visual Studio's remote debugger when it finds a file with "vsdbg" in its
    /// name anywhere below either directory, in any case. Each of these is a name that rule matches,
    /// at a depth it reaches, so the server must refuse it before clrdbg ever sees the directory.
    /// </summary>
    [TestMethod]
    [DataRow("libvsdbgremotecoreclrtarget.so")]
    [DataRow("android/arm64-v8a/libvsdbgremotecoreclrtarget.so")]
    [DataRow("osx-arm64/libremotemscordbihost.dylib.VSDBG")]
    [DataRow("win-x64/VsDbg.dll")]
    public void Resolve_ConfiguredHalfHoldsVisualStudioDebugger_IsRefused(string file)
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "vs-target")).FullName;
        // GetFullPath turns the rows' '/' into '\' on Windows, which is how the refusal names the file
        var path = Path.GetFullPath(Path.Combine(target, file));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);

        var configuration = new ServerConfiguration { RemoteCoreclrTargetDirectory = target };

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => RemoteCoreclrLibraries.Resolve(configuration, _root));

        StringAssert.Contains(error.Message, path);
    }

    /// <summary>
    /// The bundled copy is checked too, so a package that somehow carried Microsoft's libraries would
    /// fail here rather than be used
    /// </summary>
    [TestMethod]
    public void Resolve_BundledCopyHoldsVisualStudioDebugger_IsRefused()
    {
        var path = Path.Combine(_root, "remote-host", "osx-arm64", "vsdbg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => RemoteCoreclrLibraries.Resolve(new ServerConfiguration(), _root));

        StringAssert.Contains(error.Message, path);
    }

    /// <summary>
    /// Only a file name counts, as in clrdbg: a directory merely called that, somewhere above or
    /// below, is not Visual Studio's debugger and must not stop a locally built copy from working
    /// </summary>
    [TestMethod]
    public void Resolve_DirectoryNamedVsdbg_IsNotRefused()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "vsdbg-experiments", "target")).FullName;
        Directory.CreateDirectory(Path.Combine(target, "vsdbg"));
        File.WriteAllText(Path.Combine(target, "libremotecoreclrtarget.so"), string.Empty);

        var configuration = new ServerConfiguration { RemoteCoreclrTargetDirectory = target };

        var libraries = RemoteCoreclrLibraries.Resolve(configuration, _root);

        Assert.AreEqual(target, libraries.TargetDirectory);
    }

    /// <summary>
    /// The build downloads the libraries and puts them beside the server. Checked against the real
    /// output rather than a stand-in, since a build that silently skipped them would otherwise only
    /// show up as a failed mobile launch.
    /// </summary>
    [TestMethod]
    public void BundledCopy_ShipsBesideTheServer()
    {
        var libraries = RemoteCoreclrLibraries.Resolve(new ServerConfiguration());

        Assert.IsTrue(
            File.Exists(Path.Combine(libraries.TargetDirectory, "android", "arm64-v8a", "libremotecoreclrtarget.so")),
            libraries.TargetDirectory);
        Assert.IsTrue(
            File.Exists(Path.Combine(libraries.TargetDirectory, "maccatalyst", "maccatalyst-arm64", "libremotecoreclrtarget.dylib")),
            libraries.TargetDirectory);
        Assert.IsTrue(
            Directory.EnumerateFiles(libraries.HostDirectory, "*remotecoreclrhost.*", SearchOption.AllDirectories).Any(),
            libraries.HostDirectory);
    }

    /// <summary>
    /// The build file is passed to somebody else's MSBuild by path, so it has to be on disk beside
    /// the server rather than embedded in it
    /// </summary>
    [TestMethod]
    public void TargetsFile_ShipsBesideTheServer()
    {
        Assert.IsTrue(File.Exists(RemoteCoreclrLibraries.TargetsFile), RemoteCoreclrLibraries.TargetsFile);
    }
}
