---
worth: yes
where: src/SharpDbg.MCP/Tools/MobileTools.cs:92
added: 2026-10-15
---
# build_mobile_app accepts a Release configuration

`configuration` is any non-empty string, so `build_mobile_app(configuration: "Release")` produces a
Release-configured app carrying the remote debugging profiler library and `EnableDiagnostics=true`.
`launch_mobile_app` (line 150) takes the same parameter. Ordinary builds never get the library - it
comes only from the command-line `CustomAfterMicrosoftCommonTargets` import - but nothing stops a remote
debugging entry point from being built into a release-configured app this way. Fix: accept `Debug` only, or
refuse anything else unless explicitly allowed.
