# Gneedle

Gneedle is an IL weaving library and an aspect weaver for .NET.

An aspect weaver usually asks you to learn a language of its own to say what should happen where. Gneedle asks for a plain C# method instead: the body you write is the body that ends up in the assembly, with the operands pointed at the members it names. That template is compiled by the same compiler as the rest of your code, checked by it, and debugged by the same tools.

`Gneedle.Inject` is the library that does that, and it is a weaver and nothing else: an assembly is opened, its metadata is read and rewritten, and the result is written back. It is driven the same way from a code generator, from a tool of your own, or from a build.

`Gneedle.Aspect` is the aspect weaver, and it is a package of its own that is not always needed. It drives the library from the build: it reads the injectors from the attributes of the assembly that it is building, applies them to it, and takes itself back out, so that weaving a project needs no entry point of its own beyond the attributes. A project that only ever weaves at an entry point of its own does not need it.

Where an assembly is built decides that form of it is used, and the two are the same weaver: a .NET project is given an MSBuild task, and a Unity project is given an IL post processor, that the compilation pipeline of the editor runs. Both of them read the same attributes, drive the same library, and leave the same assembly behind.

> [!IMPORTANT]
> This project is still in the early stages of development, which means that I may break compatibility in order to fix major bugs or include critical features.

# Principle

A template is an ordinary method. It reaches the members of the type it will be woven into through placeholders, that are calls into `Gneedle.Inject` — `This`, `Base`, `Instance`, `Static`, `Proceed` — and it names a generic parameter of the target through the tokens `T_0`–`T_20` and `M_0`–`M_20`. Every placeholder throws when it runs, because a template is never meant to run as it is written; the weaver replaces each of them with the member it names.

Weaving is therefore a rewrite rather than a compilation. The body of the template is copied instruction by instruction, and each operand is pointed at the member of the target that the placeholder named: a field read becomes `ldfld` of that field, a call on `This` becomes a call on the type being woven, a token becomes a generic parameter of the method or of the type that declares it. A local variable or a branch of the template is remapped to its counterpart in the body that is being woven.

A template may capture a variable as well, by being written as a lambda. What it captured is nothing the target holds, so it is not copied: it belongs to the run that wove the assembly rather than to the type that was woven, and the delegate which the template is handed over as is what holds it. The weaver reads the value out of that delegate while the injector runs and writes the value itself where the template read it, so the woven member carries the value as a constant and behaves as if it
had stood in the template's source. The delegating overloads are given the delegate rather than the method alone for that reason — `SetBody` and `AroundBody` of a handler, and `WithBody`, `WithGetter` and `WithSetter` of a decorator — and a template handed over as a `MethodInfo` holds nothing to read, so one which reads the instance it belongs to is refused.

What may be captured is settled by what the woven body can hold: a string, an integer or a floating point number of any width, a character, a boolean, an enumeration, and a null of a reference type are written, and a capture of any other type is refused by name rather than woven into a member which would fail when it ran.

