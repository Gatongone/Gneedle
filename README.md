# Gneedle

[![Build](https://github.com/Gatongone/Gneedle/actions/workflows/build.yml/badge.svg)](https://github.com/Gatongone/Gneedle/actions/workflows/build.yml) [![NuGet](https://img.shields.io/nuget/v/Gneedle.Inject.svg)](https://www.nuget.org/packages/Gneedle.Inject) [![Releases](https://img.shields.io/github/v/release/Gatongone/Gneedle.svg)](https://github.com/Gatongone/Gneedle/releases) [![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

An IL weaving library and an aspect weaver for .NET and Unity, in which the advice is written as a plain C# method.

An aspect weaver usually asks you to learn a language of its own to say what should happen where. Gneedle asks for a plain C# method instead: the body you write **is** the body that ends up in the assembly, with the operands pointed at the members it names. That template is compiled by the same compiler as the rest of your code, checked by it, and debugged by the same tools.

* **Gneedle.Inject** is the weaver: an assembly is opened, its metadata is read and rewritten, and the result is written back. The template is ordinary C# — IntelliSense, compile errors and breakpoints all work on it. The placeholders `This`, `Base`, `Instance` and `Static` reach the members of the type being woven, and `Proceed` reaches the body which the member already held. `T_0`–`T_20` / `M_0`–`M_20` name a generic parameter of the type or the method, without a `System.Type`. Around advice keeps the body that was already there and lets the template `Proceed` into it. Any part of a template is carried: `try`, `using`, `lock`, `foreach`, branches, locals, and the bodies the compiler writes for lambdas, `async` and iterators.
* **Gneedle.Aspect** drives the weaver from the build, as an MSBuild task or as a Unity IL post processor. Nothing of the weaver is needed once the assembly has been woven, so it is not a dependency of what you ship.

> [!IMPORTANT]
> This project is still in the early stages of development, which means that I may break compatibility in order to fix major bugs or include critical features.

## Inject

The library is the weaver, and it is usable without the aspect weaver. An assembly is opened or created, its metadata is reached through the handlers, a body is woven from a template, and the result is written back.

### Getting started

Nothing of `Gneedle.Aspect` is needed to weave. An assembly that is already built is opened, its metadata is reached through the handlers, its `Compute` body is replaced by the template, and the result is written back over it:

```csharp
using Gneedle.Inject;

// A template is an ordinary method. It reaches the members of the type it will be woven into
// through placeholders, and it is never meant to run as it is written.
public static int Double(int value) => This.Field<int>("m_Value").Get() + value;

// ...
using var assembly = Assembly.Read("path/to/AnAssembly.dll");
var host = (IClassHandler) assembly.Handler.GetType("My.Namespace.Host")!;

host.GetMethod("Compute", typeof(int).ToGneedleType())!.SetBody(typeof(Templates).GetMethod(nameof(Double))!);

assembly.SaveTo("path/to/AnAssembly.dll");
```

An assembly is built the same way rather than read: `Assembly.Create("MyAssembly")` hands back one that holds nothing, whose types and members are described through the same handlers, and `Load()` loads it into the process once it is written. The same library therefore serves a generator that produces an assembly, a tool that rewrites one, and the build that the task drives.

To drive the weaving from a build instead, install [the aspect weaver](#aspect) and put an attribute on the member — there is no entry point of your own to write.

### Weaving from your own entry point

`Injections.Apply` reads the injectors from the attributes of an assembly, applies each of them to the member it names, and hands back the image which holds the result — or, where an injector reported something, the image it was given, because a run which reported anything leaves an assembly woven in part, which no caller could tell from one which was woven whole:

```csharp
var image = File.ReadAllBytes("path/to/AnAssembly.dll");
var (changed, woven) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image);
if (changed) File.WriteAllBytes("path/to/AnAssembly.dll", woven);
```

The attributes and the reference to the weaver are taken out of the image by default, which is what leaves the woven assembly standing alone; `removesTheWeaver: false` keeps them.

An assembly that no file holds — one an editor has just compiled in memory, or one loaded from a network stream — is given to the weaving through `AssemblyLoader.LoadFromBytes`, which loads the assembly from its image and remembers the bytes, and `Remember` does the same for an assembly the runtime loaded in another way. That image is what a type of such an assembly is resolved against, which is how a template names a type of it through [a stub](#referring-to-a-type-you-cannot-reference). The assemblies of the weaver are answered to such an assembly as well, and to one of those alone: the weaver that its templates were compiled against is resolved to the weaver which is weaving it, rather than to a copy that would have to lie beside the image.

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

The body of the method is replaced by the body of the template, so the target is what the template has to match. An assembly that holds nothing yet is described through the handlers, and the body is given at the same time:

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

### Placeholders

| Placeholder                                            | Prototype               | Names                                                                                   |
|--------------------------------------------------------|-------------------------|-----------------------------------------------------------------------------------------|
| `This.Field<T>("name")`, `This.Property<T>("name")`    | this.name               | a field or a property of the type being woven, read and written through `Get` and `Set` |
| `This.Reference`                                       | this                    | the instance which the member being woven belongs to, as a value of `T_Self`            |
| `This.Method<TDelegate>("name")`                       | this.name()             | a method of the type being woven, called through the delegate that gives its signature  |
| `Base.Field`, `Base.Property`, `Base.Method`           | base.name/base.name()   | the same, on the type that the target derives from                                      |
| `new Instance(instance).Field`, `.Property`, `.Method` | -                       | the same, on an instance the template pushed                                            |
| `Static.From("Full.Type.Name").Method`                 | Full.Type.Name.Method() | the same, on a type named by a string                                                   |
| `T_0`–`T_20`, `M_0`–`M_20`                             | T0..T20/M0_M20          | the first to the twenty-first generic parameter of the declaring type, or of the method |
| `T_Self`                                               | -                       | the type being woven, which a template of it names wherever a type is named             |
| `ValuableMember<T>`                                    | -                       | the value of a field or a property of a value type, which `ValuableMember` would box    |
| `Proceed.Method<T>()`, `Proceed.Invoke<TResult>()`     | -                       | the body which the member already held, proceeded into                                  |

Each placeholder is named after what it reaches, and none of them is named after a type of the framework: a placeholder called `Object` would be read wherever a template writes `Object` with the usings of this library in scope, so a field, a parameter or a return type which names the type of the framework would name the placeholder instead. The keyword `object` is not affected, and neither is a template which writes `System.Object` in full.

> [!TIP]
> `T_Self` is the type being woven, which a template names wherever a type is named — `List<T_Self>`, `T_Self[]`, `typeof(T_Self)`, and `This.Field<T_Self>("name")` for a member whose type is the type itself. Where the type being woven declares generic parameters, `T_Self` is an instantiation of that type rather than the definition which stands open. `new T_Self()` and `is T_Self` are written by the compiler as a reference to the token and are read as the type being woven like any other. A **default** of it is not: `default(T_Self)` is written as the null which the default of a class is, so a member woven into a type which is not a class stands a null where the default of that type belongs. The tokens of a generic parameter do not carry that limit — the default of one is written as the default of a parameter rather than as a null — so the limit belongs to the token of a type.

A member which is static belongs to no instance, and a template of one which reads `This.Reference` is refused by name.

The name that a placeholder is given is read out of the template itself, and the instruction which the call follows is what holds it: a name is therefore one which the compiler writes there — a literal, a `nameof`, or a constant of the template — rather than one which the template computes while it runs.

```csharp
// The name of a member may be a constant of the template, since a constant is what the compiler writes there.
private const string Name = "Compute";

public static int ByAConstant() => This.Method<Func<int>>(Name)();
```

A name that no load stands ahead of is refused where the weaving runs, rather than left as a call that would reach the placeholder, and fail, when the member which was woven ran. Where a generic parameter cannot be named by a `System.Type` — in the signature of a delegate, in a local variable, in a return type — a token stands in for it, and the weaver turns it into the parameter of the method being woven:

```csharp
// A template that takes and returns the first generic parameter of the type being woven.
public static T_0 Echo(T_0 value) => value;
```

### Around advice

`AroundBody` keeps the body that the method already had, moves it to a generated method named `<Name>k__Proceed`, and weaves the template around it. The template calls the original through `Proceed`:

```csharp
public static int AddOne(int value) => Proceed.Method<Func<int, int>>()(value) + 1;

host.GetMethod("Double", typeof(int).ToGneedleType())!
    .AroundBody(typeof(Templates).GetMethod(nameof(Templates.AddOne))!);
```

The two forms of that call differ in what the template has to say about the signature of the body it proceeds into. `Proceed.Method<TMethod>()` names it — a delegate, which is how the template hands the body arguments of its own choosing rather than the arguments which the template was given — while `Proceed.Invoke<TResult>()` names only the type of the value which the body hands back and passes on the arguments which the template itself was given, which for a template of an around body are the arguments of the member:

```csharp
// The same advice as above, which passes the argument on as it is: no signature is written, and no delegate is needed.
public static int AddOne(int value) => Proceed.Invoke<int>() + 1;
```

A member that hands nothing back takes `Proceed.Invoke()`, and a member whose parameters are generic takes the token — `Proceed.Invoke<M_0>()` — so that a template of such a member needs no delegate of its own to name them with. What `Invoke<TResult>` names is checked against the type which the member hands back, and a template which proceeds without a body having been taken over is refused either way.

The template keeps the signature of the method, and the body it proceeds into is the one that the method held: the body it was added with, the body it was read with, or the throwing body that marks a method that has none. An accessor of a property is woven around the same way, through `IPropertyHandler.GetGetter()` and `GetSetter()`, which hand back the handler of the accessor as an ordinary method.

A template may be a lambda rather than a method, and then it may capture the variables that it is written among. Where the weave is asked for at a call of your own, the template is written there:

```csharp
var message = "Hello World";
host.GetMethod("Write", typeof(void).ToGneedleType())!
    .AroundBody(() =>
    {
        Proceed.Invoke();
        Console.WriteLine(message);
    });
```

A lambda stands where a `Delegate` is asked for, and which lambdas may do that is settled by the version of the language which compiles the caller: a lambda has a type of its own from C# 10, and one written against an earlier version — which is what the compiler of a Unity project is — has none, so it cannot be handed over as a `Delegate` at all. What such a caller writes is the delegate the lambda would have been, which is `Action` where the template takes nothing and hands nothing back, and the `Func` which describes it otherwise:

```csharp
host.GetMethod("Write", typeof(void).ToGneedleType())!
    .AroundBody((Action)(() =>
    {
        Proceed.Invoke();
        Console.WriteLine(message);
    }));
```

The delegate is what the weaving is given, so what the lambda captured is read out of it while the weaving runs, and the member that is woven carries `"Hello World"` as a string of its own rather than reaching for the instance the lambda was made from. What may be captured is settled by what the woven body can hold: a type, a string, an integer or a floating point number of any width, a character, a boolean, an enumeration, and a null of a reference type are written, and a capture of any other type is refused by name rather than woven into a member that would fail when it ran.

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

An injector is an attribute that implements one of the interfaces of `Gneedle.Inject` and says what to do with the member it is put on. It is applied as the project that declares it is built, so the weaving needs no step of its own. Which member an interface is read for:

1. **IAssemblyInjector**: for the assembly.
2. **IMethodInjector**: for a method.
3. **IFieldInjector**: for a field.
4. **IPropertyInjector**: for a property.
5. **ITypeInjector**: for a type, whatever kind of type it is. An injector of one of the kinds below which stands on a type of another kind is reported rather than passed over.
   + **IClassInjector**: for a class.
   + **IStructInjector**: for a struct.
   + **IEnumInjector**: for an enum.

An injector which stands on a member is read on the members which **override** it as well, and one which stands on a type on the types which **derive** from it. What decides it is the `AttributeUsage` of the type of the attribute, which is the reading the runtime makes of one: `Inherited = false` is read where it stands alone, and an attribute whose type declares no usage at all inherits, which is the default of the framework. What an override which carries an injector of its own is given is the one of the member it overrides first and its own after, so the more specific advice is the one which stands outermost.

Three things are not on that chain. A member which is inherited and not overridden is declared by the base and woven there, so a type which inherits one is woven by nothing of its own; an implementation of an interface member is not an override of it, so an injector of the interface member is read where it stands alone; and a member which hides the one above it with `new` declares a member of its own rather than overriding that one.

An injector is an attribute, so it is read out of the assembly at build time and does nothing at run time. The weaver is not a dependency of what you ship, and the attributes are taken back out once the injectors have been applied:

```csharp
public sealed class ThrowBodyAttribute : Attribute, IMethodInjector
{
    public int Priority => 0; // the order in which injectors are applied: a greater priority is applied before a lesser one
    public void Inject(MethodInfo method, IMethodHandler handler) => handler.SetBody(DefaultMethodBody.ThrowException);
}

public class Target
{
    [ThrowBody] public void Weave() => Console.WriteLine("the body ran unchanged");
}
```

An injector that carries something of its own writes its template as a lambda that captures it, and the weaving reads what it captured while the injector runs, so the member carries the value itself rather than the instance the lambda was made from:

```csharp
public sealed class LogMessageAttribute(string message) : Attribute, IMethodInjector
{
    public int Priority => 0;
    public void Inject(MethodInfo method, IMethodHandler handler) => handler.AroundBody(() =>
    {
        Proceed.Invoke();
        Console.WriteLine(message);
    });
}
```

A project whose language is older than C# 10 writes the delegate the lambda would have been rather than the lambda itself, which is what the note under [Around advice](#around-advice) describes.

### What is left in the assembly

The attributes are read at build time and do nothing at run time, and the weaver is not a dependency of what you ship. The task takes both back out once the injectors have been applied: the attributes are taken off the members which carry them, by the full names which were read, whichever assembly declares the attribute type — a type which declares one is removed where nothing else names it, and where something does it keeps its place and gives up the interfaces which make it an injector, and the `Inject` method with them — and the reference to the weaver is dropped when nothing of the assembly names it anymore.

A type that your code still names — through `typeof`, a field, a signature — cannot be removed without taking those names with it, so it keeps its place and gives up what makes it an injector instead. Either way the assembly that was woven carries no weaver.

### Project properties

Two properties change what is done with a project, and they answer different questions: whether anything is woven into the project at all, and whether the weaver is left in the assembly once it has been.

| Property     | Values             | Default  | Description                                                                                                                                                                                                                                               |
|--------------|--------------------|----------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Aspect`     | `disable`/`enable` | `enable` | `disable` leaves the aspect out of the project: the scan of the solution does not add the target to it, and the task returns without reading its assembly. A project which sets any other value, and one which does not set the property at all, is woven. |
| `KeepWeaver` | `true`/`false`     | `false`  | `true` keeps the attributes that the injectors were read from, and the reference to the weaver that they name, in the assembly. `false` takes both back out, which is what leaves the woven assembly standing alone.                                       |

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

`Aspect` is read out of the project file itself, because the scan of a solution reads the projects that it walks without building them, and a property that comes from an imported file is not in a project's own file. A group of properties that the build reads under a condition is read for the same reason whatever the condition says, so a project that sets `disable` under a condition is a project which is left out of the weaving in every configuration it is built in. The value it is read for is `disable` alone, as the letters are written: any other value, down to another case of the same word, is a project which is woven. `KeepWeaver` is matched without regard to case, and it is passed to the task by the build, so it is an ordinary property: it is set on the command line as well as in the project file.

Neither property is read by the IL post processor that a Unity project is given, so neither is set there; what a Unity project is woven by is under [Unity Editor](#unity-editor).

### Unity editor

A Unity project is not built by MSBuild, so there the aspect weaver is an IL post processor instead of a build task. A post processor is a part of the compilation rather than a step beside it: Unity hands it the image of an assembly that it has just compiled, takes back the image that it answers with, and writes that one. So the weaving is the same for the editor and for a player, no target is written into anything, and nothing has to be run after the build.

Every assembly of the project is compiled that way, Unity's own among them, so the post processor weaves the ones that declare an injector and answers each of the others with the image that it was handed. What it looks for is a type that implements one of the injector interfaces: an assembly that declares none has nothing for the weaving to read, and is left as it was compiled.

What the woven assembly is left holding is the same as under the build task: the attributes are removed with the types that declare them, and the reference to the weaver goes with them when nothing of the assembly names it anymore.

The `Aspect` and `KeepWeaver` properties belong to the build task, which reads them out of a project file, and a Unity project has none. There is therefore nothing there to turn the weaving off with, and the attributes and the reference to the weaver are always taken back out.

## Unexpected

A template is an ordinary method, and almost all of one is carried. The instructions are copied with the regions which protect them, so a `try` of a template catches where it was woven, a `using` disposes there, a `lock` releases there, and a `foreach` over an enumerator which is disposable disposes it there. The branches are carried — `if`, `switch`, `goto` and the loops — and the locals of the template are remapped to the body which is woven.

A construct which the compiler writes as a method of its own is carried as well. A lambda, a local function, an `async` body and an iterator body each live in a method beside the one the template is: the body of an `async` template, and of one which yields, is the stub which starts a state machine, whose `MoveNext` holds what was written, and a lambda leaves a type of its own. What the compiler wrote is moved onto the type being woven — a copy of it is declared inside that type, with every operand which named the original naming the copy — and the bodies it holds are woven there, so a placeholder written inside a lambda resolves where it stands.

The weaving refuses a body which it cannot carry, and names what it refused:

* a template which reads the instance it belongs to where that instance is an instance of another type than the one being woven.
* one which captures a value which has no form of its own.
* one which holds a `ValuableMember` in a local and reads it for anything other than the member it names.
* one which reaches a member of an instance where the member being woven belongs to none.
* one which reaches a member of the instance of the member being woven from a body the compiler wrote where that body holds no instance.
* one which proceeds into the body that was taken over from such a body.
* one which reads what the template captured through the instance the delegate holds.
* a template handed over as a `MethodInfo` which reads the instance the delegate was made from — unless that template is declared in the type it is woven into, where the instance it reads is the one the member being woven was made with.

A delegate which names a member that it does not describe — one which hands back another value than the member hands back, or an instantiation that the constraints of the member's own parameter refuse — is refused as a member that the assembly does not hold.

## Requirement

* **The project** that is woven references the weaver, which targets `net6.0`, `netstandard2.1` and `net472`.
* **The build task**, which is what `Gneedle.Aspect` is, runs on MSBuild 17.6 or later, which is what the .NET SDK, Visual Studio 2022 and Rider provide. A full framework MSBuild loads the `net472` build of the task and a `dotnet build` loads the `netstandard2.1` one; the package picks between them by itself. A project that drives the weaver from its own code is not bound by this, since it runs what it writes.
* **Unity** 2021.3 or later, for the two packages that are installed through the Package Manager. The weaver is a plugin of the project there, so it is compiled for `netstandard2.1`, and Mono.Cecil is brought in by the package that names it as a dependency rather than installed by hand.
* **Nothing** of the weaver is needed by the assembly once it has been woven, so the weaver does not become a dependency of what you ship.

Gneedle is built on [Mono.Cecil](https://github.com/jbevain/cecil), which reads the metadata of an assembly and writes it back. Gneedle decides what each instruction becomes; Mono.Cecil is what turns that into an assembly the runtime reads.

## Install

### NuGet

```
dotnet add package Gneedle.Aspect
```

`Gneedle.Inject` is a dependency of it, so the weaver that the task weaves with is installed along with it. It is also the package to reference on its own, and the only one, when you drive the weaving yourself or when you write an injector, because the interfaces that one implements are declared in it:

```
dotnet add package Gneedle.Inject
```

Or in the project file, where both lines name the version:

```xml
<PackageReference Include="Gneedle.Aspect" Version="0.0.4"/>
<PackageReference Include="Gneedle.Inject" Version="0.0.4"/>
```

### Unity

The same two are packages of Unity as well, under the names `com.gatongone.gneedle.inject` and `com.gatongone.gneedle.aspect`. The Package Manager reads a package either out of a git repository or out of a registry, and both of them are open to these two.

**From the repository.** A package is installed by the path it lies at, and a revision may be named after that path, so that what is installed is held to that revision:

```json
{
  "dependencies": {
    "com.gatongone.gneedle.inject": "https://github.com/Gatongone/Gneedle.git?path=Inject/Unity#v0.0.4",
    "com.gatongone.gneedle.aspect": "https://github.com/Gatongone/Gneedle.git?path=Aspect/Unity#v0.0.4"
  }
}
```

Both lines are written even where only one of the two is used, and it is the aspect weaver that is installed alone. A package that names another names a version of it, and that version is read out of a registry rather than along the path, so the weaver is installed beside the aspect weaver or not at all. This is a `Packages/manifest.json`, that the Package Manager window writes as well: *Add package from git URL* takes the one of the two that is wanted.

**From npm or OpenUPM.** The same two are published to npm, and [OpenUPM](https://openupm.com/) builds a package out of the tags of the repository and serves what it builds from a registry of its own. Both are read through a scoped registry, and what is installed this way is held at the version that is named until another is, which is the difference between the two ways in: a package is taken from a registry by version, and from a repository by revision:

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": ["com.gatongone"]
    }
  ],
  "dependencies": {
    "com.gatongone.gneedle.inject": "0.0.4",
    "com.gatongone.gneedle.aspect": "0.0.4"
  }
}
```

The same block with `NPMJS` in place of `OpenUPM`, and `https://registry.npmjs.org` in place of that URL, reads the two out of the registry of npm. OpenUPM's own command line writes that registry into the manifest and takes the package in one step:

```
openupm add com.gatongone.gneedle.inject
openupm add com.gatongone.gneedle.aspect
```

## Test

The tests of the weaver and of the build task are run with `dotnet test`, from the root of the repository, and each project is built for every framework it names:

```
dotnet test Inject/Tests/Gneedle.Inject.Test.csproj    # the weaver, built for net6.0 and net472
dotnet test Aspect/Tests/Gneedle.Aspect.Test.csproj    # the build task, built for net472
```

The tests of the weaver are run on `net6.0` by the runtime they were built for. A machine that carries a newer one rather than the 6.0 runtime starts its host only when it is told to move forward to the runtime it holds:

```
DOTNET_ROLL_FORWARD=LatestMajor dotnet test Inject/Tests/Gneedle.Inject.Test.csproj
```

The tests read the templates back as the IL that the compiler wrote for them, and which IL it is depends on whether the build of the assembly of tests was optimized: a temporary which the optimizer folds into the use of it is no local of the body which stands before a call, and the read of it is not there to be counted. The shapes which the assertions name are the ones a build without the optimizer writes, which is what the project asks for in whichever configuration the tests are built; the shapes which a release build of a consumer holds are the ones the optimizer writes, which is what the same tests are run against a second time, with the symbols asked for because the configuration writes none and a build which carries none folds the temporaries of a template into the uses of them. Which of the two legs an assembly was built as is named in its metadata, and a test of the suite reads it back: a build which does not answer the flag, or one which was left behind by the other leg, is reported rather than passing as a shape which it does not hold:

```
DOTNET_ROLL_FORWARD=LatestMajor dotnet test Inject/Tests/Gneedle.Inject.Test.csproj -c Release -p:Optimize=true -p:DebugSymbols=true
```

The tests of the post processor are beside it in the package of Unity rather than here, because the compilation pipeline which those tests are written against is one which only an editor has; they are run by the test runner of an editor, which [the readme of that package](Aspect/Unity/README.md#tests) describes.

The whole tree is built with `dotnet build Gneedle.sln -c Release`.

## License

Gneedle is released under the [MIT License](LICENSE.txt). Copyright (c) 2026, Gatongone.
