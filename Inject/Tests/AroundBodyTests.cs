using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using GenericParameterAttributes = Mono.Cecil.GenericParameterAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace Gneedle.Inject.Test;

/// <summary>
/// Signature of the method which the templates below proceed through, which a template names as the generic argument
/// of <see cref="Proceed.Method{TMethod}(string)"/>.
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
    public static int DoubleThenProceedThenAddOne(int left, int right) => Proceed.Method<IntBinaryOp>("Add")(left * 2, right * 2) + 1;

    /// <summary>
    /// Proceed with the arguments as they are.
    /// </summary>
    public static int ProceedOnly(int left, int right) => Proceed.Method<IntBinaryOp>("Add")(left, right);

    /// <summary>
    /// A template whose return type does not match the method, which the around body refuses.
    /// </summary>
    public static long ProceedWithAnotherReturnType(int left, int right) => Proceed.Method<IntBinaryOp>("Add")(left, right);

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
    public int DoubleThenProceedThenAddOne(int left, int right) => Proceed.Method<IntBinaryOp>("Add")(left * 2, right * 2) + 1;
}

/// <summary>
/// Signature of the generic method which the templates below proceed through.<para/>
/// It names the first generic parameter of the method which is woven around through the
/// <c>Gneedle.Inject.M_0</c> token rather than declaring a generic parameter of its own, because a delegate cannot.
/// </summary>
public delegate M_0 PassthroughOp(M_0 value);

/// <summary>
/// Templates for a generic target.<para/>
/// The token takes the place of the generic parameter, so that the template can be compiled at all.
/// </summary>
public static class GenericAroundTemplates
{
    /// <summary>
    /// Proceed with the value as it is.
    /// </summary>
    public static M_0 Passthrough(M_0 value) => Proceed.Method<PassthroughOp>(nameof(Passthrough))(value);
}

