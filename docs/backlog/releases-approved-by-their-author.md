---
worth: yes
added: 2026-10-20
---
# one person writes, merges and approves every release

The `main` ruleset requires a pull request but 0 approving reviews, and the `release` environment's
only required reviewer is `nevse`, with `prevent_self_review` off. Whoever opens a pull request also
merges it and approves the release it ends up in, so the rulesets stop a mistake made outside a pull
request but nothing that one account, compromised or not, decides to ship.

The fix needs a second person rather than a setting: a reviewer in the `release` environment with
`prevent_self_review` on, and optionally one required approval on `main`. clrdbg's maintainer
(JaneySprings) is the natural candidate, since the package ships clrdbg's binaries anyway. Blocked on
finding that person, not on whether it is worth doing.

Supply-chain risk: a release reaches nuget.org on one account's word.
