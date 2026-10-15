---
worth: yes
where: README.md:64
added: 2026-10-15
---
# the recommended install command is unpinned

README.md (lines 64 and 460) and `docs/nuget-readme.md:23` recommend
`dotnet tool exec DotnetDebugger.Mcp --yes`, which takes the latest version (or whatever is cached).
Supply-chain risk: a new release reaches every user on the next start without anyone choosing it. Make
`--version X.Y.Z` the default form - which means the docs need bumping every release, or a placeholder
the release workflow stamps. The upgrade example at line 117 still names 0.1.1.
