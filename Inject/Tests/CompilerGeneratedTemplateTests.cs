using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// The templates which a compiler carries a body out of, one per construct which it writes a method or a type of its
/// own for.<para/>
/// Each of them takes and returns one value, so that every one of them is a body which a member of the same signature
/// could be given, and what tells them apart is the shape the compiler wrote rather than what they compute.
/// </summary>
public static class CompilerGeneratedTemplates
{
    /// <summary>
    /// A lambda which captured nothing, whose body the compiler writes as a static method of the type which holds the
    /// lambdas of this type.
    /// </summary>
    public static int RunsALambda(int value)
    {
        Func<int, int> addOne = x => x + 1;
        return addOne(value);
    }

    /// <summary>
    /// A lambda which captured a local, whose body the compiler writes as a method of a type of its own, with the local
    /// it captured as a field of that type.
    /// </summary>
    public static int RunsACapturingLambda(int value)
    {
        var one = 1;
        Func<int, int> add = x => x + one;
        return add(value);
    }

    /// <summary>
    /// A local function, which the compiler writes as a member of the type which declares the template.
    /// </summary>
    public static int RunsALocalFunction(int value)
    {
        static int AddOne(int x) => x + 1;
        return AddOne(value);
    }

    /// <summary>
    /// An iterator body, which the compiler writes as the <c>MoveNext</c> of a state machine.
    /// </summary>
    public static IEnumerable<int> Yields(int value)
    {
        yield return value;
        yield return value + 1;
    }

    /// <summary>
    /// An async body, which the compiler writes as the <c>MoveNext</c> of a state machine.
    /// </summary>
    public static async Task<int> Awaits(int value)
    {
        await Task.Yield();
        return value;
    }

    /// <summary>
    /// A body which calls a generic method, which is a shape the weaver carries whatever the compiler did with the body
    /// of the template: the call names the method through the instantiation it makes of it.<para/>
    /// What it is called on is a sequence of the framework rather than one the compiler writes for a collection
    /// expression: a type the compiler writes for one of those stands at the top level of the assembly the template was
    /// compiled into rather than nested inside what declares it, and the carrying reads the types it writes beside a
    /// template - which are nested - rather than those.
    /// </summary>
    public static int CallsAGenericMethod(int value) => Enumerable.First(Enumerable.Repeat(value, 1));

    /// <summary>
    /// A lambda which captured two locals rather than one, so that the type the compiler wrote for it holds more than
    /// one field.
    /// </summary>
    public static int CapturesTwoLocals(int value)
    {
        var one = 1;
        var two = 2;
        Func<int, int> add = x => x + one + two;
        return add(value);
    }

    /// <summary>
    /// A lambda which captured a value of a type of this assembly rather than of a type of the framework, so that the
    /// type the compiler wrote for it names a type which an assembly it is carried into does not hold.
    /// </summary>
    public static int CapturesATypeOfItsOwnAssembly(int value)
    {
        var captured = new Captured();
        Func<int, int> add = x => x + captured.One;
        return add(value);
    }

    /// <summary>
    /// The member which the lambda of the template below reaches through a placeholder, which is a member of the type
    /// being woven: the weaving writes it there before the template which reaches it is woven.
    /// </summary>
    public static int Twice(int value) => value * 2;

    /// <summary>
    /// A lambda whose body reaches a member of the type being woven through a placeholder. The placeholder stands
    /// inside the body which the compiler wrote for the lambda rather than in the template, so resolving it is what
    /// reading the carried body rather than re-pointing it is for.
    /// </summary>
    public static int ReachesAStaticMemberFromALambda(int value)
    {
        Func<int, int> twice = x => This.Method<Func<int, int>>("Twice")(x);
        return twice(value);
    }

    /// <summary>
    /// An async body which reaches a member of the type being woven through a placeholder. The placeholder stands in the
    /// <c>MoveNext</c> of the state machine which the compiler wrote, which is the body which is carried, and the state
    /// machine is the receiver of that body rather than the member being woven.
    /// </summary>
    public static async Task<int> AwaitsAStaticMember(int value)
    {
        await Task.Yield();
        return This.Method<Func<int, int>>("Twice")(value);
    }

    /// <summary>
    /// A lambda which reaches a member of an instance of the type being woven through a placeholder, which the body the
    /// compiler wrote for it reaches through the field the compiler writes that instance into.
    /// </summary>
    public static int ReachesAnInstanceMemberFromALambda(int value)
    {
        Func<int, int> twice = x => This.Method<Func<int, int>>("InstanceTwice")(x);
        return twice(value);
    }

    /// <summary>
    /// A template which holds a lambda and names a generic parameter of the type it is woven into at the second
    /// position, which a member of a type that declares one parameter does not hold: the return type of a template is
    /// read after its body is parsed, so what the carrying wrote is what a refusal there has to take back off.
    /// </summary>
    public static T_1 HoldsALambdaAndNamesASecondParameter(int value)
    {
        Func<int, int> add = x => x + 1;
        GC.KeepAlive(add(value));
        return null!;
    }

