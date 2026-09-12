Gneedle is an IL weaving library and an aspect weaver for .NET.

An aspect weaver usually asks you to learn a language of its own to say what should happen where. Gneedle asks for a plain C# method instead: the body you write is the body that ends up in the assembly, with the operands pointed at the members it names. That template is compiled by the same compiler as the rest of your code, checked by it, and debugged by the same tools.

`Gneedle.Inject` is the library that does that, and it is a weaver and nothing else: an assembly is opened, its metadata is read and rewritten, and the result is written back. It is driven the same way from a code generator, from a tool of your own, or from a build.

`Gneedle.Aspect` is the aspect weaver, and it is a package of its own that is not always needed. It drives the library from the build: it reads the injectors from the attributes of the assembly that it is building, applies them to it, and takes itself back out, so that weaving a project needs no entry point of its own beyond the attributes. A project that only ever weaves at an entry point of its own does not need it.

Where an assembly is built decides that form of it is used, and the two are the same weaver: a .NET project is given an MSBuild task, and a Unity project is given an IL post processor, that the compilation pipeline of the editor runs. Both of them read the same attributes, drive the same library, and leave the same assembly behind.

# Principle

A template is an ordinary method. It reaches the members of the type it will be woven into through placeholders, that are calls into `Gneedle.Inject` — `This`, `Base`, `Object`, `Static`, `Proceed` — and it names a generic parameter of the target through the tokens `T_0`–`T_20` and `M_0`–`M_20`. Every placeholder throws when it runs, because a template is never meant to run as it is written; the weaver replaces each of them with the member it names.

Weaving is therefore a rewrite rather than a compilation. The body of the template is copied instruction by instruction, and each operand is pointed at the member of the target that the placeholder named: a field read becomes `ldfld` of that field, a call on `This` becomes a call on the type being woven, a token becomes a generic parameter of the method or of the type that declares it. A local variable or a branch of the template is remapped to its counterpart in the body that is being woven.

