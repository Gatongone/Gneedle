using Mono.Cecil;
using MethodInfo = System.Reflection.MethodInfo;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

/// <summary>
/// A template of five arguments which belongs to an instance of its own type, so that its first argument is the slot
/// after its receiver, and which never reads that receiver.
/// </summary>
public class WideBodyTemplates
{
    /// <summary>
    /// The five arguments as a number.
    /// </summary>
    public int Number(int a, int b, int c, int d, int e) => a * 10000 + b * 1000 + c * 100 + d * 10 + e;
}

/// <summary>
/// Templates which hold the constructs which the weaving carries or refuses, so that what the readme says of them is
/// what the weaving does.
/// </summary>
public static class ConstructTemplates
{
    /// <summary>
    /// Catch what the body throws, and hand back 42 where the catch ran.
    /// </summary>
    public static int Catch(int value)
    {
        try { throw new InvalidOperationException("thrown by the template"); }
        catch (InvalidOperationException) { return 42; }
    }

    /// <summary>
    /// Add to a value in a finally, and hand back 12 where the finally ran where it was written and 2 where it did not.
    /// </summary>
    public static int Finally(int value)
    {
        var result = value;
        try { result += 1; }
        finally { result += 10; }
        return result;
    }

    /// <summary>
    /// Dispose in a using, and hand back 42 where the dispose ran.
    /// </summary>
    public static int Using(int value)
    {
        using (new DisposableThing()) { }
        return DisposableThing.Disposed ? 42 : 0;
    }

    /// <summary>
    /// Take a lock, and hand back 42 where the lock was released.
    /// </summary>
    public static int Lock(int value)
    {
        var gate = new object();
        lock (gate) { value += 1; }
        return Monitor.IsEntered(gate) ? 0 : 42;
    }

    /// <summary>
    /// Enumerate a disposable enumerator, and hand back 43 where the dispose ran and the value came through.
    /// </summary>
    public static int Foreach(int value)
    {
        var total = 0;
        foreach (var item in new DisposableElements(value)) { total += item; }
        return DisposableElements.Disposed ? total + 42 : 0;
    }

    /// <summary>
    /// A lambda written inside the template, which is a method of a type the compiler wrote for it.
    /// </summary>
    public static int Lambda(int value)
    {
        Func<int, int> add = x => x + 1;
        return add(value);
    }

    /// <summary>
    /// A lambda which captures a local of the template, which the compiler holds in a type of its own.
    /// </summary>
    public static int LambdaWhichCaptured(int value)
    {
        var offset = 1;
        Func<int, int> add = x => x + offset;
        return add(value);
    }

    /// <summary>
    /// A local function written inside the template, which the compiler writes as a method of the type which holds the
    /// template itself when it captures nothing, so that no type of the compiler's own is named by it.
    /// </summary>
    public static int LocalFunction(int value)
    {
        return Twice(value);

        static int Twice(int number) => number * 2;
    }

    /// <summary>
    /// A body which the compiler writes as a state machine of its own.
    /// </summary>
    public static async Task<int> Async(int value)
    {
        await Task.Yield();
        return value + 1;
    }
}

/// <summary>
/// A disposable which remembers whether it was disposed.
/// </summary>
public sealed class DisposableThing : IDisposable
{
    /// <summary>Whether Dispose ran.</summary>
    public static bool Disposed;

    /// <inheritdoc/>
    public void Dispose() => Disposed = true;
}

/// <summary>
/// An enumeration whose enumerator is disposable, so that the foreach which reads it is written with a try.
/// </summary>
public sealed class DisposableElements(int value) : IEnumerable<int>
{
    /// <summary>Whether the enumerator was disposed.</summary>
    public static bool Disposed;

    /// <inheritdoc/>
    public IEnumerator<int> GetEnumerator() => new Enumerator(value);

    /// <inheritdoc/>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator(int value) : IEnumerator<int>
    {
        /// <summary>Whether the single value of the enumeration was read.</summary>
        private bool m_Read;

        /// <inheritdoc/>
        public int Current { get; private set; }

        /// <inheritdoc/>
        object System.Collections.IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (m_Read) return false;

            m_Read  = true;
            Current = value;
            return true;
        }

        /// <inheritdoc/>
        public void Reset() => m_Read = false;

        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }
}