    /// <summary>
    /// A template which holds a local function which is generic in its own right and hands back what it was given.<para/>
    /// The member the compiler wrote for it declares a parameter of its own and its return type is that parameter, so
    /// the signature of a copy of it is written after the parameters it declares; and the call names the member through
    /// the instantiation it makes of it, so what is written is the instantiation of the copy rather than the open
    /// member, which is a call the runtime does not run.
    /// </summary>
    public static int CallsAGenericLocalFunction(int value)
    {
        return Identity(value);

        static T Identity<T>(T item) => item;
    }

    /// <summary>
    /// A lambda written inside a lambda, where the inner one captured what the outer one holds as well as a local of
    /// its own, which is what makes the compiler write a type for the one of them.
    /// </summary>
    public static int NestsLambdas(int value)
    {
        var one = 1;
        Func<int, int> outer = x =>
        {
            var two = 2;
            var inner = () => one + x + two;
            return inner();
        };
        return outer(value);
    }
}

/// <summary>
/// A type of this assembly, which the template above captures so that what the compiler wrote for its lambda names a
/// type which an assembly it is carried into does not hold.
/// </summary>
public class Captured
{
    /// <summary>
    /// The value the template reads out of what it captured.
    /// </summary>
    public int One = 1;
}

/// <summary>
/// A type which is generic, so that the body the compiler carries out of a template of it is generic as well: the type
/// it writes for the lambda is generic over <typeparamref name="T"/>, which is what the move has to re-parent.
/// </summary>
public static class GenericCompilerGeneratedTemplates<T>
{
    /// <summary>
    /// A lambda which captured a value of the type the template is generic over.
    /// </summary>
    public static T ReturnsWhatItCaptured(T value)
    {
        var captured = () => value;
        return captured();
    }
}

/// <summary>
/// The variable of the process which the injector below weaves, which is what keeps the members which carry it from
/// being woven by every test which weaves this assembly.
/// </summary>
public static class Carried
{
    /// <summary>
    /// The environment variable which the injector below reads, which holds the type whose member is woven, the name of
    /// the template which is woven into it, and the type which declares that template, separated by bars.
    /// </summary>
    public const string VARIABLE = "GneedleCarried";
}

/// <summary>
/// An attribute which gives the member it is put on the body of the template which the variable of the process names,
/// which is one injector for every template here: what an injector of this file is gated by is which test is running,
/// and an injector per template would say the same thing a second and a third time.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CarriedInjectorAttribute : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler)
    {
        if (Environment.GetEnvironmentVariable(Carried.VARIABLE) is not { } wanted) return;

        var parts = wanted.Split('|');
        if (parts[0] != method.DeclaringType!.FullName) return;

        var holder = typeof(GenericCompilerGeneratedTemplates<>).Assembly.GetType(parts[2]);
        Assert.That(holder, Is.Not.Null, $"the variable names no type of the assembly of these tests: {string.Join("|", parts)}");

        handler.SetBody(holder!.GetMethod(parts[1])!);
    }
}

/// <summary>
/// The type whose member carries the injector above, and which declares the template which is woven into it.<para/>
/// The template belongs to the type being woven, so the instance it reaches is the instance of that member, and the
/// lambda the template holds captures that instance, which is what makes the compiler write it into a field of the
/// type it writes for the lambda.
/// </summary>
public class CarriedInstanceFixture
{
    /// <summary>
    /// The value which the woven member adds, which is read off the instance rather than captured by the lambda.
    /// </summary>
    public readonly int Offset = 1;

    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public int Run(int value) => -1;

    /// <summary>
    /// The template which is woven into the member above, which reaches the field of the instance it belongs to both
    /// ways at once: the lambda reads the field itself, which is what makes the compiler hold that instance in the type
    /// it writes for the lambda, and it reaches the same field through a placeholder, which is what the weaving has to
    /// write against that instance rather than against the receiver of the lambda.
    /// </summary>
    public int ReachesTheInstanceFromALambda(int value)
    {
        // The local is what makes the compiler write a type for the lambda and hold the instance in a field of it: a
        // lambda which captures the instance alone is a method of the type it was written in, and its receiver is the
        // instance already.
        var one = 1;
        Func<int, int> add = x => x + Offset + one + This.Field<int>(nameof(Offset)).Get();
        return add(value);
    }
}

/// <summary>
/// The type whose member carries the injector above, and which declares a template whose local function the compiler
/// writes on that same type.<para/>
/// A local function which captured nothing is a member of the type which declares the template rather than a type of
/// its own, so the copy of it is declared by the type being woven where the compiler wrote the original: the two are
/// members of one name and one signature unless the copy takes a name of its own, and a type which declares two of
/// those is a type the runtime refuses to load.
/// </summary>
public class CarriedOwnLocalFunctionFixture
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static int Run(int value) => -1;

    /// <summary>
    /// The template which is woven into the member above, whose local function the compiler writes on this type.
    /// </summary>
    public static int ReachesALocalFunctionOfItsOwnType(int value)
    {
        return Twice(value);

        static int Twice(int number) => number * 2;
    }
}

