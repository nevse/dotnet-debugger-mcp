# DotnetDebugger.Mcp — review profile

## What it is

- An MCP server in C# (.NET 10) that gives an AI agent a real .NET debugger. It can attach or launch,
  set breakpoints, step, read locals, evaluate expressions, and debug MAUI apps on Android, iOS and
  Mac Catalyst.
- Shipped as one RID-agnostic NuGet .NET tool, `DotnetDebugger.Mcp`, run through `dnx` or
  `dotnet tool exec`, and listed in the MCP Registry.
- Drives clrdbg (JaneySprings/clrdbg) as a child process over the Debug Adapter Protocol
  (`Debugging/ChildProcessDebugAdapter.cs`).
  - The adapter and the remote CoreCLR libraries come from a pinned clrdbg release: `ClrdbgVersion`
    and a SHA-256 per zip in `Directory.Build.props`, with build provenance checked in CI.
  - `ClrdbgSourcePath` builds the adapter from a local clone instead.
- Public open source with one maintainer. Runs on macOS, Linux and Windows, and CI covers all three.
  Users run it locally under an MCP client, against programs they chose to debug.

## What a real failure looks like

- **Hangs.** A tool call hangs the server or the session: a DAP request with no bound, a lock held
  across `await`, a nested MSBuild that deadlocks.
- **Crashes and leaks.** A native crash in the debugger takes the server down with it, or a debuggee is
  left orphaned or suspended after a detach, an exit or a dispose.
- **Wrong answers the agent acts on.** A stale frame id, the wrong thread, a value from the wrong frame,
  an error explained as the wrong cause, or a tool that reports success when it did nothing (such as an
  unbound breakpoint).
- **Packaging.** A package missing `clrdbg/` or `remote-coreclr/`, or a build that only works with a
  warm `obj/` cache or on the author's platform.
- **Weakened checks.** A pin or check that silently stops checking: the release hash, `gh attestation
  verify`, the README version pin.
- **Release path.** Anything that weakens the `ci.yml` package or registry jobs: an action not pinned
  to a commit, broader token permissions, or a skipped verification step.

## Blast radius

- **Server and tool code:** every user of the published package, on every platform. The agent acts on
  what the tools return.
- **Build, CI and release:** every contributor, and the next release. nuget.org never takes a version
  twice, so a bad release can only be replaced by a new version.
- **Docs** (README, CONTRIBUTING.md, `.github/BUILD_CONFIGURATION.md`, `docs/RELEASING.md`): people and
  agents follow them literally.

## Reporting bar

- **Report:**
  - correctness bugs, hangs, process or handle leaks;
  - platform-specific breakage (path separators, case sensitivity, Windows vs Unix shells);
  - anything that weakens the security or supply-chain measures listed above;
  - docs or comments that contradict what the code does.
- **Known risks.** Accepted and deferred risks live in `docs/backlog/`, one file per item with a
  `worth: yes|later|no` front matter. Do not report one as new unless the change makes it worse.
- **Powerful by design.** The server runs and evaluates arbitrary code in a debuggee the user chose.
  "`evaluate_expression` can run code" or "`launch_program` starts a process" is not a finding.
- **Severity cap.** An edge case that is only local and needs several unlikely steps in a row is minor
  at most. CI and the release start from a clean checkout.
- **Noise:**
  - style beyond `.editorconfig` and `dotnet format`;
  - naming taste;
  - proposals for new abstractions;
  - "add logging";
  - preferences about test layout;
  - comment wording that does not change the meaning.

## Where the rules live

- `CONTRIBUTING.md`:
  - coding standards: no `#region`, nullable enabled, async for I/O, locks for shared state,
    `_camelCase` private fields, PascalCase constants, XML docs on public APIs;
  - how to add a tool or a configuration option;
  - MSTest;
  - commit prefixes.
- **CI gates:** `.editorconfig` with `dotnet format --verify-no-changes`, and `dotnet build
  /p:TreatWarningsAsErrors=true`.
- `.github/BUILD_CONFIGURATION.md`: central package management, the clrdbg release pin, and the MSBuild
  targets.
- `docs/RELEASING.md`: the release flow, trusted publishing and the `release` environment.
- `docs/backlog/`: deferred work and the format of its items.

Breaking any of these rules is always worth reporting.

## Deliberate, not findings

- **Long comments.** Comments are dense and explain why, often with measured numbers and history (see
  `Directory.Build.targets`, `ci.yml`). Their length is intended. Report a comment only when it is
  wrong or out of date.
- **Pinning.** Every GitHub Action is pinned to a commit SHA, with its version in a comment. Every
  install command in the docs names a version.
- **Workflow rules.** `main` takes changes only through pull requests, and `ci.yml` jobs default to
  `contents: read`.
- **Integration tests** (`TestCategory=Integration`) drive a real debuggee. A known `libmscordbi`
  segfault on macOS is retried in CI on purpose.
- **Backlog items** describe the risk impersonally and never cite who found it or which review.
- **HRESULT names.** `Tools/DebuggerErrors.cs` keeps the `CORDBG_E_*` names from corerror.h instead
  of PascalCase, so they can be searched for in the runtime's sources. This is the one intended
  exception to the naming rule.
