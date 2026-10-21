---
worth: yes
where: src/DotnetDebugger.Mcp/Tools/DebuggingTools.cs
added: 2026-10-20
---
# tools declare no MCP annotations

No tool sets `ReadOnly`, `Destructive`, `Idempotent` or `OpenWorld` on its `[McpServerTool]`, so a
client sees `get_stack_trace` and `launch_program` alike and cannot offer to auto-approve the reads
while asking for every launch. A user who wants to confirm the dangerous calls by hand ends up
confirming all ~30, or allowing all of them.

The split is roughly: inspection (`list_dotnet_processes`, `list_sessions`, `get_threads`,
`get_stack_trace`, `get_variables`, `get_program_output`, `list_breakpoints`, the documentation tools)
is read-only; stepping, breakpoints and continuing change only the session; `launch_program`,
`attach_to_process`, `evaluate_expression`, `build_mobile_app` and `launch_mobile_app` run or reach
code, and are the ones to mark destructive and open-world. Mechanical, one
attribute per tool across `DebuggingTools.cs`, `McpTools.cs` and `MobileTools.cs`, plus a README table
column. The hints are advisory - a client may ignore them - so the README's advice to confirm those
calls stays.