None of that could be expressed without [Mono.Cecil](https://github.com/jbevain/cecil), that Gneedle is built on: Mono.Cecil is what reads the metadata of an assembly and writes it back, and the rewrite above is described in the terms that it hands over — the instructions, the references, the signatures and the tables of the image. Gneedle decides what each of them becomes; Mono.Cecil is what turns that into an assembly the runtime reads.

Around advice keeps the body that the target already had instead of discarding it. That body is moved to a generated method of the declaring type, and the template reaches it through `Proceed`, so that the advice can inspect the arguments, call the original, and return what it chose.

The attribute that names an injector is only the way the build finds out what to do. By the time the assembly is written the injector has been applied and the attribute has done its work, so the weaver takes it back out along with the reference to itself: the assembly that was woven carries neither the attributes nor the weaver that read them.

> [!WARNING]
> A template is an ordinary method, and almost all of one is carried. The instructions are copied with the regions which
> protect them, so a `try` of a template catches where it was woven, a `using` disposes there, a `lock` releases there,
> and a `foreach` over an enumerator which is disposable disposes it there. The branches are carried — `if`, `switch`,
`goto` and the loops — and the locals of the template are remapped to the body which is woven.
>
> What is not carried is a construct which the compiler writes as a method of its own. A lambda, a local function, an
`async` body and an iterator body each live in a method beside the one the template is: the body of an `async` template,
> and of one which yields, is the stub which starts a state machine, whose `MoveNext` holds what was written, and a lambda
> leaves a type of its own which is private to the assembly the template was compiled into. Such a template is refused
> where the weaving runs, rather than written into a member which would reach for that type and fail when it is run.
>
> The weaving refuses a body which it cannot carry, and names what it refused: a template which reads the instance it belongs to, one which captures a value which has no form of its own, one which calls a lambda or a local function written inside it, one which holds a `ValuableMember` in a local and reads it for anything other than the member it names, and one which reaches a member of an instance where the member being woven belongs to none. A delegate which names a member that it does not describe — one which hands back another value than the member hands back, or an instantiation which the constraints of the member's own parameter refuse — is refused as a member which the assembly does not hold.

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

<PackageReference Include="Gneedle.Aspect" Version="0.0.2"/>
```

`Gneedle.Inject` is a dependency of it, so the weaver that the task weaves with is installed along with it. It is also the package to reference on its own, and the only one, when you drive the weaving yourself or when you write an injector, because the interfaces that one implements are declared in it:

```xml

<PackageReference Include="Gneedle.Inject" Version="0.0.2"/>
```

## Unity

The same two are packages of Unity as well. The Package Manager reads a package either out of a git repository or out of a registry, and both of them are open to these two.

### From the repository

A package is installed by the path it lies at, and a revision may be named after that path, so that a tag holds what is installed to a release:

```json
{
  "dependencies": {
    "com.gatongone.gneedle.inject": "https://github.com/Gatongone/Gneedle.git?path=Inject/Unity#v0.0.2",
    "com.gatongone.gneedle.aspect": "https://github.com/Gatongone/Gneedle.git?path=Aspect/Unity#v0.0.2"
  }
}
```

Both lines are written even where only one of the two is used, and the aspect weaver is the one that is used alone. A package that names another names a version of it, and that version is read out of a registry rather than along the path, so the weaver is installed beside the aspect weaver or not at all.

This is a `Packages/manifest.json`, that the Package Manager window writes as well: *Add package from git URL* takes the one of the two that is wanted.

### From NPMJS

The same two are published to npm under the names above, which the Package Manager reads through a scoped registry:

```json
{
  "scopedRegistries": [
    {
      "name": "npmjs",
      "url": "https://registry.npmjs.org",
      "scopes": [
        "com.gatongone"
      ]
    }
  ],
  "dependencies": {
    "com.gatongone.gneedle.inject": "0.0.2",
    "com.gatongone.gneedle.aspect": "0.0.2"
  }
}
```

What is installed this way is held at the version that is named until another is, which is the difference between the two ways in: a package is taken from a registry by version, and from a repository by revision.

### From OpenUPM

[OpenUPM](https://openupm.com/) builds a package out of the tags of the repository that declares it, and serves what it builds from a registry of its own. The same two are listed there under the names above:

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.gatongone"
      ]
    }
  ],
  "dependencies": {
    "com.gatongone.gneedle.inject": "0.0.2",
    "com.gatongone.gneedle.aspect": "0.0.2"
  }
}
```

Its own command line writes that registry into the manifest and takes the package in one step, which is the way its documentation recommends:

```
openupm add com.gatongone.gneedle.aspect
```

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

The injectors which an assembly declares are applied from an entry point of your own as well, without the aspect weaver: `Injections.Apply` reads them from the attributes of the assembly, applies each of them to the member it names, and hands back the image which holds the result — or, where an injector reported something, the image it was given, because a run which reported anything leaves an assembly woven in part, which no caller could tell from one which was woven whole. The assembly is given as the one which was loaded from those bytes, because how an assembly is loaded belongs to whoever loads it, and the types which an injector names are resolved against it:

```csharp
var image = File.ReadAllBytes("path/to/AnAssembly.dll");
var (changed, woven) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image);
if (changed) File.WriteAllBytes("path/to/AnAssembly.dll", woven);
```

The attributes and the reference to the weaver are taken out of the image by default, which is what leaves the woven assembly standing alone; `removesTheWeaver: false` keeps them. What an injector is, and which member each of the interfaces of one is read for, is under [Aspect](#aspect).

> [!IMPORTANT]
> An assembly that no file holds (location missing) — one that an editor has just compiled in memory, or loaded from network stream — is given to the weaving through `AssemblyLoader.LoadFromBytes`, which loads the assembly from its image and remembers the bytes, and `Remember` does the same for an assembly that the runtime loaded by another way. That image is what a type of such an assembly is resolved against, which is how a template names a type of it
> through [a stub](#referring-to-a-type-you-cannot-reference). An assembly which the runtime loaded out of a file is read where that file lies, and one that it loaded from bytes in another way is read out of the memory of the process, which only Windows can do.
>
> The assemblies of the weaver are answered to such an assembly as well, and to one of those alone: the weaver that its templates were compiled against is resolved to the weaver which is weaving it, rather than to a copy that would have to lie beside the image.

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

| Placeholder                                                                                  | Names                                                                                   |
|--------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------|
| `This.Field<T>("name")`, `This.Property<T>("name")`                                          | a field or a property of the type being woven, read and written through `Get` and `Set` |
| `This.Method<TDelegate>("name")`                                                             | a method of the type being woven, called through the delegate that gives its signature  |
| `Base.Field`, `Base.Property`, `Base.Method`                                                 | the same, on the type that the target derives from                                      |
| `new Instance(instance).Field`, `new Instance(instance).Property`, `new Instance(instance).Method` | the same, on an instance the template pushed                                       |
| `Static.From("Full.Type.Name").Method`                                                       | the same, on a type named by a string                                                   |
| `T_0`–`T_20`, `M_0`–`M_20`                                                                 | the first to the twenty-first generic parameter of the declaring type, or of the method |
| `ValuableMember<T>`                                                                          | the value of a field or a property of a value type, which `ValuableMember` would box    |

Each placeholder is named after what it reaches, and none of them is named after a type of the framework: a placeholder which was called `Object` would be the type which a template reads wherever it writes `Object` among the usings of this library, so that a field, a parameter or a return type which names the type of the framework would name the placeholder instead. The keyword `object` is not affected by it, and neither is a template which writes `System.Object` in full.

The name which a placeholder is given is read out of the template itself, and the one instruction which the call follows is what holds it: a name is therefore one which the compiler writes there — a literal, a `nameof`, or a constant of the template — rather than one which the template computes while it runs.

```csharp
// The name of a member may be a constant of the template, since a constant is what the compiler writes there.
private const string Name = "Compute";

public static int ByAConstant() => This.Method<Func<int>>(Name)();
```

A name which no load stands ahead of is refused where the weaving runs, rather than left as a call which would reach the placeholder, and fail, when the member which was woven ran.

Where a generic parameter cannot be named by a `System.Type` — in the signature of a delegate, in a local variable, in a return type — a token stands in for it, and the weaver turns it into the parameter of the method being woven:

```csharp
// A template that takes and returns the first generic parameter of the type being woven.
public static T_0 Echo(T_0 value) => value;
```

### Around advice

`AroundBody` keeps the body that the method already had, moves it to a generated method named `<Name>k__Proceed`, and weaves the template around it. The template calls the original through `Proceed`:

```csharp
public static int AddOne(int value) => Proceed.Method<Func<int, int>>()(value) + 1;
```

```csharp
host.GetMethod("Double", typeof(int).ToGneedleType())!
    .AroundBody(typeof(Templates).GetMethod(nameof(Templates.AddOne))!);
```

The two forms of that call differ in what the template has to say about the signature of the body it proceeds into. `Proceed.Method<TMethod>()` names it — a delegate, which is how the template takes arguments of its own choosing, as `left * 2` above does — while `Proceed.Invoke<TResult>()` names only the type of the value which the body hands back and passes on the arguments which the template itself was given, which for a template of an around body are the arguments of the member:

```csharp
// The same advice as above, which passes the argument on as it is: no signature is written, and no delegate is needed.
public static int AddOne(int value) => Proceed.Invoke<int>() + 1;
```

A member which hands nothing back takes `Proceed.Invoke()`, and a member whose parameters are generic takes the token — `Proceed.Invoke<M_0>()` — so that a template of such a member needs no delegate of its own to name them with. What `Invoke<TResult>` names is checked against the type which the member hands back, and a template which proceeds without a body having been taken over is refused either way.

The template keeps the signature of the method, and the body it proceeds into is the one that the method held: the body it was added with, the body it was read with, or the throwing body that marks a method that has none. An accessor of a property is woven around the same way, through `IPropertyHandler.GetGetter()` and `GetSetter()`, that hand back the handler of the accessor as an ordinary method.

A template may be a lambda rather than a method, and then it may capture the variables which it is written among. An attribute is only what a driver of the library reads, and the library is driven from the build by [the aspect weaver](#aspect) or from a call of your own by `Injections.Apply`, which [the entry point above](#weaving-from-your-own-entry-point) describes. Where the weave is asked for at a call of your own, the template is written there:

```csharp
var message = "Hello World";
host.GetMethod("Write", typeof(void).ToGneedleType())!
    .AroundBody(() =>
    {
        Proceed.Invoke();
        Console.WriteLine(message);
    });
```

The delegate is what the weaving is given, so what the lambda captured is read out of it while the weaving runs, and the member which is woven carries `"Hello World"` as a string of its own rather than reaching for the instance the lambda was made from. What may be captured, and what is refused, is written out under [Principle](#principle).

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

An injector which carries something of its own writes its template as a lambda which captures it, and the weaving reads what it captured while the injector runs, so the member carries the value itself rather than the instance the lambda was made from:

```csharp
public sealed class LogMessageAttribute(string message) : Attribute, IMethodInjector
{
    public void Inject(MethodInfo method, IMethodHandler handler) => handler.AroundBody(() =>
    {
        Proceed.Invoke();
        Console.WriteLine(message);
    });
}
```

Which member an interface is read for: `IAssemblyInjector` for the assembly and `ITypeInjector` for a type, `IClassInjector`, `IStructInjector` and `IEnumInjector` for a type of that kind, and `IMethodInjector`, `IFieldInjector` and `IPropertyInjector` for a member. Every member of a type is looked at, whatever way a caller could reach it, and an injector that names a kind of type that it was put on is reported rather than passed over.

### What is left in the assembly

The attributes are read at build time and do nothing at run time, and the weaver is not a dependency of what you ship. The task takes both back out once the injectors have been applied: the attributes are taken off the members which carry them, by the full names which were read, whichever assembly declares the attribute type — a type which declares one is removed where nothing else names it, and where something does it keeps its place and gives up the interfaces which make it an injector, and the `Inject` method with them — and the reference to the weaver is dropped when nothing of the assembly names it any more.

A type that your code still names — through `typeof`, a field, a signature — cannot be removed without taking those names with it, so it keeps its place and gives up what makes it an injector instead. Either way the assembly that was woven carries no weaver.

### The properties that a project is described by

Two properties change what is done with a project, and they answer different questions: whether anything is woven into the project at all, and whether the weaver is left in the assembly once it has been.

| Property     | Values           | Default  | What it does                                                                                                                                                                                                                                               |
|--------------|------------------|----------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Aspect`     | `disable/enable` | `enable` | `disable` leaves the aspect out of the project: the scan of the solution does not add the target to it, and the task returns without reading its assembly. A project which sets any other value, and one which does not set the property at all, is woven. |
| `KeepWeaver` | `true/false`     | `false`  | `true` keeps the attributes that the injectors were read from, and the reference to the weaver that they name, in the assembly. `false` takes both back out, which is what leaves the woven assembly standing alone.                                       |

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

`Aspect` is read out of the project file itself, because the scan of a solution reads the projects that it walks without building them, and a property that comes from an imported file is not in a project's own file. A group of properties that the build reads under a condition is read for the same reason whatever the condition says, so a project that sets `disable` under a condition is a project which is left out of the weaving in every configuration it is built in. The value it is read for is `disable` alone, as the letters are written: any other value, down to another case of the same word, is a project which is woven. `KeepWeaver` is matched without regard to case, and it is passed to the task by the build, so it is an ordinary property: it
is set on the command line as well as in the project file.

Neither property is read by the IL post processor which a Unity project is given, so neither is set there; what a Unity project is woven by is under [Unity Editor](#unity-editor).

### Unity Editor

A Unity project is not built by MSBuild, so there the aspect weaver is an IL post processor instead of a build task. A post processor is a part of the compilation rather than a step beside it: Unity hands it the image of an assembly that it has just compiled, takes back the image that it answers with, and writes that one. So the weaving is the same for the editor and for a player, no target is written into anything, and nothing has to be run after the build.

Every assembly of the project is compiled that way, Unity's own among them, so the post processor weaves the ones that declare an injector and answers each of the others with the image that it was handed. What it looks for is a type that implements one of the injector interfaces: an assembly that declares none has nothing for the weaving to read, and is left as it was compiled.

What the woven assembly is left holding is the same as under the build task: the attributes are removed with the types that declare them, and the reference to the weaver goes with them when nothing of the assembly names it anymore.

The `Aspect` and `KeepWeaver` properties belong to the build task, that reads them out of a project file, and a Unity project has none. There is therefore nothing there to turn the weaving off with, and the attributes and the reference to the weaver are always taken back out.

# Test

The tests of the weaver and of the build task are run with `dotnet test`, from the root of the repository, and each project is built for every framework it names:

```
dotnet test Inject/Tests/Gneedle.Inject.Test.csproj    # the weaver, built for net5.0 and net472
dotnet test Aspect/Tests/Gneedle.Aspect.Test.csproj    # the build task, built for net472
```

The tests of the weaver are run on `net5.0` by the runtime they were built for. A machine which carries a newer one rather than the 5.0 runtime starts their host only when it is told to move forward to the runtime it holds:

```
DOTNET_ROLL_FORWARD=LatestMajor dotnet test Inject/Tests/Gneedle.Inject.Test.csproj
```

The tests read the templates back as the IL which the compiler wrote for them, and which IL that is depends on whether the build of the assembly of tests was optimized: a temporary which the optimizer folds into the use of it is no local of the body which stands before a call, and the read of it is not there to be counted. The shapes which the assertions name are the ones a build without the optimizer writes, which is what the project asks for whichever configuration the tests are built in; the shapes which a release build of a consumer holds are the ones the optimizer writes, which is what the same tests are run against a second time:

```
DOTNET_ROLL_FORWARD=LatestMajor dotnet test Inject/Tests/Gneedle.Inject.Test.csproj -p:Optimize=true
```

Which of the two legs an assembly was built as is named in its metadata, and a test of the suite reads it back: a build which does not answer the flag, or one which was left behind by the other leg, is reported rather than passing as a shape which it does not hold.

What is tested of the post processor is beside it in the package of Unity rather than here, because the compilation pipeline which those tests are written against is one which only an editor has; they are run by the test runner of an editor, which [the readme of that package](Aspect/Unity/README.md#tests) describes.

The whole tree is built with `dotnet build Gneedle.sln -c Release`.

# License

Gneedle is released under the [MIT](LICENSE.txt) Copyright (c) 2026, Gatongone.