---
worth: yes
where: src/DotnetDebugger.Mcp/Tools/DebuggingTools.cs:977
added: 2026-10-15
---
# debuggee values reach the model unredacted

`get_variables`, `expand_variable`, `evaluate_expression`, `get_program_output` and
`get_exception_info` return values as the debugger reports them, so connection strings, tokens and
customer data in the debuggee go to the LLM verbatim. Some other debugging MCP servers redact them.

Likely shape: mask by member name (`password`, `secret`, `token`, `apiKey`, `connectionString`…) and by
value pattern (`Bearer …`, JWTs, `Password=` inside a string), on by default, off with an env var. It
reduces exposure rather than closing it - a secret in a local called `s` passes - and the docs have to
say so.
