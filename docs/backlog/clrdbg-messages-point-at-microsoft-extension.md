---
worth: yes
added: 2026-10-15
---
# clrdbg's error messages point at the Microsoft MAUI extension

clrdbg's `DotNet.Debugging.Adapter/Resources.cs` (`MsgMissingCoreclrHost`, `MsgMissingCoreclrTarget`)
still tell users to "provide a compatible copy from the Microsoft .NET MAUI VS Code extension". The
server never shows them, since the bundled directory always exists, but they steer users towards
Microsoft's debugger components, whose license covers Visual Studio's products, while the project's
position is that vsdbg is not used. Upstream fix: point at a clrdbg release's
`RemoteCoreClrLibraries.zip`. The vsdbg detection in `MobileDebugAgent` stays; PR #15 makes it
unreachable from the server.
