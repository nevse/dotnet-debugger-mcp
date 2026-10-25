using System.Text;

using DotnetDebugger.Mcp.Logging;

using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

using Newtonsoft.Json.Linq;

using MSBreakpoint = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Breakpoint;

namespace DotnetDebugger.Mcp.Debugging;

/// <summary>
/// Drives SharpDbg through the surface its package actually supports: an in-process debug adapter
/// spoken to over DAP. The alternative - calling <c>ManagedDebugger</c> directly - is public but
/// unsupported, and misses every piece of synchronisation SharpDbg has, all of which lives in its
/// DebugAdapter.
/// Requests are never sent from an event handler: events are delivered on the protocol's reader
/// thread, which is also what would have to read the response.
/// </summary>
internal sealed class DapDebugger : IDisposable
{
    private readonly DebugProtocolHost _host;
    private readonly ChildProcessDebugAdapter.AdapterProcess _adapter;
    private readonly Action<string>? _logger;
    private readonly TimeSpan _requestTimeout;
    private readonly TaskCompletionSource _initialized =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _disposed;

    /// <summary>
    /// Thread id, reason, and the adapter ids of the breakpoints the stop is attributed to. A DAP stop
    /// carries no source location - SharpDbg does attach one as an additional property, but
    /// <c>ProtocolObject.AdditionalProperties</c> is not public, so the location is read from the top
    /// stack frame instead, by whoever needs it. It must not be read here: this runs on the protocol's
    /// reader thread, which is also what reads request responses.
    /// </summary>
    public event Action<int, string, IReadOnlyList<int>?>? OnStopped;

    public event Action<int>? OnContinued;

    /// <summary>
    /// The debuggee has gone, with its exit code when there is one. Null means the code is unknown:
    /// a terminate carries none, and neither does the exit of a process we merely attached to.
    /// </summary>
    public event Action<int?>? OnExited;

    /// <summary>
    /// The pid of a program the debugger started, once it has started it. SharpDbg raises this from
    /// its launch path only, so attaching produces nothing - which is right, since an attaching
    /// caller named the process itself.
    /// </summary>
    public event Action<int>? OnProcessStarted;

    public event Action<string, bool>? OnOutput;
    public event Action<AppliedBreakpoint>? OnBreakpointChanged;

    /// <summary>The debugger's own process, which is a child of this one</summary>
    public int AdapterProcessId => _adapter.ProcessId;

    /// <param name="requestTimeout">
    /// How long any request without a bound of its own may take. SharpDbg answers requests one at a
    /// time, so the first one to stall holds off every later one, the disconnect that would tear the
    /// session down included; without a bound its caller waits for the life of the process.
    /// </param>
    public DapDebugger(TimeSpan requestTimeout, Action<string>? logger = null)
    {
        _logger = logger;
        _requestTimeout = requestTimeout;

        var (input, output, adapter) = ChildProcessDebugAdapter.Start(logger);
        _adapter = adapter;
        _host = new DebugProtocolHost(input, output, false);

        _host.RegisterEventType<InitializedEvent>(_ => _initialized.TrySetResult());
        _host.RegisterEventType<StoppedEvent>(OnStoppedEvent);
        _host.RegisterEventType<ContinuedEvent>(e => OnContinued?.Invoke(e.ThreadId));
        _host.RegisterEventType<ExitedEvent>(e => OnExited?.Invoke(e.ExitCode));
        // Sent after exited and carrying no code of its own, so it reports the exit without claiming
        // to know how it ended
        _host.RegisterEventType<TerminatedEvent>(_ => OnExited?.Invoke(null));
        _host.RegisterEventType<ProcessEvent>(OnProcessEvent);
        _host.RegisterEventType<OutputEvent>(OnOutputEvent);
        _host.RegisterEventType<BreakpointEvent>(OnBreakpointEvent);

        _host.VerifySynchronousOperationAllowed();
        _host.Run();
    }

