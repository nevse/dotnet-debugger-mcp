---
worth: no
added: 2026-10-15
---
# packages are published from personal accounts

`DotnetDebugger.Mcp` is owned on nuget.org by `nevse83`, the repo is `nevse/dotnet-debugger-mcp`, and
the bundled debugger is `JaneySprings/clrdbg` - all personal accounts, so one of them compromised is
enough to ship a malicious version.

Decided on 2026-10-16 to keep it as is for now. The risk is reduced by hardening how releases are made
instead. Every workflow action is pinned to a commit and mcp-publisher to a checksum; still to do:
release-publish-needs-no-approval, main-and-release-tags-unprotected, package-has-no-build-provenance,
nuget-id-prefix-not-reserved and clrdbg-releases-unattested-and-recreated.
