---
worth: yes
added: 2026-10-15
---
# clrdbg's RemoteCoreClrLibraries.zip is uploaded by hand

In clrdbg release 18.0.0 the adapter zips were uploaded by `github-actions[bot]`, but
`RemoteCoreClrLibraries.zip` - which this server downloads and packs, pinned by SHA-256 in
`Directory.Build.props` - was uploaded by `JaneySprings`. clrdbg's `remote.yml` is `workflow_dispatch`
only and produces artifacts, not release assets, so nothing ties the zip to a source commit. Upstream
fix: build and attach it from CI, ideally with `actions/attest-build-provenance`. Supply-chain risk: a
binary in the package that cannot be traced to its source.