    /// <summary>
    /// Initializes the adapter, attaches, and waits for the attach to land. The DAP order is
    /// initialize, attach, wait for initialized, configurationDone - breakpoints would normally be
    /// sent between the last two, which an MCP server cannot do because they arrive as separate tool
    /// calls long afterwards.
    /// </summary>
    public async Task Attach(
        int processId,
        bool justMyCode,
        bool allowFuncEval,
        IReadOnlyList<string> filters,
        string? typeCondition,
        TimeSpan timeout)
    {
        Initialize();

        Send(new AttachRequest
        {
            ConfigurationProperties = new Dictionary<string, JToken>
            {
                ["name"] = ".NET Debugger MCP",
                ["type"] = "coreclr",
                ["processId"] = processId,
                ["console"] = "internalConsole",
                ["justMyCode"] = justMyCode,
                ["expressionEvaluationOptions"] = EvaluationOptions(allowFuncEval)
            }
        }, "Attaching");

        await _initialized.Task.WaitAsync(timeout).ConfigureAwait(false);

        SetExceptionBreakpoints(filters, typeCondition);

        Send(new ConfigurationDoneRequest(), _requestTimeout, "Finishing the attach");
    }

    /// <summary>
    /// Prepares a program to be debugged and returns before it runs: the adapter only records the
    /// launch and performs it on configurationDone, which is what <see cref="Start"/> sends. Anything
    /// set in between - breakpoints above all - is already in place when the program starts, and that
    /// is the only way to debug its startup, since SharpDbg accepts stopAtEntry and ignores it.
    /// </summary>
    public async Task Launch(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        bool justMyCode,
        bool allowFuncEval,
        IReadOnlyList<string> filters,
        string? typeCondition,
        TimeSpan timeout,
        MobileLaunchOptions? mobile = null)
    {
        Initialize();

        var configuration = new Dictionary<string, JToken>
        {
            ["name"] = ".NET Debugger MCP",
            ["type"] = "coreclr",
            ["request"] = "launch",
            ["program"] = program,
            ["args"] = new JArray(arguments),
            ["cwd"] = workingDirectory,
            ["env"] = JObject.FromObject(environment),
            ["console"] = "internalConsole",
            ["justMyCode"] = justMyCode,
            ["expressionEvaluationOptions"] = EvaluationOptions(allowFuncEval)
        };

        if (mobile is not null)
            AddMobileOptions(configuration, mobile);

        Send(new LaunchRequest { ConfigurationProperties = configuration }, "Launching");

        await _initialized.Task.WaitAsync(timeout).ConfigureAwait(false);

        SetExceptionBreakpoints(filters, typeCondition);
    }

    /// <summary>
    /// Off, the debugger runs nothing in the debuggee on its own and refuses every call, construction
    /// and enumeration an expression makes. It also turns ToString off, so allowToString is not sent.
    /// </summary>
    private static JObject EvaluationOptions(bool allowFuncEval) =>
        new() { ["allowImplicitFuncEval"] = allowFuncEval };

    /// <summary>
    /// Adds what the adapter reads to decide this is a mobile launch. The property names are the
    /// adapter's own, shared with the VS Code extension the debugger was written for, so they are
    /// spelled here exactly as its launch.json spells them.
    /// </summary>
    private static void AddMobileOptions(Dictionary<string, JToken> configuration, MobileLaunchOptions mobile)
    {
        configuration["remoteCoreclrHost"] = mobile.RemoteCoreclrHost;
        configuration["remoteCoreclrTarget"] = mobile.RemoteCoreclrTarget;

        var options = new JObject
        {
            ["platform"] = mobile.Platform,
            ["assetsPath"] = mobile.AssetsPath,
            ["isDevice"] = mobile.IsDevice,
            ["uninstallApp"] = mobile.UninstallApp,
            // Zero and null are what the debugger reads as "choose for yourself", which is what it
            // should do: it forwards the port to the device, so nothing else has to agree on it
            ["port"] = 0
        };

        if (mobile.RuntimeIdentifier is not null)
            options["runtimeIdentifier"] = mobile.RuntimeIdentifier;

        if (mobile.Device is not null)
            options["device"] = mobile.Device;

        configuration["coreClrMobileDebuggerOptions"] = options;
    }

    /// <summary>
    /// Starts the program prepared by <see cref="Launch"/>. The request returns once the process has
    /// been created and attached to, so a stop can already be on its way when it does.
    /// It is timed by the caller rather than by the request timeout, because a mobile start deploys
    /// to a device first and has a timeout of its own. <see cref="Disconnect"/> is timed too, so that
    /// a start which expires can still tear its half-made session down.
    /// </summary>
    public void Start(TimeSpan timeout) =>
        Send(new ConfigurationDoneRequest(), timeout, "Starting the program");

