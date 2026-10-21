# Releasing

A release is a git tag and a GitHub Release. `ci.yml` does the rest in two jobs: `package` reads the
version from the tag, stamps it into `.mcp/server.json`, packs and pushes to nuget.org; `registry`
then publishes that same manifest to the MCP Registry.

The package carries the debug adapter, built from the `external/clrdbg` submodule during the pack, so
every job in `ci.yml` checks the repository out with `submodules: true`. A checkout without them fails
the build rather than shipping a package with no debugger in it.

Nothing here holds a NuGet API key. Publishing uses
[trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing): the
workflow asks GitHub for an OIDC token, nuget.org validates it against a policy naming this
repository and the `release` environment, and issues a key that lives one hour. The `package` job runs
in that environment, which waits for a maintainer to approve the run, so nothing reaches nuget.org that
nobody approved.

## One-time setup on nuget.org

Sign in, then **your username → Trusted Publishing**, and add a policy:

| field | value |
|---|---|
| Repository Owner | `nevse` |
| Repository | `dotnet-debugger-mcp` |
| Workflow File | `ci.yml` — the file name only, no `.github/workflows/` prefix |
| Environment | `release` |
| Scopes | Push → **Push only new package versions**; unlist/relist off |
| Packages | `DotnetDebugger.Mcp` — the exact ID, no glob |

The narrowest policy that publishes: a key it issues can push new versions of this one package and
nothing else under the account. The very first release needed "Push new packages and package
versions" instead, because the ID did not exist yet.

Then add one repository secret, **`NUGET_USER`**: your nuget.org *profile name*, not the email you
sign in with. It is not sensitive; it lives in a secret because that is what NuGet's own example does
and it keeps the workflow free of a personal detail.

A policy for a private repository starts out active for seven days only, and becomes permanent on the
first successful publish — that push is what gives nuget.org the immutable GitHub owner and repository
ids that pin the policy against someone deleting the repository and recreating it under the same name.
This repository is public, so that window should not apply, but the status is visible in the same UI
if a publish is ever refused for no other apparent reason.

## One-time setup on GitHub

**The `release` environment** (Settings → Environments), which the `package` job runs in:

- Required reviewers: the maintainers. Self-review is allowed, since there is one maintainer.
- Deployment branches and tags: selected only, with the tag rule `v*`. A release runs on the tag, so a
  run from any branch is refused before approval is even asked for.

Create it before any workflow names it: a job naming an environment that does not exist creates it,
with no protection at all.

**Rulesets** (Settings → Rules):

- `main`, on the default branch, with no bypass: a pull request is required (no approvals, since a
  maintainer cannot approve their own), the build, test, integration and Code Quality checks must
  pass, CodeQL must report no new alert of high severity or above and no error, and force pushes and
  deletion are blocked. Everything reaches `main` through a pull request, the README bump included.
- `release tags`, on `refs/tags/v*`: tags cannot be moved or deleted once pushed, so a released
  version always names the commit it was built from.

CodeQL runs as default setup (Settings → Code security), with the extended query suite.

**Auto-merge** (Settings → General → Allow auto-merge) is on, which `scripts/release.sh` uses for the
README pull request.

## Cutting a release

1. Decide the version and write the release notes to a file. The tag carries a leading `v`; the
   package version is the tag without it, so `v0.1.0` publishes `0.1.0`.
2. From an up-to-date `main` with no uncommitted changes, run:

   ```bash
   ./scripts/release.sh 0.1.0 notes.md
   ```

   It opens a pull request pinning `0.1.0` in every `README.md` install command, with auto-merge on,
   waits for the required checks to pass and the pull request to merge - a few minutes - then puts an
   annotated `v0.1.0` tag on that merge commit, pushes it, and creates the GitHub Release with the
   notes. **`ci.yml` publishes on the release, not on the tag.** It runs as you through `gh`: the
   pull request's checks and the release's workflow only start for something a person created, not
   for what a workflow creates with its own token.
