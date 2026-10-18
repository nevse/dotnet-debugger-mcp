---
worth: later
where: src/SharpDbg.MCP/Tools/InputValidation.cs:45
added: 2026-10-18
---
# launch_program is not limited to chosen directories

`launch_program` accepts any .NET app on the machine: a `.dll` with its `.runtimeconfig.json`, or an
apphost beside them. Native executables and code-injecting environment variables are refused, but a
.NET tool installed anywhere still runs with whatever arguments the agent passes. An allow-list of
roots (`SHARPDBG_LAUNCH_ROOTS`, off by default) would let a stricter environment confine it to, say, the
project's `bin/`.

Unknown that settles it: whether anyone needs it. An agent that can write and build code launches what
it built inside any root, so it only narrows the choice of ready-made .NET tools.
