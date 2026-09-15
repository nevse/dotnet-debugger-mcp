namespace SharpDbg.MCP.TestApp;

/// <summary>
/// Debuggee used by the integration tests. It loops forever printing a tick, so a test can tell
/// whether the process is running or suspended by watching its output.
/// Lines are located by the marker comments below - never hard-code line numbers in tests. A marker
/// is matched as a substring and the first line wins, so no marker may contain another.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Named so a test can tell a managed thread name from the id SharpDbg used to report in its
        // place. Naming the thread we already have keeps the thread list the same shape. The tests
        // are not linked against this assembly, so the name is repeated in TestPaths.
        Thread.CurrentThread.Name = "debuggee-main";

        // Off by default, so tests that are not about exceptions are not disturbed by them
        var throwEachIteration = args.Contains("--throw");

        // Throws the same exception wrapped around another one, for the tests that read what the
        // reported exception wraps
        var wrapWhatIsThrown = args.Contains("--throw-wrapped");

        Console.WriteLine($"PID={Environment.ProcessId}"); // STARTUP-TARGET
        Console.Out.Flush();

        // Exits instead of looping, so a test can compare the code the debugger reports against the
        // one the program actually returned. Read after the pid is printed, which is what lets a test
        // find the process either way.
        if (ExitCodeArgument(args) is { } requested)
            return requested;

        var counter = 0;
        while (true)
        {
            counter = Work(counter);

            if (throwEachIteration || wrapWhatIsThrown)
                ThrowAndCatch(counter, wrapWhatIsThrown);

            Console.WriteLine($"tick {counter}");
            Console.Out.Flush();
            Thread.Sleep(150);
        }
    }

    /// <summary>
    /// The code asked for by --exit-code=N, or null to run as usual
    /// </summary>
    private static int? ExitCodeArgument(string[] args)
    {
        const string Flag = "--exit-code=";

        var argument = args.FirstOrDefault(a => a.StartsWith(Flag, StringComparison.Ordinal));

        return argument is null ? null : int.Parse(argument[Flag.Length..]);
    }

    /// <summary>
    /// Throws and handles the exception itself, which is what a debugger that breaks on every
    /// first-chance exception has to cope with: nothing is actually wrong with this program.
    /// The wrapped form builds its inner exception rather than throwing one, so the stop under test
    /// is still the wrapper's and the inner carries no recorded trace - which is what an exception
    /// that was never thrown looks like to a debugger.
    /// </summary>
    private static void ThrowAndCatch(int counter, bool wrapped)
    {
        try
        {
            if (wrapped)
            {
                throw new InvalidOperationException(
                    $"thrown on iteration {counter}",
                    new FormatException($"wrapped on iteration {counter}")); // WRAPPED-TARGET
            }

            throw new InvalidOperationException($"thrown on iteration {counter}"); // THROW-TARGET
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static int Work(int current)
    {
        var next = current + 1; // BREAKPOINT-TARGET
        var label = $"n={next}"; // STEP-TARGET
        var point = new Point(next, label.Length);
        return point.X; // EXPAND-TARGET
    }

    /// <summary>
    /// Gives the expansion tests an object with members to walk into
    /// </summary>
    private sealed record Point(int X, int Y);
}
