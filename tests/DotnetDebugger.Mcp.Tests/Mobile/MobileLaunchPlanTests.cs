using DotnetDebugger.Mcp.Mobile;

namespace DotnetDebugger.Mcp.Tests.Mobile;

/// <summary>
/// What the debugger is told about a device. Every field here changes what the adapter does -
/// whether it boots an emulator, installs over adb or over the Apple device tools, and where it
/// looks for the app's assemblies - so the mapping is pinned rather than left to be discovered by
/// a launch that quietly does the wrong thing.
/// </summary>
[TestClass]
public class MobileLaunchPlanTests
{
    private static readonly RemoteCoreclrLibraries Libraries = new("/libs/host", "/libs/target");

    private static MobileLaunchPlan PlanFor(MobileDevice device) => new(
        ProjectPath: "/src/App/App.csproj",
        Configuration: "Debug",
        TargetFramework: "net11.0-" + device.Platform,
        Device: device,
        RuntimeIdentifier: device.RuntimeIdentifier,
        AdbSerial: device.AdbSerial,
        Program: "/src/App/bin/app",
        AssetsPath: "/src/App/bin/assets",
        Libraries: Libraries);

    /// <summary>
    /// An emulator is named by its AVD rather than by a serial, and marked as not a device, which
    /// is what makes the debugger boot it before installing anything
    /// </summary>
    [TestMethod]
    public void ToLaunchOptions_AndroidEmulator_NamesTheAvdAndIsNotADevice()
    {
        var device = new MobileDevice(
            "Pixel_7_API_35", "Pixel_7_API_35", MobilePlatforms.Android, null, "android-35", true, false);

        var options = PlanFor(device).ToLaunchOptions(uninstallApp: false);

        Assert.AreEqual("Android", options.Platform);
        Assert.AreEqual("Pixel_7_API_35", options.Device);
        Assert.IsFalse(options.IsDevice);
        Assert.IsNull(options.RuntimeIdentifier);
    }

    [TestMethod]
    public void ToLaunchOptions_AndroidPhone_IsADevice()
    {
        var device = new MobileDevice(
            "39021FDJH00", "Pixel 7", MobilePlatforms.Android, null, "android-35", false, true);

        var options = PlanFor(device).ToLaunchOptions(uninstallApp: true);

        Assert.AreEqual("39021FDJH00", options.Device);
        Assert.IsTrue(options.IsDevice);
        Assert.IsTrue(options.UninstallApp);
    }

    [TestMethod]
    public void ToLaunchOptions_IosSimulator_CarriesTheUdidAndSimulatorRuntime()
    {
        var device = new MobileDevice(
            "95B7B58D", "iPhone 17 Pro", MobilePlatforms.IOS, "iossimulator-arm64", "iOS 26.4", true, false);

        var options = PlanFor(device).ToLaunchOptions(uninstallApp: false);

        Assert.AreEqual("IOS", options.Platform);
        Assert.AreEqual("95B7B58D", options.Device);
        Assert.AreEqual("iossimulator-arm64", options.RuntimeIdentifier);
        Assert.IsFalse(options.IsDevice);
    }

    [TestMethod]
    public void ToLaunchOptions_IosPhone_IsADevice()
    {
        var device = new MobileDevice(
            "00008101-0005", "Bubu", MobilePlatforms.IOS, "ios-arm64", "iOS 26.2", false, true);

        var options = PlanFor(device).ToLaunchOptions(uninstallApp: false);

        Assert.IsTrue(options.IsDevice);
        Assert.AreEqual("ios-arm64", options.RuntimeIdentifier);
    }

    /// <summary>
    /// Mac Catalyst runs here, so there is no device to name and nothing to install onto. The
    /// debugger starts the bundle itself and would try to use a device name if it were given one.
    /// </summary>
    [TestMethod]
    public void ToLaunchOptions_MacCatalyst_NamesNoDevice()
    {
        var device = new MobileDevice(
            "maccatalyst", "This Mac", MobilePlatforms.MacCatalyst, "maccatalyst-arm64", "26.6", false, true);

        var options = PlanFor(device).ToLaunchOptions(uninstallApp: false);

        Assert.AreEqual("Maccatalyst", options.Platform);
        Assert.IsNull(options.Device);
        Assert.IsFalse(options.IsDevice);
    }

