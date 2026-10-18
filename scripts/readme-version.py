#!/usr/bin/env python3
"""The version README.md pins in its install commands, checked or bumped in one place.

The release workflow runs `check` to refuse a release whose version the README does not pin;
release.sh runs `bump` to change it in the pull request that precedes the tag. Both read the pins
the same way, so a command added to the README in a new shape is either seen by both or by neither.

Usage: readme-version.py check <version>
       readme-version.py bump <version>
"""
import pathlib
import re
import sys

README = pathlib.Path(__file__).resolve().parent.parent / "README.md"

# `DotnetDebugger.Mcp --version 0.6.1` on a command line, and
# `"DotnetDebugger.Mcp", "--version", "0.6.1"` in a JSON args array
PIN = re.compile(r'(DotnetDebugger\.Mcp"?,? "?--version"?,? "?)([0-9][^\s"]*)')

# The Upgrading section quotes 0.1.1 as what was measured, which is history rather than advice
HISTORY = {"0.1.1"}


def pinned(text):
    return {match.group(2) for match in PIN.finditer(text)} - HISTORY


def main(argv):
    if len(argv) != 3 or argv[1] not in ("check", "bump") or not re.fullmatch(r"\d+\.\d+\.\d+", argv[2]):
        raise SystemExit(__doc__.split("Usage: ", 1)[1])

    command, version = argv[1], argv[2]
    text = README.read_text()
    current = pinned(text)

    if command == "check":
        if current != {version}:
            raise SystemExit(f"README.md pins {sorted(current)}, not {version}. Bump it before releasing.")
        return

    if len(current) != 1:
        raise SystemExit(f"README.md pins {sorted(current)}; expected exactly one version to bump from.")

    old = current.pop()
    bumped = PIN.sub(lambda m: m.group(1) + (version if m.group(2) == old else m.group(2)), text)
    README.write_text(bumped)
    print(f"README.md: {old} -> {version}")


if __name__ == "__main__":
    main(sys.argv)
