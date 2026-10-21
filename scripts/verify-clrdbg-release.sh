#!/bin/bash
# Check that the clrdbg release this repository pins is what clrdbg's own release workflow built from
# its tag. The build already checks both zips against the SHA-256 in Directory.Build.props; this ties
# those bytes to their source, so a version bump cannot pin a file nobody can trace back to a commit.
#
# Fetches the zips through the same MSBuild target the build uses, so it checks the very files that
# get packed, and works whether or not a build ran before it. Needs gh signed in, or GH_TOKEN set.
#
# Usage: ./scripts/verify-clrdbg-release.sh
set -euo pipefail

root=$(git rev-parse --show-toplevel)
cd "$root"

project=src/DotnetDebugger.Mcp/DotnetDebugger.Mcp.csproj
version=$(dotnet msbuild "$project" -getProperty:ClrdbgVersion)
cache=$(dotnet msbuild "$project" -t:FetchClrdbgRelease -getProperty:ClrdbgReleaseCache)

for zip in clrdbg_universal.zip RemoteCoreClrLibraries.zip; do
    # The signer and the ref narrow it from "some workflow in that repository attested it" to the
    # release workflow, building this tag
    gh attestation verify "$cache/$zip" \
        --repo JaneySprings/clrdbg \
        --signer-workflow JaneySprings/clrdbg/.github/workflows/build.yml \
        --source-ref "refs/tags/$version"
done