    /// <summary>
    /// Sends a request and waits for it, giving up after <paramref name="timeout"/>.
    /// <c>SendRequestSync</c> has no timeout of its own, so a stalled handler blocks its caller for
    /// the life of the process.
    /// Giving up stops us waiting; it does not stop the adapter. SharpDbg implements no cancel
    /// handler, and it serializes every request behind one lock, so a stalled handler also holds off
    /// the disconnect a teardown would send. Disposing the adapter is the only step that does not
    /// need that lock, which is why a caller on the teardown path has to be able to reach it.
    /// Only a request that changes nothing the session records may give up this way: one whose
    /// outcome the session tracks has to record it from the adapter's answer, as <see
    /// cref="TryResume"/>, <see cref="TryPause"/> and the breakpoint requests do, since the answer
    /// can land after the wait.
    /// </summary>
    private void Send<TArgs>(DebugRequest<TArgs> request, TimeSpan timeout, string what)
        where TArgs : class, new()
    {
        // Same guard SendRequestSync applies: the reader thread delivers events and reads responses,
        // so blocking it on a response would deadlock
        _host.VerifySynchronousOperationAllowed();

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _host.SendRequest(
            request,
            _ => completed.TrySetResult(),
            (_, ex) => completed.TrySetException(ex));

        Await(completed.Task, timeout, what);
    }

    private void Send<TArgs>(DebugRequest<TArgs> request, string what)
        where TArgs : class, new() =>
        Send(request, _requestTimeout, what);

    private TResponse Send<TArgs, TResponse>(
        DebugRequestWithResponse<TArgs, TResponse> request, string what, Action<TResponse>? onAnswered = null)
        where TArgs : class, new()
        where TResponse : ResponseBody =>
        Send(request, _requestTimeout, what, onAnswered);

    /// <summary>
    /// As above, for a request that answers with something. <paramref name="onAnswered"/> is how a
    /// request whose outcome the session records may still be sent this way: it runs with the answer
    /// whenever that comes, before or after the wait, on the adapter's callback thread, and must not
    /// throw or block.
    /// </summary>
    private TResponse Send<TArgs, TResponse>(
        DebugRequestWithResponse<TArgs, TResponse> request,
        TimeSpan timeout,
        string what,
        Action<TResponse>? onAnswered = null)
        where TArgs : class, new()
        where TResponse : ResponseBody
    {
        _host.VerifySynchronousOperationAllowed();

        var completed = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        _host.SendRequest(
            request,
            (_, response) =>
            {
                // Before signalling, so a caller still waiting never sees the answer unrecorded
                onAnswered?.Invoke(response);
                completed.TrySetResult(response);
            },
            (_, ex) => completed.TrySetException(ex));

        Await(completed.Task, timeout, what);
        return completed.Task.Result;
    }

    private static void Await(Task completed, TimeSpan timeout, string what)
    {
        // Waiting on the handle rather than on the task: Task.Wait throws the fault wrapped in an
        // AggregateException, which would hide the ProtocolException the line below exists to
        // rethrow with its own type and stack, the way SendRequestSync surfaces one.
        if (!((IAsyncResult)completed).AsyncWaitHandle.WaitOne(timeout))
        {
            LogLateFailure(completed, what);

            throw new TimeoutException(
                $"{what} did not complete within {timeout.TotalSeconds:0.#}s. The debug adapter is "
                + "still working on the request, and answers nothing else until it is done.");
        }

        completed.GetAwaiter().GetResult();
    }

    /// <summary>Nobody is left to receive an exception from a request that fails after the wait</summary>
    private static void LogLateFailure(Task completed, string what) =>
        completed.ContinueWith(
            t => McpLogger.LogWarning(
                "{Request} failed after the caller stopped waiting: {Error}",
                what,
                t.Exception!.GetBaseException().Message),
            TaskContinuationOptions.OnlyOnFaulted);

