---
worth: yes
where: .github/workflows/ci.yml:340
added: 2026-10-16
---
# release jobs run unpinned code

The `package` and `registry` jobs hold `id-token: write`, which is all it takes to get a nuget.org
key from trusted publishing and to publish to the MCP registry. The two third-party actions there,
`NuGet/login` and `softprops/action-gh-release`, are pinned to a commit and kept current by
`.github/dependabot.yml`. What those jobs still run unpinned:

- GitHub's own actions by movable tag - `actions/checkout@v7`, `actions/setup-dotnet@v6`,
  `actions/upload-artifact@v4`, `actions/download-artifact@v4`. Lower risk than third-party ones,
  and CodeQL's extended suite does not report them, but a retagged release would still run with the
  publishing token;
- `Install mcp-publisher` (line 340) downloads `releases/latest/download/mcp-publisher_*.tar.gz` and
  runs it with no version and no checksum. Nothing reports this one.

Fix: pin the `actions/*` uses to commit SHAs as well (Dependabot already moves them), and pin
mcp-publisher to a version with its SHA-256 checked before it runs.
