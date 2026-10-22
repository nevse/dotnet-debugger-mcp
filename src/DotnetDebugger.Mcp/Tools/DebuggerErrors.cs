using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using DotnetDebugger.Mcp.Debugging;

namespace DotnetDebugger.Mcp.Tools;

/// <summary>
/// Turns ICorDebug failures into something a caller can act on. Without this the client only sees
/// the raw COM text - "Returned from a call to Continue that was not matched with a stopping event.
/// (0x8013132F)" - which describes the symptom and nothing about what to do next.
/// </summary>
public static partial class DebuggerErrors
{
    // Runtime HRESULTs from corerror.h, written out rather than referenced: they are part of the
    // runtime's contract and do not change, and nothing else of the debugger is compiled against.
    // Named as corerror.h names them rather than in PascalCase, so each one can be searched for in
    // the runtime's sources and in the error text it shows up in
    private const int CORDBG_E_UNRECOVERABLE_ERROR = unchecked((int)0x80131300);
    private const int CORDBG_E_PROCESS_TERMINATED = unchecked((int)0x80131301);
    private const int CORDBG_E_PROCESS_NOT_SYNCHRONIZED = unchecked((int)0x80131302);
    private const int CORDBG_E_CLASS_NOT_LOADED = unchecked((int)0x80131303);
    private const int CORDBG_E_IL_VAR_NOT_AVAILABLE = unchecked((int)0x80131304);
    private const int CORDBG_E_FIELD_NOT_AVAILABLE = unchecked((int)0x80131306);
    private const int CORDBG_E_NON_NATIVE_FRAME = unchecked((int)0x80131307);
    private const int CORDBG_E_CODE_NOT_AVAILABLE = unchecked((int)0x80131309);
    private const int CORDBG_E_FUNC_EVAL_BAD_START_POINT = unchecked((int)0x80131313);
    private const int CORDBG_E_FUNC_EVAL_NOT_COMPLETE = unchecked((int)0x80131315);
    private const int CORDBG_E_STATIC_VAR_NOT_AVAILABLE = unchecked((int)0x8013131A);
    private const int CORDBG_E_PAST_END_OF_STACK = unchecked((int)0x80131328);
    private const int CORDBG_E_BAD_THREAD_STATE = unchecked((int)0x8013132D);
    private const int CORDBG_E_DEBUGGER_ALREADY_ATTACHED = unchecked((int)0x8013132E);
    private const int CORDBG_E_SUPERFLOUS_CONTINUE = unchecked((int)0x8013132F);
    private const int CORDBG_E_PROCESS_DETACHED = unchecked((int)0x80131335);
    private const int CORDBG_E_HANDLE_HAS_BEEN_DISPOSED = unchecked((int)0x80131C01);

