"""Tests of the script which writes the version of a release into every file which names one.

What is kept here is that every file which names a version is written, and that what is written is written into the
placeholders a reader copies and nowhere else: a readme which the script does not write is a readme which names the
release before the one being made from the moment that one is made, and a version written where it does not belong is a
readme which says something its author did not.

The script is run against a copy of the files of this repository rather than against the repository itself, so that a
test run leaves the working tree as it was. What is copied is laid out at the paths the script reads by, because those
paths are relative to the directory it is run from.

    python .github/scripts/set-version-tests.py
"""

import importlib.util
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

# The script is imported rather than run, and importing it would leave a directory of bytecode beside it, which the
# release run commits along with everything else which is new in the tree.
sys.dont_write_bytecode = True

REPOSITORY = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = REPOSITORY / ".github" / "scripts" / "set-version.py"

# The files the script reads and writes, at the paths it reads them by.
FILES = [
    "Build/Package.props",
    "Inject/Unity/package.json",
    "Aspect/Unity/package.json",
    "README.md",
    "Inject/Unity/README.md",
    "Aspect/Unity/README.md",
]

# The readmes, which say the version a reader installs as well as the packages do.
READMES = ["README.md", "Inject/Unity/README.md", "Aspect/Unity/README.md"]

# The version which the repository names today, which is read out of the file the packages of NuGet are packed from.
CURRENT = re.search(r"<Version>([^<]*)</Version>",
                    (REPOSITORY / "Build" / "Package.props").read_text(encoding="utf-8")).group(1)

# The script under test, which is imported so that the patterns it writes with are the ones the tests assert against.
SPEC = importlib.util.spec_from_file_location("set_version", SCRIPT)
SET_VERSION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SET_VERSION)


class SetVersion(unittest.TestCase):
    """One test of the script, over a copy of the files which name a version."""

    def setUp(self) -> None:
        self.tree = pathlib.Path(tempfile.mkdtemp(prefix="gneedle-set-version-"))
        self.addCleanup(shutil.rmtree, self.tree, ignore_errors=True)
        for relative in FILES:
            target = self.tree / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(REPOSITORY / relative, target)

    def run_script(self, *arguments: str) -> subprocess.CompletedProcess:
        """The script, run over the copy as the release run runs it."""
        return subprocess.run([sys.executable, str(SCRIPT), *arguments], cwd=self.tree,
                              capture_output=True, text=True)

    def read_bytes(self, relative: str) -> bytes:
        return (self.tree / relative).read_bytes()

    def read(self, relative: str) -> str:
        return self.read_bytes(relative).decode("utf-8")

    def write(self, relative: str, text: str) -> None:
        (self.tree / relative).write_bytes(text.encode("utf-8"))

    def versions(self, relative: str) -> list[str]:
        """Every version which the patterns of the script name in one of the files."""
        text = self.read(relative)
        return [match[1] for file, pattern, _ in SET_VERSION.VERSIONS if file == relative
                          for match in re.findall(pattern, text)]

    def test_the_version_is_written_into_every_readme(self) -> None:
        """Every placeholder of a readme names the version written, and no readme names the one before it."""
        written = self.run_script("1.2.3")
        self.assertEqual(written.returncode, 0, written.stderr)

        for readme in READMES:
            self.assertEqual(set(self.versions(readme)), {"1.2.3"}, f"a placeholder of {readme} was not written")
            self.assertNotIn(CURRENT, self.read(readme), f"{readme} still names {CURRENT}")

    def test_a_readme_which_is_taken_from_the_repository_keeps_its_url(self) -> None:
        """The revision of a placeholder which names a tag is written without taking the URL for a version."""
        urls = {readme: set(re.findall(r"https://github\.com/Gatongone/Gneedle\.git\?path=[A-Za-z]+/Unity",
                                       self.read(readme)))
                for readme in READMES}
        self.assertTrue(all(urls.values()), "a readme names no package of the repository, and this test reads one")

        written = self.run_script("1.2.3")
        self.assertEqual(written.returncode, 0, written.stderr)

        for readme, named in urls.items():
            text = self.read(readme)
            for url in named:
                self.assertIn(f"{url}#v1.2.3", text, f"{readme} no longer names {url}")

    def test_only_the_version_of_a_file_changes(self) -> None:
        """A file is written back as it was read: the line endings it lies with and how it ends are not rewritten."""
        before = {relative: self.read_bytes(relative) for relative in FILES}

        written = self.run_script("1.2.3")
        self.assertEqual(written.returncode, 0, written.stderr)

        for relative, original in before.items():
            after = self.read_bytes(relative)
            self.assertEqual(after.count(b"\r\n"), original.count(b"\r\n"), f"the line endings of {relative} changed")
            self.assertEqual(after.endswith(b"\n"), original.endswith(b"\n"), f"how {relative} ends changed")

    def test_a_version_which_no_placeholder_names_is_left_alone(self) -> None:
        """A version in the prose of a readme is the author's, and is not the one being released."""
        prose = "The examples above were written with 4.5.6 of the package, which is not this release.\n"
        self.write("README.md", self.read("README.md") + prose)

        written = self.run_script("1.2.3")
        self.assertEqual(written.returncode, 0, written.stderr)

        self.assertIn("4.5.6", self.read("README.md"))
        self.assertEqual(set(self.versions("README.md")), {"1.2.3"})

    def test_a_readme_which_lost_a_placeholder_stops_the_run(self) -> None:
        """A readme which no longer holds what the version is written into is refused rather than passed over."""
        readme = self.read("Inject/Unity/README.md")
        self.write("Inject/Unity/README.md", "\n".join(line for line in readme.splitlines() if "?path=" not in line))

        written = self.run_script("1.2.3")
        self.assertNotEqual(written.returncode, 0)
        self.assertIn("Inject/Unity/README.md", written.stderr)

    def test_check_refuses_a_readme_which_names_another_version(self) -> None:
        """A readme which names a version other than the one of the release stops the run."""
        for readme in READMES:
            self.write(readme, self.read(readme).replace(CURRENT, "9.9.9"))

        checked = self.run_script("--check", CURRENT)
        self.assertNotEqual(checked.returncode, 0)
        self.assertIn("README.md", checked.stderr)

    def test_check_accepts_the_version_of_the_repository(self) -> None:
        """The files of the repository name the version they are released at, which is what a release asks of them."""
        checked = self.run_script("--check", CURRENT)
        self.assertEqual(checked.returncode, 0, checked.stderr)

    def test_every_readme_which_names_a_package_is_written(self) -> None:
        """A readme the script does not write is one which names the release before the one being made."""
        written = {relative for relative, _, _ in SET_VERSION.VERSIONS}

        for readme in sorted(REPOSITORY.rglob("*.md")):
            relative = readme.relative_to(REPOSITORY).as_posix()
            if any(part in relative for part in (".git/", "/bin/", "/obj/")):
                continue

            text = readme.read_text(encoding="utf-8")
            if "com.gatongone.gneedle." in text or 'Include="Gneedle.' in text:
                self.assertIn(relative, written, f"{relative} names a package, and no pattern writes its version")


if __name__ == "__main__":
    unittest.main()
