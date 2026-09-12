"""Write the version which a release is being made of into the files which carry one.

The version is the one the release was tagged with, and it is written into every file which names a version of its own
rather than being handed to the build: a package of NuGet takes its version from `Build/Package.props`, and a package of
Unity takes its version from the `package.json` which is packed, which is the one OpenUPM reads as well.

    python .github/scripts/set-version.py 1.2.3
"""

import pathlib
import re
import sys

# A version of the shape this repository releases: three numbers, and a pre-release suffix when there is one.
VERSION = re.compile(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?")

# What a version is written into: the text before it, the version itself, and the text after it.
REPLACEMENTS = [
    ("Build/Package.props", r"(<Version>)([^<]*)(</Version>)"),
    ("Build/Package.props", r"(<AssemblyVersion>)([^<]*)(</AssemblyVersion>)"),
    ("Build/Package.props", r"(<FileVersion>)([^<]*)(</FileVersion>)"),
    ("Inject/Unity/package.json", r'("version"\s*:\s*")([^"]*)(")'),
    ("Aspect/Unity/package.json", r'("version"\s*:\s*")([^"]*)(")'),
    # The aspect package names the inject one, and it names a version of it: a package which is installed with the
    # version of the release before it carries a weaver which the release it belongs to never published.
    ("Aspect/Unity/package.json", r'("com\.gatongone\.gneedle\.inject"\s*:\s*")([^"]*)(")'),
]


def write(relative: str, pattern: str, version: str) -> None:
    """Write the version over what the pattern matched in the file, which is the one match which it has to have."""
    path = pathlib.Path(relative)
    text = path.read_text(encoding="utf-8")

    replaced, found = re.subn(pattern, lambda match: match.group(1) + version + match.group(3), text)
    if found != 1:
        raise SystemExit(f"{relative}: {pattern!r} matched {found} times rather than once")

    # Neither file ends with a newline of its own, so the text is written back exactly as it was read.
    with path.open("w", encoding="utf-8", newline="") as handle:
        handle.write(replaced)


def main() -> None:
    if len(sys.argv) != 2 or not VERSION.fullmatch(sys.argv[1]):
        raise SystemExit("usage: set-version.py <version>, where the version is one like 1.2.3 or 1.2.3-rc.1")

    version = sys.argv[1]
    for relative, pattern in REPLACEMENTS:
        write(relative, pattern, version)
        print(f"{relative}: {version}")


if __name__ == "__main__":
    main()