    private static readonly Dictionary<int, string> Explanations = new()
    {
        [CORDBG_E_PROCESS_TERMINATED] =
            "The debuggee has exited. Attach to another process to keep debugging.",

        [CORDBG_E_PROCESS_DETACHED] =
            "The debugger is no longer attached to the process. Attach again.",

        [CORDBG_E_DEBUGGER_ALREADY_ATTACHED] =
            "Another debugger is already attached to that process. Only one debugger can attach at " +
            "a time, so close the other one - an IDE session, or a previous run of this server.",

        [CORDBG_E_PROCESS_NOT_SYNCHRONIZED] =
            "This needs the debuggee to be stopped. Wait for a stop with wait_for_stop, or stop it " +
            "yourself with pause_execution, and then retry.",

        [CORDBG_E_SUPERFLOUS_CONTINUE] =
            "The process was already running, so there was nothing to resume. Check " +
            "get_process_status before continuing.",

        // Fixed in SharpDbg 0.1.8, kept because the explanation is what makes it recognisable if a
        // handle is ever released twice again
        [CORDBG_E_HANDLE_HAS_BEEN_DISPOSED] =
            "The debugger is holding a handle it has already released. Retrying does not help; " +
            "detach_from_process releases the debuggee, after which you can attach again.",

        [CORDBG_E_IL_VAR_NOT_AVAILABLE] =
            "The variable does not exist at this instruction. It is either out of scope or the " +
            "target was built optimized, which removes locals; build the debuggee with " +
            "<Optimize>false</Optimize> to read them.",

        [CORDBG_E_FIELD_NOT_AVAILABLE] =
            "The field is not available in the target. This usually means the type is a generic " +
            "instantiation the runtime has not laid out yet.",

        [CORDBG_E_STATIC_VAR_NOT_AVAILABLE] =
            "The static field has no storage yet, because its type has not been initialized in the " +
            "target. Let the program run until the type is used and try again.",

        [CORDBG_E_CLASS_NOT_LOADED] =
            "The type has not been loaded in the target yet. Let the program run further and retry.",

        [CORDBG_E_CODE_NOT_AVAILABLE] =
            "The method has no code in the target yet, so it cannot be inspected or have a " +
            "breakpoint bound. It is either not jitted yet or has been optimized away.",

        [CORDBG_E_BAD_THREAD_STATE] =
            "The thread cannot be used for this. A thread with no managed frames - a native or " +
            "runtime thread - has no stack to walk and no frame to evaluate in; get the current " +
            "thread from get_process_status while stopped.",

        [CORDBG_E_FUNC_EVAL_BAD_START_POINT] =
            "Code cannot be run in the target from where this thread is stopped. Evaluation needs a " +
            "thread stopped at a managed safe point, such as a breakpoint.",

        [CORDBG_E_FUNC_EVAL_NOT_COMPLETE] =
            "An earlier evaluation in the target has not finished, so a new one cannot start.",

        [CORDBG_E_PAST_END_OF_STACK] =
            "The stack walk ran off the end of the stack. The frame is stale: frame ids only apply " +
            "to the stop they were taken in, so call get_stack_trace again after every stop.",

        [CORDBG_E_NON_NATIVE_FRAME] =
            "The frame is a managed frame and does not support this operation.",

        [CORDBG_E_UNRECOVERABLE_ERROR] =
            "The debugging session is broken and cannot be used any more. Detach with " +
            "detach_from_process and attach again."
    };

    /// <summary>
    /// The explanation for a known ICorDebug HRESULT, or null when the failure is something else
    /// </summary>
    public static string? Explain(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current != null; current = current.InnerException)
        {
            if (Explanations.TryGetValue(current.HResult, out var explanation))
                return explanation;
        }

        // A wrapper keeps its own HResult, so the original may only survive as text in the message
        for (var current = exception; current != null; current = current.InnerException)
        {
            foreach (var match in HResultInText().Matches(current.Message).Cast<Match>())
            {
                var value = uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                if (Explanations.TryGetValue(unchecked((int)value), out var explanation))
                    return explanation;
            }
        }

        return null;
    }

    /// <summary>
    /// The error response every tool returns on failure. The raw message is always kept, so nothing
    /// the debugger said is lost, and explanation is null for failures that are not ICorDebug ones.
    /// </summary>
    public static string ErrorResponse(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var response = new
        {
            success = false,
            error = exception.Message,
            explanation = Explain(exception)
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The same response for a failure whose message can quote a debuggee value, as an evaluation's
    /// can. The message is masked before it is serialized: the JSON escaping of + and quotes would
    /// otherwise split a credential where the patterns could no longer see it.
    /// </summary>
    public static string ErrorResponse(Exception exception, SecretRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(redactor);

        var response = new
        {
            success = false,
            error = redactor.Text(exception.Message),
            explanation = Explain(exception),
            redacted = redactor.Count
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
    }

    [GeneratedRegex(@"0x([0-9A-Fa-f]{8})", RegexOptions.CultureInvariant)]
    private static partial Regex HResultInText();
}
