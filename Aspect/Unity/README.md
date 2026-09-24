# Gneedle.Aspect

The aspect weaver of [Gneedle](https://github.com/Gatongone/Gneedle). An injector is an attribute which implements one of
the interfaces of `Gneedle.Inject` and says what to do with the member it is put on, and this package applies the
injectors which an assembly declares to that assembly as it is compiled. The template which an injector applies is
usually a lambda which captures what the attribute was given, and what it captured is written into the assembly which is
woven as the value itself. A lambda stands where a `Delegate` is asked for from C# 10 — which is the version of the
language a lambda has a type of its own in — so an injector of a project whose compiler is older, which is what the
compiler of Unity is, hands over the delegate the lambda would have been: `handler.AroundBody((Action)(() => …))`.

This package is that weaver as a package of Unity, where it is an IL post processor rather than the build task which
the package of NuGet carries: the compilation pipeline runs it on an assembly which it has just compiled, and writes the
image which it answers with. So a project is woven by the compilation which produces it, no target is written into
anything, and the attributes and the reference to the weaver are taken back out of the assembly once they have been
applied.

## Install

Through a registry, which is what the Package Manager takes a package of this name from. The weaver is named here as
well, because a version of it is what this package asks for:

```json
{
  "scopedRegistries":
  [
    {
      "name": "npmjs",
      "url": "https://registry.npmjs.org",
      "scopes": ["com.gatongone"]
    }
  ],
  "dependencies":
  {
    "com.gatongone.gneedle.inject": "0.0.3",
    "com.gatongone.gneedle.aspect": "0.0.3"
  }
}
```

Or out of the repository, by the path which each package lies at, at a tag which names a release. Both lines are written
even where only one of the two is used, and this is the one which is used alone: a package which names another names a
version of it, and that version is read out of a registry rather than along the path.

```json
{
  "dependencies":
  {
    "com.gatongone.gneedle.inject": "https://github.com/Gatongone/Gneedle.git?path=Inject/Unity#v0.0.3",
    "com.gatongone.gneedle.aspect": "https://github.com/Gatongone/Gneedle.git?path=Aspect/Unity#v0.0.3"
  }
}
```

## Documentation

[The readme of the repository](https://github.com/Gatongone/Gneedle/blob/main/README.md#aspect) describes what an
injector is, which member each of the interfaces is read for, and what is left in the assembly once they have been
applied.

## Tests

The tests of the weaver lie beside it, in `Test`, and are run by the test runner of the editor. What they are written
against is the compilation pipeline which the editor compiles an assembly of its own against, so the assembly of the
tests is named `Unity.Gneedle.CodeGen.Tests`: the name and not the folder is what the pipeline reads such an assembly
by, and Unity grants it the types of the compiler by that name.

The tests of a package which is not in the `Packages` folder of a project are compiled only where the project names
the package as one to test, which is written in its manifest:

```json
{
  "testables": ["com.gatongone.gneedle.aspect"]
}
```

The tests are then listed with the tests of the project, under EditMode, in the Test Runner window.

## Licence

MIT. Copyright (c) 2026, Gatongone.