    /// <summary>
    /// Says which exceptions should suspend the debuggee. Sent on attach and launch, and again whenever
    /// the caller changes its mind, because the debugger stops on nothing until it is asked to - unlike
    /// SharpDbg, which broke on every first-chance exception unconditionally and had no way to say
    /// otherwise.
    ///
    /// Two filters exist. "all" covers every throw, first-chance included; "userUnhandled" covers the
    /// ones that escape the caller's own code. Neither covers a genuinely unhandled exception, which
    /// stops the program whatever is asked here - a crash cannot be hidden, so sending no filters at all
    /// is what leaves only those.
    ///
    /// <paramref name="typeCondition"/> narrows a filter to particular exception types, and belongs to
    /// whichever filter is enabled. It is a comma-separated list of fully-qualified names, matched
    /// exactly, and a leading '!' turns the whole list into exclusions - the two cannot be mixed, which
    /// is the debugger's own rule rather than ours.
    /// </summary>
    public void SetExceptionBreakpoints(IReadOnlyList<string> filters, string? typeCondition)
    {
        var request = new SetExceptionBreakpointsRequest { Filters = [.. filters] };

        if (typeCondition is not null)
        {
            request.FilterOptions = [.. filters.Select(
                filter => new ExceptionFilterOptions { FilterId = filter, Condition = typeCondition })];
        }

        Send(request, _requestTimeout, "Setting exception breakpoints");
    }

    private void Initialize() =>
        Send(new InitializeRequest
        {
            ClientID = "dotnet-debugger-mcp",
            ClientName = ".NET Debugger MCP Server",
            AdapterID = "coreclr",
            Locale = "en-us",
            LinesStartAt1 = true,
            ColumnsStartAt1 = true,
            PathFormat = InitializeArguments.PathFormatValue.Path,
            SupportsVariableType = true
        }, "Initializing the debug adapter");

    public List<(int Id, string Name)> GetThreads()
    {
        var response = Send(new ThreadsRequest(), "Listing threads");
        return response.Threads?.Select(t => (t.Id, t.Name)).ToList() ?? [];
    }

    /// <summary>
    /// The debuggee's threads, or null if the adapter did not answer within <paramref
    /// name="timeout"/>. Exists for the pause path, which needs a thread to pause and so asks for
    /// one first, within the same deadline as the pause rather than a request timeout of its own.
    /// </summary>
    public List<(int Id, string Name)>? TryGetThreads(TimeSpan timeout)
    {
        _host.VerifySynchronousOperationAllowed();

        var completed = new TaskCompletionSource<ThreadsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        _host.SendRequest(
            new ThreadsRequest(),
            (_, response) => completed.TrySetResult(response),
            (_, ex) => completed.TrySetException(ex));

        if (!((IAsyncResult)completed.Task).AsyncWaitHandle.WaitOne(timeout))
        {
            LogLateFailure(completed.Task, "Listing threads");
            return null;
        }

        var threads = completed.Task.GetAwaiter().GetResult();
        return threads.Threads?.Select(t => (t.Id, t.Name)).ToList() ?? [];
    }

    public List<StackFrameInfo> GetStackTrace(int threadId)
    {
        var response = Send(new StackTraceRequest { ThreadId = threadId }, "Reading the stack trace");

        return response.StackFrames?.Select(f => new StackFrameInfo(
            f.Id,
            f.Name,
            f.Line,
            f.EndLine ?? f.Line,
            f.Column,
            f.EndColumn ?? f.Column,
            f.Source?.Path)).ToList() ?? [];
    }

    /// <summary>
    /// The variables of a frame. SharpDbg exposes a single scope per frame, which already covers the
    /// current exception, the arguments and the locals.
    /// </summary>
    public List<VariableInfo> GetFrameVariables(int frameId)
    {
        var scopes = Send(new ScopesRequest { FrameId = frameId }, "Reading the frame's scopes");
        var first = scopes.Scopes?.FirstOrDefault();

        return first is null ? [] : GetVariables(first.VariablesReference);
    }

    /// <summary>
    /// With function evaluation off a property comes back lazy: no value, and a reference whose
    /// expansion runs the getter after all. That reference is withheld, so turning evaluation off
    /// means no getter runs, not one that runs a step later.
    /// </summary>
    public List<VariableInfo> GetVariables(int variablesReference)
    {
        var response = Send(new VariablesRequest { VariablesReference = variablesReference }, "Reading variables");

        return response.Variables?
            .Select(v => v.PresentationHint?.Lazy == true
                ? new VariableInfo(NameWithoutType(v.Name, v.Type), NotEvaluated, v.Type, 0)
                : new VariableInfo(NameWithoutType(v.Name, v.Type), v.Value, v.Type, v.VariablesReference))
            .ToList() ?? [];
    }

