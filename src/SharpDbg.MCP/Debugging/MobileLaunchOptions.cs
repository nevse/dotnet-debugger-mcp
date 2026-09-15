namespace SharpDbg.MCP.Debugging;

/// <summary>
/// The extra half of a launch request that turns it into a mobile one. Its presence is what makes
/// the debugger connect to an app on a phone, a simulator or an emulator instead of starting a
/// process here: it picks a different agent inside the adapter, which installs the app, sets the
/// environment its runtime reads at startup, launches it, and then waits for it to connect back.
///
/// The address and port are deliberately absent. The debugger picks a free port and listens on
/// 127.0.0.1 when told neither, which is right for every case this server has - it forwards the
/// port to the device itself - and a port chosen here could be taken by the time it was used.
/// </summary>
public sealed record MobileLaunchOptions(
    string Platform,
    string? RuntimeIdentifier,
    string? Device,
    bool IsDevice,
    string AssetsPath,
    bool UninstallApp,
    string RemoteCoreclrHost,
    string RemoteCoreclrTarget);
