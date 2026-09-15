using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpDbg.MCP.Mobile;

/// <summary>
/// Finds the devices and emulators a MAUI app can be debugged on. Every source is optional and
/// asked for separately: a machine with no Android SDK still has simulators, a Linux machine has
/// neither, and a failure in one source must not hide the others - so each is guarded and what went
/// wrong is reported beside the devices rather than instead of them.
/// </summary>
public sealed partial class MobileDeviceDiscovery
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The devices found, and the reason for every source that could not be asked</summary>
    public sealed record Listing(IReadOnlyList<MobileDevice> Devices, IReadOnlyList<string> Warnings);

    public Listing List()
    {
        var devices = new List<MobileDevice>();
        var warnings = new List<string>();

        Collect(devices, warnings, "Android", AndroidDevices);

        if (OperatingSystem.IsMacOS())
        {
            Collect(devices, warnings, "iOS simulators", AppleSimulators);
            Collect(devices, warnings, "iOS devices", AppleDevices);
            devices.Add(MacCatalyst());
        }

        return new Listing(devices, warnings);
    }

    /// <summary>
    /// The device a tool call names. Matched on id first and on name second, because the id is what
    /// list_mobile_devices reports and what a caller should be passing, while a name is what a
    /// person types. An ambiguous name is refused rather than resolved to the first match - two
    /// simulators called "iPhone 16" on different iOS versions is the normal case, not a corner one.
    /// </summary>
    public MobileDevice Resolve(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("A device id is required. Use list_mobile_devices to see them.", nameof(deviceId));

        var listing = List();

        var byId = listing.Devices
            .Where(d => d.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byId.Count > 0)
            return byId[0];

        var byName = listing.Devices
            .Where(d => d.Name.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byName.Count == 1)
            return byName[0];

        if (byName.Count > 1)
        {
            throw new ArgumentException(
                $"'{deviceId}' matches {byName.Count} devices: "
                + string.Join(", ", byName.Select(d => $"{d.Id} ({d.OsVersion})"))
                + ". Pass one of those ids.",
                nameof(deviceId));
        }

        var trouble = listing.Warnings.Count == 0
            ? string.Empty
            : " " + string.Join(" ", listing.Warnings);

        throw new ArgumentException(
            $"There is no device '{deviceId}'. {Sample(listing.Devices)}{trouble}", nameof(deviceId));
    }

    /// <summary>
    /// A few ids to recognise the right one by, not all of them: a Mac with every simulator runtime
    /// installed has dozens, and an error message listing them all is one nobody reads.
    /// </summary>
    private static string Sample(IReadOnlyList<MobileDevice> devices)
    {
        const int Shown = 8;

        if (devices.Count == 0)
            return "No devices or emulators were found.";

        var ids = string.Join(", ", devices.Take(Shown).Select(d => d.Id));

        return devices.Count <= Shown
            ? $"Known ids: {ids}."
            : $"Known ids include {ids}; use list_mobile_devices to see all {devices.Count}.";
    }

    private static void Collect(
        List<MobileDevice> devices, List<string> warnings, string source, Func<IEnumerable<MobileDevice>> read)
    {
        try
        {
            devices.AddRange(read());
        }
        catch (Exception ex)
        {
            warnings.Add($"{source} could not be listed: {ex.Message}");
        }
    }

    /// <summary>
    /// Every AVD on the machine plus everything adb can see. The two overlap - a booted emulator is
    /// both - and the AVD entry wins, because its id is the AVD name, which the debugger can boot
    /// from cold. A booted emulator with no .ini of its own is still reported: it was created
    /// somewhere this does not look, and it is plainly usable.
    /// </summary>
    private static IEnumerable<MobileDevice> AndroidDevices()
    {
        var running = RunningAndroidDevices();
        var devices = new List<MobileDevice>();
        var avdHome = AndroidSdk.AvdHome();

        if (Directory.Exists(avdHome))
        {
            foreach (var ini in Directory.EnumerateFiles(avdHome, "*.ini").OrderBy(f => f, StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(ini);
                var booted = running.FirstOrDefault(d => d.AvdName == name);

                devices.Add(new MobileDevice(
                    Id: name,
                    Name: name,
                    Platform: MobilePlatforms.Android,
                    RuntimeIdentifier: null,
                    OsVersion: ReadIniField(ini, "target"),
                    IsEmulator: true,
                    IsRunning: booted != null) { AdbSerial = booted?.Serial });

                if (booted != null)
                    running.Remove(booted);
            }
        }

        foreach (var device in running)
        {
            var isEmulator = device.AvdName != null;

            devices.Add(new MobileDevice(
                Id: device.AvdName ?? device.Serial,
                Name: device.AvdName ?? device.Model ?? device.Serial,
                Platform: MobilePlatforms.Android,
                RuntimeIdentifier: null,
                OsVersion: device.SdkVersion is null ? null : $"android-{device.SdkVersion}",
                IsEmulator: isEmulator,
                IsRunning: true) { AdbSerial = device.Serial });
        }

        return devices;
    }

    private sealed record RunningAndroid(string Serial, string? AvdName, string? Model, string? SdkVersion);

    private static List<RunningAndroid> RunningAndroidDevices()
    {
        var adb = AndroidSdk.AdbPath();

        // Not an error: a machine with no Android SDK still has its AVD directory read, and one
        // with no SDK at all simply has no Android devices. The explanation is saved for the point
        // where a caller actually asks for an Android device.
        if (adb == null)
            return [];

        var result = ShellCommand.Run(adb, ["devices", "-l"], ToolTimeout);

        if (!result.Success)
            throw new InvalidOperationException($"adb devices failed: {result.Explain()}");

        var devices = new List<RunningAndroid>();

        foreach (var line in result.Output)
        {
            var columns = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            // Only devices in the "device" state are usable; "offline" and "unauthorized" are not,
            // and the header line has no second column at all
            if (columns.Length < 2 || !columns[1].Equals("device", StringComparison.OrdinalIgnoreCase))
                continue;

            var serial = columns[0];

            devices.Add(new RunningAndroid(
                serial,
                AvdName: serial.StartsWith("emulator-", StringComparison.Ordinal) ? AvdNameOf(adb, serial) : null,
                Model: Getprop(adb, serial, "ro.product.model"),
                SdkVersion: Getprop(adb, serial, "ro.build.version.sdk")));
        }

        return devices;
    }

    /// <summary>
    /// The AVD an emulator was booted from, which is the only thing tying a serial back to the
    /// .ini. The console command it uses answers "OK" on its own line after the name.
    /// </summary>
    private static string? AvdNameOf(string adb, string serial)
    {
        var result = ShellCommand.Run(adb, ["-s", serial, "emu", "avd", "name"], ToolTimeout);

        return result.Success
            ? result.Output.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && l.Trim() != "OK")?.Trim()
            : null;
    }

    private static string? Getprop(string adb, string serial, string property)
    {
        var result = ShellCommand.Run(adb, ["-s", serial, "shell", "getprop", property], ToolTimeout);

        return result.Success
            ? result.Output.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim()
            : null;
    }

    /// <summary>
    /// An AVD's .ini is a flat key=value file. Only 'target' is read from it, which is the API
    /// level the emulator runs - enough to tell two AVDs of the same device apart.
    /// </summary>
    private static string? ReadIniField(string path, string field)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var separator = line.IndexOf('=');

                if (separator > 0 && line[..separator].Trim().Equals(field, StringComparison.OrdinalIgnoreCase))
                    return line[(separator + 1)..].Trim();
            }
        }
        catch (IOException)
        {
            // A half-written or unreadable .ini costs the version, not the device
        }

        return null;
    }

    /// <summary>
    /// The iOS simulators, read as JSON rather than by scanning ~/Library/Developer/CoreSimulator:
    /// simctl is what owns that directory, it reports which runtime each device belongs to and
    /// whether the runtime is still installed, and it is the same tool the debugger boots them with.
    /// </summary>
    private static IEnumerable<MobileDevice> AppleSimulators()
    {
        var result = ShellCommand.Run("/usr/bin/xcrun", ["simctl", "list", "devices", "--json"], ToolTimeout);

        if (!result.Success)
            throw new InvalidOperationException($"xcrun simctl failed: {result.Explain()}");

        using var json = JsonDocument.Parse(result.OutputText);

        if (!json.RootElement.TryGetProperty("devices", out var runtimes))
            return [];

        var devices = new List<MobileDevice>();
        var runtimeIdentifier = MobilePlatforms.RuntimeIdentifierFor(MobilePlatforms.IOS, isEmulator: true);

        foreach (var runtime in runtimes.EnumerateObject())
        {
            // Everything Apple simulates lives under the same key - watchOS, tvOS, visionOS - and
            // only iOS is a MAUI target this debugger can attach to
            if (!runtime.Name.Contains("SimRuntime.iOS", StringComparison.Ordinal))
                continue;

            var osVersion = OsVersionOf(runtime.Name);

            foreach (var device in runtime.Value.EnumerateArray())
            {
                // A device whose runtime has been uninstalled is still listed, and cannot be booted
                if (device.TryGetProperty("isAvailable", out var available) && !available.GetBoolean())
                    continue;

                var udid = device.TryGetProperty("udid", out var id) ? id.GetString() : null;

                if (udid == null)
                    continue;

                devices.Add(new MobileDevice(
                    Id: udid,
                    Name: device.TryGetProperty("name", out var name) ? name.GetString() ?? udid : udid,
                    Platform: MobilePlatforms.IOS,
                    RuntimeIdentifier: runtimeIdentifier,
                    OsVersion: osVersion,
                    IsEmulator: true,
                    IsRunning: device.TryGetProperty("state", out var state)
                        && string.Equals(state.GetString(), "Booted", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return devices;
    }

    /// <summary>"com.apple.CoreSimulator.SimRuntime.iOS-18-2" is iOS 18.2</summary>
    private static string OsVersionOf(string runtimeKey)
    {
        var tokens = runtimeKey.Split('.').Last().Split('-');

        return tokens.Length > 1
            ? $"{tokens[0]} {string.Join('.', tokens.Skip(1))}"
            : tokens[0];
    }

    /// <summary>
    /// Physical iPhones and iPads. xctrace is the only listing that includes devices paired over the
    /// network, and it also lists the Mac itself and every simulator, under headings: "== Devices =="
    /// is what is connected, "== Devices Offline ==" what is known but not reachable, and
    /// "== Simulators ==" is simctl's territory and already covered. An offline device is reported
    /// rather than dropped - it is a real device that can be plugged in, and saying it is there but
    /// not connected is more use than not mentioning it.
    /// The Mac itself is filtered out by the pattern, which requires an OS version between the name
    /// and the UDID; the Mac's line carries only a UDID.
    /// </summary>
    private static IEnumerable<MobileDevice> AppleDevices()
    {
        var result = ShellCommand.Run("/usr/bin/xcrun", ["xctrace", "list", "devices"], ToolTimeout);

        if (!result.Success)
            throw new InvalidOperationException($"xcrun xctrace failed: {result.Explain()}");

        var devices = new List<MobileDevice>();
        var connected = false;
        var inDeviceSection = false;

        foreach (var line in result.Output)
        {
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                inDeviceSection = line.Contains("Devices", StringComparison.OrdinalIgnoreCase);
                connected = inDeviceSection && !line.Contains("Offline", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inDeviceSection)
                continue;

            var match = PhysicalDeviceLine().Match(line);

            if (!match.Success)
                continue;

            var udid = match.Groups["udid"].Value;

            devices.Add(new MobileDevice(
                Id: udid,
                Name: match.Groups["name"].Value.Trim(),
                Platform: MobilePlatforms.IOS,
                RuntimeIdentifier: MobilePlatforms.RuntimeIdentifierFor(MobilePlatforms.IOS, isEmulator: false),
                OsVersion: $"iOS {match.Groups["os"].Value}",
                IsEmulator: false,
                IsRunning: connected));
        }

        return devices;
    }

    /// <summary>
    /// This Mac, as a Mac Catalyst target. There is only ever one and nothing has to be discovered
    /// about it, but it is listed as a device anyway so that every MAUI target is chosen the same
    /// way - and because Mac Catalyst is the one that needs no emulator, which makes it the cheapest
    /// place to check that a debugging setup works at all.
    /// </summary>
    private static MobileDevice MacCatalyst() => new(
        Id: MobilePlatforms.MacCatalyst,
        Name: "This Mac (Mac Catalyst)",
        Platform: MobilePlatforms.MacCatalyst,
        RuntimeIdentifier: MobilePlatforms.RuntimeIdentifierFor(MobilePlatforms.MacCatalyst, isEmulator: false),
        OsVersion: Environment.OSVersion.Version.ToString(),
        IsEmulator: false,
        IsRunning: true);

    [GeneratedRegex(@"^(?<name>.+?)\s+\((?<os>[\d.]+)\)\s+\((?<udid>[0-9A-Fa-f-]{8,})\)\s*$")]
    private static partial Regex PhysicalDeviceLine();
}
