using SharpDbg.MCP.Configuration;
using SharpDbg.MCP.Mobile;

namespace SharpDbg.MCP.Tests.Mobile;

/// <summary>
/// The one thing a user has to supply by hand, and so the one worth being precise about: every way
/// of pointing at the libraries, and what is said when the pointer is wrong.
/// </summary>
[TestClass]
public class VsdbgLibrariesTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), $"vsdbg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "VsdbgRemoteCoreclrHost"));
        Directory.CreateDirectory(Path.Combine(_root, "VsdbgRemoteCoreclrTarget"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Resolve_PathArgument_SplitsIntoHostAndTarget()
    {
        var libraries = VsdbgLibraries.Resolve(new ServerConfiguration(), _root);

        Assert.AreEqual(Path.Combine(_root, "VsdbgRemoteCoreclrHost"), libraries.HostDirectory);
        Assert.AreEqual(Path.Combine(_root, "VsdbgRemoteCoreclrTarget"), libraries.TargetDirectory);
    }

    [TestMethod]
    public void Resolve_ConfiguredRoot_SplitsIntoHostAndTarget()
    {
        var configuration = new ServerConfiguration { VsdbgLibrariesDirectory = _root };

        var libraries = VsdbgLibraries.Resolve(configuration, null);

        Assert.AreEqual(Path.Combine(_root, "VsdbgRemoteCoreclrTarget"), libraries.TargetDirectory);
    }

    /// <summary>
    /// The argument wins over the configuration, so a caller debugging against a second copy does
    /// not have to restart the server with a different environment
    /// </summary>
    [TestMethod]
    public void Resolve_PathArgument_OverridesConfiguration()
    {
        var configuration = new ServerConfiguration
        {
            VsdbgLibrariesDirectory = Path.Combine(_root, "does-not-exist")
        };

        var libraries = VsdbgLibraries.Resolve(configuration, _root);

        Assert.AreEqual(Path.Combine(_root, "VsdbgRemoteCoreclrHost"), libraries.HostDirectory);
    }

    [TestMethod]
    public void Resolve_HalvesConfiguredSeparately_UsesThemAsGiven()
    {
        var host = Path.Combine(_root, "VsdbgRemoteCoreclrHost");
        var target = Path.Combine(_root, "VsdbgRemoteCoreclrTarget");

        var configuration = new ServerConfiguration
        {
            RemoteCoreclrHostDirectory = host,
            RemoteCoreclrTargetDirectory = target
        };

        var libraries = VsdbgLibraries.Resolve(configuration, null);

        Assert.AreEqual(host, libraries.HostDirectory);
        Assert.AreEqual(target, libraries.TargetDirectory);
    }

    [TestMethod]
    public void Resolve_NothingConfigured_ExplainsWhatToSet()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => VsdbgLibraries.Resolve(new ServerConfiguration(), null));

        StringAssert.Contains(error.Message, "SHARPDBG_VSDBG_LIBRARIES");
        StringAssert.Contains(error.Message, "vsdbg_libraries_path");
    }

    /// <summary>
    /// Pointing one level too deep - straight at the target libraries - is the mistake the layout
    /// invites, and the message has to name the two directories that were expected
    /// </summary>
    [TestMethod]
    public void Resolve_RootWithoutTheTwoFolders_NamesThem()
    {
        var error = Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => VsdbgLibraries.Resolve(
                new ServerConfiguration(), Path.Combine(_root, "VsdbgRemoteCoreclrTarget")));

        StringAssert.Contains(error.Message, "VsdbgRemoteCoreclrHost");
        StringAssert.Contains(error.Message, "VsdbgRemoteCoreclrTarget");
    }

    [TestMethod]
    public void Resolve_MissingRoot_SaysSo()
    {
        var missing = Path.Combine(_root, "nowhere");

        var error = Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => VsdbgLibraries.Resolve(new ServerConfiguration(), missing));

        StringAssert.Contains(error.Message, missing);
    }

    /// <summary>
    /// The build file is passed to somebody else's MSBuild by path, so it has to be on disk beside
    /// the server rather than embedded in it
    /// </summary>
    [TestMethod]
    public void TargetsFile_ShipsBesideTheServer()
    {
        Assert.IsTrue(File.Exists(VsdbgLibraries.TargetsFile), VsdbgLibraries.TargetsFile);
    }
}
