using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using GenericParameterAttributes = Mono.Cecil.GenericParameterAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// Signature of the method which the templates below proceed through, which a template names as the generic argument
/// of <see cref="Proceed.Method{TMethod}()"/>.
/// </summary>
public delegate int IntBinaryOp(int left, int right);

/// <summary>
/// Templates for a static target, which live top level in the test assembly so that Cecil can resolve them from disk.
/// </summary>
public static class AroundTemplates
{
    /// <summary>
    /// Double both arguments, proceed with them, and add one to what the original returned.
    /// </summary>
    public static int DoubleThenProceedThenAddOne(int left, int right) => Proceed.Method<IntBinaryOp>()(left * 2, right * 2) + 1;

    /// <summary>
    /// Proceed with the arguments which this template was given, and add one to what the original returned. The call
    /// names no signature, so nothing of the member is written out a second time.
    /// </summary>
    public static int ProceedWithItsOwnArgumentsThenAddOne(int left, int right) => Proceed.Invoke<int>() + 1;

    /// <summary>
    /// Proceed inside a region which catches what the body which was taken over throws, which is the region whose
    /// boundaries the weaving rewrites with the instructions of the template: what catches is carried with them.
    /// </summary>
    public static int ProceedInsideACatch()
    {
        try { return Proceed.Invoke<int>(); }
        catch (NotSupportedException) { return -1; }
    }

    /// <summary>
    /// Proceed with the arguments as they are.
    /// </summary>
    public static int ProceedOnly(int left, int right) => Proceed.Method<IntBinaryOp>()(left, right);

    /// <summary>
    /// Call for a type which the member does not hand back, which the weaving refuses rather than writing a call the
    /// runtime would find disagreeing with what the caller reads off the stack.
    /// </summary>
    public static int ProceedWithAnotherHandedBackType(int left, int right)
    {
        Proceed.Invoke<long>();
        return left + right;
    }

    /// <summary>
    /// Name a member which the member being woven does not hold, which the parse of the template refuses. The signature
    /// is the one of the member which it is woven around, so that what refuses it is the member which is missing rather
    /// than the signature.
    /// </summary>
    public static int ReadAMissingField(int left, int right) => This.Field<int>("Missing").Get();

    /// <summary>
    /// A template whose return type does not match the method, which the around body refuses.
    /// </summary>
    public static long ProceedWithAnotherReturnType(int left, int right) => Proceed.Method<IntBinaryOp>()(left, right);

    /// <summary>
    /// A template whose parameters do not match the method, which the around body refuses.
    /// </summary>
    public static int ProceedWithAnotherSignature(int left) => left;
}

/// <summary>
/// Templates for an instance target.<para/>
/// They are instance methods themselves, so that the <c>ldarg.0</c> which the marker leaves behind is the <c>this</c>
/// which the generated method expects.
/// </summary>
public class AroundInstanceTemplates
{
    /// <summary>
    /// Double both arguments, proceed with them, and add one to what the original returned.
    /// </summary>
    public int DoubleThenProceedThenAddOne(int left, int right) => Proceed.Method<IntBinaryOp>()(left * 2, right * 2) + 1;
}

/// <summary>
/// Signature of the four argument method which the templates below proceed through.<para/>
/// A template of four arguments and more is loaded by an operand which names the parameter rather than by one of the
/// four macro opcodes, which carry the slot in the opcode itself.
/// </summary>
public delegate int IntQuadOp(int a, int b, int c, int d);

/// <summary>
/// Templates whose argument loads reach past the four slots which a macro opcode holds.
/// </summary>
public static class WideAroundTemplates
{
    /// <summary>
    /// The four arguments as a number, which is given as the body of the member which the template below wraps.
    /// </summary>
    public static int Number(int a, int b, int c, int d) => a * 1000 + b * 100 + c * 10 + d;

    /// <summary>
    /// Proceed with the arguments reversed, so that a load which reached another argument is told apart from one which
    /// reached the right one.
    /// </summary>
    public static int ReversedThenAddOne(int a, int b, int c, int d) => Proceed.Method<IntQuadOp>()(d, c, b, a) + 1;
}

/// <summary>
/// Signature of the generic method which the templates below proceed through.<para/>
/// It names the first generic parameter of the method which is woven around through the
/// <c>Gneedle.Inject.M_0</c> token rather than declaring a generic parameter of its own, because a delegate cannot.
/// </summary>
public delegate M0 PassthroughOp(M0 value);

/// <summary>
/// Templates for a generic target.<para/>
/// The token takes the place of the generic parameter, so that the template can be compiled at all.
/// </summary>
public static class GenericAroundTemplates
{
    /// <summary>
    /// Proceed with the value as it is.
    /// </summary>
    public static M0 Passthrough(M0 value) => Proceed.Method<PassthroughOp>()(value);