None of that could be expressed without [Mono.Cecil](https://github.com/jbevain/cecil), that Gneedle is built on: Mono.Cecil is what reads the metadata of an assembly and writes it back, and the rewrite above is described in the terms that it hands over — the instructions, the references, the signatures and the tables of the image. Gneedle decides what each of them becomes; Mono.Cecil is what turns that into an assembly the runtime reads.

Around advice keeps the body that the target already had instead of discarding it. That body is moved to a generated method of the declaring type, and the template reaches it through `Proceed`, so that the advice can inspect the arguments, call the original, and return what it chose.

The attribute that names an injector is only the way the build finds out what to do. By the time the assembly is written the injector has been applied and the attribute has done its work, so the weaver takes it back out along with the reference to itself: the assembly that was woven carries neither the attributes nor the weaver that read them.

# Requirement

- **The project** that is woven references the weaver, that targets `net5.0`, `netstandard2.1` and `net472`.
- **The build task**, that is what `Gneedle.Aspect` is, runs on MSBuild 17.6 or later, that is what the .NET SDK, Visual Studio 2022 and Rider provide. A full framework MSBuild loads the `net472` build of the task and a `dotnet build` loads the `netstandard2.1` one; the package picks between them by itself. A project that drives the weaver from its own code is not bound by this, since it runs what it writes.
- **Unity** 2021.3 or later, for the two packages that are installed through the Package Manager. The weaver is a plugin of the project there, so it is compiled for `netstandard2.1`, and Mono.Cecil is brought in by the package that names it as a dependency rather than installed by hand.
- **Nothing** of the weaver is needed by the assembly once it has been woven, so the weaver does not become a dependency of what you ship.

# Install

## NuGet

```
dotnet add package Gneedle.Aspect
```

Or, in the project file:

```xml
<PackageReference Include="Gneedle.Aspect" Version="0.0.1" />
```

`Gneedle.Inject` is a dependency of it, so the weaver that the task weaves with is installed along with it. It is also the package to reference on its own, and the only one, when you drive the weaving yourself or when you write an injector, because the interfaces that one implements are declared in it:

```xml
<PackageReference Include="Gneedle.Inject" Version="0.0.1" />
```

## Unity

The same two are packages of Unity as well, installed from this repository by path through the Package Manager:

```json
{
  "dependencies": {
    "com.gatongone.gneedle.inject": "https://github.com/Gatongone/Gneedle.git?path=Inject/Unity",
    "com.gatongone.gneedle.aspect": "https://github.com/Gatongone/Gneedle.git?path=Aspect/Unity"
  }
}
```

This is a `Packages/manifest.json`, that the Package Manager window writes as well: *Add package from git URL* takes the one of the two that is wanted. A revision may be appended to the path — `?path=Aspect/Unity#<tag or branch>` — to hold the package at a tag or a branch rather than at whatever the default branch is at the time.

# Features

The two packages are described apart from each other here, since either of them is usable without the other: the library is what weaves, and the aspect weaver is one of the things that drive it.

## Inject

The library is the weaver. An assembly is opened or created, its metadata is reached through the handlers, and the result is written back; a body is woven from a template, and the placeholders that a template names its members with are resolved at that moment.

### Weaving from your own entry point

Nothing of `Gneedle.Aspect` is needed to weave. An assembly that is already built is opened, its metadata is reached through the handlers, and the result is written back over it:

```csharp
using var assembly = Assembly.Read("path/to/AnAssembly.dll");
var host = (IClassHandler) assembly.Handler.GetType("My.Namespace.Host")!;

host.GetMethod("Compute", typeof(int).ToGneedleType())!.SetBody(template);

assembly.SaveTo("path/to/AnAssembly.dll");
```

An assembly that does not exist yet is built the same way: `Assembly.Create("MyAssembly")` hands back one that holds nothing, whose types and members are described through the same handlers, and `Load()` loads it into the process once it is written. So the same library serves a generator that produces an assembly, a tool that rewrites one, and the build that the task drives.

### Weaving a body from a template

A template names the members of the type it is woven into. Each placeholder is a call that throws when the template is run on its own:

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

A default body is asked for the same way, when no template is needed: `WithBody(DefaultMethodBody.ThrowException)`, `WithBody(DefaultMethodBody.WithDefaultReturn)`, or `WithBody(DefaultMethodBody.CallFromBase)` to hand the call to the type that the target derives from.

### The placeholders

| Placeholder                                         | Names                                                                                   |
|-----------------------------------------------------|-----------------------------------------------------------------------------------------|
| `This.Field<T>("name")`, `This.Property<T>("name")` | a field or a property of the type being woven, read and written through `Get` and `Set` |
| `This.Method<TDelegate>("name")`                    | a method of the type being woven, called through the delegate that gives its signature |
| `Base.Field`, `Base.Property`, `Base.Method`        | the same, on the type that the target derives from                                     |
| `Object(instance).Field`, `.Property`, `.Method`    | the same, on an instance the template pushed                                            |
| `Static.From("Full.Type.Name").Method`              | the same, on a type named by a string                                                   |
| `T_0`–`T_20`, `M_0`–`M_20`                          | the first to the twenty-first generic parameter of the declaring type, or of the method |
| `ValuableMember<T>`                                 | the value of a field or a property, without the boxing that `ValuableMember` costs     |

Where a generic parameter cannot be named by a `System.Type` — in the signature of a delegate, in a local variable, in a return type — a token stands in for it, and the weaver turns it into the parameter of the method being woven:

```csharp
// A template that takes and returns the first generic parameter of the type being woven.
public static T_0 Echo(T_0 value) => value;
```

### Around advice

`AroundBody` keeps the body that the method already had, moves it to a generated method named `<Name>k__Proceed`, and weaves the template around it. The template calls the original through `Proceed`:

```csharp
public static int AddOne(int value) => Proceed.Method<Func<int, int>>("Double")(value) + 1;
```

```csharp
host.GetMethod("Double", typeof(int).ToGneedleType())!
    .AroundBody(typeof(Templates).GetMethod(nameof(Templates.AddOne))!);
```

The template keeps the signature of the method, and the body it proceeds into is the one that the method held: the body it was added with, the body it was read with, or the throwing body that marks a method that has none. An accessor of a property is woven around the same way, through `IPropertyHandler.GetGetter()` and `GetSetter()`, that hand back the handler of the accessor as an ordinary method.

### Referring to a type you cannot reference

A template is compiled before the type it will be woven into exists, so it cannot name that type. A stub with the same full name, marked with the name of the assembly that declares the real one, is how the template names it: the weaver resolves the stub to the real type and leaves the assembly of the stub out of the produced one.

```csharp
[FromAssembly("MyAssembly")]
public class Stub
{
    public int Value;
}
```

## Aspect

The aspect weaver drives the library from the build that produces the assembly. It is added to a project as a package and needs no entry point of its own: it reads the injectors from the attributes that the assembly declares, applies them to it as it is built, and takes itself back out of the assembly once they have been applied.

### Injectors

An injector is an attribute that implements one of the interfaces of `Gneedle.Inject` and says what to do with the member it is put on. It is applied as the project that declares it is built, so the weaving needs no step of its own:

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

Which member an interface is read for: `IAssemblyInjector` for the assembly and `ITypeInjector` for a type, `IClassInjector`, `IStructInjector` and `IEnumInjector` for a type of that kind, and `IMethodInjector`, `IFieldInjector` and `IPropertyInjector` for a member. Every member of a type is looked at, thatever way a caller could reach it, and an injector that names a kind of type that it was put on is reported rather than passed over.

### What is left in the assembly

The attributes are read at build time and do nothing at run time, and the weaver is not a dependency of what you ship. The task takes both back out once the injectors have been applied: the attributes are removed with the types that declare them, and the reference to the weaver is dropped when nothing of the assembly names it any more.

A type that your code still names — through `typeof`, a field, a signature — cannot be removed without taking those names with it, so it keeps its place and gives up what makes it an injector instead. Either way the assembly that was woven carries no weaver.

### The properties that a project is described by

Two properties change what is done with a project, and they answer different questions: whether anything is woven into the project at all, and whether the weaver is left in the assembly once it has been.

| Property     | Value     | What it does                                                                                                                                               |
|--------------|-----------|------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Aspect`     | `disable` | Nothing is woven into the project. The scan of the solution does not add the target to it, and the task returns without reading its assembly.              |
| `KeepWeaver` | `true`    | The attributes that the injectors were read from, and the reference to the weaver that they name, are kept in the assembly. Both are removed by default. |

```xml
<PropertyGroup>
  <!-- Nothing is woven into this project. -->
  <Aspect>disable</Aspect>
</PropertyGroup>
```

```xml
<PropertyGroup>
  <!-- The attributes are kept in the assembly, and the reference to the weaver with them. -->
  <KeepWeaver>true</KeepWeaver>
</PropertyGroup>
```

`Aspect` is for a project that has nothing to weave: one that only declares the attributes for other projects to read, or one whose assembly is woven by something other than this build. A project that declares attributes that another project weaves with wants `KeepWeaver`, because removing them would leave that other project with nothing to name.

`Aspect` is read out of the project file itself, because the scan of a solution reads the projects that it walks without building them, and a property that comes from an imported file is not in a project's own file. `KeepWeaver` is passed to the task by the build, so it is an ordinary property, set on the command line as well as in the project file.

### Unity Editor

A Unity project is not built by MSBuild, so there the aspect weaver is an IL post processor instead of a build task. A post processor is a part of the compilation rather than a step beside it: Unity hands it the image of an assembly that it has just compiled, takes back the image that it answers with, and writes that one. So the weaving is the same for the editor and for a player, no target is written into anything, and nothing has to be run after the build.

Every assembly of the project is compiled that way, Unity's own among them, so the post processor weaves the ones that declare an injector and answers each of the others with the image that it was handed. What it looks for is a type that implements one of the injector interfaces: an assembly that declares none has nothing for the weaving to read, and is left as it was compiled.

What the woven assembly is left holding is the same as under the build task: the attributes are removed with the types that declare them, and the reference to the weaver goes with them when nothing of the assembly names it anymore.

The `Aspect` and `KeepWeaver` properties belong to the build task, that reads them out of a project file, and a Unity project has none. There is therefore nothing there to turn the weaving off with, and the attributes and the reference to the weaver are always taken back out.

# License

Gneedle is released under the [MIT](LICENSE.txt) Copyright (c) 2026, Gatongone.
