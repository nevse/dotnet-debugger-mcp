---
worth: yes
added: 2026-10-16
---
# main and release tags are unprotected

`main` has no branch protection, and the repository's `main` ruleset exists but is disabled. Release
tags `v*` are not protected either. So a push to `main` needs no review, history can be force-pushed,
and a release tag can be moved or recreated to point at another commit before or after publishing.

Fix: enable the ruleset for `main` (pull request required, no force push, no deletion, required status
checks) and add a tag ruleset for `v*` (no update, no deletion, creation restricted to maintainers).
Repository settings only, nothing in the tree; record the setup in docs/RELEASING.md.