/// <summary>
/// The type whose member carries the injector above, and onto which the type which the compiler wrote for the lambda is
/// carried by the tests below.
/// </summary>
public class CarriedClosureFixture
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static int Run(int value) => -1;
}

/// <summary>
/// The type whose member carries the injector above, which the lambda that captured two locals is woven into.
/// </summary>
public class CarriedTwoLocalsFixture
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static int Run(int value) => -1;
}

/// <summary>
/// The type whose member carries the injector above, which the lambda written inside a lambda is woven into.
/// </summary>
public class CarriedNestedFixture
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static int Run(int value) => -1;
}

/// <summary>
/// The type whose member carries the injector above, which the local function is woven into.
/// </summary>
public class CarriedLocalFunctionFixture
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static int Run(int value) => -1;
}

/// <summary>
/// The type whose member carries the injector above, which the state machine the compiler wrote for the iterator is
/// carried onto.
/// </summary>
public class CarriedIteratorFixture
{
    /// <summary>
    /// The member which the injector above weaves, which hands back what the template hands back.
    /// </summary>
    [CarriedInjector]
    public static IEnumerable<int> Run(int value) => [];
}

/// <summary>
/// The type whose member carries the injector above, which the state machine the compiler wrote for the async body is
/// carried onto.
/// </summary>
public class CarriedAsyncFixture
{
    /// <summary>
    /// The member which the injector above weaves, which hands back what the template hands back.
    /// </summary>
    [CarriedInjector]
    public static Task<int> Run(int value) => Task.FromResult(-1);
}

/// <summary>
/// The type whose member carries the injector above, which is generic so that the type the compiler wrote for the lambda
/// of its template is generic as well.
/// </summary>
public class CarriedGenericClosureFixture<T>
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static T Run(T value) => value;
}

/// <summary>
/// What the weaver does with a template which holds a construct the compiler carried out of it.<para/>
/// The constructs are read one by one rather than through one case, because the shape the compiler wrote differs with
/// each of them - a type of its own for a lambda which captured, a member of the type which declares the template for a
/// local function which captured nothing, a state machine for an iterator and for an async body - and what the carrying
/// does with one shape is not what it does with another.
/// </summary>
[TestFixture]
public class CompilerGeneratedTemplateTests
{
    /// <summary>
    /// Weave a template into a member of the signature it has, run that member, and hand back what it computed.<para/>
    /// The carrying here is the weaving's own: no move is written by hand, and what is read is what the weaving does
    /// with a template which holds a construct the compiler wrote. The member is run rather than read, because a body
    /// which reads back correctly is not yet a body which runs, and what a carrying got wrong is answered by the
    /// runtime rather than by the shape of the body. The assembly is one of this test's own, because two assemblies of
    /// one name cannot be loaded into one run.
    /// </summary>
    private static object? WovenAndRun(MethodInfo template, IType returnType, Parameter[] parameters, params object?[] arguments)
    {
        var (_, host, _) = NewHost($"CompilerGenerated{template.Name}{Guid.NewGuid():N}");
        var run = host.AddMethod("Run", returnType, [], parameters, MethodFlags.Public | MethodFlags.Static);

        run.SetBody(template);

        var woven = host.AssemblyHandler.Assembly.Load().GetType($"{NS}.Host")!.GetMethod("Run")!;
        return woven.Invoke(null, arguments);
    }

    /// <summary>
    /// The parameters which every template above is written with: each takes one value and hands one back, whatever it
    /// does with it, and the two which yield and await are woven with the same argument as the rest.
    /// </summary>
    private static Parameter[] OneValue => [new Parameter(typeof(int).ToGneedleType())];