3. Watch `ci.yml`. `Package` waits for the unit and integration jobs, then for approval: open the
   run and **Review deployments → release → Approve**. It then checks that `README.md` pins this
   version, packs, attests the package's build provenance and does the NuGet push; `MCP Registry`
   runs after it and spends most of its time waiting for nuget.org to finish validating that push.
4. Check the published package's provenance, on the copy nuget.org serves:

   ```bash
   curl -fsSLo DotnetDebugger.Mcp.0.1.0.nupkg \
     https://api.nuget.org/v3-flatcontainer/dotnetdebugger.mcp/0.1.0/dotnetdebugger.mcp.0.1.0.nupkg
   gh attestation verify DotnetDebugger.Mcp.0.1.0.nupkg --repo nevse/dotnet-debugger-mcp
   ```

   There are two attestations per release. `package` attests the `.nupkg` as built, the copy
   attached to the GitHub Release. nuget.org then adds its repository signature, which changes the
   file, so `registry` downloads nuget.org's copy, checks that every entry but `.signature.p7s`
   matches the build by name, CRC and size, and attests that copy as well.

Doing it by hand is the same steps: a pull request running `python3 scripts/readme-version.py bump
0.1.0`, then, once it has merged, `git tag -a v0.1.0 -m "Version 0.1.0"` on the merge commit, `git push
origin v0.1.0` and the GitHub Release. The `package` job runs `readme-version.py check` and stops a
release whose version `README.md` does not pin. `readme-version.py lint`, which `check` also runs and
the Code Quality job runs on every change, refuses an install command in either README that names no
version at all.

If the script stops partway, nothing after that point happened. A pull request that did not merge
leaves no tag; one that merged leaves `README.md` ahead of the release, and the tag and release can be
finished by hand as above.

The push uses `--skip-duplicate`, so re-running a release that already published is harmless.

## The MCP Registry

Automatic since 0.1.1, and part of `ci.yml` since 0.3.0: the `registry` job needs nothing from you.
It authenticates with `mcp-publisher login github-oidc` — the same OIDC exchange the NuGet push uses,
so there is no second secret — and publishes the very `server.json` the `package` job stamped and
packed, handed over as a build artifact rather than stamped a second time. The registry entry and the
package therefore cannot claim different versions.

The one-time namespace claim is done. `io.github.nevse/dotnet-debugger-mcp` was published by hand on
15 August 2026 for 0.1.0, which established `io.github.nevse/*`. What the registry holds now:

```bash
curl -s "https://registry.modelcontextprotocol.io/v0/servers?search=io.github.nevse"
```

**Why it waits, and why it waits after the push rather than beside it.** The registry proves we own
the name by reading the `mcp-name` marker out of the package README *on nuget.org*, so there is
nothing to verify until nuget has finished validating the push — several minutes for 0.1.1. The job
polls `https://api.nuget.org/v3-flatcontainer/dotnetdebugger.mcp/<version>/readme` for up to 20
minutes against a 25-minute job timeout.

It used to be a workflow of its own, triggered by the same release, so it started polling while
`ci.yml` was still building and spent part of that budget on a package that had not been pushed yet.
That is what cancelled the 0.2.0 registry run on its timeout while the README was still on its way.
`needs: [package]` costs nothing — the polling never made nuget any faster — and the timeout now
covers nuget's validation alone.

The marker itself is in `docs/nuget-readme.md`, which opens with
`<!-- mcp-name: io.github.nevse/dotnet-debugger-mcp -->`, matching `name` in
`src/DotnetDebugger.Mcp/.mcp/server.json`. Changing either without the other breaks ownership verification.

### Re-publishing a version

`publish-registry.yml` is kept for exactly this, and no longer triggers on a release. Use it for a
version already on nuget.org whose registry entry is missing or stale — a `registry` job that failed,
or a release cut before any of this existed:

```bash
gh workflow run publish-registry.yml -f version=0.1.1
```

The version must already be live on nuget.org — the workflow waits for it rather than creating it.
This is how 0.1.1 was brought up to date after the workflow was added.

Publishing by hand needs `mcp-publisher login github`, a browser device flow, and the real version
written into the manifest first: the committed `server.json` carries `0.0.0-dev`, and both CI paths
rewrite it. Prefer the dispatch above.
