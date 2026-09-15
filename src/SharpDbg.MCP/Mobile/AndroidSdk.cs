namespace SharpDbg.MCP.Mobile;

/// <summary>
/// Finds the Android SDK and the two tools this server needs out of it: adb, to see what is
/// connected, and the AVD directory, to see what emulators exist.
///
/// The search order is the debugger's own, and deliberately so - the build is handed
/// AndroidSdkDirectory explicitly, and the debugger finds adb for itself when it installs and
/// launches the app. Looking in different places would let the two disagree about which SDK is in
/// use, which shows up as an app installed on a device the debugger cannot then see.
/// </summary>
internal static class AndroidSdk
{
    /// <summary>
    /// The SDK root, or null when there is none. Not cached: an SDK installed while the server is
    /// running is worth picking up, and this costs a handful of directory probes.
    /// </summary>
    public static string? Directory()
    {
        foreach (var variable in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var configured = Environment.GetEnvironmentVariable(variable);

            if (!string.IsNullOrEmpty(configured) && System.IO.Directory.Exists(configured))
                return configured;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Combine(home, "AppData", "Local", "Android", "Sdk")]
            : OperatingSystem.IsMacOS()
                ? [Path.Combine(home, "Library", "Android", "sdk"),
                   Path.Combine(home, "Library", "Developer", "Xamarin", "android-sdk-macosx")]
                : [Path.Combine(home, "Android", "Sdk")];

        return candidates.FirstOrDefault(System.IO.Directory.Exists);
    }

    /// <summary>The adb binary, or null when the SDK has no platform-tools installed</summary>
    public static string? AdbPath()
    {
        var sdk = Directory();

        if (sdk == null)
            return null;

        var adb = Path.Combine(sdk, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");

        return File.Exists(adb) ? adb : null;
    }

    /// <summary>
    /// Where the emulator keeps its virtual devices: one .ini per AVD, named after it. This is the
    /// only way to see an emulator that is not running - adb shows the booted ones and nothing else.
    /// </summary>
    public static string AvdHome()
    {
        var configured = Environment.GetEnvironmentVariable("ANDROID_AVD_HOME");

        if (!string.IsNullOrEmpty(configured) && System.IO.Directory.Exists(configured))
            return configured;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd");
    }

    /// <summary>
    /// Says what is wrong when adb cannot be found, in the terms the caller can act on. Everything
    /// Android needs it, so this is the one explanation rather than one per call site.
    /// </summary>
    public static string MissingAdbMessage() =>
        Directory() == null
            ? "The Android SDK was not found. Set ANDROID_HOME or ANDROID_SDK_ROOT to point at it."
            : $"The Android SDK at {Directory()} has no platform-tools/adb. Install the "
                + "'Android SDK Platform-Tools' package from the SDK manager.";
}