    /// <summary>
    /// Proceed with the value which this template was given, where neither that argument nor the value which is handed
    /// back has a type of its own: the call names the token which the weaving turns into the parameter of the member,
    /// and the signature of the delegate which the template above needs is written nowhere.
    /// </summary>
    public static M0 PassthroughWithItsOwnArguments(M0 value) => Proceed.Invoke<M0>();
}

/// <summary>
/// A template which belongs to an instance of a type which the sources name, which reads a member of that instance
/// rather than a variable of the method it is written in.
/// </summary>
public class InstanceFieldTemplates
{
    /// <summary>
    /// A value which the instance holds, which a template of it reads off the instance rather than out of itself.
    /// </summary>
    private readonly int m_Value = 7;

    /// <summary>
    /// Read the field of the instance which this template belongs to.
    /// </summary>
    public int ReadsItsOwnField(int value) => value + m_Value;
}

/// <summary>
/// A template which is a lambda of an instance of a type which the sources name, and which reads a member of that
/// instance rather than a variable of the method it is written in.
/// </summary>
public class InstanceCaptureTemplate
{
    /// <summary>
    /// The value which the lambda below reaches off the instance which the closure holds.
    /// </summary>
    private readonly int m_Captured;

    /// <summary>
    /// Create the instance which holds what the lambda reaches.
    /// </summary>
    /// <param name="captured">The value which the instance holds.</param>
    public InstanceCaptureTemplate(int captured) => m_Captured = captured;

    /// <summary>
    /// Weave the lambda, which proceeds and adds the value of this instance, around the method.
    /// </summary>
    /// <param name="handler">The handler of the method which is woven around.</param>
    public void Weave(IMethodHandler handler) => handler.AroundBody(() => Proceed.Method<Func<int>>()() + m_Captured);
}

/// <summary>
/// Tests for the around version of <see cref="IMethodHandler.SetBody(MethodInfo)"/>, which keeps the body of the method
/// and lets the template proceed through it.
/// </summary>
[TestFixture]
public class AroundBodyTests
{
    private const string PROCEED_METHOD_NAME = "<Add>k__Proceed";

    #region Fixture

