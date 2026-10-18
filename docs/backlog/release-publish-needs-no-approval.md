---
worth: yes
added: 2026-10-16
---
# nuget.org's trusted-publishing policy is not bound to the release environment

The `package` job now runs in the `release` GitHub environment, which waits for a maintainer's
approval and only runs for `v*` tags. But the nuget.org trusted-publishing policy still has
Environment empty, so nuget.org would issue a key to any run of `ci.yml` in this repository, approved
or not. The approval gate holds only as long as nobody edits the workflow to drop `environment:`.

Fix, on nuget.org only: **your username → Trusted Publishing → the policy for this repository →
Environment: `release`**. Nothing in the tree changes; docs/RELEASING.md already lists that value.
Delete this item once it is set.
