# Gneedle.Inject

The weaver of [Gneedle](https://github.com/Gatongone/Gneedle). It rewrites the IL of an assembly through
[Mono.Cecil](https://github.com/jbevain/cecil), and weaves a body into it from a template, which is an ordinary C#
method: the template reaches the members of the type it is woven into through the placeholders `This`, `Base`,
`Object`, `Static` and `Proceed`, and names a generic parameter of that type through the tokens `T_0`–`T_20` and
`M_0`–`M_20`.

This package is that weaver as a package of Unity. It is carried as a plugin of the project rather than as source which
is compiled into it, so that every assembly of the project can reach it: the assemblies which are woven are woven by
the aspect weaver beside this package, or by a tool of your own which drives the weaver itself.

## Install

Through a registry, which is what the Package Manager takes a package of this name from:

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
    "com.gatongone.gneedle.inject": "0.0.1"
  }
}
```

Or out of the repository, by the path which the package lies at, at a tag which names a release:

```json
{
  "dependencies":
  {
    "com.gatongone.gneedle.inject": "https://github.com/Gatongone/Gneedle.git?path=Inject/Unity#v0.0.1"
  }
}
```

## Documentation

[The readme of the repository](https://github.com/Gatongone/Gneedle/blob/main/README.md#inject) describes what a
template is, the placeholders and the tokens which it names its members with, and the weaving of a body, with examples.

## Licence

MIT. Copyright (c) 2026, Gatongone.
