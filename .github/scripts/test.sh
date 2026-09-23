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

# The tests of the weaver, built and run for every framework the project names, which is net5.0 and net472.
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