    private const string NotEvaluated = "[not evaluated: function evaluation is off]";

    /// <summary>
    /// The debugger labels a variable "current [int]", which suits a tree in an editor and not a
    /// caller that is handed the type in a field of its own. The suffix is only removed when it is
    /// the reported type in brackets - either in full or shortened the way the debugger shortens it
    /// for the label - so a name that genuinely ends in brackets - an array element, "[0]" - is
    /// left alone.
    /// </summary>
    private static string NameWithoutType(string name, string? type)
    {
        if (string.IsNullOrEmpty(type))
            return name;

        var suffix = $" [{type}]";
        if (name.EndsWith(suffix, StringComparison.Ordinal))
            return name[..^suffix.Length];

        suffix = $" [{ShortTypeName(type)}]";

        return name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[..^suffix.Length]
            : name;
    }

    /// <summary>
    /// The label carries the type with every namespace dropped, generic arguments included, so
    /// "System.Collections.Generic.List&lt;MyApp.Point&gt;" is labelled "List&lt;Point&gt;". Only
    /// the label is shortened - the type reported beside it stays whole - so stripping the suffix
    /// means shortening the type the same way to compare against it.
    /// </summary>
    private static string ShortTypeName(string type)
    {
        var shortened = new StringBuilder(type.Length);
        var segmentStart = 0;

        for (var i = 0; i <= type.Length; i++)
        {
            if (i < type.Length && type[i] is not ('<' or '>' or ',' or ' '))
                continue;

            var segment = type.AsSpan(segmentStart, i - segmentStart);
            shortened.Append(segment[(segment.LastIndexOf('.') + 1)..]);
            if (i < type.Length)
                shortened.Append(type[i]);
            segmentStart = i + 1;
        }

        return shortened.ToString();
    }

    /// <summary>
    /// Bounded by <paramref name="timeout"/> rather than the request timeout, because evaluation
    /// runs code in the target and is configured on its own
    /// </summary>
    public EvaluationResult Evaluate(string expression, int frameId, TimeSpan timeout)
    {
        var response = Send(
            new EvaluateRequest { Expression = expression, FrameId = frameId }, timeout, "Evaluating the expression");
        return new EvaluationResult(response.Result, response.Type, response.VariablesReference);
    }

    /// <summary>
    /// The exception a thread is stopped on. This is the one read that runs code in the target, so it
    /// costs far more than reading frames or locals: Message is a property getter, and so is
    /// InnerException, which the debugger reads whether or not there is one. Two evaluations, three
    /// when the exception wraps another. The type name and the recorded trace are read off the
    /// exception object itself and cost nothing. A thread carrying no exception is a failure rather
    /// than an empty answer, because that is all the adapter reports.
    /// Several fields of the response are dropped. The short type name and the description are
    /// assembled upstream out of what is kept here anyway; the break mode is hardcoded to Always, so
    /// the one field that exists to say how the exception will be treated says it of every exception.
    /// HResult and Source left with clrdbg 909f807: two more getters for what the type name already
    /// says, and $exception reads them on demand for the types whose code carries anything at all
    /// (COMException, Win32Exception).
    /// </summary>
    public ThrownException GetException(int threadId, TimeSpan timeout)
    {
        var response = Send(new ExceptionInfoRequest { ThreadId = threadId }, timeout, "Reading the exception");
        var details = response.Details;

        // The debugger sends the direct inner exception only, as a list of one
        var inner = details?.InnerException?.FirstOrDefault();

        return new ThrownException(
            details?.FullTypeName ?? response.ExceptionId,
            details?.Message,
            details?.StackTrace,
            inner is null
                ? null
                : new InnerThrownException(inner.FullTypeName, inner.Message, inner.StackTrace));
    }

    /// <summary>
    /// Replaces a file's breakpoints. What the adapter made of them goes to <paramref name="onApplied"/>
    /// rather than back to the caller, because a request the caller stopped waiting for is still
    /// applied once the adapter gets to it, and the session has to learn about it then.
    /// <paramref name="onApplied"/> runs on the adapter's callback thread and must not throw or block.
    /// </summary>
    public void SetBreakpoints(
        string filePath,
        IReadOnlyList<SourceBreakpointRequest> breakpoints,
        Action<List<AppliedBreakpoint>> onApplied)
    {
        Send(new SetBreakpointsRequest
        {
            Source = new Source { Path = filePath },
            Breakpoints = breakpoints
                .Select(b => new SourceBreakpoint
                {
                    Line = b.Line,
                    Condition = b.Condition,
                    HitCondition = b.HitCondition
                })
                .ToList()
        }, "Setting breakpoints", response => onApplied(Map(response.Breakpoints)));
    }

