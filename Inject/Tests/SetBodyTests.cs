using Mono.Cecil;
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

        // The runtime loader can't load this net5.0-targeted image here, but Cecil
        // re-reading the emitted bytes proves the produced image is well-formed.
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
        Assert.Catch<ArgumentException>(() => method.SetBody(DefaultMethodBody.CallFromBase));
    }

    #endregion
}
