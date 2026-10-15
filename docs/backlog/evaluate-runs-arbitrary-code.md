---
worth: later
where: src/SharpDbg.MCP/Tools/DebuggingTools.cs:1286
added: 2026-10-15
---
# evaluate_expression runs arbitrary code in the debuggee

Any method call is allowed, including `Process.Start`, file and network I/O, with the debuggee's
privileges; only `SHARPDBG_EVAL_TIMEOUT_MS` bounds it. Inherent to a debugger, so the default answer is
documentation.

Unknowns that settle it: whether a restricted mode is worth offering, and whether clrdbg can evaluate
without side effects (property getters but no calls, or a no-side-effects flag) - ask upstream first.
