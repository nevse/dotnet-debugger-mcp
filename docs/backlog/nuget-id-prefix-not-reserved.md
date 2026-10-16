---
worth: later
added: 2026-10-16
---
# the DotnetDebugger NuGet ID prefix is not reserved

`DotnetDebugger.Mcp` shows no verified checkmark on nuget.org and anyone can publish
`DotnetDebugger.*` packages next to it, which makes a look-alike package easy. Prefix reservation
fixes both.

Unknown that settles it: whether nuget.org grants a reservation to an individual owner for a
single-package project - its criteria favour identifiable organizations. If the request is refused,
drop this.
