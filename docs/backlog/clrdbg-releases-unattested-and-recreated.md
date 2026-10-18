---
worth: yes
added: 2026-10-15
---
# clrdbg's releases carry no provenance, and 18.0.0 was recreated in place

Since clrdbg f4b48ef its `build.yml` builds `RemoteCoreClrLibraries.zip` from source and attaches it to
the release along with the adapter zips, so the hand upload this item started from is gone. Two things
remain, both upstream:

- nothing attests the release assets, so a consumer cannot check which commit and workflow produced
  them. `actions/attest-build-provenance` in `build.yml` would.
- release 18.0.0 was deleted and recreated on 2026-10-17 on another commit, with every asset rebuilt
  under the same version. The SHA-256 pin in `Directory.Build.props` caught it - every clean build here
  failed until #17 followed - but anyone pinning the version alone silently got different binaries.
  A new version (18.0.1) instead of a recreated one would avoid both.

Supply-chain risk: binaries in our package that cannot be traced to their source, under a version that
does not identify them.