    /// <summary>As <see cref="SetBreakpoints"/>, for every function breakpoint at once</summary>
    public void SetFunctionBreakpoints(
        IReadOnlyList<FunctionBreakpointRequest> breakpoints,
        Action<List<AppliedBreakpoint>> onApplied)
    {
        Send(new SetFunctionBreakpointsRequest
        {
            Breakpoints = breakpoints
                .Select(b => new FunctionBreakpoint(b.FunctionName)
                {
                    Condition = b.Condition,
                    HitCondition = b.HitCondition
                })
                .ToList()
        }, "Setting function breakpoints", response => onApplied(Map(response.Breakpoints)));
    }

    public bool TryContinue(int threadId, Action<Exception> onFailed) =>
        TryResume(new ContinueRequest { ThreadId = threadId }, "Continue", onFailed);

    /// <summary>
    /// Suspends the debuggee, returning whether the adapter confirmed it within <paramref
    /// name="timeout"/>. <paramref name="onPaused"/> runs the moment the adapter confirms, which may
    /// be after this call has already given up waiting - that is the point of it. Recording the stop
    /// from there rather than from the return is what lets the wait be bounded at all: giving up
    /// stops us waiting but does not stop the adapter, so a pause can still land afterwards, and a
    /// caller that recorded state only on return would then believe the program is running while it
    /// is suspended.
    ///
    /// <paramref name="onPaused"/> runs on the adapter's callback thread and must not throw or block.
    /// A false return means unconfirmed, not failed. A request that actually fails throws, and does
    /// so on this thread only while this call is still waiting; afterwards there is nobody to throw
    /// to, so the failure is logged instead.
    /// </summary>
    public bool TryPause(int threadId, TimeSpan timeout, Action onPaused)
    {
        // Same guard SendRequestSync applies: the reader thread delivers events and reads responses,
        // so blocking it on a response would deadlock
        _host.VerifySynchronousOperationAllowed();

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _host.SendRequest(
            new PauseRequest { ThreadId = threadId },
            _ =>
            {
                // Before signalling, so a caller still waiting never sees the pause confirmed with
                // the state not yet written
                onPaused();
                completed.TrySetResult();
            },
            (_, ex) => completed.TrySetException(ex));

        if (!((IAsyncResult)completed.Task).AsyncWaitHandle.WaitOne(timeout))
        {
            LogLateFailure(completed.Task, "Pause");
            return false;
        }

        // Waiting on the handle rather than on the task: Task.Wait throws the fault wrapped in an
        // AggregateException, which would hide the ProtocolException this rethrows with its own type
        completed.Task.GetAwaiter().GetResult();
        return true;
    }

    public bool TryStepOver(int threadId, Action<Exception> onFailed) =>
        TryResume(new NextRequest { ThreadId = threadId }, "Step over", onFailed);

    public bool TryStepIn(int threadId, Action<Exception> onFailed) =>
        TryResume(new StepInRequest { ThreadId = threadId }, "Step into", onFailed);

    public bool TryStepOut(int threadId, Action<Exception> onFailed) =>
        TryResume(new StepOutRequest { ThreadId = threadId }, "Step out", onFailed);

    /// <summary>
    /// Resumes the debuggee, returning whether the adapter confirmed it within the request timeout.
    /// The mirror of <see cref="TryPause"/>: the session counts the program as running before it
    /// sends this, so what has to be recorded from the adapter's answer is a refusal, and <paramref
    /// name="onFailed"/> does that whenever it comes - giving up stops us waiting, not the adapter,
    /// and a resume refused after the wait would otherwise leave the session claiming a program runs
    /// while it stands still.
    ///
    /// <paramref name="onFailed"/> runs on the adapter's callback thread and must not throw or
    /// block. A failure that arrives while this call is still waiting is also thrown here.
    /// </summary>
    private bool TryResume<TArgs>(DebugRequest<TArgs> request, string what, Action<Exception> onFailed)
        where TArgs : class, new()
    {
        _host.VerifySynchronousOperationAllowed();

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _host.SendRequest(
            request,
            _ => completed.TrySetResult(),
            (_, ex) =>
            {
                // Before signalling, for the same reason TryPause records its stop first
                onFailed(ex);
                completed.TrySetException(ex);
            });

        if (!((IAsyncResult)completed.Task).AsyncWaitHandle.WaitOne(_requestTimeout))
        {
            LogLateFailure(completed.Task, what);
            return false;
        }

        completed.Task.GetAwaiter().GetResult();
        return true;
    }