/// <summary>
/// Tests for the around version of <see cref="IMethodHandler.SetBody(MethodInfo)"/>, which keeps the body of the method
/// and lets the template proceed through it.
/// </summary>
[TestFixture]
public class AroundBodyTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private const string ProceedMethodName = "<Add>k__Proceed";

    // region Fixture

    /// <summary>
    /// Create a host which holds <c>public int Add(int left, int right)</c> whose body adds its two arguments, so that
    /// the templates above have something to proceed into.
    /// </summary>
    private static (Assembly Assembly, TypeHandler Host, MethodDefinition Add) NewHost(bool isStatic, string assemblyName = "AroundBodyTestAssembly")
    {
        var assembly = Assembly.Create(assemblyName);
        var module = assembly.Source.MainModule;
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();

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
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

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
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();

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

    private static MethodHandler HandlerOf(TypeHandler host, string name) => (MethodHandler) host.GetMethod(name)!;

    private static MethodInfo Template(Type holder, string name) => holder.GetMethod(name)!;

    // endregion

    // region The woven method runs

    /// <summary>
    /// Weave the template around the method and run it.<para/>
    /// Each caller names its assembly, because an assembly is loaded by its identity: two images with the same name in
    /// the same load context are one assembly to the runtime, whatever their content.
    /// </summary>
    private static object? WeaveAndInvoke(string assemblyName, bool isStatic, Type templateHolder, string templateName, object[] arguments)
    {
        var (assembly, host, _) = NewHost(isStatic, assemblyName);
        HandlerOf(host, "Add").AroundBody(Template(templateHolder, templateName));

        var type = assembly.Load().GetType($"{Ns}.Host")!;
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

    // endregion

    // region The woven method is well formed

    [Test]
    public void AroundBody_Moves_The_Original_Body_To_A_Generated_Method()
    {
        var (_, host, add) = NewHost(true);
        var originalInstructions = add.Body.Instructions.ToArray();

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        var generated = host.Source.Methods.Single(method => method.Name == ProceedMethodName);
        Assert.That(generated.IsStatic, Is.True);
        Assert.That(generated.Body.Instructions, Is.EqualTo(originalInstructions));
        Assert.That(generated.Body.Instructions.Last().OpCode, Is.EqualTo(OpCodes.Ret));

        // The body which was moved is not left behind in the method which was woven around.
        Assert.That(add.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Add), Is.False);
    }

    [Test]
    public void AroundBody_Gives_The_Generated_Method_The_Parameters_Of_The_Target()
    {
        // The parameters are shared rather than copied, which is what leaves every argument operand of the moved body
        // naming the argument it named. A copy would mean rewriting all of them.
        var (_, host, add) = NewHost(true);

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        var generated = host.Source.Methods.Single(method => method.Name == ProceedMethodName);
        Assert.That(generated.Parameters[0], Is.SameAs(add.Parameters[0]));
        Assert.That(generated.Parameters[1], Is.SameAs(add.Parameters[1]));
    }

    [Test]
    public void AroundBody_Keeps_The_Signature_Of_The_Target()
    {
        // The template keeps the signature, so the return type must not follow the template the way SetBody makes it.
        var (_, host, add) = NewHost(true);

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        Assert.That(add.ReturnType.FullName, Is.EqualTo(typeof(int).FullName));
        Assert.That(add.Parameters.Count, Is.EqualTo(2));
        Assert.That(add.IsStatic, Is.True);
    }

    [Test]
    public void AroundBody_Emits_A_Direct_Call_And_Leaves_No_Proceed()
    {
        var (_, host, add) = NewHost(true);

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        var instructions = add.Body.Instructions;
        var call = instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                               .FirstOrDefault(reference => reference.Name == ProceedMethodName);

        Assert.That(call, Is.Not.Null);
        Assert.That(instructions.Any(instruction => instruction.OpCode == OpCodes.Callvirt
                                                 && instruction.Operand is MethodReference reference && reference.Name == "Invoke"), Is.False);
        Assert.That(instructions.Any(instruction => instruction.Operand is MethodReference reference
                                                 && reference.DeclaringType.FullName == Proceed.TYPE_NAME), Is.False);
    }

    [Test]
    public void AroundBody_On_A_Virtual_Target_Leaves_The_Generated_Method_Not_Virtual()
    {
        var (_, host, add) = NewHost(true);
        add.Attributes |= MethodAttributes.Virtual;

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        var generated = host.Source.Methods.Single(method => method.Name == ProceedMethodName);
        Assert.That(generated.IsVirtual, Is.False);

        // The generated method is not virtual, so the call to it is not a virtual one.
        Assert.That(add.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Call
                                                         && instruction.Operand is MethodReference reference && reference.Name == ProceedMethodName), Is.True);
    }

    [Test]
    public void AroundBody_Survives_A_Save_And_ReRead()
    {
        // The body which is moved carries a local and an exception handler, which are the two parts of a body which a
        // move could leave behind.
        var (assembly, host, add) = NewHost(true);
        var module = assembly.Source.MainModule;

        var tryStart     = Instruction.Create(OpCodes.Ldc_I4_7);
        var handlerStart = Instruction.Create(OpCodes.Pop);
        var end          = Instruction.Create(OpCodes.Ret);
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
            CatchType    = module.ImportReference(typeof(Exception)),
            TryStart     = tryStart,
            TryEnd       = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd   = end
        });

        HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));

        using var stream = new MemoryStream();
        assembly.SaveTo(stream);
        stream.Position = 0;

        var reread = AssemblyDefinition.ReadAssembly(stream);
        var emitted = reread.MainModule.GetType($"{Ns}.Host")!.Methods.Single(method => method.Name == ProceedMethodName);

        Assert.That(emitted.Body.Variables.Count, Is.EqualTo(1));
        Assert.That(emitted.Body.ExceptionHandlers.Count, Is.EqualTo(1));
        Assert.That(emitted.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Stloc_0), Is.True);
    }

    // endregion

    // region Refusals

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
        host.Source.Fields.Add(new FieldDefinition(ProceedMethodName, FieldAttributes.Private, add.ReturnType));

        Assert.Throws<ArgumentException>(() => HandlerOf(host, "Add").AroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly))));
    }

    [Test]
    public void SetBody_With_A_Template_Which_Proceeds_Throws()
    {
        // Proceed without a body being woven around is a mistake of its own, and it has a message of its own.
        var (_, host, _) = NewHost(true);

        Assert.Throws<ArgumentException>(() => HandlerOf(host, "Add").SetBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly))));
    }

    // endregion

    // region The generic method

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
        Assert.That(copy.Name, Is.EqualTo(identity.GenericParameters[0].Name));
        Assert.That(copy.Position, Is.EqualTo(identity.GenericParameters[0].Position));
        Assert.That(copy.Owner, Is.SameAs(generated));

        // The method which was woven around keeps its own signature.
        Assert.That(identity.GenericParameters.Count, Is.EqualTo(1));
        Assert.That(identity.GenericParameters[0].Owner, Is.SameAs(identity));
        Assert.That(identity.ReturnType.Name, Is.EqualTo("T"));
    }

    [Test]
    public void AroundBody_Of_A_Generic_Method_Keeps_The_Constraints_Of_The_Generic_Parameters()
    {
        var (_, host, identity) = NewGenericHost("AroundBodyGenericConstraintAssembly");
        identity.GenericParameters[0].Attributes = GenericParameterAttributes.ReferenceTypeConstraint;
        identity.GenericParameters[0].Constraints.Add(new GenericParameterConstraint(identity.Module.ImportReference(typeof(IComparable<>))));

        HandlerOf(host, "Identity").AroundBody(Template(typeof(GenericAroundTemplates), nameof(GenericAroundTemplates.Passthrough)));

        var copy = host.Source.Methods.Single(method => method.Name == "<Identity>k__Proceed").GenericParameters[0];
        Assert.That(copy.Attributes, Is.EqualTo(GenericParameterAttributes.ReferenceTypeConstraint));
        Assert.That(copy.Constraints.Count, Is.EqualTo(1));
        Assert.That(copy.Constraints[0].ConstraintType.FullName, Is.EqualTo(typeof(IComparable<>).FullName));
    }

    [Test]
    public void AroundBody_Of_A_Generic_Method_Runs_The_Original()
    {
        // The token resolved to the generic parameter of the method, so the template proceeds into the original with
        // whatever the method was closed over at the call.
        var (assembly, host, _) = NewGenericHost("AroundBodyGenericExecutionAssembly");
        HandlerOf(host, "Identity").AroundBody(Template(typeof(GenericAroundTemplates), nameof(GenericAroundTemplates.Passthrough)));

        var identity = assembly.Load().GetType($"{Ns}.Host")!.GetMethod("Identity")!.MakeGenericMethod(typeof(string));

        Assert.That(identity.Invoke(null, ["hello"]), Is.EqualTo("hello"));
    }

    // endregion

    // region The decorator

    [Test]
    public void MethodDecorator_WithAroundBody_Weaves_Around_The_Body_Which_Was_Added()
    {
        var assembly = Assembly.Create("AroundBodyDecoratorAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var method = (MethodHandler) host.AddMethod("Add", MethodFlags.Public | MethodFlags.Static)
                                     .WithParameter("left", typeof(int))
                                     .WithParameter("right", typeof(int))
                                     .WithReturnType(typeof(int))
                                     .WithAroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)))
                                     .GetHandler();

        // A method which is added carries a body which throws, which is the body the around template proceeds into.
        var generated = host.Source.Methods.Single(methodDef => methodDef.Name == ProceedMethodName);
        Assert.That(generated.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj), Is.True);

        // The method itself now calls the generated one rather than holding the throwing body.
        Assert.That(method.Source.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == ProceedMethodName), Is.True);
    }

    [Test]
    public void MethodDecorator_WithBody_Then_WithAroundBody_Weaves_Around_The_Body_Which_Was_Described()
    {
        var assembly = Assembly.Create("AroundBodyDecoratorBothAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        // Describing a body and asking for one to be woven around it are two statements rather than one chain, because
        // every member of the body stage returns the end of the chain.
        var decorator = host.AddMethod("Add", MethodFlags.Public | MethodFlags.Static)
                            .WithParameter("left", typeof(int))
                            .WithParameter("right", typeof(int))
                            .WithReturnType(typeof(int));
        decorator.WithBody(DefaultMethodBody.WithDefaultReturn);
        decorator.WithAroundBody(Template(typeof(AroundTemplates), nameof(AroundTemplates.ProceedOnly)));
        var method = (MethodHandler) decorator.GetHandler();

        // The body which was described moved to the generated method, so the around body wraps the default return rather
        // than the throwing body which the method was added with.
        var generated = host.Source.Methods.Single(methodDef => methodDef.Name == ProceedMethodName);
        Assert.That(generated.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Initobj), Is.True);
        Assert.That(generated.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj), Is.False);

        // The method holds the template alone, so the body it was described with is not left behind in it as well.
        Assert.That(method.Source.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Initobj), Is.False);
    }

    // endregion
}
