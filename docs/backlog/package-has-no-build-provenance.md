---
worth: yes
where: .github/workflows/ci.yml:254
added: 2026-10-16
---
# the published package carries no build provenance

The `.nupkg` is neither author-signed nor attested - nuget.org's repository signature only proves it
came through nuget.org. Nobody consuming it can check that it was built by `ci.yml` from a given commit
of this repository rather than pushed from somewhere else with the same key.

Fix: after `Pack`, run `actions/attest-build-provenance` over `./artifacts/*.nupkg` (needs
`attestations: write`), and document `gh attestation verify <nupkg> --repo nevse/dotnet-debugger-mcp`
in the README for anyone mirroring the package. The bundled clrdbg libraries need the same upstream:
see clrdbg-releases-unattested-and-recreated.
