using System.Runtime.InteropServices;

namespace SharpDbg.MCP.Mobile;

/// <summary>
/// A device or emulator a MAUI app can be run on. Id is what every other tool takes to name it, and
/// is also what the debugger is given: an AVD name for an Android emulator, an adb serial for an
/// Android phone, a UDID for anything Apple. The AVD name works whether or not the emulator is
/// booted, which is why it is preferred over the serial - the debugger boots a cold one itself.
/// </summary>
public sealed record MobileDevice(
    string Id,
    string Name,
    string Platform,
    string? RuntimeIdentifier,
    string? OsVersion,
    bool IsEmulator,
    bool IsRunning)
{
    /// <summary>
    /// The adb serial of a running Android device, which is not always its id: a booted emulator is
    /// identified by the AVD it came from and answers to a serial the emulator assigned it. Null for
    /// anything not connected through adb, and for an emulator that is not running yet.
    /// </summary>
    public string? AdbSerial { get; init; }

    /// <summary>The debugger's own name for the platform, as its launch configuration spells it</summary>
    public string DebugTarget => Platform switch
    {
        MobilePlatforms.Android => "Android",
        MobilePlatforms.IOS => "IOS",
        MobilePlatforms.MacCatalyst => "Maccatalyst",
        _ => throw new InvalidOperationException($"Unknown platform: {Platform}")
    };
}

/// <summary>
/// The platform names, which are also the suffixes of the target frameworks they are built for -
/// 'net10.0-android' - so one set of constants covers both.
/// </summary>
public static class MobilePlatforms
{
    public const string Android = "android";
    public const string IOS = "ios";
    public const string MacCatalyst = "maccatalyst";

    /// <summary>
    /// The runtime identifier a device is built for, or null for Android, which has none: an
    /// Android app is built for every ABI its project lists at once, and the debugger is told so by
    /// being given no runtime identifier at all.
    /// </summary>
    public static string? RuntimeIdentifierFor(string platform, bool isEmulator)
    {
        var arm64 = RuntimeInformation.OSArchitecture is Architecture.Arm64;

        return platform switch
        {
            Android => null,
            IOS => isEmulator
                ? (arm64 ? "iossimulator-arm64" : "iossimulator-x64")
                : "ios-arm64",
            MacCatalyst => arm64 ? "maccatalyst-arm64" : "maccatalyst-x64",
            _ => throw new ArgumentException($"Unknown platform: {platform}", nameof(platform))
        };
    }
}