    [TestMethod]
    public void ToLaunchOptions_CarriesBothLibraryDirectories()
    {
        var device = new MobileDevice(
            "maccatalyst", "This Mac", MobilePlatforms.MacCatalyst, "maccatalyst-arm64", "26.6", false, true);

        var options = PlanFor(device).ToLaunchOptions(uninstallApp: false);

        Assert.AreEqual("/libs/host", options.RemoteCoreclrHost);
        Assert.AreEqual("/libs/target", options.RemoteCoreclrTarget);
        Assert.AreEqual("/src/App/bin/assets", options.AssetsPath);
    }

    /// <summary>
    /// Android is built for every ABI at once, so it has no runtime identifier - which is also what
    /// the debugger reads as "this app carries libraries for more than one architecture"
    /// </summary>
    [TestMethod]
    public void RuntimeIdentifierFor_Android_IsNone()
    {
        Assert.IsNull(MobilePlatforms.RuntimeIdentifierFor(MobilePlatforms.Android, isEmulator: true));
        Assert.IsNull(MobilePlatforms.RuntimeIdentifierFor(MobilePlatforms.Android, isEmulator: false));
    }

    [TestMethod]
    public void RuntimeIdentifierFor_IosDevice_IsAlwaysArm64()
    {
        Assert.AreEqual("ios-arm64", MobilePlatforms.RuntimeIdentifierFor(MobilePlatforms.IOS, isEmulator: false));
    }

    /// <summary>
    /// A simulator runs the host's architecture, not the phone's, which is why it has a runtime
    /// identifier of its own rather than sharing the device's
    /// </summary>
    [TestMethod]
    public void RuntimeIdentifierFor_IosSimulator_FollowsTheHostArchitecture()
    {
        var runtime = MobilePlatforms.RuntimeIdentifierFor(MobilePlatforms.IOS, isEmulator: true);

        StringAssert.StartsWith(runtime, "iossimulator-");
        Assert.AreEqual(
            System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                is System.Runtime.InteropServices.Architecture.Arm64
                    ? "iossimulator-arm64"
                    : "iossimulator-x64",
            runtime);
    }

    [TestMethod]
    public void DebugTarget_UnknownPlatform_Throws()
    {
        var device = new MobileDevice("x", "x", "windows", null, null, false, true);

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = device.DebugTarget);
    }

    [TestMethod]
    [DataRow("Debug")]
    [DataRow("debug")]
    [DataRow("DEBUG")]
    public void ValidateConfiguration_Debug_IsAcceptedInAnyCase(string configuration)
    {
        Assert.AreEqual("Debug", MobileLaunchPlan.ValidateConfiguration(configuration));
    }

    /// <summary>
    /// The build puts the remote debugging library inside the app, so anything a release pipeline
    /// could ship is refused - including names that are not "Release", since a project's shipping
    /// configuration can be called anything
    /// </summary>
    [TestMethod]
    [DataRow("Release")]
    [DataRow("AppStore")]
    [DataRow("Debug-Staging")]
    public void ValidateConfiguration_AnythingButDebug_IsRefused(string configuration)
    {
        var error = Assert.ThrowsExactly<ArgumentException>(
            () => MobileLaunchPlan.ValidateConfiguration(configuration));

        Assert.AreEqual("configuration", error.ParamName);
        StringAssert.Contains(error.Message, configuration);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ValidateConfiguration_Empty_IsRefused(string configuration)
    {
        Assert.ThrowsExactly<ArgumentException>(() => MobileLaunchPlan.ValidateConfiguration(configuration));
    }

    /// <summary>
    /// The refusal comes before MSBuild is asked anything about the project, so a Release request
    /// never evaluates it at all - here the project does not even exist
    /// </summary>
    [TestMethod]
    public void Create_ReleaseConfiguration_IsRefusedBeforeTheProjectIsRead()
    {
        var device = new MobileDevice("Pixel", "Pixel", MobilePlatforms.Android, null, null, true, true);

        var error = Assert.ThrowsExactly<ArgumentException>(() => MobileLaunchPlan.Create(
            "/nowhere/App.csproj", device, "Release", "net10.0-android", Libraries, TimeSpan.FromSeconds(1)));

        Assert.AreEqual("configuration", error.ParamName);
    }
}
