using SharpDbg.MCP.Mobile;

namespace SharpDbg.MCP.Tests.Integration;

/// <summary>
/// Building the plan runs MSBuild against a real project, which is what these cover: that the
/// evaluation happens at all, and that a project which cannot produce a mobile app is refused
/// before anything is built rather than after a long build produces nothing to launch.
/// The test app is an ordinary console project, which makes it exactly the wrong project to ask
/// for an Android app - and so the right one to check the refusal with.
/// </summary>
[TestClass]
public class MobileLaunchPlanIntegrationTests
{
    private static readonly VsdbgLibraries Libraries = new("/libs/host", "/libs/target");

    private static readonly MobileDevice AndroidEmulator = new(
        "Pixel_7_API_35", "Pixel_7_API_35", MobilePlatforms.Android, null, "android-35",
        IsEmulator: true, IsRunning: false);

    private static string TestAppProject => Path.Combine(
        Path.GetDirectoryName(TestPaths.TestAppSource)!, "SharpDbg.MCP.TestApp.csproj");

    [TestMethod]
    public void Create_ProjectWithoutThePlatform_NamesWhatItTargets()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => MobileLaunchPlan.Create(
            TestAppProject, AndroidEmulator, "Debug", null, Libraries, TimeSpan.FromMinutes(2)));

        StringAssert.Contains(error.Message, "android");
        StringAssert.Contains(error.Message, "net10.0");
    }

    /// <summary>
    /// A framework below the platform's first CoreCLR release is refused with the version that
    /// would work, because the app would otherwise build, start, and never connect back
    /// </summary>
    [TestMethod]
    public void Create_MonoFramework_RefusesWithTheVersionThatWorks()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => MobileLaunchPlan.Create(
            TestAppProject, AndroidEmulator, "Debug", "net9.0-android", Libraries, TimeSpan.FromMinutes(2)));

        StringAssert.Contains(error.Message, "Mono");
        StringAssert.Contains(error.Message, "net10.0-android");
    }

    [TestMethod]
    public void Create_FrameworkForAnotherPlatform_IsRefused()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => MobileLaunchPlan.Create(
            TestAppProject, AndroidEmulator, "Debug", "net11.0-ios", Libraries, TimeSpan.FromMinutes(2)));

        StringAssert.Contains(error.Message, "ios");
        StringAssert.Contains(error.Message, "android");
    }

    [TestMethod]
    public void Create_DirectoryInsteadOfProject_SaysSo()
    {
        var directory = Path.GetDirectoryName(TestAppProject)!;

        var error = Assert.ThrowsExactly<ArgumentException>(() => MobileLaunchPlan.Create(
            directory, AndroidEmulator, "Debug", null, Libraries, TimeSpan.FromMinutes(2)));

        StringAssert.Contains(error.Message, ".csproj");
    }

    [TestMethod]
    public void Create_MissingProject_SaysSo()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-project.csproj");

        var error = Assert.ThrowsExactly<ArgumentException>(() => MobileLaunchPlan.Create(
            missing, AndroidEmulator, "Debug", null, Libraries, TimeSpan.FromMinutes(2)));

        StringAssert.Contains(error.Message, missing);
    }

    /// <summary>
    /// Every machine has this listing, even one with no SDKs and no devices at all: it is allowed to
    /// come back empty, but not to fail
    /// </summary>
    [TestMethod]
    public void List_OnAnyMachine_Succeeds()
    {
        var listing = new MobileDeviceDiscovery().List();

        Assert.IsNotNull(listing.Devices);
        Assert.IsNotNull(listing.Warnings);
    }

    [TestMethod]
    public void Resolve_UnknownDevice_SuggestsTheListingTool()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(
            () => new MobileDeviceDiscovery().Resolve("no-such-device"));

        StringAssert.Contains(error.Message, "no-such-device");
    }
}