    [Test]
    public void A_Template_Which_Holds_A_Lambda_Is_Woven()
        => Assert.That(WovenAndRun(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.RunsALambda)),
                typeof(int).ToGneedleType(), OneValue, 41),
            Is.EqualTo(42), "the member which was woven did not compute what the template computes.");

    [Test]
    public void A_Template_Which_Holds_A_Capturing_Lambda_Is_Woven()
        => Assert.That(WovenAndRun(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.RunsACapturingLambda)),
                typeof(int).ToGneedleType(), OneValue, 41),
            Is.EqualTo(42), "the value which the template captured was not written where the template read it.");

    [Test]
    public void A_Template_Which_Holds_A_Local_Function_Is_Woven()
    {
        // The one of the five which holds no type of its own: a local function which captured nothing is a private
        // method of the type which declares the template, so there is no type to move for it and the member is moved on
        // its own. It is the cheaper of the two moves and the one the other four are not.
        Assert.That(WovenAndRun(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.RunsALocalFunction)),
                typeof(int).ToGneedleType(), OneValue, 41),
            Is.EqualTo(42), "the member which was woven did not compute what the template computes.");
    }

    [Test]
    public void A_Template_Which_Is_An_Iterator_Is_Woven_And_Enumerated()
    {
        var produced = ((IEnumerable<int>) WovenAndRun(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.Yields)),
            typeof(IEnumerable<int>).ToGneedleType(), OneValue, 7)!).ToArray();

        Assert.That(produced, Is.EqualTo(new[] {7, 8}), "the member which was woven did not produce what the template produces.");
    }

    [Test]
    public void A_Template_Which_Is_An_Async_Body_Is_Woven_And_Awaited()
    {
        var awaited = (Task<int>) WovenAndRun(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.Awaits)),
            typeof(Task<int>).ToGneedleType(), OneValue, 5)!;

        Assert.That(awaited.GetAwaiter().GetResult(), Is.EqualTo(5), "the member which was woven did not hand back what the template hands back.");
    }

    [Test]
    [NonParallelizable]
    public void A_Placeholder_Inside_A_Lambda_Is_Woven_Where_It_Stands()
    {
        // The body of the lambda is carried onto the type being woven and is read there, so a placeholder which stands
        // inside it is resolved where it stands: the call it makes names a member of that type, and the member computes
        // what the template computes. Nothing of the placeholder is left in the body which was woven.
        var intType = typeof(int).ToGneedleType();
        var (_, host, _) = NewHost($"CompilerGeneratedInALambda{Guid.NewGuid():N}");

        host.AddMethod("Twice", intType, [], [new Parameter(intType)], MethodFlags.Public | MethodFlags.Static)
            .SetBody(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.Twice)));

        var run = host.AddMethod("Run", intType, [], [new Parameter(intType)], MethodFlags.Public | MethodFlags.Static);
        run.SetBody(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.ReachesAStaticMemberFromALambda)));

        // What the member computes is what tells whether the placeholder was woven: a placeholder which was left in the
        // body of the copy throws when it is reached, which is the run below.
        var woven = host.AssemblyHandler.Assembly.Load().GetType($"{NS}.Host")!.GetMethod("Run")!;

        Assert.That(woven.Invoke(null, [21]), Is.EqualTo(42), "the placeholder which stands inside the lambda was not woven.");
    }

    [Test]
    [NonParallelizable]
    public void A_Placeholder_Inside_An_Async_Body_Is_Woven_Where_It_Stands()
    {
        // The body which is carried here is the MoveNext of the state machine, whose receiver is the machine rather than
        // the member being woven: a placeholder which reaches a member which belongs to no instance needs no receiver,
        // so it is woven where it stands and the machine the copy holds is what runs it.
        var intType = typeof(int).ToGneedleType();
        var (_, host, _) = NewHost($"CompilerGeneratedInAnAsyncBody{Guid.NewGuid():N}");

        host.AddMethod("Twice", intType, [], [new Parameter(intType)], MethodFlags.Public | MethodFlags.Static)
            .SetBody(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.Twice)));

        var run = host.AddMethod("Run", typeof(Task<int>).ToGneedleType(), [], [new Parameter(intType)], MethodFlags.Public | MethodFlags.Static);
        run.SetBody(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.AwaitsAStaticMember)));

        var woven = host.AssemblyHandler.Assembly.Load().GetType($"{NS}.Host")!.GetMethod("Run")!;
        var awaited = (Task<int>) woven.Invoke(null, [21])!;

        Assert.That(awaited.GetAwaiter().GetResult(), Is.EqualTo(42), "the placeholder which stands inside the state machine was not woven.");
    }

    [Test]
    [NonParallelizable]
    public void A_Placeholder_Which_Needs_The_Instance_Inside_A_Lambda_Is_Refused()
    {
        // The instance which the member being woven belongs to is not the receiver of the body the compiler wrote for a
        // lambda, and a lambda which captured nothing holds no field to read it out of: the placeholder is refused
        // rather than written against the receiver of that body, which is a value of another type.
        var intType = typeof(int).ToGneedleType();
        var (_, host, _) = NewHost($"CompilerGeneratedInALambdaForAnInstance{Guid.NewGuid():N}");

        // The member the placeholder names belongs to an instance of the type being woven, which is what the body of the
        // lambda would have to reach it through.
        host.AddMethod("InstanceTwice", intType, [], [new Parameter(intType)], MethodFlags.Public)
            .SetBody(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.Twice)));

        var run = host.AddMethod("Run", intType, [], [new Parameter(intType)], MethodFlags.Public | MethodFlags.Static);
        var refusal = Assert.Throws<ArgumentException>(
            () => run.SetBody(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.ReachesAnInstanceMemberFromALambda))));

        Assert.That(refusal!.Message, Does.Contain("can reach no instance of the member being woven"),
            $"the refusal does not say that the body reaches no instance: {refusal.Message}");
    }

    /// <summary>
    /// The type whose member is woven and which declares the template which is woven into it.
    /// </summary>
    private const string CARRIED_INSTANCE_TYPE = "Gneedle.Inject.Test.CarriedInstanceFixture";

    [Test]
    [NonParallelizable]
    public void A_Template_Of_The_Type_Being_Woven_Reaches_Its_Instance_From_A_Lambda()
    {
        // The instance which the placeholder reaches is not the receiver of the lambda: the compiler holds it in a field
        // of the type it wrote for the lambda, and the weaving reads the member off that instance rather than off the
        // lambda. The field is read by the lambda itself as well, so what the member computes is the value it was given
        // with the field added twice and the local the lambda captured added once.
        var (result, reported) = Woven(CARRIED_INSTANCE_TYPE, nameof(CarriedInstanceFixture.ReachesTheInstanceFromALambda), CARRIED_INSTANCE_TYPE);
        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        var type = AssemblyLoader.LoadFromBytes(result).GetType(CARRIED_INSTANCE_TYPE)!;
        var instance = Activator.CreateInstance(type);

        Assert.That(type.GetMethod("Run")!.Invoke(instance, [41]), Is.EqualTo(44),
            "the placeholder which reaches the instance of the member being woven from a lambda was not woven.");
    }

    [Test]
    [NonParallelizable]
    public void A_Local_Function_Of_The_Type_Being_Woven_Is_Carried_Under_A_Name_Of_Its_Own()
    {
        // The compiler writes the local function on the type which declares the template, which is the type being woven
        // here, so the copy of it would be a second member of that name and that signature: the type would not load, and
        // what is read is that it does - the member is run - and that the type declares the copy apart from the member
        // the compiler wrote.
        var (result, reported) = Woven(CARRIED_OWN_LOCAL_FUNCTION_TYPE,
            nameof(CarriedOwnLocalFunctionFixture.ReachesALocalFunctionOfItsOwnType), CARRIED_OWN_LOCAL_FUNCTION_TYPE);
        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using (var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result)))
        {
            var carried = read.MainModule.GetType(CARRIED_OWN_LOCAL_FUNCTION_TYPE)!
                              .Methods.Where(method => method.Name.IndexOf(">g__", StringComparison.Ordinal) >= 0).ToArray();

            Assert.That(carried, Has.Length.EqualTo(2),
                $"the type does not declare the member the compiler wrote and the copy of it: {string.Join(", ", carried.Select(method => method.Name))}");
            Assert.That(carried.Select(method => method.Name).Distinct().Count(), Is.EqualTo(2),
                "the copy was declared under the name of the member the compiler wrote, so the type declares it twice.");
        }

        Assert.That(Ran(result, CARRIED_OWN_LOCAL_FUNCTION_TYPE, "Run", 21), Is.EqualTo(42),
            "the member which was woven did not compute what the template computes.");
    }

    [Test]
    [NonParallelizable]
    public void A_Template_Which_Is_Refused_After_It_Was_Carried_Leaves_The_Type_As_It_Was()
    {
        // The return type of a template is read after its body was parsed, so a template which names a token at a
        // position the member being woven does not declare is refused after the carrying of it succeeded: what the
        // carrying wrote onto the type being woven is taken back off there as well, which is what the type declaring
        // nothing of the compiler's own reads.
        var intType = typeof(int).ToGneedleType();
        var (_, host, _) = NewHost($"CompilerGeneratedRefusedAfterTheCarrying{Guid.NewGuid():N}");
        var run = host.AddMethod("Run", intType, [], [new Parameter(intType)], MethodFlags.Public | MethodFlags.Static);

        Assert.Throws<ArgumentException>(
            () => run.SetBody(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.HoldsALambdaAndNamesASecondParameter))));

        Assert.Multiple(() =>
        {
            Assert.That(host.Source.NestedTypes, Is.Empty,
                "the type which was woven declares the copy of a type which the compiler wrote, which the refused weaving left behind.");
            Assert.That(host.Source.Methods.Any(method => method.Name.IndexOf(">b__", StringComparison.Ordinal) >= 0), Is.False,
                "the type which was woven declares the copy of a member which the compiler wrote, which the refused weaving left behind.");
        });
    }

    [Test]
    public void A_Template_Which_Holds_A_Generic_Local_Function_Is_Woven_And_Run()
    {
        // What the compiler wrote is a member of the type being woven which declares a parameter of its own, and its
        // return type is that parameter: the copy of it is signed after those are declared, and the call of it is the
        // instantiation the template names rather than the open member.
        Assert.That(WovenAndRun(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.CallsAGenericLocalFunction)),
                typeof(int).ToGneedleType(), OneValue, 41),
            Is.EqualTo(41), "the member which was woven did not compute what the template computes.");
    }

    [Test]
    public void A_Template_Which_Calls_A_Generic_Method_Is_Woven()
    {
        // Nothing here is a construct the compiler carried out of the template: the call names a generic method of the
        // framework through the instantiation it makes of it, which is a shape any template may write. What the member
        // computes is what tells a call which was carried from one written against the instantiation of another.
        Assert.That(WovenAndRun(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.CallsAGenericMethod)),
                typeof(int).ToGneedleType(), OneValue, 41),
            Is.EqualTo(41), "the member which was woven did not compute what the template computes.");
    }

    /// <summary>
    /// The type which declares the templates above, and each of the types whose member carries an injector and is woven
    /// with one of them, named as the tests address them.
    /// </summary>
    private const string TEMPLATES_TYPE = "Gneedle.Inject.Test.CompilerGeneratedTemplates";

    /// <inheritdoc cref="TEMPLATES_TYPE"/>
    private const string CARRIED_CLOSURE_TYPE = "Gneedle.Inject.Test.CarriedClosureFixture";

    /// <inheritdoc cref="TEMPLATES_TYPE"/>
    private const string CARRIED_TWO_LOCALS_TYPE = "Gneedle.Inject.Test.CarriedTwoLocalsFixture";

    /// <inheritdoc cref="TEMPLATES_TYPE"/>
    private const string CARRIED_NESTED_TYPE = "Gneedle.Inject.Test.CarriedNestedFixture";

    /// <inheritdoc cref="TEMPLATES_TYPE"/>
    private const string CARRIED_LOCAL_FUNCTION_TYPE = "Gneedle.Inject.Test.CarriedLocalFunctionFixture";

    /// <inheritdoc cref="TEMPLATES_TYPE"/>
    private const string CARRIED_STATE_MACHINE_TYPE = "Gneedle.Inject.Test.CarriedIteratorFixture";

    /// <inheritdoc cref="TEMPLATES_TYPE"/>
    private const string CARRIED_ASYNC_TYPE = "Gneedle.Inject.Test.CarriedAsyncFixture";

    /// <inheritdoc cref="TEMPLATES_TYPE"/>
    private const string CARRIED_OWN_LOCAL_FUNCTION_TYPE = "Gneedle.Inject.Test.CarriedOwnLocalFunctionFixture";

    /// <summary>
    /// The type which declares the generic template, and the generic type which its lambda is carried onto, which are
    /// named as the metadata names a generic type rather than as the runtime writes an instantiation of it.
    /// </summary>
    private const string GENERIC_TEMPLATES_TYPE = "Gneedle.Inject.Test.GenericCompilerGeneratedTemplates`1";

    /// <inheritdoc cref="GENERIC_TEMPLATES_TYPE"/>
    private const string CARRIED_GENERIC_CLOSURE_TYPE = "Gneedle.Inject.Test.CarriedGenericClosureFixture`1";

    /// <summary>
    /// Weave the assembly of these tests once with the injector of the type which is named turned on, and hand back the
    /// image which was woven and what the run reported.<para/>
    /// The carrying is the weaving's own: no move is written by hand here, so what the tests below read is what the
    /// weaving does with a template which holds a construct the compiler wrote. What a template which is declared in
    /// the type being woven needs could not be written by hand anyway, because the instance it reaches is the one the
    /// member being woven was made with.
    /// </summary>
    /// <param name="holder">Full name of the type which declares the template.</param>
    /// <param name="template">Name of the template which is woven.</param>
    /// <param name="intoType">Full name of the type whose member is woven.</param>
    /// <param name="before">What is written into the image before it is woven, or null when nothing is.</param>
    private static (byte[] Result, List<string> Reported) Woven(string holder, string template, string intoType, Action<ModuleDefinition>? before = null)
    {
        var image = File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location);

        // A caller which writes something into the image first is woven from an image of its own, so that what it wrote
        // is what the weaving of it is read against.
        if (before is not null)
        {
            using var definition = AssemblyDefinition.ReadAssembly(new MemoryStream(image));
            before(definition.MainModule);

            using var written = new MemoryStream();
            definition.Write(written);
            image = written.ToArray();
        }

        var reported = new List<string>();

        Environment.SetEnvironmentVariable(Carried.VARIABLE, $"{intoType}|{template}|{holder}");
        try
        {
            var (changed, result) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image, reportError: reported.Add);
            Assert.That(changed, Is.True,
                $"the member which carries the injector was woven by nothing.{Environment.NewLine}{string.Join(Environment.NewLine, reported)}");

            return (result, reported);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Carried.VARIABLE, null);
        }
    }

    /// <summary>
    /// Run the member which was woven of the type which is named, and hand back what it computes.<para/>
    /// The assembly which was woven is loaded rather than read, because IL which reads back correctly is not yet IL
    /// which runs: what a move got wrong is answered here and not by the shape of the body.
    /// </summary>
    private static object? Ran(byte[] result, string intoType, string member, params object?[] arguments)
        => AssemblyLoader.LoadFromBytes(result).GetType(intoType)!.GetMethod(member)!.Invoke(null, arguments);

    [Test]
    [NonParallelizable]
    public void The_Type_Which_The_Compiler_Wrote_A_Lambda_Into_Can_Be_Carried_Onto_Another_Type()
    {
        // Tier 1 of the proposal is one move: the type which the compiler wrote for the body of a lambda goes onto the
        // type being woven, and every operand which named a member of it names the copy. What is read here is whether
        // that move is mechanical - whether an assembly which was written that way still reads, and holds no reference
        // of the body to the type which was moved.
        var (result, reported) = Woven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.RunsACapturingLambda), CARRIED_CLOSURE_TYPE);
        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using (var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result)))
        {
            var run = read.MainModule.GetType(CARRIED_CLOSURE_TYPE)!.Methods.Single(method => method.Name == "Run");

            // What the body names is the copy, which is a nested type of the type being woven, rather than the type the
            // compiler wrote, which is nested in the type which declares the template: a copy keeps the name of what it
            // was written from, so the two are told apart by where they stand rather than by what they are called.
            var reached = run.Body.Instructions
                             .Select(instruction => instruction.Operand as MemberReference)
                             .Where(member => member?.DeclaringType?.Name.StartsWith("<", StringComparison.Ordinal) == true)
                             .Where(member => member!.DeclaringType!.GetElementType().DeclaringType?.FullName != CARRIED_CLOSURE_TYPE)
                             .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(reached, Is.Empty,
                    $"the body of the member still names what the compiler wrote: {string.Join(", ", reached.Select(member => member!.FullName))}");
                Assert.That(run.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldftn),
                    Is.True, "the member holds no delegate, so the template was not carried into it.");
            });
        }

        Assert.That(Ran(result, CARRIED_CLOSURE_TYPE, "Run", 41), Is.EqualTo(42),
            "the member which was woven did not compute what the template computes.");

        // The template is a member of the assembly which was woven as well, and the carrying does not write to it: what
        // it names is what the compiler wrote, so the template still runs. A carrying which re-pointed the body of the
        // template at the copies would leave it naming a private type of another one, which the runtime refuses to run,
        // and a second weave of the same template would read what the first one wrote.
        Assert.That(AssemblyLoader.LoadFromBytes(result).GetType(TEMPLATES_TYPE)!
                        .GetMethod(nameof(CompilerGeneratedTemplates.RunsACapturingLambda))!.Invoke(null, [41]),
            Is.EqualTo(42), "the body of the template was written to, so what it names is a type it cannot reach.");
    }

    [Test]
    [NonParallelizable]
    public void The_Lambda_Which_Captured_Two_Locals_Is_Carried_With_Both_Of_Its_Fields()
    {
        var (result, reported) = Woven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.CapturesTwoLocals), CARRIED_TWO_LOCALS_TYPE);
        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));
            Assert.That(Ran(result, CARRIED_TWO_LOCALS_TYPE, "Run", 41), Is.EqualTo(44),
                "the member which was woven did not compute what the template computes.");
        });
    }

    [Test]
    [NonParallelizable]
    public void The_Lambda_Written_Inside_A_Lambda_Is_Carried_With_What_It_Reaches_In_Turn()
    {
        // The body of the outer lambda holds the pointer to the inner one, and that pointer names a type the compiler
        // wrote just as the pointer of the template does: what the move carries is not one type but everything the
        // bodies it carries reach, and this tells a move that follows what it carries from one that does not.
        var (result, reported) = Woven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.NestsLambdas), CARRIED_NESTED_TYPE);
        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));
            Assert.That(Ran(result, CARRIED_NESTED_TYPE, "Run", 41), Is.EqualTo(44),
                "the member which was woven did not compute what the template computes.");
        });
    }

    [Test]
    [NonParallelizable]
    public void The_Local_Function_Is_Carried_As_A_Member_Rather_Than_As_A_Type()
    {
        // A local function which captured nothing is not a type of its own: it is a member of the type which declares
        // the template, so what the carrying writes is a member of the type being woven rather than a nested type of
        // it, which is the other shape the move has.
        var (result, reported) = Woven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.RunsALocalFunction), CARRIED_LOCAL_FUNCTION_TYPE);
        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using (var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result)))
        {
            var woven = read.MainModule.GetType(CARRIED_LOCAL_FUNCTION_TYPE)!;
            Assert.Multiple(() =>
            {
                Assert.That(woven.Methods.Any(method => method.Name.IndexOf(">g__", StringComparison.Ordinal) >= 0),
                    Is.True, "the copy of the local function is not a member of the type which was woven.");
                Assert.That(woven.NestedTypes, Is.Empty,
                    "the copy of the local function was declared as a type of the type which was woven.");
            });
        }

        Assert.That(Ran(result, CARRIED_LOCAL_FUNCTION_TYPE, "Run", 41), Is.EqualTo(42),
            "the member which was woven did not compute what the template computes.");
    }

    [Test]
    [NonParallelizable]
    public void A_Template_Which_Is_An_Iterator_Whose_State_Machine_Was_Carried_Is_Woven_And_Enumerated()
    {
        // Tier 2 of the proposal, and the largest thing which tells it from Tier 1: the body of the template is not the
        // stub the member is given but the MoveNext of a state machine, and what is carried is a type whose method holds
        // a switch, a region which protects instructions and locals of its own.
        //
        // What is read here is the move rather than the parsing of MoveNext: the weaving is handed the stub, which is
        // what the member holds, because the move is what the proposal turns on and it is the move which is in doubt.
        var (result, reported) = Woven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.Yields), CARRIED_STATE_MACHINE_TYPE);

        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using (var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result)))
        {
            var run = read.MainModule.GetType(CARRIED_STATE_MACHINE_TYPE)!.Methods.Single(method => method.Name == "Run");
            Assert.That(run.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj),
                Is.True, "the member holds no state machine, so the template was not carried into it.");
        }

        // The sequence is enumerated, because a state machine which was moved is a type whose methods still have to
        // find the fields they were written against: the switch of MoveNext turns on a field of the type.
        var produced = ((IEnumerable<int>) Ran(result, CARRIED_STATE_MACHINE_TYPE, "Run", 7)!).ToArray();
        Assert.That(produced, Is.EqualTo(new[] {7, 8}),
            "the member which was woven did not produce what the template produces.");
    }

    [Test]
    [NonParallelizable]
    public void A_Carried_Type_Whose_Name_The_Target_Already_Holds_Is_Declared_Under_Another()
    {
        // What a copy is declared under is the name the compiler wrote, which the type being woven may already hold:
        // the type which was carried here is nested in the type which declares the template as well, and a type of that
        // name is put beside it, so the name is taken and the copy has to take one of its own.
        var (result, reported) = Woven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.RunsACapturingLambda), CARRIED_CLOSURE_TYPE,
            module => module.GetType(CARRIED_CLOSURE_TYPE)!
                            .NestedTypes.Add(new TypeDefinition("", "<>c__DisplayClass1_0", TypeAttributes.NestedPrivate, module.TypeSystem.Object)));

        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using (var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result)))
        {
            var names = read.MainModule.GetType(CARRIED_CLOSURE_TYPE)!.NestedTypes.Select(nested => nested.Name).ToArray();

            Assert.That(names, Does.Contain("<>c__DisplayClass1_0"), "the type which was there already was taken away.");
            Assert.That(names, Does.Contain("<>c__DisplayClass1_0_1"),
                $"the copy was not declared under a name of its own: {string.Join(", ", names)}");
        }

        Assert.That(Ran(result, CARRIED_CLOSURE_TYPE, "Run", 41), Is.EqualTo(42),
            "the member which was woven did not compute what the template computes.");
    }

    [Test]
    public void A_Copy_Which_Names_A_Type_Of_Its_Own_Assembly_Keeps_That_Type()
    {
        // What a body the compiler wrote reaches may be a type of another assembly than the one which is woven, and the
        // carrying writes what it reaches as it stands: the field of the copy names the type the template captured,
        // which is a type of the assembly which is woven here, so nothing is referenced and the image still reads.
        var (result, reported) = Woven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.CapturesATypeOfItsOwnAssembly), CARRIED_CLOSURE_TYPE);
        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result));
        var carried = read.MainModule.GetType(CARRIED_CLOSURE_TYPE)!.NestedTypes.Single();

        Assert.That(carried.Fields.Single(field => field.Name == "captured").FieldType.FullName,
            Is.EqualTo("Gneedle.Inject.Test.Captured"),
            "the field of the copy does not name the type the template captured.");
    }

    [Test]
    [NonParallelizable]
    public void A_Template_Which_Is_An_Async_Body_Whose_State_Machine_Was_Carried_Is_Woven_And_Awaited()
    {
        var (result, reported) = Woven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.Awaits), CARRIED_ASYNC_TYPE);
        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));
            Assert.That(((Task<int>) Ran(result, CARRIED_ASYNC_TYPE, "Run", 5)!).GetAwaiter().GetResult(), Is.EqualTo(5),
                "the member which was woven did not hand back what the template hands back.");
        });
    }

    [Test]
    [NonParallelizable]
    public void The_Lambda_Of_A_Generic_Template_Is_Carried_And_Its_Parameter_Re_Parented()
    {
        // Tier 1 where the type the compiler wrote is generic over the type which declares the template, so the copy
        // declares a parameter of its own and every reference to the original names the copy of it. This is the move
        // which CreateProceedMethod already makes for the method it generates, one level down: a generic parameter
        // belongs to the type which declares it, so a copy which was added to another type declares its own.
        var (result, reported) = Woven(GENERIC_TEMPLATES_TYPE, "ReturnsWhatItCaptured", CARRIED_GENERIC_CLOSURE_TYPE);

        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using (var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result)))
        {
            var copy = read.MainModule.GetType(CARRIED_GENERIC_CLOSURE_TYPE)!.NestedTypes.Single();
            Assert.Multiple(() =>
            {
                Assert.That(copy.HasGenericParameters, Is.True,
                    "the copy declares no generic parameter, so nothing was re-parented.");
                Assert.That(copy.Fields.Single(field => field.Name == "value").FieldType, Is.SameAs(copy.GenericParameters.Single()),
                    "the field of the copy is not the parameter of the copy.");
            });
        }

        // The type is closed before the member is called, because the runtime refuses a late bound call of a member of a
        // type which holds generic parameters: what the type declares is the parameter which the copy re-parented onto
        // itself, so the member belongs to the type which the parameter is instantiated with rather than to the open one.
        var run = AssemblyLoader.LoadFromBytes(result).GetType(CARRIED_GENERIC_CLOSURE_TYPE)!.MakeGenericType(typeof(int)).GetMethod("Run")!;

        Assert.That(run.Invoke(null, [41]), Is.EqualTo(41),
            "the member which was woven did not compute what the template computes.");
    }
}