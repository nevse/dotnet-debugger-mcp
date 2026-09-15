---
worth: yes
where: src/SharpDbg.MCP/Debugging/DapDebugger.cs:280
added: 2026-09-15
---
# most adapter requests still have no bound

`pause_execution` used to be the named case and is bounded now: `TryPause` and `TryGetThreads` take a
timeout, and the stop is recorded from the adapter's own confirmation rather than after the wait, so
giving up waiting no longer risks the session claiming a program runs while it stands still.

Its siblings were never the outlier and are still unbounded. `get_threads`, `get_stack_trace`,
`get_variables`, `expand_variable`, `evaluate_expression`, `get_exception_info`, the three steps and
`continue_execution` all go through `SendRequestSync`, which has no timeout, so a wedged adapter blocks
the caller for the life of the process.

The adapter cannot spread the damage over other requests either, because it answers them one at a time:
measured against clrdbg, a `threads` request that takes 9ms took 1861ms while a pause ahead of it was
waiting, and a `disconnect` would have waited exactly as long. So the first request to wedge holds off
every other one, including the one that would tear the session down.

`evaluate_expression` and `get_exception_info` look bounded and are not: both wrap the request in
`Task.Run(...).WaitAsync(_evaluationTimeout)` — `DebugSession.cs:1558` and `:1568` — which releases the
caller and leaves the pool thread, the adapter and its lock exactly where they were. Worth knowing
before either is counted as done.

`SendRequestWithTimeout` (`DapDebugger.cs:211`) is the shape to copy, and its own doc comment already
records why: bound the request, and write whatever state the operation records from the completion
callback rather than after the wait. Doing it wholesale means deciding what each tool reports when
unconfirmed, which is why it is not a mechanical change. Effort: M.
