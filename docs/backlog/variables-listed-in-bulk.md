---
worth: later
where: src/DotnetDebugger.Mcp/Tools/DebuggingTools.cs:1042
added: 2026-10-25
---
# get_variables hands every value in scope to the model

`get_variables` returns every local and argument of a frame with its value, and `expand_variable`
every member of an object, so one call sweeps whatever the frame holds into model context and the
transcript, whether the agent needed it or not. Masking catches what looks like a secret; anything it
misses goes along in bulk.

An opt-in mode (`DOTNET_DEBUGGER_MCP_VARIABLE_ACCESS=explicit`, off by default) would make both tools
take a `names` list and refuse an unfiltered call, listing names and types without values otherwise.
`evaluate_expression` stays as it is: it already reads one thing by name. debugmcp/mcp-debugger ships
the same mode as `DEBUG_MCP_VARIABLE_ACCESS=explicit`, and microsoft/DebugMCP makes it the only mode.

This narrows accidental exposure, not access: an agent can still ask for each name it sees.

Unknown that settles it: whether anyone runs the server where values in transcripts are a concern
and would trade the extra round trips for it. If no one asks, drop it.