    /// <summary>
    /// Releases the debuggee, terminating it when this session started it. Since 0.1.13 the terminate
    /// synchronizes the process itself, so this is the whole teardown rather than its second half.
    /// Its only caller is that teardown, which times it because blocking forever here costs the
    /// adapter's Dispose and every later operation on the session.
    /// </summary>
    public void Disconnect(bool terminateDebuggee, TimeSpan timeout) =>
        Send(
            new DisconnectRequest { TerminateDebuggee = terminateDebuggee }, timeout, "Disconnecting");

    private static List<AppliedBreakpoint> Map(List<MSBreakpoint>? breakpoints)
    {
        return breakpoints?
            .Select(b => new AppliedBreakpoint(
                b.Id ?? 0,
                b.Verified,
                b.Message,
                b.Line,
                b.Source?.Path))
            .ToList() ?? [];
    }

    /// <summary>
    /// SystemProcessId is optional in the protocol, so an event without one is passed over rather
    /// than reported as pid 0.
    /// </summary>
    private void OnProcessEvent(ProcessEvent started)
    {
        if (started.SystemProcessId is { } processId)
            OnProcessStarted?.Invoke(processId);
    }

    private void OnStoppedEvent(StoppedEvent stopped) =>
        OnStopped?.Invoke(stopped.ThreadId ?? 0, ReasonToString(stopped.Reason), stopped.HitBreakpointIds);

    /// <summary>
    /// Only what the debuggee itself wrote. The debugger reports its own progress through the same
    /// event under other categories - every module it loads, one line each - and passing that on would
    /// put it in get_program_output, where a caller reads it as program output and cannot tell the
    /// difference. An event with no category at all is taken as stdout, which is what it means.
    /// </summary>
    private void OnOutputEvent(OutputEvent output)
    {
        if (output.Output is null)
            return;

        var isError = output.Category == OutputEvent.CategoryValue.Stderr;

        if (!isError && output.Category is not (null or OutputEvent.CategoryValue.Stdout))
            return;

        OnOutput?.Invoke(output.Output, isError);
    }

    private void OnBreakpointEvent(BreakpointEvent breakpointEvent)
    {
        var breakpoint = breakpointEvent.Breakpoint;

        if (breakpoint is null)
            return;

        OnBreakpointChanged?.Invoke(new AppliedBreakpoint(
            breakpoint.Id ?? 0,
            breakpoint.Verified,
            breakpoint.Message,
            breakpoint.Line,
            breakpoint.Source?.Path));
    }

    /// <summary>
    /// Back to the debugger's own vocabulary, which is what the session and its callers speak
    /// </summary>
    private static string ReasonToString(StoppedEvent.ReasonValue reason) => reason switch
    {
        StoppedEvent.ReasonValue.Step => "step",
        StoppedEvent.ReasonValue.Breakpoint => "breakpoint",
        StoppedEvent.ReasonValue.FunctionBreakpoint => "breakpoint",
        StoppedEvent.ReasonValue.Exception => "exception",
        StoppedEvent.ReasonValue.Pause => "pause",
        StoppedEvent.ReasonValue.Entry => "entry",
        StoppedEvent.ReasonValue.Goto => "goto",
        _ => "unknown"
    };

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _adapter.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Failed to release the debug adapter: {ex.Message}");
        }
    }
}

/// <summary>A breakpoint as the adapter reports it back</summary>
internal sealed record AppliedBreakpoint(int Id, bool Verified, string? Message, int? Line, string? SourcePath);

/// <summary>A source breakpoint to apply</summary>
internal sealed record SourceBreakpointRequest(int Line, string? Condition, string? HitCondition);

/// <summary>A function breakpoint to apply</summary>
internal sealed record FunctionBreakpointRequest(string FunctionName, string? Condition, string? HitCondition);
