---
worth: yes
added: 2026-10-15
---
# clrdbg's releases carry no provenance, and 18.0.0 was recreated in place

Since clrdbg f4b48ef its `build.yml` builds `RemoteCoreClrLibraries.zip` from source and attaches it to
the release along with the adapter zips, so the hand upload this item started from is gone. What
remains is upstream:

- nothing attests the release assets, so a consumer cannot check which commit and workflow produced
  them. `actions/attest-build-provenance` in `build.yml` would.
- release 18.0.0 was deleted and recreated on 2026-10-17 on another commit, with every asset rebuilt
  under the same version. The SHA-256 pin in `Directory.Build.props` caught it - every clean build here
  failed until #17 followed - but anyone pinning the version alone silently got different binaries.
  It happened again on 2026-10-18, on 003212a. A new version (18.0.1) instead of a recreated one
  would avoid both.
- nothing stops a tag from moving: `build.yml` creates a release for any pushed tag, and clrdbg has
  no rulesets, so neither its tags nor `main` are protected and commits land on `main` directly.
- its workflows reference actions by tag (`actions/checkout@v4`, `actions/upload-artifact@v4`,
  `github/codeql-action/init@v4` and others), so a moved action tag changes what builds the assets.
  The workflows here pin every action to a SHA.

The fixes to the workflows can come as a pull request; the rulesets need clrdbg's admin. With
attestations in place, CI here could run `gh attestation verify --repo JaneySprings/clrdbg` on the zip
on top of the hash.

Until that changes, each recreation is checked by hand before the pin moves: the tag's commit, the
Build run on it that uploaded the assets, and the zip's files against that run's artifacts. Nothing
upstream makes that check possible without trusting the run's logs.

Supply-chain risk: binaries in our package that cannot be traced to their source, under a version that
does not identify them.
