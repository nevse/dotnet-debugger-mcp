---
worth: later
where: src/DotnetDebugger.Mcp/Program.cs:66
added: 2026-10-15
---
# mobile tools cannot be switched off

`WithToolsFromAssembly()` registers `list_mobile_devices`, `build_mobile_app` and `launch_mobile_app`
for everyone, and `build_mobile_app` runs `dotnet build` on any project - executing whatever its MSBuild
files contain. A `DOTNET_DEBUGGER_MCP_ENABLE_MOBILE` flag, off by default, would let a desktop-only install expose
no build step at all.

Unknown that settles it: whether exposing a build of arbitrary projects by default is acceptable for
users who only debug desktop apps. If not, this becomes `yes`; if it is, drop it.
