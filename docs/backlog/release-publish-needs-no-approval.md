---
worth: yes
where: .github/workflows/ci.yml:160
added: 2026-10-16
---
# publishing a release needs nobody's approval

Creating a GitHub release starts `package`, which pushes to nuget.org with no further step. Anyone who
can create a release - or a stolen token that can - publishes a package every user of the tool will
run. The nuget.org trusted-publishing policy names the repository and `ci.yml` but no environment
(docs/RELEASING.md), so it cannot tell an approved run from any other run of that workflow.

Fix: run `package` and `registry` in a GitHub Environment (`release`) with a required reviewer, and
name that environment in the nuget.org trusted-publishing policy, so a key is only issued to a run
someone approved. Update the policy table in docs/RELEASING.md.
