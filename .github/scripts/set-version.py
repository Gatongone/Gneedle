"""Write the version which a release is being made of into the files which carry one, or read it back.

A version is named by the tag of the release, and each file which carries one of its own is where its readers find it: a
package of NuGet takes its version from `Build/Package.props`, a package of Unity takes its version from the
`package.json` which is packed, which is the one OpenUPM reads out of the tree of the tag as well, and the readme of each
says at which version to install it. So the version has to be in the tree of the tag before the tag is made, and this is
what writes it there:

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

# The placeholders of a readme, which are the three shapes a reader copies a version out of: the version of a package
# of NuGet, the revision of one which is taken out of the repository, which is the tag its URL ends at, and the version
# of one which is taken out of a registry. The name of a package is what each of them is anchored by, so that a version
# the prose around them names is left as its author wrote it - and a URL is not taken for a version, because the
# revision of one is written after the `#v` which the tag it ends at is named by.
NUGET_PLACEHOLDER = r'(<PackageReference Include="Gneedle\.(?:Aspect|Inject)" Version=")(%s)(")' % VERSION.pattern
REVISION_PLACEHOLDER = (r'("com\.gatongone\.gneedle\.(?:inject|aspect)"\s*:\s*'
                        r'"https://github\.com/Gatongone/Gneedle\.git\?path=[A-Za-z]+/Unity#v)(%s)(")' % VERSION.pattern)
REGISTRY_PLACEHOLDER = r'("com\.gatongone\.gneedle\.(?:inject|aspect)"\s*:\s*")(%s)(")' % VERSION.pattern

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
    # The readmes, which are read by whoever is deciding what to install: one which is written by hand names the
    # release before the one being made from the moment that one is made, and the tag it is read at is one which the
    # release moved, so what it says has to be written here, before the commit which the tag is moved onto.
    ("README.md", NUGET_PLACEHOLDER, "the version of a package of NuGet"),
    ("README.md", REVISION_PLACEHOLDER, "the revision of a package of the repository"),
    ("README.md", REGISTRY_PLACEHOLDER, "the version of a package of a registry"),
    ("Inject/Unity/README.md", REVISION_PLACEHOLDER, "the revision of a package of the repository"),
    ("Inject/Unity/README.md", REGISTRY_PLACEHOLDER, "the version of a package of a registry"),
    ("Aspect/Unity/README.md", REVISION_PLACEHOLDER, "the revision of a package of the repository"),
    ("Aspect/Unity/README.md", REGISTRY_PLACEHOLDER, "the version of a package of a registry"),
]


def text_of(relative: str) -> str:
    """The text of the file, read as it lies.

    No line ending is translated, because what is written back is the text which was read but for the version: a file
    of this repository lies with the line endings of the machine it was checked out on, which are none of this script's
    business, and one which was read with them translated would be written back with every line ending of it changed.
    """
    with pathlib.Path(relative).open("r", encoding="utf-8", newline="") as handle:
        return handle.read()


def read(relative: str, pattern: str) -> str:
    """The version which the file names, of which the pattern has to match, and to name one version with every match.

    A pattern is anchored by the name of a member or of a package, and a file may name one of them more than once: a
    readme which shows the several ways to install the same package names a version of it in each of them, and every
    one of them is where a reader who takes that way finds it. So what is asked of a file is that it names one version
    rather than that it names it once.
    """
    text = text_of(relative)
    matches = re.findall(pattern, text)
    if not matches:
        raise SystemExit(f"{relative}: {pattern!r} matched nowhere")

    versions = {match[1] for match in matches}
    if len(versions) != 1:
        raise SystemExit(f"{relative}: {pattern!r} names {', '.join(sorted(versions))} rather than one version")
    return versions.pop()


def write(relative: str, pattern: str, version: str) -> None:
    """Write the version over every match of the pattern in the file, which has to match at least once."""
    path = pathlib.Path(relative)
    text = text_of(relative)

    replaced, found = re.subn(pattern, lambda match: match.group(1) + version + match.group(3), text)
    if not found:
        raise SystemExit(f"{relative}: {pattern!r} matched nowhere")

    # The text is written back exactly as it was read, line endings and the newline it ends with alike.
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