/// <summary>
/// Tests for <see cref="IMethodHandler.SetBody"/>: the body which is copied out of a member, and the default bodies
/// which are written from the kind of body which is asked for.
/// </summary>
[TestFixture]
public class SetBodyTests
{
    private const string Ns = "Gneedle.Test.Generated";

    // Template method bodies copied by the injector. Kept in the test assembly so
    // Cecil can resolve them from disk via the default assembly resolver.
    public static class BodyTemplates
    {
        public static int Add(int a, int b) => a + b;
        public static int Echo(int x) => x;

        // Five parameters, so the fifth is addressed by ldarg.s with a parameter as its operand rather than by one of the
        // macro opcodes, which carry no operand at all.
        public static int Sum(int a, int b, int c, int d, int e) => a + b + c + d + e;

        // Six parameters, and each of them at its own place in a number, so that a load which reached another argument
        // is told apart from one which reached the right one. The six loads cover every form of the opcode: the four
        // macro opcodes of the first four slots, and the operand form for the two which they do not reach.
        public static int Number(int a, int b, int c, int d, int e, int f) => a * 100000 + b * 10000 + c * 1000 + d * 100 + e * 10 + f;
    }

    /// <summary>
    /// Create the assembly which the tests of the copied body build, and hand back the handler of the assembly and of
    /// the type which was added to it.
    /// </summary>
    private static (AssemblyHandler handler, TypeHandler host) NewCalc()
    {
        var handler = (AssemblyHandler) Assembly.Create("SetBodyTestAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Calc", Ns, ClassFlags.Public).GetHandler();
        return (handler, host);
    }

