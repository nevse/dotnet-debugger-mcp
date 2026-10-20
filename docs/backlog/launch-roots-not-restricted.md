---
worth: later
where: src/SharpDbg.MCP/Tools/InputValidation.cs:45
added: 2026-10-18
---
# launch_program is not limited to chosen directories

`launch_program` accepts any .NET app on the machine: a `.dll` with its `.runtimeconfig.json`, or an
apphost beside them. Native executables and code-injecting environment variables are refused, but a
.NET tool installed anywhere still runs with whatever arguments the agent passes. The SDK ships one:
`sdk/<version>/dotnet.dll` has its `.runtimeconfig.json` beside it, so `launch_program` on it is the
whole `dotnet` CLI, `dotnet run` of any project included. An allow-list of
roots (`SHARPDBG_LAUNCH_ROOTS`, off by default) would let a stricter environment confine it to, say, the
project's `bin/`.

Unknown that settles it: whether anyone needs it. An agent that can write and build code launches what
it built inside any root, and `evaluate_expression` can call `Process.Start` in it, so roots would only
take away the ready-made tools, not the ability to run commands. The README says the server is
equivalent to a shell either way.