    /// <summary>
    /// Create a host which holds <c>public int Add(int left, int right)</c> whose body adds its two arguments, so that
    /// the templates above have something to proceed into.
    /// </summary>
    private static (Assembly Assembly, TypeHandler Host, MethodDefinition Add) NewHost(bool isStatic, string assemblyName = "AroundBodyTestAssembly")
    {
        var assembly = Assembly.Create(assemblyName);
        var module = assembly.Source.MainModule;
        var host = AddAHost(assembly);

        var add = new MethodDefinition("Add", MethodAttributes.Public | (isStatic ? MethodAttributes.Static : 0), module.TypeSystem.Int32);
        add.Parameters.Add(new ParameterDefinition("left", ParameterAttributes.None, module.TypeSystem.Int32));
        add.Parameters.Add(new ParameterDefinition("right", ParameterAttributes.None, module.TypeSystem.Int32));
        // A static method addresses its arguments from the slot zero, an instance one from the slot after `this`.
        add.Body.Instructions.Add(Instruction.Create(isStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1));
        add.Body.Instructions.Add(Instruction.Create(isStatic ? OpCodes.Ldarg_1 : OpCodes.Ldarg_2));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Add));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(add);

        if (isStatic) return (assembly, host, add);

        // A type which Cecil emits carries no constructor of its own, and one is needed to create an instance of it.
        AddAnInstanceConstructor(host);

        return (assembly, host, add);
    }

    /// <summary>
    /// Create a host which holds <c>public static T Identity&lt;T&gt;(T value)</c> whose body returns its argument, so
    /// that a generic method has something to proceed into.
    /// </summary>
    private static (Assembly Assembly, TypeHandler Host, MethodDefinition Identity) NewGenericHost(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        var module = assembly.Source.MainModule;
        var host = AddAHost(assembly);

        var identity = new MethodDefinition("Identity", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        var parameter = new GenericParameter("T", identity);
        identity.GenericParameters.Add(parameter);
        identity.ReturnType = parameter;
        identity.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, parameter));
        identity.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        identity.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(identity);

        return (assembly, host, identity);
    }

    /// <summary>
    /// Create a host which declares a parameter of its own and which holds <c>public int Add(int left, int right)</c>,
    /// so that the call which a template makes through <c>Proceed</c> stands on a member of a generic type.
    /// </summary>
    private static (Assembly Assembly, TypeHandler Host, MethodDefinition Add) NewGenericTypeHost(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        var module = assembly.Source.MainModule;
        var host = (TypeHandler)((AssemblyHandler)assembly.Handler).AddClass("Host", NS, ClassFlags.Public)
                                                                    .WithGenericParameter("T")
                                                                    .GetHandler();

        var add = new MethodDefinition("Add", MethodAttributes.Public, module.TypeSystem.Int32);
        add.Parameters.Add(new ParameterDefinition("left", ParameterAttributes.None, module.TypeSystem.Int32));
        add.Parameters.Add(new ParameterDefinition("right", ParameterAttributes.None, module.TypeSystem.Int32));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Add));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(add);

        AddAnInstanceConstructor(host);

        return (assembly, host, add);
    }

    private static MethodHandler HandlerOf(TypeHandler host, string name) => (MethodHandler)host.GetMethod(name)!;

    #endregion

    #region The woven method runs

    /// <summary>
    /// Weave the template around the method and run it.<para/>
    /// Each caller names its assembly, because an assembly is loaded by its identity: two images with the same name in
    /// the same load context are one assembly to the runtime, whatever their content.
    /// </summary>
    private static object? WeaveAndInvoke(string assemblyName, bool isStatic, Type templateHolder, string templateName, object[] arguments)
    {
        var (assembly, host, _) = NewHost(isStatic, assemblyName);
        HandlerOf(host, "Add").AroundBody(Template(templateHolder, templateName));

        var type = assembly.Load().GetType($"{NS}.Host")!;
        var instance = isStatic ? null : Activator.CreateInstance(type);
        return type.GetMethod("Add")!.Invoke(instance, arguments);
    }

    [Test]
    public void AroundBody_Runs_The_Original_And_Returns_What_The_Template_Chose()
    {
        // (3 * 2) + (4 * 2) + 1. The value tells every failure apart: 7 is the original run unchanged, 8 is the
        // template's arithmetic without the original, and 13 is the original run with the arguments swapped.
        var result = WeaveAndInvoke("AroundBodyStaticAssembly", true, typeof(AroundTemplates), nameof(AroundTemplates.DoubleThenProceedThenAddOne), [3, 4]);

        Assert.That(result, Is.EqualTo(15));
    }

    [Test]
    public void AroundBody_On_An_Instance_Target_Runs_The_Original()
    {
        var result = WeaveAndInvoke("AroundBodyInstanceAssembly", false, typeof(AroundInstanceTemplates), nameof(AroundInstanceTemplates.DoubleThenProceedThenAddOne), [3, 4]);

        Assert.That(result, Is.EqualTo(15));
    }

    [Test]
    public void AroundBody_Reads_Every_Argument_Of_A_Static_Template_At_The_Slot_Of_An_Instance_Member()
    {
        // The member which is wrapped holds a receiver ahead of its arguments, so every load of an argument of the
        // template is written one slot after the one which the template names, whichever form of the opcode carries it.
        var assembly = Assembly.Create("AroundBodyWideArgumentsAssembly");
        var host = AddAHost(assembly);
        var intType = typeof(int).ToGneedleType();
        host.AddMethod(".ctor", typeof(void).ToGneedleType(), [], [], MethodFlags.Public).SetBody(DefaultMethodBody.CallFromBase);

        var method = host.AddMethod("Number", intType, [],
                                    [new Parameter(intType), new Parameter(intType), new Parameter(intType), new Parameter(intType)],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(WideAroundTemplates), nameof(WideAroundTemplates.Number)));
        method.AroundBody(Template(typeof(WideAroundTemplates), nameof(WideAroundTemplates.ReversedThenAddOne)));

        var type = assembly.Load().GetType($"{NS}.Host")!;

        // 1234 reversed is 4321, and the template adds one to what it returned: 4322 rather than 3212, which is what
        // the arguments come to when the loads keep the slots of the template.
        Assert.That(type.GetMethod("Number")!.Invoke(Activator.CreateInstance(type), [1, 2, 3, 4]), Is.EqualTo(4322));
    }

    [Test]
    public void AroundBody_Of_A_Template_Which_Catches_What_The_Body_Throws_Runs_The_Catch()
    {
        // The body of the member which is added without one throws, and the region of the template which catches that
        // is written with it: -1 rather than the exception leaving the woven member, and rather than 0, which is what a
        // region without a handler would hand back by reading nothing off the stack.
        var assembly = Assembly.Create("AroundBodyCatchAssembly");
        var host = AddAHost(assembly);
        var run = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);

        run.AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedInsideACatch)));

        var type = assembly.Load().GetType($"{NS}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(null, null), Is.EqualTo(-1));
    }

    [Test]
    public void AroundBody_Of_A_Symbol_Which_Proceeds_With_Its_Own_Arguments_Runs_The_Original()
    {
        // 3 + 4 + 1. The call names no signature and no argument: the template proceeds with the arguments which it was
        // given, which are the arguments of the member which is woven.
        var result = WeaveAndInvoke("AroundBodyOwnArgumentsAssembly", true, typeof(AroundTemplates),
                                    nameof(AroundTemplates.ProceedWithItsOwnArgumentsThenAddOne), [3, 4]);

        Assert.That(result, Is.EqualTo(8));
    }

    [Test]
    public void AroundBody_Of_An_Instance_Member_Which_Proceeds_With_Its_Own_Arguments_Runs_The_Original()
    {
        // The member belongs to an instance, so its receiver is the one which the call is made on, and the arguments of
        // the template follow it.
        var result = WeaveAndInvoke("AroundBodyOwnArgumentsInstanceAssembly", false, typeof(AroundTemplates),
                                    nameof(AroundTemplates.ProceedWithItsOwnArgumentsThenAddOne), [3, 4]);

        Assert.That(result, Is.EqualTo(8));
    }

    [Test]
    public void AroundBody_Of_A_Generic_Member_Which_Proceeds_With_Its_Own_Arguments_Runs_The_Original()
    {
        // The token stands for the generic parameter of the member, so the template names neither the type of the
        // argument nor the type of the value which is handed back, and declares no delegate to name them with.
        var (assembly, host, _) = NewGenericHost("AroundBodyOwnArgumentsGenericAssembly");
        HandlerOf(host, "Identity")
            .AroundBody(Template(typeof(GenericAroundTemplates), nameof(GenericAroundTemplates.PassthroughWithItsOwnArguments)));

        var identity = assembly.Load().GetType($"{NS}.Host")!.GetMethod("Identity")!.MakeGenericMethod(typeof(string));

        Assert.That(identity.Invoke(null, ["hello"]), Is.EqualTo("hello"));
    }

    [Test]
    public void AroundBody_Of_A_Member_Of_A_Generic_Type_Proceeds_Into_The_Member()
    {
        // The call of the body which was taken over stands in a body whose type declares a parameter of its own, and a
        // call of a method of a type which stands open is one which the runtime refuses to run: the call names the
        // instantiation which the body is a member of instead.
        var (assembly, host, _) = NewGenericTypeHost("AroundBodyOfAGenericTypeAssembly");
        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.DoubleThenProceedThenAddOne)));

        var type = assembly.Load().GetType($"{NS}.Host")!.MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Add")!.Invoke(Activator.CreateInstance(type), [3, 4]), Is.EqualTo(15));
    }

    [Test]
    public void AroundBody_Of_A_Call_Which_Names_Another_Type_Than_The_Member_Hands_Back_Throws()
    {
        // What the call names is what the caller reads off the stack, and the member hands back the type which is
        // written at the member. The two are compared, so the disagreement is named at the weave rather than left to
        // the runtime to refuse the type which was written.
        var (_, host, _) = NewHost(true);

        var thrown = Assert.Throws<ArgumentException>(() => HandlerOf(host, "Add")
            .AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedWithAnotherHandedBackType))));

        Assert.That(thrown!.Message, Does.Contain("is not the type which the member being woven hands back"));
    }

    [Test]
    public void SetBody_With_A_Template_Which_Takes_The_Arguments_Of_Itself_Throws()
    {
        // The call stands for the body which was taken over, so a template which is copied rather than woven around has
        // none for it to stand for.
        var (_, host, _) = NewHost(true);

        Assert.Throws<ArgumentException>(() => HandlerOf(host, "Add")
            .SetBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedWithItsOwnArgumentsThenAddOne))));
    }

    [Test]
    public void AroundBody_Of_A_Member_Which_Hands_Nothing_Back_Proceeds_Through_The_Call_Which_Names_No_Type()
    {
        // The call is a statement rather than an expression, and the body which it proceeds into is the one which the
        // method already held: the throwing body which a method is added with, which the call reaches and runs.
        var assembly = Assembly.Create("AroundBodyOwnArgumentsVoidAssembly");
        var host = AddAHost(assembly);
        var run = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);

        run.AroundBody(() => Proceed.Invoke());

        Assert.That(((MethodHandler)run).Source.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference
                                                                                        && reference.Name == "<Run>k__Proceed"), Is.True);

        var type = assembly.Load().GetType($"{NS}.Host")!;
        var thrown = Assert.Throws<TargetInvocationException>(() => type.GetMethod("Run")!.Invoke(null, null));
        Assert.That(thrown!.InnerException, Is.InstanceOf<NotSupportedException>());
    }

    #endregion

    #region The woven method is well formed

    [Test]
    public void AroundBody_Moves_The_Original_Body_To_A_Generated_Method()
    {
        var (_, host, add) = NewHost(true);
        var originalInstructions = add.Body.Instructions.ToArray();

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        var generated = host.Source.Methods.Single(method => method.Name == PROCEED_METHOD_NAME);
        Assert.Multiple(() =>
        {
            Assert.That(generated.IsStatic, Is.True);
            Assert.That(generated.Body.Instructions, Is.EqualTo(originalInstructions));
        });
        Assert.Multiple(() =>
        {
            Assert.That(generated.Body.Instructions.Last().OpCode, Is.EqualTo(OpCodes.Ret));

            // The body which was moved is not left behind in the method which was woven around.
            Assert.That(add.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Add), Is.False);
        });
    }

    [Test]
    public void AroundBody_Gives_The_Generated_Method_The_Parameters_Of_The_Target()
    {
        // The parameters are shared rather than copied, which is what leaves every argument operand of the moved body
        // naming the argument it named. A copy would mean rewriting all of them.
        var (_, host, add) = NewHost(true);

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        var generated = host.Source.Methods.Single(method => method.Name == PROCEED_METHOD_NAME);
        Assert.Multiple(() =>
        {
            Assert.That(generated.Parameters[0], Is.SameAs(add.Parameters[0]));
            Assert.That(generated.Parameters[1], Is.SameAs(add.Parameters[1]));
        });
    }

    [Test]
    public void AroundBody_Keeps_The_Signature_Of_The_Target()
    {
        // The template keeps the signature, so the return type must not follow the template the way SetBody makes it.
        var (_, host, add) = NewHost(true);

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));
        Assert.Multiple(() =>
        {
            Assert.That(add.ReturnType.FullName, Is.EqualTo(typeof(int).FullName));
            Assert.That(add.Parameters.Count, Is.EqualTo(2));
            Assert.That(add.IsStatic, Is.True);
        });
    }

    [Test]
    public void AroundBody_Emits_A_Direct_Call_And_Leaves_No_Proceed()
    {
        var (_, host, add) = NewHost(true);

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        var instructions = add.Body.Instructions;
        var call = instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                               .FirstOrDefault(reference => reference.Name == PROCEED_METHOD_NAME);
        Assert.Multiple(() =>
        {
            Assert.That(call, Is.Not.Null);
            Assert.That(instructions.Any(instruction => instruction.OpCode == OpCodes.Callvirt
                                                     && instruction.Operand is MethodReference reference && reference.Name == "Invoke"), Is.False);
            Assert.That(instructions.Any(instruction => instruction.Operand is MethodReference reference
                                                     && reference.DeclaringType.FullName == Proceed.TYPE_NAME), Is.False);
        });
    }

    [Test]
    public void AroundBody_On_A_Virtual_Target_Leaves_The_Generated_Method_Not_Virtual()
    {
        var (_, host, add) = NewHost(true);
        add.Attributes |= MethodAttributes.Virtual;

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        var generated = host.Source.Methods.Single(method => method.Name == PROCEED_METHOD_NAME);
        Assert.Multiple(() =>
        {
            Assert.That(generated.IsVirtual, Is.False);

            // The generated method is not virtual, so the call to it is not a virtual one.
            Assert.That(add.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Call
                                                             && instruction.Operand is MethodReference reference && reference.Name == PROCEED_METHOD_NAME), Is.True);
        });
    }

    [Test]
    public void AroundBody_Survives_A_Save_And_ReRead()
    {
        // The body which is moved carries a local and an exception handler, which are the two parts of a body which a
        // move could leave behind.
        var (assembly, host, add) = NewHost(true);
        var module = assembly.Source.MainModule;

        var tryStart = Instruction.Create(OpCodes.Ldc_I4_7);
        var handlerStart = Instruction.Create(OpCodes.Pop);
        var end = Instruction.Create(OpCodes.Ret);
        add.Body.Instructions.Clear();
        add.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
        add.Body.Instructions.Add(tryStart);
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Stloc_0));
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Leave_S, end));
        add.Body.Instructions.Add(handlerStart);
        add.Body.Instructions.Add(Instruction.Create(OpCodes.Leave_S, end));
        add.Body.Instructions.Add(end);
        add.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            CatchType = module.ImportReference(typeof(Exception)),
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = end
        });

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        using var stream = new MemoryStream();
        assembly.SaveTo(stream);
        stream.Position = 0;

        var reread = AssemblyDefinition.ReadAssembly(stream);
        var emitted = reread.MainModule.GetType($"{NS}.Host")!.Methods.Single(method => method.Name == PROCEED_METHOD_NAME);
        Assert.Multiple(() =>
        {
            Assert.That(emitted.Body.Variables.Count, Is.EqualTo(1));
            Assert.That(emitted.Body.ExceptionHandlers.Count, Is.EqualTo(1));
            Assert.That(emitted.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Stloc_0), Is.True);
        });
    }

    #endregion

    #region Refusals

    [Test]
    public void AroundBody_Of_A_Template_Which_Captures_A_Variable_Writes_The_Value_Into_The_Member()
    {
        // A lambda which captures a variable is an instance method of the type which the compiler wrote to hold what it
        // captured, and it reads each of them off the instance of that type which the delegate was made from. That
        // instance belongs to the run of the injector and not to the assembly being woven, so the value is written into
        // the member instead: the woven body reaches the same value, and holds no instance which it could not.
        var assembly = Assembly.Create("AroundBodyCapturedValueAssembly");
        var host = AddAHost(assembly);
        var run = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        run.SetBody(DefaultMethodBody.WithDefaultReturn);
        var captured = 41;

        run.AroundBody(() => Proceed.Method<Func<int>>()() + captured);

        var body = ((MethodHandler)run).Source.Body;
        Assert.Multiple(() =>
        {
            // Nothing reads the instance which the delegate held, and the value which was captured stands in the body.
            Assert.That(body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False);
            Assert.That(body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldc_I4 && instruction.Operand is 41), Is.True);
        });

        // The body which was taken over hands back the default of int, which what the template captured is added to.
        var type = assembly.Load().GetType($"{NS}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(null, null), Is.EqualTo(41));
    }

    [Test]
    public void AroundBody_Of_A_Template_Which_Captures_A_Member_Of_Its_Instance_Writes_The_Value_Into_The_Member()
    {
        // A lambda of an instance of a type which the sources name captures that instance rather than one of the
        // variables of the method it is written in, so the closure holds the instance and what the template reads is a
        // member of it, which is read off the instance the delegate holds.
        var assembly = Assembly.Create("AroundBodyInstanceCaptureAssembly");
        var host = AddAHost(assembly);
        var run = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        run.SetBody(DefaultMethodBody.WithDefaultReturn);

        new InstanceCaptureTemplate(7).Weave(run);

        var body = ((MethodHandler)run).Source.Body;
        Assert.Multiple(() =>
        {
            Assert.That(body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False);
            Assert.That(body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldc_I4 && instruction.Operand is 7), Is.True);
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(null, null), Is.EqualTo(7));
    }

    [Test]
    public void AroundBody_Of_A_Template_Which_Captures_A_Value_Which_Cannot_Be_Written_Throws()
    {
        // What a template captured is written into the member as a value of its own, which only a string, a number, a
        // character, a boolean, an enumeration or a null of a reference type has a form for. A capture of any other
        // type is named rather than woven into a member which the runtime would refuse.
        var assembly = Assembly.Create("AroundBodyUnwritableCaptureAssembly");
        var host = AddAHost(assembly);
        var run = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        var captured = new object();

        var thrown = Assert.Throws<ArgumentException>(() => run.AroundBody(() =>
        {
            Proceed.Method<Action>()();
            Console.WriteLine(captured);
        }));

        Assert.That(thrown!.Message, Does.Contain("cannot be written"));
        Assert.That(thrown!.Message, Does.Contain("captured"));
    }

    [Test]
    public void AroundBody_Of_A_Template_Given_As_A_Method_Which_Reads_Its_Instance_Throws()
    {
        // A template which is given as the method alone is given no instance, so what it reads off the instance it
        // belongs to is held by nothing, and there is no value of it to write into the member being woven.
        var assembly = Assembly.Create("AroundBodyInstanceFieldAssembly");
        var host = AddAHost(assembly);
        var intType = typeof(int).ToGneedleType();
        var one = host.AddMethod("One", intType, [], [new Parameter(intType)], MethodFlags.Public | MethodFlags.Static);
        one.SetBody(DefaultMethodBody.WithDefaultReturn);

        var thrown = Assert.Throws<ArgumentException>(
            () => one.AroundBody(typeof(InstanceFieldTemplates).GetMethod(nameof(InstanceFieldTemplates.ReadsItsOwnField))!));

        Assert.That(thrown!.Message, Does.Contain("instance which it belongs to"));
    }

    [Test]
    public void AroundBody_Of_An_Abstract_Method_Throws()
    {
        var (_, host, add) = NewHost(true);
        add.Attributes |= MethodAttributes.Abstract;

        Assert.Throws<ArgumentException>(() => HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly))));
    }

    [Test]
    public void AroundBody_Of_A_Constructor_Throws()
        => Assert.Throws<ArgumentException>(() =>
           {
               var (_, host, _) = NewHost(false);
               HandlerOf(host, ".ctor").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));
           });

    [Test]
    public void AroundBody_Of_A_Generic_Method_With_A_Template_Of_Plain_Types_Throws()
    {
        // A template of plain types cannot match a method whose parameters are generic, which is what the token is for.
        var (_, host, _) = NewGenericHost("AroundBodyGenericRefusalAssembly");

        Assert.Throws<ArgumentException>(() => HandlerOf(host, "Identity").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly))));
    }

    [Test]
    public void AroundBody_With_A_Template_Of_Another_Signature_Throws()
        => Assert.Throws<ArgumentException>(() =>
           {
               var (_, host, _) = NewHost(true);
               HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedWithAnotherSignature)));
           });

    [Test]
    public void AroundBody_With_A_Template_Of_Another_Return_Type_Throws()
        => Assert.Throws<ArgumentException>(() =>
           {
               var (_, host, _) = NewHost(true);
               HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedWithAnotherReturnType)));
           });

    [Test]
    public void AroundBody_Twice_Throws()
    {
        var (_, host, _) = NewHost(true);
        var method = HandlerOf(host, "Add");
        method.AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        Assert.Throws<ArgumentException>(() => method.AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly))));
    }

    [Test]
    public void AroundBody_With_An_Occupied_Generated_Name_Throws()
    {
        var (_, host, add) = NewHost(true);
        host.Source.Fields.Add(new FieldDefinition(PROCEED_METHOD_NAME, FieldAttributes.Private, add.ReturnType));

        Assert.Throws<ArgumentException>(() => HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly))));
    }

    [Test]
    public void AroundBody_Of_A_Template_Which_Cannot_Be_Woven_Leaves_The_Member_As_It_Was()
    {
        // The body of the member is taken over in the course of being woven around, and the template is parsed after
        // it: a template which cannot be parsed is the case which tells whether the member survives the attempt. It
        // holds the body it held, the declaring type declares no method of its own, and the weave which is done next
        // is done as the first one rather than refused for one which never happened.
        var (assembly, host, add) = NewHost(true);
        var body = add.Body;
        var instructions = body.Instructions.ToArray();

        Assert.Throws<ArgumentException>(() => HandlerOf(host, "Add")
           .AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ReadAMissingField))));

        // The member holds the very body object it held, with the very instructions in it, and neither a variable nor
        // a handler of the template was written into it: what the parse wrote went to a body which was discarded.
        Assert.That(add.Body, Is.SameAs(body));
        Assert.Multiple(() =>
        {
            Assert.That(add.Body.Instructions, Is.EqualTo(instructions));
            Assert.That(add.Body.Variables, Is.Empty);
            Assert.That(add.Body.ExceptionHandlers, Is.Empty);
            Assert.That(host.Source.Methods.Any(method => method.Name == PROCEED_METHOD_NAME), Is.False);
        });

        // A second attempt which fails leaves as little behind as the first one did: nothing of the member is carried
        // from one attempt into the next, and nothing of the template either.
        Assert.Throws<ArgumentException>(() => HandlerOf(host, "Add")
           .AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ReadAMissingField))));

        Assert.That(add.Body, Is.SameAs(body));
        Assert.Multiple(() =>
        {
            Assert.That(add.Body.Instructions, Is.EqualTo(instructions));
            Assert.That(add.Body.Variables, Is.Empty);
            Assert.That(add.Body.ExceptionHandlers, Is.Empty);
            Assert.That(host.Source.Methods.Any(method => method.Name == PROCEED_METHOD_NAME), Is.False);
        });

        // The weave which is done next is done as the first one rather than refused for one which never happened, and
        // the body which it takes over is the one which the attempts above left alone: what the member held before
        // them is what the generated method holds after it, in the image as it is written rather than in memory only.
        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        var generated = host.Source.Methods.Single(method => method.Name == PROCEED_METHOD_NAME);
        Assert.That(generated.Body.Instructions, Is.EqualTo(instructions));

        using var stream = new MemoryStream();
        assembly.SaveTo(stream);
        stream.Position = 0;

        var reread = AssemblyDefinition.ReadAssembly(stream);
        var emitted = reread.MainModule.GetType($"{NS}.Host")!.Methods.Single(method => method.Name == PROCEED_METHOD_NAME);
        Assert.That(emitted.Body.Instructions.Select(instruction => instruction.OpCode),
                    Is.EqualTo(new[] { OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Add, OpCodes.Ret }));
    }

    [Test]
    public void SetBody_With_A_Template_Which_Proceeds_Throws()
    {
        // Proceed without a body being woven around is a mistake of its own, and it has a message of its own.
        var (_, host, _) = NewHost(true);

        Assert.Throws<ArgumentException>(() => HandlerOf(host, "Add").SetBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly))));
    }

    #endregion

    #region The generic method

    [Test]
    public void AroundBody_Of_A_Generic_Method_Mirrors_The_Generic_Parameters()
    {
        // The M_0 token of the template is resolved to the generic parameter of the method, so the generated method has
        // to declare that parameter for the resolved one to name it, and for the operands of the moved body, which are
        // emitted by the position they were compiled with, to keep pointing at it.
        var (_, host, identity) = NewGenericHost("AroundBodyGenericAssembly");

        HandlerOf(host, "Identity").AroundBody(Template(typeof(GenericAroundTemplates), nameof(GenericAroundTemplates.Passthrough)));

        var generated = host.Source.Methods.Single(method => method.Name == "<Identity>k__Proceed");
        Assert.That(generated.GenericParameters.Count, Is.EqualTo(1));

        // The parameter is copied rather than shared, because a generic parameter belongs to the single method which
        // declares it: the copy is the parameter of the same name and the same position, which is what a reference to a
        // generic parameter names.
        var copy = generated.GenericParameters[0];
        Assert.That(copy, Is.Not.SameAs(identity.GenericParameters[0]));
        Assert.Multiple(() =>
        {
            Assert.That(copy.Name, Is.EqualTo(identity.GenericParameters[0].Name));
            Assert.That(copy.Position, Is.EqualTo(identity.GenericParameters[0].Position));
            Assert.That(copy.Owner, Is.SameAs(generated));

            // The method which was woven around keeps its own signature.
            Assert.That(identity.GenericParameters.Count, Is.EqualTo(1));
        });
        Assert.Multiple(() =>
        {
            Assert.That(identity.GenericParameters[0].Owner, Is.SameAs(identity));
            Assert.That(identity.ReturnType.Name, Is.EqualTo("T"));
        });
    }

    [Test]
    public void AroundBody_Of_A_Generic_Method_Keeps_The_Constraints_Of_The_Generic_Parameters()
    {
        var (_, host, identity) = NewGenericHost("AroundBodyGenericConstraintAssembly");
        identity.GenericParameters[0].Attributes = GenericParameterAttributes.ReferenceTypeConstraint;
        identity.GenericParameters[0].Constraints.Add(new GenericParameterConstraint(identity.Module.ImportReference(typeof(IComparable<>))));

        HandlerOf(host, "Identity").AroundBody(Template(typeof(GenericAroundTemplates), nameof(GenericAroundTemplates.Passthrough)));

        var copy = host.Source.Methods.Single(method => method.Name == "<Identity>k__Proceed").GenericParameters[0];
        Assert.Multiple(() =>
        {
            Assert.That(copy.Attributes, Is.EqualTo(GenericParameterAttributes.ReferenceTypeConstraint));
            Assert.That(copy.Constraints.Count, Is.EqualTo(1));
        });
        Assert.That(copy.Constraints[0].ConstraintType.FullName, Is.EqualTo(typeof(IComparable<>).FullName));
    }

    [Test]
    public void AroundBody_Of_A_Generic_Method_Runs_The_Original()
    {
        // The token resolved to the generic parameter of the method, so the template proceeds into the original with
        // whatever the method was closed over at the call.
        var (assembly, host, _) = NewGenericHost("AroundBodyGenericExecutionAssembly");
        HandlerOf(host, "Identity").AroundBody(Template(typeof(GenericAroundTemplates), nameof(GenericAroundTemplates.Passthrough)));

        var identity = assembly.Load().GetType($"{NS}.Host")!.GetMethod("Identity")!.MakeGenericMethod(typeof(string));

        Assert.That(identity.Invoke(null, ["hello"]), Is.EqualTo("hello"));
    }

    #endregion

    #region A method which the decorator added

    [Test]
    public void MethodDecorator_Around_Of_A_Method_It_Added_Proceeds_Into_The_Body_Which_Was_Added()
    {
        var assembly = Assembly.Create("AroundBodyDecoratorAssembly");
        var host = AddAHost(assembly);

        // The decorator describes the body of the method alone, of which one part is the body it holds. A body is woven
        // around through the handler, which is what holds one.
        var method = (MethodHandler)host.AddMethod("Add", MethodFlags.Public | MethodFlags.Static)
                                     .WithParameter("left", typeof(int))
                                     .WithParameter("right", typeof(int))
                                     .WithReturnType(typeof(int))
                                     .GetHandler();
        method.AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        // A method which is added carries a body which throws, which is the body the around template proceeds into.
        var generated = host.Source.Methods.Single(methodDef => methodDef.Name == PROCEED_METHOD_NAME);
        Assert.Multiple(() =>
        {
            Assert.That(generated.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj), Is.True);

            // The method itself now calls the generated one rather than holding the throwing body.
            Assert.That(method.Source.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == PROCEED_METHOD_NAME), Is.True);
        });
    }

    #endregion
}