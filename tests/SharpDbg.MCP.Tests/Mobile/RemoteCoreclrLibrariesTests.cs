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
