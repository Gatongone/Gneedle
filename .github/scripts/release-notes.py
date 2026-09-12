"""Write the notes of a release from the commits which it holds.

The commits between the tag before this one and this one are read, and each is grouped by the kind which the prefix of
its subject names, which is the convention every commit of this repository follows. The notes are written to standard
output, which the workflow hands to `gh release edit`.

    python .github/scripts/release-notes.py v1.2.3
"""

import os
import re
import subprocess
import sys
import urllib.parse

# The heading which the kind of a commit is written under, in the order the headings are written.
KINDS = [
    ("feat", "Added"),
    ("fix", "Fixed"),
    ("perf", "Performance"),
    ("refactor", "Changed"),
    ("docs", "Documentation"),
    ("test", "Tests"),
    ("build", "Build"),
    ("chore", "Chores"),
    ("style", "Style"),
]

SUBJECT = re.compile(r"^(?P<kind>[a-z]+)(?:\([^)]*\))?!?:\s*(?P<text>.+)$")


def git(*arguments: str) -> str:
    """Run git and hand back what it wrote, which is the one thing this reads the history through."""
    return subprocess.run(["git", *arguments], check=True, capture_output=True, text=True).stdout


def previous_tag(tag: str) -> str | None:
    """The tag which was released before this one, or None when this is the first release of the repository."""
    try:
        return git("describe", "--tags", "--abbrev=0", f"{tag}^").strip() or None
    except subprocess.CalledProcessError:
        return None


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: release-notes.py <tag>")

    tag = sys.argv[1]
    repository = os.environ.get("GITHUB_REPOSITORY", "")
    previous = previous_tag(tag)
    commits = git("log", "--no-merges", "--format=%H%x09%s", f"{previous}..{tag}" if previous else tag).splitlines()

    grouped: dict[str, list[str]] = {heading: [] for _, heading in KINDS}
    other: list[str] = []
    for line in commits:
        if "\t" not in line:
            continue

        sha, subject = line.split("\t", 1)
        match = SUBJECT.match(subject)
        text = match.group("text") if match else subject
        text = text[0].upper() + text[1:] if text else text
        entry = f"- {text} ([{sha[:7]}](https://github.com/{repository}/commit/{sha}))"

        heading = next((heading for kind, heading in KINDS if match and match.group("kind") == kind), None)
        (grouped[heading] if heading else other).append(entry)

    lines: list[str] = []
    for _, heading in KINDS:
        if grouped[heading]:
            lines += [f"## {heading}", "", *grouped[heading], ""]
    if other:
        lines += ["## Other", "", *other, ""]

    if previous:
        compare = f"https://github.com/{repository}/compare/{urllib.parse.quote(previous)}...{urllib.parse.quote(tag)}"
        lines += [f"**Full Changelog**: {compare}", ""]

    if not lines:
        lines = ["No changes."]

    print("\n".join(lines))


if __name__ == "__main__":
    main()
