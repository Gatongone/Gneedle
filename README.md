Gneedle is an IL weaving library and an aspect weaver for .NET.

An aspect weaver usually asks you to learn a language of its own to say what should happen where. Gneedle asks for a plain C# method instead: the body you write is the body which ends up in the assembly, with the operands pointed at the members it names. That template is compiled by the same compiler as the rest of your code, checked by it, and debugged by the same tools.

`Gneedle.Inject` is the library which does that, and it is a weaver and nothing else: an assembly is opened, its metadata is read and rewritten, and the result is written back. It is driven the same way from a code generator, from a tool of your own, or from a build.

`Gneedle.Aspect` is the aspect weaver, and it is a package of its own which is not always needed. It drives the library from the build: it reads the injectors from the attributes of the assembly which it is building, applies them to it, and takes itself back out, so that weaving a project needs no entry point of its own beyond the attributes. A project which only ever weaves at an entry point of its own does not need it.

# Principle

A template is an ordinary method. It reaches the members of the type it will be woven into through placeholders, which are calls into `Gneedle.Inject` — `This`, `Base`, `Object`, `Static`, `Proceed` — and it names a generic parameter of the target through the tokens `T_0`–`T_20` and `M_0`–`M_20`. Every placeholder throws when it runs, because a template is never meant to run as it is written; the weaver replaces each of them with the member it names.

Weaving is therefore a rewrite rather than a compilation. The body of the template is copied instruction by instruction, and each operand is pointed at the member of the target which the placeholder named: a field read becomes `ldfld` of that field, a call on `This` becomes a call on the type being woven, a token becomes a generic parameter of the method or of the type which declares it. A local variable or a branch of the template is remapped to its counterpart in the body which is being woven.

Around advice keeps the body which the target already had instead of discarding it. That body is moved to a generated method of the declaring type, and the template reaches it through `Proceed`, so that the advice can inspect the arguments, call the original, and return what it chose.

The attribute which names an injector is only the way the build finds out what to do. By the time the assembly is written the injector has been applied and the attribute has done its work, so the weaver takes it back out along with the reference to itself: the assembly which was woven carries neither the attributes nor the weaver which read them.

# Requirement

- **The project** which is woven references the weaver, which targets `net5.0`, `netstandard2.1` and `net472`.
- **The build task**, which is what `Gneedle.Aspect` is, runs on MSBuild 17.6 or later, which is what the .NET SDK, Visual Studio 2022 and Rider provide. A full framework MSBuild loads the `net472` build of the task and a `dotnet build` loads the `netstandard2.1` one; the package picks between them by itself. A project which drives the weaver from its own code is not bound by this, since it runs what it writes.
- **Nothing** of the weaver is needed by the assembly once it has been woven, so the weaver does not become a dependency of what you ship.

# Install

```
dotnet add package Gneedle.Aspect
```

Or, in the project file:

```xml
<PackageReference Include="Gneedle.Aspect" Version="0.0.1" />
```

`Gneedle.Inject` is a dependency of it, so the weaver which the task weaves with is installed along with it. It is also the package to reference on its own, and the only one, when you drive the weaving yourself or when you write an injector, because the interfaces which one implements are declared in it:

```xml
<PackageReference Include="Gneedle.Inject" Version="0.0.1" />
```

# Features

## Weaving from your own entry point

Nothing of `Gneedle.Aspect` is needed to weave. An assembly which is already built is opened, its metadata is reached through the handlers, and the result is written back over it:

```csharp
using var assembly = Assembly.Read("path/to/AnAssembly.dll");
var host = (IClassHandler) assembly.Handler.GetType("My.Namespace.Host")!;

host.GetMethod("Compute", typeof(int).ToGneedleType())!.SetBody(template);

assembly.SaveTo("path/to/AnAssembly.dll");
```

An assembly which does not exist yet is built the same way: `Assembly.Create("MyAssembly")` hands back one which holds nothing, whose types and members are described through the same handlers, and `Load()` loads it into the process once it is written. So the same library serves a generator which produces an assembly, a tool which rewrites one, and the build which the task drives.

## Weaving a body from a template

A template names the members of the type it is woven into. Each placeholder is a call which throws when the template is run on its own:

```csharp
public static int AddThenDouble(int value) => This.Field<int>("m_Value").Get() + value;

public static void SetBoth(int left, int right)
{
    This.Property<int>("Left").Set(left);
    This.Property<int>("Right").Set(right);
}
```

The body of the method is replaced by the body of the template, so the target is what the template has to match:

