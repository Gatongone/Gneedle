"""Write the version which a release is being made of into the files which carry one, or read it back.

A version is named by the tag of the release, and each file which carries one of its own is where its readers find it: a
package of NuGet takes its version from `Build/Package.props`, and a package of Unity takes its version from the
`package.json` which is packed, which is the one OpenUPM reads out of the tree of the tag as well. So the version has to
be in the tree of the tag before the tag is made, and this is what writes it there:

    python .github/scripts/set-version.py 1.2.3

What the release run asks of it instead is that the tree of the tag names the version of the tag, because a tag which
names a version its own tree does not carry is a release which OpenUPM publishes under the version that tree carries:

    python .github/scripts/set-version.py --check 1.2.3
"""

import pathlib
import re
import sys

# A version of the shape this repository releases: three numbers, and a pre-release suffix when there is one.
VERSION = re.compile(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?")

# What a version is written into: the text before it, the version itself, and the text after it.
VERSIONS = [
    ("Build/Package.props", r"(<Version>)([^<]*)(</Version>)", "<Version>"),
    ("Build/Package.props", r"(<AssemblyVersion>)([^<]*)(</AssemblyVersion>)", "<AssemblyVersion>"),
    ("Build/Package.props", r"(<FileVersion>)([^<]*)(</FileVersion>)", "<FileVersion>"),
    ("Inject/Unity/package.json", r'("version"\s*:\s*")([^"]*)(")', "package.json version"),
    ("Aspect/Unity/package.json", r'("version"\s*:\s*")([^"]*)(")', "package.json version"),
    # The aspect package names the inject one, and it names a version of it: a package which is installed with the
    # version of the release before it carries a weaver which the release it belongs to never published.
    ("Aspect/Unity/package.json", r'("com\.gatongone\.gneedle\.inject"\s*:\s*")([^"]*)(")', "com.gatongone.gneedle.inject"),
]


def read(relative: str, pattern: str) -> str:
    """The version which the file names, which the pattern has to match once."""
    text = pathlib.Path(relative).read_text(encoding="utf-8")
    matches = re.findall(pattern, text)
    if len(matches) != 1:
        raise SystemExit(f"{relative}: {pattern!r} matched {len(matches)} times rather than once")
    return matches[0][1]


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
    arguments = sys.argv[1:]
    checking = arguments[:1] == ["--check"]
    if checking:
        arguments = arguments[1:]

    if len(arguments) != 1 or not VERSION.fullmatch(arguments[0]):
        raise SystemExit("usage: set-version.py [--check] <version>, where the version is one like 1.2.3 or 1.2.3-rc.1")

    version = arguments[0]
    for relative, pattern, name in VERSIONS:
        if checking:
            held = read(relative, pattern)
            if held != version:
                raise SystemExit(f"{relative}: {name} is {held}, and the release is {version}")
        else:
            write(relative, pattern, version)

    print(f"every file names {version}" if checking else f"every file was written {version}")


if __name__ == "__main__":
    main()
