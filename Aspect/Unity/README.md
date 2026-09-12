# Gneedle.Aspect

The aspect weaver of [Gneedle](https://github.com/Gatongone/Gneedle). An injector is an attribute which implements one of
the interfaces of `Gneedle.Inject` and says what to do with the member it is put on, and this package applies the
injectors which an assembly declares to that assembly as it is compiled.

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
    "com.gatongone.gneedle.inject": "0.0.1",
    "com.gatongone.gneedle.aspect": "0.0.1"
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
    "com.gatongone.gneedle.inject": "https://github.com/Gatongone/Gneedle.git?path=Inject/Unity#v0.0.1",
    "com.gatongone.gneedle.aspect": "https://github.com/Gatongone/Gneedle.git?path=Aspect/Unity#v0.0.1"
  }
}
```

## Documentation

[The readme of the repository](https://github.com/Gatongone/Gneedle/blob/main/README.md#aspect) describes what an
injector is, which member each of the interfaces is read for, and what is left in the assembly once they have been
applied.

## Licence

MIT. Copyright (c) 2026, Gatongone.
