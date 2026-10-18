---
worth: no
added: 2026-10-15
---
# packages are published from personal accounts

`DotnetDebugger.Mcp` is owned on nuget.org by `nevse83`, the repo is `nevse/dotnet-debugger-mcp`, and
the bundled debugger is `JaneySprings/clrdbg` - all personal accounts, so one of them compromised is
enough to ship a malicious version.

Decided on 2026-10-16 to keep it as is for now. The risk is reduced by hardening how releases are made
instead. Every workflow action is pinned to a commit and mcp-publisher to a checksum, publishing waits
for approval in the `release` environment, `main` and release tags are protected by rulesets, and the
package carries a build provenance attestation. Still to do: release-publish-needs-no-approval (the
nuget.org side), nuget-id-prefix-not-reserved and clrdbg-releases-unattested-and-recreated.
