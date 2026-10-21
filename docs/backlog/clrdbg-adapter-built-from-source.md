---
worth: yes
where: Directory.Build.targets:21
added: 2026-10-21
---
# the clrdbg adapter is built from a submodule though its release ships it

`BuildClrdbgAdapter` compiles the adapter from the `external/clrdbg` submodule on every build. clrdbg's
release already carries the same thing: `clrdbg_universal.zip` is a framework-dependent publish with
`runtimes/*/native/libdbgshim` for every RID - for 18.0.0 the same files this build produces, 6.6 MB
zipped. The remote CoreCLR libraries already come from that release by version and hash, so the
adapter is the one part still built here.

Upstream fixed what used to stop us trusting those assets. Since clrdbg 4640321 its `build.yml` pins
every action to a SHA, refuses a tag that is not on `main`, releases from a `release` environment and
attests every zip with `actions/attest-build-provenance`; rulesets now cover `main` and release tags.
Before that, 18.0.0 was deleted and recreated in place twice (2026-10-17, 2026-10-18) - the SHA-256 pin
caught both times.

The switch:

- one `ClrdbgVersion` in `Directory.Build.props` for the adapter zip and `RemoteCoreClrLibraries.zip`,
  with a SHA-256 for each; a fetch target like `FetchRemoteCoreclrLibraries` replaces the build
- `BuildClrdbgAdapter` stays only when `ClrdbgSourcePath` is set, for working on a local clone of the
  fork
- `DebuggerErrors` is the only code compiled against clrdbg: 17 `Cor.CORDBG_E_*` constants through a
  `ProjectReference` to `DotNet.Debugging.CorApi`. Write the values out - they are runtime HRESULTs
  from corerror.h and do not change
- drop the submodule, `submodules: true` and `--exclude external/` from `ci.yml`, and the submodule
  steps in CONTRIBUTING and BUILD_CONFIGURATION
- in CI, `gh attestation verify --repo JaneySprings/clrdbg` on both zips on top of the hash

Wait for the release after 18.0.0: its assets predate the attestation step (`gh attestation verify`
answers 404), and clrdbg `main` is already 4 commits past it. Switching on 18.0.0 would need the old
hand check - tag commit, the Build run, files against its artifacts - with the 90-day artifact
retention as its deadline.

Cost of the switch: only released clrdbg reaches the package. A fix on its `main` waits for a release,
or is built locally through `ClrdbgSourcePath`.
