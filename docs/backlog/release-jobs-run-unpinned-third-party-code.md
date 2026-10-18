---
worth: yes
where: .github/workflows/ci.yml:332
added: 2026-10-16
---
# release jobs run unpinned third-party code

The `package` and `registry` jobs hold `id-token: write`, which is all it takes to get a nuget.org
key from trusted publishing and to publish to the MCP registry. Yet both run code nobody pinned:

- every action is referenced by a movable tag - `NuGet/login@v1`, `softprops/action-gh-release@v3`,
  `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/upload-artifact@v4`,
  `actions/download-artifact@v4`. A retagged or compromised action runs with the publishing token;
- `Install mcp-publisher` (line 332) downloads `releases/latest/download/mcp-publisher_*.tar.gz` and
  runs it with no version and no checksum.

Fix: pin every action to a full commit SHA with the tag in a comment (Dependabot keeps them current),
pin mcp-publisher to a version and verify its SHA-256. CodeQL's extended suite reports the two
third-party ones in `package` as `actions/unpinned-tag` (`NuGet/login`, `softprops/action-gh-release`);
it does not report GitHub's own `actions/*` or the mcp-publisher download, so a clean result there
does not cover those.
