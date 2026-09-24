#!/usr/bin/env bash
#
# The suite of the tree, which a pull request and a push to the branch the releases are cut from are held to, and which
# a release runs before it publishes anything.
#
# Both of them call this rather than naming the commands themselves. A gate which runs something other than what the
# release runs is a gate which lets through the fault the release then finds, and two copies of a command are two
# commands the day one of them is changed: what the frameworks are and why each run is there is written here, once.
#
# -e is what makes the run above answer for all of them rather than for the last: a suite which failed and a suite which
# passed after it are a run which failed.
set -euo pipefail

# The weaver which the package of Unity ships is built here rather than by whoever consumes it, so what is committed is
# what a Unity project loads: a change to the weaver which does not carry a copy of it leaves the two out of step, and
# nothing of the build would say so.
#
# What answers that is the commit the assembly records it was built at, read out of the assembly itself. A build of it
# is not what is asked, because a build at another path is another binary: the debug directory of the image and the
# mapping of the sources in the symbols name the absolute paths of the machine which built them, so two machines do not
# produce one binary from one source, and no assertion over the bytes of a rebuild can stand.
#
# The question is whether the weaver has changed since the commit which is recorded, rather than whether that commit is
# the head: the copy is committed after the sources it was built from, so the record names the parent of the commit
# which carries it, and it is what came after the record which says the binary is old.
built=$(grep -aoE '[0-9]+\.[0-9]+\.[0-9]+\+[0-9a-f]{40}' Inject/Unity/Plugins/Gneedle.Inject.dll | head -1 | cut -d+ -f2 || true)
if [ -z "$built" ]; then
  echo "the plugin of Unity records no commit, so whether it is the weaver of these sources cannot be read."
  exit 1
fi

if ! git merge-base --is-ancestor "$built" HEAD; then
  # A clone of one commit holds the commit it stands at and none of the ones behind it, so the record of a plugin which
  # is not out of date is a name which such a clone has no answer for: the two read alike from here, and they are told
  # apart by the clone, because what the reader has to do about them is not one thing.
  if [ "$(git rev-parse --is-shallow-repository)" = "true" ]; then
    echo "the plugin of Unity was built at $built, which this clone does not hold: the clone is shallow, and the record is read against the history. Fetch the history rather than build anything."
    exit 1
  fi

  echo "the plugin of Unity was built at $built, which is not a commit of this history: build the copy and commit it."
  exit 1
fi

if ! git diff --quiet "$built" HEAD -- Inject/Source; then
  echo "the plugin of Unity was built at $built, and Inject/Source has changed since:"
  git --no-pager diff --stat "$built" HEAD -- Inject/Source
  exit 1
fi

# Every framework which every project of the tree names, which the runs below do not build: a test project is built for
# the frameworks it names, and a project which it refers to is built for the one of those which the reference takes, so
# netstandard2.1 - the framework a Unity project reaches both packages through - is built by none of them. What the
# comment in the test project of the weaver says is that a build of it which does not compile is a build which fails,
# and until this line stood here there was no build of it at all.
dotnet build Gneedle.sln -c Release

# The tests of the weaver, built and run for every framework the project names, which is net6.0 and net472.
dotnet test Inject/Tests/Gneedle.Inject.Test.csproj -c Release

# The same suite against the shapes which the optimizer writes, which are the ones a release build of a consumer holds.
# The tests read the templates back as the IL which the compiler wrote for them, and the shapes which they name are the
# ones a build without the optimizer writes, which is the run above: without this one the assertions would be read
# against shapes which no consumer ever has.
#
# The symbols are asked for as well, because the release configuration writes them without, and a build which carries
# none folds the temporaries of a template into the uses of them: a body which holds a local of the stub compiles to
# `ldnull; call; ldc.i4.1; ret` and holds none, which is a shape no consumer holds either, since the release build of a
# consumer carries the symbols of it.
dotnet test Inject/Tests/Gneedle.Inject.Test.csproj -c Release -p:Optimize=true -p:DebugSymbols=true

# The tests of the aspect weaver, which is the build task of MSBuild and the post processor of Unity. The tests of the
# post processor are not among them: they are written against the pipeline of an editor, and are run by its own runner.
dotnet test Aspect/Tests/Gneedle.Aspect.Test.csproj -c Release
