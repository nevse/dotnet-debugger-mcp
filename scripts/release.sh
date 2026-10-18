#!/bin/bash
# Cut a release: pin the version in README.md through a pull request, then tag and release the commit
# that merge produced. ci.yml then publishes it, once the run is approved in the release environment.
#
# main takes changes only through pull requests, so the README bump cannot be pushed; the pull request
# is opened with auto-merge and merges itself once the required checks pass. Runs as you, through gh,
# so the pull request's checks start and the release starts ci.yml - neither happens for something a
# workflow creates with its own token.
#
# Usage: ./scripts/release.sh <version> <notes-file>
#   version     X.Y.Z, without the leading v
#   notes-file  the release notes, in Markdown
set -euo pipefail

version=${1:-}
notes=${2:-}

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ || -z "$notes" ]]; then
    echo "Usage: $0 <version X.Y.Z> <notes-file>" >&2
    exit 2
fi

tag="v$version"
branch="release/$tag"
root=$(git rev-parse --show-toplevel)
cd "$root"

[ -f "$notes" ] || { echo "No release notes at $notes" >&2; exit 2; }
gh auth status >/dev/null 2>&1 || { echo "gh is not signed in; run 'gh auth login'" >&2; exit 1; }

echo "=== Checking the starting point ==="
[ "$(git branch --show-current)" = main ] || { echo "Run this on main" >&2; exit 1; }
# Untracked files are left alone: nothing below stages them
git diff --quiet && git diff --cached --quiet || { echo "Commit or stash your changes first" >&2; exit 1; }
git fetch --quiet origin main --tags
[ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] || { echo "main is not at origin/main; pull first" >&2; exit 1; }
if git rev-parse --quiet --verify "refs/tags/$tag" >/dev/null; then
    echo "$tag already exists" >&2
    exit 1
fi

echo "=== Pinning $version in README.md ==="
git switch --quiet --create "$branch"
python3 scripts/readme-version.py bump "$version"
git commit --quiet --message "docs: Pin $version in the README install commands" README.md
git push --quiet --set-upstream origin "$branch"

pr=$(gh pr create --base main --head "$branch" \
    --title "docs: Pin $version in the README install commands" \
    --body "Release $tag: pins the version every install command in README.md names. Opened by scripts/release.sh, which tags the merge commit once this merges.")
echo "$pr"
gh pr merge "$pr" --auto --squash --delete-branch

echo "=== Waiting for the required checks to pass and the pull request to merge ==="
# The checks take a few minutes; give up after an hour rather than wait forever on one that hangs
for _ in $(seq 1 120); do
    state=$(gh pr view "$pr" --json state --jq .state)
    [ "$state" = MERGED ] && break
    if [ "$state" = CLOSED ]; then
        echo "$pr was closed without merging; nothing was tagged" >&2
        exit 1
    fi
    sleep 30
done

if [ "$state" != MERGED ]; then
    echo "$pr has not merged after an hour; nothing was tagged. Check it, then rerun from main." >&2
    exit 1
fi

merge_commit=$(gh pr view "$pr" --json mergeCommit --jq .mergeCommit.oid)

echo "=== Tagging $merge_commit as $tag ==="
git switch --quiet main
git pull --quiet --ff-only origin main
[ "$(git rev-parse HEAD)" = "$merge_commit" ] || { echo "main moved past the bump; tag $merge_commit by hand" >&2; exit 1; }
python3 scripts/readme-version.py check "$version"
git branch --quiet --delete --force "$branch" 2>/dev/null || true

git tag --annotate "$tag" --message "Version $version" "$merge_commit"
git push --quiet origin "$tag"

echo "=== Creating the GitHub Release ==="
gh release create "$tag" --verify-tag --title "Version $version" --notes-file "$notes"

echo ""
echo "Released $tag. ci.yml now builds and tests it, then waits for approval:"
echo "open the run, Review deployments -> release -> Approve. The package job publishes after that."