```csharp
var assembly = Assembly.Create("MyAssembly");
var host = (IClassHandler) assembly.Handler.AddClass("Host", "My.Namespace", ClassFlags.Public).GetHandler();

host.AddMethod("Compute", MethodFlags.Public)
    .WithParameter("value", typeof(int))
    .WithReturnType(typeof(int))
    .WithBody(template)
    .GetHandler();
```

A default body is asked for the same way, when no template is needed: `WithBody(DefaultMethodBody.ThrowException)`, `WithBody(DefaultMethodBody.WithDefaultReturn)`, or `WithBody(DefaultMethodBody.CallFromBase)` to hand the call to the type which the target derives from.

## The placeholders

| Placeholder | Names |
| --- | --- |
| `This.Field<T>("name")`, `This.Property<T>("name")` | a field or a property of the type being woven, read and written through `Get` and `Set` |
| `This.Method<TDelegate>("name")` | a method of the type being woven, called through the delegate which gives its signature |
| `Base.Field`, `Base.Property`, `Base.Method` | the same, on the type which the target derives from |
| `Object(instance).Field`, `.Property`, `.Method` | the same, on an instance the template pushed |
| `Static.From("Full.Type.Name").Method` | the same, on a type named by a string |
| `T_0`–`T_20`, `M_0`–`M_20` | the first to the twenty-first generic parameter of the declaring type, or of the method |
| `ValuableMember<T>` | the value of a field or a property, without the boxing which `ValuableMember` costs |

Where a generic parameter cannot be named by a `System.Type` — in the signature of a delegate, in a local variable, in a return type — a token stands in for it, and the weaver turns it into the parameter of the method being woven:

```csharp
// A template which takes and returns the first generic parameter of the type being woven.
public static T_0 Echo(T_0 value) => value;
```

## Around advice

`AroundBody` keeps the body which the method already had, moves it to a generated method named `<Name>k__Proceed`, and weaves the template around it. The template calls the original through `Proceed`:

```csharp
public static int AddOne(int value) => Proceed.Method<Func<int, int>>("Double")(value) + 1;
```

```csharp
host.GetMethod("Double", typeof(int).ToGneedleType())!
    .AroundBody(typeof(Templates).GetMethod(nameof(Templates.AddOne))!);
```

The template keeps the signature of the method, and the body it proceeds into is the one which the method held: the body it was added with, the body it was read with, or the throwing body which marks a method that has none. An accessor of a property is woven around the same way, through `IPropertyHandler.GetGetter()` and `GetSetter()`, which hand back the handler of the accessor as an ordinary method.

## Injectors at build time

An injector is an attribute which implements one of the interfaces of `Gneedle.Inject` and says what to do with the member it is put on. It is applied by the build of the project which declares it, so the weaving needs no extra step:

```csharp
public sealed class ThrowBodyAttribute : Attribute, IMethodInjector
{
    public void Inject(MethodInfo method, IMethodHandler handler) => handler.SetBody(DefaultMethodBody.ThrowException);
}
```

```csharp
public class Target
{
    [ThrowBody] public void Weave() => Console.WriteLine("the body ran unchanged");
}
```

Which member an interface is read for: `IAssemblyInjector` for the assembly and `ITypeInjector` for a type, `IClassInjector`, `IStructInjector` and `IEnumInjector` for a type of that kind, and `IMethodInjector`, `IFieldInjector` and `IPropertyInjector` for a member. Every member of a type is looked at, whichever way a caller could reach it, and an injector which names a kind of type which it was put on is reported rather than passed over.

## What is left in the assembly

The attributes are read at build time and do nothing at run time, and the weaver is not a dependency of what you ship. The task takes both back out once the injectors have been applied: the attributes are removed with the types which declare them, and the reference to the weaver is dropped when nothing of the assembly names it any more.

A type which your code still names — through `typeof`, a field, a signature — cannot be removed without taking those names with it, so it keeps its place and gives up what makes it an injector instead. Either way the assembly which was woven carries no weaver.

A project which declares its attributes for *another* project to weave with keeps them, since removing them would leave that project with nothing to name:

```xml
<PropertyGroup>
  <GneedleKeepWeaver>true</GneedleKeepWeaver>
</PropertyGroup>
```

A project which nothing should be woven into says so in the same way:

```xml
<PropertyGroup>
  <Gneedle>disable</Gneedle>
</PropertyGroup>
```

## Referring to a type you cannot reference

A template is compiled before the type it will be woven into exists, so it cannot name that type. A stub with the same full name, marked with the name of the assembly which declares the real one, is how the template names it: the weaver resolves the stub to the real type and leaves the assembly of the stub out of the produced one.

```csharp
[FromAssembly("MyAssembly")]
public class Stub
{
    public int Value;
}
```

# License

Gneedle is released under the [LICENSE.txt](LICENSE.txt). Copyright (c) 2026, Gatongone.
