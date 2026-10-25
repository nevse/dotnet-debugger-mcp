# Security policy

## Reporting a vulnerability

Report it privately through GitHub: open the repository's **Security** tab and choose **Report a
vulnerability**. Please do not open a public issue for it. Include the version (`dotnet tool list -g`
shows it), the operating system, and the steps or tool calls that reproduce it.

Only the latest release is supported; a fix ships as a new release rather than as a patch to an older one.

## Trust model

This server is a debugger, not a sandbox. It runs as the user who started it, and it is driven by an MCP
client - usually an AI agent - that this user has chosen to trust. Whatever the client can make the
server do, it does with that user's privileges. The trust boundary is the deployment: which user the
server runs as, which client it is given to, and what that user can reach.

Two capabilities come with being a debugger and cannot be removed without defeating its purpose:

- **Running programs.** `launch_program` starts a .NET app, and `build_mobile_app` builds a project,
  which runs whatever its MSBuild files contain. Native executables and environment variables that load
  other code are refused, but a .NET app can do anything its user can, and the SDK itself is one. See
  [What `launch_program` will run](README.md#what-launch_program-will-run).
- **Running code inside the debuggee.** `evaluate_expression` calls whatever the expression calls, and
  listing variables runs property getters and `ToString` overrides. A breakpoint condition is an
  expression evaluated at every hit. Whether an expression is harmless cannot be decided in general, and
  the server does not try.

What a debugger shows is also what the debuggee holds, credentials included. Values that look like
secrets are masked before they reach the client (see
[Secrets in what the debugger shows](README.md#secrets-in-what-the-debugger-shows)). That masking keeps
credentials out of model context and transcripts; it is not a boundary against an agent set on reading
them, which can compute over a secret instead of printing it.

To limit what a client can reach, limit the server:

- **Treat tool approval as shell approval.** Confirm `launch_program`, `evaluate_expression`,
  `build_mobile_app`, `attach_to_process` and breakpoints with conditions instead of approving them
  automatically. These tools are annotated as destructive, so a client that honours annotations can ask
  for them alone.
- **Run it as a user with access to the code being debugged and nothing else** - no SSH keys, cloud
  credentials or other secrets - or inside a container with only the workspace mounted.
- **Do not give it to a client you trust less than that user.** Content the agent reads, such as source
  files, program output or web pages, can carry instructions, so "the agent is mine" is not the same as
  "everything the agent reads is mine".
- **Keep other users' processes out of reach.** Attaching to them is refused unless
  `DOTNET_DEBUGGER_MCP_ALLOW_OTHER_USER_PROCESSES` is set.

## In scope

- A way for the client to do something the server documents it refuses: launching a native executable,
  passing a refused environment variable, attaching to another user's process with the setting off,
  turning secret masking off.
- A secret in a shape the masking claims to catch reaching the client unmasked.
- The server writing debuggee values or program output somewhere other than the client - logs, files -
  unmasked.
- The release pipeline: tampering with the published NuGet package, the bundled debugger it downloads,
  or the GitHub Actions workflows that build them.

## Out of scope

- That `evaluate_expression`, a breakpoint condition or a launched program can read files, open network
  connections or run arbitrary code with the user's privileges. That is the trust model above.
- That the client can read any value the debuggee holds. Masking reduces exposure; it does not claim to
  prevent it.
- Defects in the underlying debugger, clrdbg, which belong with its maintainer
  ([JaneySprings/clrdbg](https://github.com/JaneySprings/clrdbg)).
- Anything requiring an attacker who can already run code as the user the server runs as.
