# Gneedle.Inject

The weaver of [Gneedle](https://github.com/Gatongone/Gneedle). It rewrites the IL of an assembly through
[Mono.Cecil](https://github.com/jbevain/cecil), and weaves a body into it from a template, which is an ordinary C#
method: the template reaches the members of the type it is woven into through the placeholders `This`, `Base`,
`Instance` and `Static`, reads the instance which the member being woven belongs to through `This.Reference`, and lets
the template proceed into the body which the member held through `Proceed`. It names a generic parameter of that type
through the tokens `T_0`–`T_20` and `M_0`–`M_20`, and the type itself through `T_Self` — which is also how a template
names a member of that type reached through another instance of it, `other.Field<int>("name")`. Around advice keeps the body which a method already held and lets the template proceed into it, either
with a signature which the template names through `Proceed.Method<TMethod>()` or with the arguments which the template
itself was given, through `Proceed.Invoke<TResult>()`. A template may also be a lambda which captures what it is written among, and what it captured is written
into the assembly which is woven as the value itself, which
[the readme of the repository](https://github.com/Gatongone/Gneedle/blob/main/README.md#principle) describes in full.

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
    "com.gatongone.gneedle.inject": "0.0.3"
  }
}
```

Or out of the repository, by the path which the package lies at, at a tag which names a release:

```json
{
  "dependencies":
  {
    "com.gatongone.gneedle.inject": "https://github.com/Gatongone/Gneedle.git?path=Inject/Unity#v0.0.3"
  }
}
```

## Documentation

[The readme of the repository](https://github.com/Gatongone/Gneedle/blob/main/README.md#inject) describes what a
template is, the placeholders and the tokens which it names its members with, and the weaving of a body, with examples.

## Licence

MIT. Copyright (c) 2026, Gatongone.