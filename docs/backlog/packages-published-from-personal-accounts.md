---
worth: later
added: 2026-10-15
---
# packages are published from personal accounts

`DotnetDebugger.Mcp` is owned on nuget.org by `nevse83` alone, with no reserved ID prefix and no author
signature (nuget.org's repository signature only). The repo is `nevse/dotnet-debugger-mcp` and the
bundled debugger is `JaneySprings/clrdbg`. Supply-chain risk: one personal account compromised is
enough to ship a malicious version. The fix is moving both to the DevExpress GitHub organization and
publishing from a company NuGet account. Publishing already uses trusted publishing (one-hour OIDC keys,
no stored secret), which limits but does not remove that risk.

Unknown that settles it: whether DevExpress and the clrdbg author agree to the move. Moving also changes
the MCP registry namespace (`io.github.nevse/*`) and the trusted-publishing policy.