    /// <summary>
    /// Create the assembly which the tests of the default bodies build, and hand back the handler of the type which was
    /// added to it.
    /// </summary>
    private static TypeHandler NewHost()
    {
        var handler = (AssemblyHandler) Assembly.Create("DefaultBodyAssembly").Handler;
        return (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
    }

    private static MethodDefinition SourceOf(IMethodHandler method) => ((MethodHandler) method).Source;

    #region The body which is copied from a member

    [Test]
    public void SetBody_Copies_Instructions_From_Template()
    {
        var (_, host) = NewCalc();
        var method = host.AddMethod(
            "Add",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);

        method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Add))!);

        var body = SourceOf(method).Body;
        Assert.That(body.Instructions, Is.Not.Empty);
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Add), Is.True);
        Assert.That(body.Instructions.Last().OpCode, Is.EqualTo(OpCodes.Ret));
    }

    [Test]
    public void SetBody_Produces_Structurally_Valid_Assembly()
    {
        var assembly = Assembly.Create("SetBodyValidAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass("Calc", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod(
            "Add",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Add))!);

        // The image is read back rather than run, because what this test holds is the shape of what was emitted rather
        // than what it does: the member which the body was written for is one which a reader of the image finds, and the
        // body which it holds is the one which was copied.
        using var stream = new MemoryStream();
        assembly.SaveTo(stream);
        stream.Position = 0;

        var reread = AssemblyDefinition.ReadAssembly(stream);
        var type = reread.MainModule.GetType($"{Ns}.Calc");
        Assert.That(type, Is.Not.Null);
        var emitted = type.Methods.First(m => m.Name == "Add");
        Assert.That(emitted.Body.Instructions.Any(i => i.OpCode == OpCodes.Add), Is.True);
    }

    [Test]
    public void SetBody_Maps_A_Parameter_Addressed_By_Operand_To_The_Parameter_Of_The_Same_Position()
    {
        // The parameters of the template and the parameters of the method are two different sets, and the name of one
        // is not the name of the other. A parameter which the body addresses by operand is therefore matched by the
        // position it holds, which is the only thing the two sets share. A body which is copied by name alone leaves
        // the operand null where the names differ, which Cecil rejects while the instruction is built.
        var assembly = Assembly.Create("SetBodyParameterOperandAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Calc", Ns, ClassFlags.Public).GetHandler();
        var intType = typeof(int).ToGneedleType();
        var method = host.AddMethod("Sum", intType, [], [new Parameter(intType), new Parameter(intType), new Parameter(intType),
                                                         new Parameter(intType), new Parameter(intType)],
                                    MethodFlags.Public | MethodFlags.Static);

        method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Sum))!);

        // 15 rather than 1, 5 or 10 tells the arguments apart from a body which reached the wrong parameters.
        var sum = assembly.Load().GetType($"{Ns}.Calc")!.GetMethod("Sum")!;
        Assert.That(sum.Invoke(null, [1, 2, 3, 4, 5]), Is.EqualTo(15));
    }

    [Test]
    public void SetBody_With_More_Parameters_Than_The_Method_Holds_Throws()
    {
        var (_, host) = NewCalc();
        var method = host.AddMethod("Echo", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public | MethodFlags.Static);

        var thrown = Assert.Throws<ArgumentException>(() => method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Sum))!));

        Assert.That(thrown!.Message, Does.Contain("position"));
    }

    [Test]
    public void SetBody_Remaps_Ldarg_For_Static_Template_Into_Instance_Method()
    {
        var (_, host) = NewCalc();
        // Instance method receiving a static template: the template's ldarg.0 (its first
        // parameter) must be remapped to ldarg.1 because arg0 of the instance method is `this`.
        var method = host.AddMethod(
            "Echo",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Echo))!);

        var body = SourceOf(method).Body;
        Assert.That(SourceOf(method).IsStatic, Is.False);
        // The single load must target ldarg.1 (the real parameter), not ldarg.0 (this).
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_1), Is.True);
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_0), Is.False);
    }

    [Test]
    public void SetBody_Moves_Every_Argument_Of_A_Static_Template_Past_The_Receiver_Of_An_Instance_Method()
    {
        // The first argument of the template is not the only one which moves: the member being woven holds a receiver
        // ahead of all of them, so every load of an argument is written one slot after the one which the template
        // names, whichever form of the opcode carries the slot.
        var assembly = Assembly.Create("SetBodyShiftedArgumentsAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Calc", Ns, ClassFlags.Public).GetHandler();
        var intType = typeof(int).ToGneedleType();
        host.AddMethod(".ctor", typeof(void).ToGneedleType(), [], [], MethodFlags.Public).SetBody(DefaultMethodBody.CallFromBase);

        var method = host.AddMethod("Number", intType, [],
                                    [new Parameter(intType), new Parameter(intType), new Parameter(intType),
                                     new Parameter(intType), new Parameter(intType), new Parameter(intType)],
                                    MethodFlags.Public);

        method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Number))!);

        // Nothing loads the receiver, because the template names no such argument.
        Assert.That(SourceOf(method).Body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_0), Is.False);

        // 123456 rather than 112345 tells a body which read every argument of the member from one which kept the slots
        // of the template, where the load of the second argument reaches into the first.
        var type = assembly.Load().GetType($"{Ns}.Calc")!;
        var number = type.GetMethod("Number")!;
        Assert.That(number.Invoke(Activator.CreateInstance(type), [1, 2, 3, 4, 5, 6]), Is.EqualTo(123456));
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Is_An_Instance_Reads_Its_Arguments_Around_Its_Receiver()
    {
        // A template which belongs to an instance holds its receiver ahead of its arguments, so its first argument is
        // the second slot, and the last of five is the sixth. The member which the body is woven into reads every one
        // of them at the slot which it holds there, which is the slot of the template shifted by the receivers of the
        // two: none of them moves where both belong to an instance.
        var assembly = Assembly.Create("SetBodyInstanceTemplateAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Calc", Ns, ClassFlags.Public).GetHandler();
        var intType = typeof(int).ToGneedleType();
        host.AddMethod(".ctor", typeof(void).ToGneedleType(), [], [], MethodFlags.Public).SetBody(DefaultMethodBody.CallFromBase);

        var method = host.AddMethod("Number", intType, [],
                                    [new Parameter(intType), new Parameter(intType), new Parameter(intType),
                                     new Parameter(intType), new Parameter(intType)],
                                    MethodFlags.Public);

        method.SetBody(typeof(WideBodyTemplates).GetMethod(nameof(WideBodyTemplates.Number))!);

        // 12345 rather than 12344 tells a body which read the last argument from one which read it at the position
        // which it holds among the parameters, where it reaches the argument before it. A template written as a lambda
        // which captures a variable cannot be used here, because reading the capture reads the receiver, which the
        // weaving refuses.
        var type = assembly.Load().GetType($"{Ns}.Calc")!;
        var number = type.GetMethod("Number")!;
        Assert.That(number.Invoke(Activator.CreateInstance(type), [1, 2, 3, 4, 5]), Is.EqualTo(12345));
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Catches_Runs_The_Catch()
    {
        // The instructions of a template are carried with the regions which protect them, so what a template catches is
        // caught where it was woven rather than let out of the member.
        var probe = NewProbe(nameof(ConstructTemplates.Catch));

        Assert.That(probe.Invoke(null, [1]), Is.EqualTo(42));
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Adds_In_A_Finally_Hands_Back_What_The_Finally_Made()
    {
        var probe = NewProbe(nameof(ConstructTemplates.Finally));

        Assert.That(probe.Invoke(null, [1]), Is.EqualTo(12));
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Disposes_In_A_Using_Disposes()
    {
        ConstructTemplatesReset();
        var probe = NewProbe(nameof(ConstructTemplates.Using));

        Assert.That(probe.Invoke(null, [1]), Is.EqualTo(42));
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Takes_A_Lock_Releases_It()
    {
        var probe = NewProbe(nameof(ConstructTemplates.Lock));

        Assert.That(probe.Invoke(null, [1]), Is.EqualTo(42));
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Enumerates_A_Disposable_Enumerator_Disposes_It()
    {
        ConstructTemplatesReset();
        var probe = NewProbe(nameof(ConstructTemplates.Foreach));

        Assert.That(probe.Invoke(null, [1]), Is.EqualTo(43));
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Holds_A_Lambda_Throws()
    {
        // A lambda is a method of a type which the compiler writes beside the template, and which is private to the
        // assembly the template was compiled into: the weaving refuses it rather than writing a member which fails
        // when it is run.
        var (_, host) = NewCalc();
        var method = host.AddMethod("Probe", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public | MethodFlags.Static);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(typeof(ConstructTemplates).GetMethod(nameof(ConstructTemplates.Lambda))!));

        Assert.That(thrown!.Message, Does.Contain("the weaving cannot carry it"));
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Holds_A_Lambda_Which_Captured_Throws()
    {
        var (_, host) = NewCalc();
        var method = host.AddMethod("Probe", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public | MethodFlags.Static);

        Assert.Throws<ArgumentException>(
            () => method.SetBody(typeof(ConstructTemplates).GetMethod(nameof(ConstructTemplates.LambdaWhichCaptured))!));
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Holds_A_Local_Function_Throws()
    {
        // A local function which captures nothing is written as a method of the type which holds the template, so the
        // member which names it is a member of a type which the template's own assembly declares rather than one which
        // the compiler wrote: it is refused by the name of the member rather than by the name of the type which holds it.
        var (_, host) = NewCalc();
        var method = host.AddMethod("Probe", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public | MethodFlags.Static);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(typeof(ConstructTemplates).GetMethod(nameof(ConstructTemplates.LocalFunction))!));

        Assert.That(thrown!.Message, Does.Contain("the weaving cannot carry it"));
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Is_Async_Throws()
    {
        // The body of an async method is the stub which starts a state machine, whose MoveNext holds what was written,
        // so what the weaving would carry is the stub rather than the body which the template was written with.
        var (_, host) = NewCalc();
        var method = host.AddMethod("Probe", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public | MethodFlags.Static);

        Assert.Throws<ArgumentException>(
            () => method.SetBody(typeof(ConstructTemplates).GetMethod(nameof(ConstructTemplates.Async))!));
    }

    /// <summary>
    /// Weave the named template into <c>public static int Probe(int value)</c> of an assembly of its own, and hand back
    /// the method of the type which was woven, so that what the template holds can be run.
    /// </summary>
    /// <param name="templateName">Name of the template of <see cref="ConstructTemplates"/> which is woven.</param>
    /// <returns>The method which was woven.</returns>
    private static MethodInfo NewProbe(string templateName)
    {
        var assembly = Assembly.Create($"SetBody{templateName}Assembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Calc", Ns, ClassFlags.Public).GetHandler();
        var intType = typeof(int).ToGneedleType();
        var method = host.AddMethod("Probe", intType, [], [new Parameter(intType)], MethodFlags.Public | MethodFlags.Static);

        method.SetBody(typeof(ConstructTemplates).GetMethod(templateName)!);

        return assembly.Load().GetType($"{Ns}.Calc")!.GetMethod("Probe")!;
    }

    /// <summary>
    /// Forget what the constructs of the templates remember, so that a test of one of them reads only what it did.
    /// </summary>
    private static void ConstructTemplatesReset()
    {
        DisposableThing.Disposed = false;
        DisposableElements.Disposed = false;
    }

    [Test]
    public void SetBody_Of_A_Template_Which_Cannot_Be_Woven_Leaves_The_Body_As_It_Was()
    {
        // A method which is added carries a body of its own before it is given one, and the template is parsed into the
        // body which takes its place: the parse is the step which refuses a template, so what the member holds when a
        // template is refused is what tells whether the refusal left the member as it was.
        var (_, host) = NewCalc();
        var method = host.AddMethod("Probe", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public | MethodFlags.Static);
        var source = SourceOf(method);
        var body = source.Body;
        var instructions = body.Instructions.ToArray();

        Assert.Throws<ArgumentException>(() => method.SetBody(typeof(ConstructTemplates).GetMethod(nameof(ConstructTemplates.Lambda))!));

        // The body is the one the member held rather than a new one which the template wrote what it could into, and
        // nothing of the template is in it: no variable and no handler of it is left where the body was replaced.
        Assert.That(source.Body, Is.SameAs(body));
        Assert.That(source.Body.Instructions, Is.EqualTo(instructions));
        Assert.That(source.Body.Variables, Is.Empty);
        Assert.That(source.Body.ExceptionHandlers, Is.Empty);
    }

    #endregion

    #region ThrowException

    [Test]
    public void ThrowException_Emits_Newobj_And_Throw()
    {
        var host = NewHost();
        var method = host.AddMethod("Do", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.ThrowException);

        var ins = SourceOf(method).Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Throw), Is.True);
    }

    #endregion

    #region WithDefaultReturn

    [Test]
    public void WithDefaultReturn_ReferenceType_Emits_Ldnull()
    {
        var host = NewHost();
        var method = host.AddMethod("Get", typeof(string).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.WithDefaultReturn);

        var ins = SourceOf(method).Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldnull), Is.True);
        Assert.That(ins.Last().OpCode, Is.EqualTo(OpCodes.Ret));
    }

    [Test]
    public void WithDefaultReturn_ValueType_Emits_Initobj()
    {
        var host = NewHost();
        var method = host.AddMethod("Get", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.WithDefaultReturn);

        var ins = SourceOf(method).Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Initobj), Is.True);
        Assert.That(ins.Last().OpCode, Is.EqualTo(OpCodes.Ret));
    }

    [Test]
    public void WithDefaultReturn_Void_Emits_Just_Ret()
    {
        var host = NewHost();
        var method = host.AddMethod("Do", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.WithDefaultReturn);

        var ins = SourceOf(method).Body.Instructions.ToArray();
        Assert.That(ins.Count, Is.EqualTo(1));
        Assert.That(ins[0].OpCode, Is.EqualTo(OpCodes.Ret));
    }

    #endregion

    #region CallFromBase

    [Test]
    public void CallFromBase_Calls_Base_Method()
    {
        var asm = Assembly.Create("DefaultBodyBaseAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var mod = asm.Source.MainModule;

        // base class with virtual Method
        var baseDef = new TypeDefinition(Ns, "BaseType", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        var baseMethod = new MethodDefinition("Method", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
        baseMethod.Body.GetILProcessor().Emit(OpCodes.Ret);
        baseDef.Methods.Add(baseMethod);
        mod.Types.Add(baseDef);

        // derived host extends base
        var host = (TypeHandler) handler.AddClass("Derived", Ns, ClassFlags.Public).GetHandler();
        host.Source.BaseType = baseDef;

        var method = host.AddMethod("Method", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.CallFromBase);

        var ins = SourceOf(method).Body.Instructions.ToArray();
        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && i.Operand is MethodReference mr && mr.Name == "Method"), Is.True);
        Assert.That(ins.Last().OpCode, Is.EqualTo(OpCodes.Ret));
    }

    [Test]
    public void CallFromBase_Of_An_Instance_Loads_The_Receiver_And_The_Arguments()
    {
        // The receiver of an instance is an argument of the call to the base member as much as the parameters are, and
        // no parameter of the member holds it, so a body which loads the parameters alone hands the base member the
        // first of them where it expects the instance and leaves it one short at the end.
        var assembly = Assembly.Create("CallFromBaseInstanceAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Calc", Ns, ClassFlags.Public).GetHandler();

        // The constructor is a call from base of its own, which is the first thing which an instance of the type runs.
        host.AddMethod(".ctor", typeof(void).ToGneedleType(), [], [], MethodFlags.Public).SetBody(DefaultMethodBody.CallFromBase);
        host.AddMethod("Equals", typeof(bool).ToGneedleType(), [], [new Parameter(typeof(object).ToGneedleType())], MethodFlags.Public)
            .SetBody(DefaultMethodBody.CallFromBase);

        var type = assembly.Load().GetType($"{Ns}.Calc")!;
        var instance = Activator.CreateInstance(type);
        var equals = type.GetMethod("Equals", [typeof(object)])!;

        // The base implementation answers whether the reference is the one it was given, which it can only answer
        // about the instance the call was made on.
        Assert.That(equals.Invoke(instance, [instance]), Is.True);
        Assert.That(equals.Invoke(instance, [new object()]), Is.False);
    }

    [Test]
    public void CallFromBase_Without_Base_Method_Throws()
    {
        var host = NewHost();
        var method = host.AddMethod("Method", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);

        // No base type with Method -> should throw
        Assert.Throws<ArgumentException>(() => method.SetBody(DefaultMethodBody.CallFromBase));
    }

    #endregion

    #region The capture which a delegate holds

    /// <summary>
    /// A handler of a method which is not the one which this library builds, which holds nothing to write what a
    /// template captured into.
    /// </summary>
    private sealed class ForeignMethodHandler : IMethodHandler
    {
        /// <summary>
        /// The bodies which were described on it as the template alone, which stay empty where a delegate is refused
        /// instead of being read for the method which it holds.
        /// </summary>
        internal List<MethodInfo> Bodies { get; } = [];

        /// <inheritdoc cref="Bodies"/>
        internal List<MethodInfo> Arounds { get; } = [];

        /// <inheritdoc/>
        public MethodFlags Flags => MethodFlags.Public;

        /// <inheritdoc/>
        public string Name => "Foreign";

        /// <inheritdoc/>
        public ITypeHandler DeclaringTypeHandler => throw new NotSupportedException("A handler of a test declares no type.");

        /// <inheritdoc/>
        public void SetBody(MethodInfo method) => Bodies.Add(method);

        /// <inheritdoc/>
        public void SetBody(DefaultMethodBody defaultMethodBody) { }

        /// <inheritdoc/>
        public void AroundBody(MethodInfo method) => Arounds.Add(method);

        /// <inheritdoc/>
        public bool ContainsAttribute(IType attributeType) => false;

        /// <inheritdoc/>
        public void AddAttribute(IType attributeType, params object[] arguments) { }
    }

    [Test]
    public void A_Template_Which_Captured_A_Variable_Is_Refused_By_A_Handler_Of_Another_Implementation()
    {
        // What the template captured is held by the delegate rather than by the method, so a handler which this library
        // does not build holds nothing to write it into: the value would be lost rather than woven, which is refused
        // where the delegate is given rather than passed over.
        var handler = new ForeignMethodHandler();
        var captured = 41;

        var body = Assert.Throws<ArgumentException>(() => handler.SetBody(() => captured));
        var around = Assert.Throws<ArgumentException>(() => handler.AroundBody(() => captured));

        Assert.Multiple(() =>
        {
            Assert.That(body!.Message, Does.Contain(nameof(ForeignMethodHandler)), "the message does not name the handler which was given.");
            Assert.That(around!.Message, Does.Contain(nameof(ForeignMethodHandler)), "the message does not name the handler which was given.");
            Assert.That(handler.Bodies, Is.Empty, "the body was described from the template alone, which loses what it captured.");
            Assert.That(handler.Arounds, Is.Empty, "the around body was described from the template alone, which loses what it captured.");
        });
    }

    #endregion
}