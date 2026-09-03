using System.Diagnostics;

using SharpDbg.MCP.Configuration;
using SharpDbg.MCP.Debugging;

namespace SharpDbg.MCP.Tests.Integration;

/// <summary>
/// Three defects that were reported against SharpDbg and left the backlog with the dependency rather
/// than being fixed for us, re-tested against clrdbg on 20 August 2026 and again on 3 September
/// 2026. None of them reproduces through this server, so these stay as the guard on that: each one
/// drives the sequence that used to fail, and each pins what a caller sees now.
///
/// The terminate one is the one to read carefully: the defect is alive upstream, and what keeps it
/// away from here is two things that could each change. Its own doc comment carries that.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Integration")]
public sealed class UpstreamProbeTests
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    private static DebugSession CreateSession() => new(1, new ServerConfiguration());

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void KillIfAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
        }
    }

    /// <summary>
    /// UPSTREAM.md defect 15: terminating a debuggee that is running rather than stopped.
    /// ICorDebugProcess::Terminate needs the process synchronized, and on a running one it fails with
    /// CORDBG_E_PROCESS_NOT_SYNCHRONIZED. SharpDbg swallowed that and answered success, leaking the
    /// program; 0.1.13 fixed it by stopping first, which is why DebugSession stopped pausing before
    /// the terminate. clrdbg has the pre-fix shape again - Terminate(0) inside a catch that only logs
    /// - and a running attached process does survive it, which is filed as clrdbg#4.
    ///
    /// It does not reach a program this server launched, and that is what these two cover between
    /// them: Launch_Detach_KillsTheProgramItStarted already kills one that is running, and this one
    /// covers the other side of the pair, a program stopped at a breakpoint. Both hold only because
    /// clrdbg's Dispose kills the OS process it started itself and because this server asks to
    /// terminate nothing else. The attached side is pinned by
    /// TwoSessions_DebugTwoProcessesIndependently, which requires an attached debuggee to run on
    /// after its session closes.
    /// </summary>
    [TestMethod]
    public async Task Probe_ClosingASessionWhileTheProgramIsStopped_KillsIt()
    {
        var line = TestPaths.FindMarkerLine("BREAKPOINT-TARGET");

        using var session = CreateSession();

        await session.Launch(TestPaths.TestAppAssembly);
        session.SetBreakpoint(TestPaths.TestAppSource, line);
        session.Start();

        var stopped = DebuggeeProcess.SpinUntil(
            () => session.GetExecutionState() is { Started: true, IsRunning: false },
            StopTimeout);
        Assert.IsTrue(stopped, "The program never reached the breakpoint");

        var processId = session.GetExecutionState().ProcessId;
        Assert.IsNotNull(processId);

        session.Dispose();

        var died = DebuggeeProcess.SpinUntil(() => !IsAlive(processId.Value), TimeSpan.FromSeconds(10));

        if (!died)
            KillIfAlive(processId.Value);

        Assert.IsTrue(died, $"Process {processId} outlived the session that started it");
    }

    /// <summary>
    /// UPSTREAM.md defect 5: a breakpoint hit already in flight when the file's breakpoints are
    /// replaced. Every set_breakpoint and remove_breakpoint re-sends the whole file's set, which
    /// deactivates the old ICorDebugFunctionBreakpoint objects; on SharpDbg a hit that arrived in that
    /// window threw on the debugger's callback thread, which then neither continued the process nor
    /// reported a stop. The debuggee stayed suspended with the session still believing it ran.
    ///
    /// It fired in roughly one run in five, so this drives the race twenty times: resume, and replace
    /// the file's set while the program is running back into a breakpoint it hits every 150ms.
    /// </summary>
    [TestMethod]
    public async Task Probe_ReplacingBreakpointsWhileOneIsBeingHit_LeavesTheDebuggeeRunnable()
    {
        var hotLine = TestPaths.FindMarkerLine("BREAKPOINT-TARGET");
        var secondLine = TestPaths.FindMarkerLine("STEP-TARGET");

        using var debuggee = DebuggeeProcess.Start();
        using var session = CreateSession();

        await session.Attach(debuggee.ProcessId);

        // Stays armed for the whole probe: it is what the debuggee keeps running back into, and
        // removing it would leave nothing to stop on
        session.SetBreakpoint(TestPaths.TestAppSource, hotLine);

        var first = session.WaitForStop(StopTimeout);
        Assert.IsNotNull(first, "The debuggee runs through the marker every 150ms, so it must stop");

        int? secondId = null;

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            Assert.IsTrue(session.Continue(), $"Resume {attempt} was refused");

            // Replace the file's whole set while the program is running back into the armed
            // breakpoint. Adding and removing a second line alternately keeps every pass a real
            // replacement of the set that owns the hit in flight.
            if (secondId is { } id)
            {
                session.RemoveBreakpoint(id);
                secondId = null;
            }
            else
            {
                secondId = session.SetBreakpoint(TestPaths.TestAppSource, secondLine).Id;
            }

            var stop = session.WaitForStop(TimeSpan.FromSeconds(10));

            // Built only on failure: DescribeResumption waits, and a stopped debuggee never prints,
            // so an eagerly interpolated message costs its whole timeout on every passing iteration
            if (stop is null)
            {
                Assert.Fail(
                    $"No stop arrived on attempt {attempt}. The debuggee "
                    + $"{debuggee.DescribeResumption(TimeSpan.FromSeconds(5))}, which says whether it "
                    + "is suspended with nobody reporting it or merely slow");
            }
        }
    }

    /// <summary>
    /// UPSTREAM.md defect 6: stepping into code with no symbols of its own. On SharpDbg the debugger
    /// decompiled such a module to get a location, the decompilation threw inside itself, the
    /// failure was caught and logged, and the callback then neither continued the process nor
    /// reported a stop - so the debuggee stayed suspended and the step was retried forever. It
    /// presented as a hang rather than an error, which is why no test covered it.
    ///
    /// clrdbg cannot reach that state at all since 59ebe09 (29 August 2026): it carries no
    /// decompiler, and a step that lands in a module without symbols steps straight back out, the
    /// way vsdbg does - there is no source to show, so it does not stop there. That makes the step
    /// over the interpolated string below land on the next statement of the user's own method
    /// instead of inside System.Private.CoreLib, and it is fast rather than costing a cold
    /// decompilation.
    ///
    /// So what this pins is the shape of the outcome, not the destination: the step completes, it
    /// reports itself as a step, and the debuggee is left able to run. Those are what the defect
    /// took away. justMyCode is off because that is the setting that used to let a step reach code
    /// without symbols, and it is the configuration the defect needed. The breakpoint has to be
    /// removed before the step: left armed it fires every 150ms, and a breakpoint hit cancels the
    /// stepper, so a stop would arrive that reads as success without the step ever having completed.
    /// Only a stop whose reason is 'step' counts.
    /// </summary>
    [TestMethod]
    public async Task Probe_SteppingIntoCodeWithoutSymbols_DoesNotStrandTheDebuggee()
    {
        var line = TestPaths.FindMarkerLine("STEP-TARGET");

        using var debuggee = DebuggeeProcess.Start();
        using var session = new DebugSession(1, new ServerConfiguration { JustMyCode = false });

        await session.Attach(debuggee.ProcessId);

        var breakpoint = session.SetBreakpoint(TestPaths.TestAppSource, line);

        var stopped = session.WaitForStop(StopTimeout);
        Assert.IsNotNull(stopped, "The debuggee runs through the marker every 150ms, so it must stop");

        Assert.IsTrue(session.RemoveBreakpoint(breakpoint.Id),
            "Left armed, a hit would cancel the stepper and look like the step completing");

        session.StepInto(stopped.StoppedThreadId!.Value);

        // Still generous, though no longer for the decompilation: the step crosses code without
        // symbols and steps back out of it, and CI has fewer cores than this machine
        var afterStep = session.WaitForStop(TimeSpan.FromSeconds(120));

        // Built only on failure, for the reason the breakpoint probe above records
        if (afterStep is null)
        {
            Assert.Fail(
                "The step never completed. The debuggee "
                + $"{debuggee.DescribeResumption(TimeSpan.FromSeconds(5))}, so this is either the "
                + "freeze the defect describes or a step that is merely slow");
        }

        Assert.AreEqual("step", afterStep.StopReason,
            $"Stopped for '{afterStep.StopReason}' rather than the step completing");

        // The statement the marker sits on builds an interpolated string, so the step goes through
        // System.Private.CoreLib, which has no symbols here, and comes back to the next statement of
        // the method it started in. Landing anywhere else means the step did not cross that code
        var expected = $"{TestPaths.TestAppSource}:{line + 1}";
        Assert.AreEqual(expected, afterStep.CurrentLocation,
            "A step across code without symbols must come back to the next statement of the caller");

        // The whole of the defect: the process must still be running once the step is done with it
        Assert.IsTrue(session.Continue());
        Assert.IsTrue(
            debuggee.WaitForOutput(TimeSpan.FromSeconds(10)),
            "The debuggee never resumed, so the step left it stranded");
    }
}
