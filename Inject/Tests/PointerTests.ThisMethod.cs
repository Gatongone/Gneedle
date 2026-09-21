using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Assembly = Gneedle.Inject.Assembly;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using GenericParameterAttributes = Mono.Cecil.GenericParameterAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Gneedle.Inject.Test;

using static Gneedle.Inject.Test.TestFixtures;

/// <summary>
/// Tests for the method of the type which the template is woven into, which are the tests of <see cref="PointerTests"/> for that one placeholder.
/// </summary>
public partial class PointerTests
{
    /// <summary>
    /// Create a host which declares a real instance method <c>int Add(int, int)</c>, so that a template which reaches a
    /// method of it has one to be rewritten to.
    /// </summary>
    /// <param name="isVirtual">Whether the member which is added is one which a type of its own may override.</param>
    /// <param name="assemblyName">The name of the assembly to build, which a test which runs its host gives one of its own.</param>
    private static TypeHandler NewHostWithAdd(bool isVirtual, string assemblyName = "MethodInjectionAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        var attrs = MethodAttributes.Public | MethodAttributes.HideBySig
                    | (isVirtual ? MethodAttributes.Virtual | MethodAttributes.NewSlot : 0);
        var add = new MethodDefinition("Add", attrs, module.TypeSystem.Int32) { DeclaringType = host.Source };
        add.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, module.TypeSystem.Int32));
        add.Parameters.Add(new ParameterDefinition("b", ParameterAttributes.None, module.TypeSystem.Int32));
        var il = add.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(add);
        return host;
    }

    /// <summary>
    /// Create a host which declares a real instance method <c>T Echo(T)</c>, where T is a type which IL loads with one
    /// of the <c>ldc.i4</c> instructions.
    /// </summary>
    private static TypeHandler NewHostWithEcho(Type echoType)
    {
        var handler = (AssemblyHandler) Assembly.Create("MethodInjectionEchoAssembly").Handler;
        var host = AddAHost(handler);
        var t = host.Source.Module.ImportReference(host.AssemblyHandler.GetCecilType(echoType).Reference);
        var echo = new MethodDefinition("Echo", MethodAttributes.Public | MethodAttributes.HideBySig, t) { DeclaringType = host.Source };
        echo.Parameters.Add(new ParameterDefinition("c", ParameterAttributes.None, t));
        echo.Body.GetILProcessor().Emit(OpCodes.Ret);
        host.Source.Methods.Add(echo);
        return host;
    }

    /// <summary>
    /// Create a host which declares a real instance method <c>int Echo(int)</c>, which hands a value back, and one
    /// <c>void Silent(int)</c>, which hands none, so that a template which reaches a member of either kind through a
    /// delegate of the other kind has one to be refused for.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAnEchoAndASilence(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        AddAMember("Echo", module.TypeSystem.Int32);
        AddAMember("Silent", module.TypeSystem.Void);
        return host;

        void AddAMember(string name, TypeReference returnType)
        {
            var member = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.HideBySig, returnType) { DeclaringType = host.Source };
            member.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
            var il = member.Body.GetILProcessor();
            if (returnType.MetadataType != MetadataType.Void) il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
            host.Source.Methods.Add(member);
        }
    }

    /// <summary>
    /// Create a host which declares a real instance method <c>StringComparison Kind(int)</c>, whose value the stack
    /// carries as the value under an enumeration of the integer family rather than as a type of its own.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAValueOfAnEnumeration(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        var kind = new MethodDefinition("Kind", MethodAttributes.Public | MethodAttributes.HideBySig, module.ImportReference(typeof(StringComparison)))
        {
            DeclaringType = host.Source,
        };
        kind.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
        var il = kind.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(kind);
        return host;
    }

    [Test]
    public void ThisMethod_Of_A_Name_Which_Is_A_Constant_Is_Read_Like_A_Literal()
    {
        // A constant of the template is what the compiler writes where the call is, so a name which is one is the name
        // which is read out of the instruction ahead of the call.
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeByNameWhichIsAConstant)));

        Assert.That(((MethodHandler) method).Source.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference
                                                                                        && reference.Name == "Add"), Is.True);
    }

    [Test]
    public void ThisMethod_Of_A_Name_Which_Is_Not_Written_Throws()
    {
        // The name is read out of the instruction ahead of the call, so a name which the template computes is one which
        // nothing holds: the weaving used to leave the call as it was written, and the member which was woven reached
        // the placeholder and threw when it ran. It is refused where the weaving runs instead, by name.
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [],
            MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeByNameWhichIsComputed))));

        Assert.That(thrown!.Message, Does.Contain("is not written where the call is"));
    }

    [Test]
    public void InvokeInstanceMethod_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeInstanceMethod)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        // The delegate Invoke must be rewritten to a direct call to Add, and no delegate
        // construction (ldftn/newobj) should remain.
        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Add"), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);
    }

    /// <summary>
    /// Create a host which declares a real instance method <c>bool TryHalf(int, out int)</c>, which hands back half of
    /// the value it is given, and a real instance method <c>void BumpByRef(ref int)</c>, so that a template which hands
    /// an argument of its own to one of them by address has a member to be rewritten to.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAnArgumentTakenByAddress(string assemblyName = "MethodInjectionByRefAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        var tryHalf = new MethodDefinition("TryHalf", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Boolean)
        {
            DeclaringType = host.Source,
        };
        tryHalf.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
        tryHalf.Parameters.Add(new ParameterDefinition("half", ParameterAttributes.Out, new ByReferenceType(module.TypeSystem.Int32)));
        var il = tryHalf.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stind_I4); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(tryHalf);

        var bump = new MethodDefinition("BumpByRef", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void)
        {
            DeclaringType = host.Source,
        };
        bump.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, new ByReferenceType(module.TypeSystem.Int32)));
        var bumpIl = bump.Body.GetILProcessor();
        bumpIl.Emit(OpCodes.Ldarg_1); bumpIl.Emit(OpCodes.Ldarg_1); bumpIl.Emit(OpCodes.Ldind_I4); bumpIl.Emit(OpCodes.Ldc_I4_1);
        bumpIl.Emit(OpCodes.Add); bumpIl.Emit(OpCodes.Stind_I4); bumpIl.Emit(OpCodes.Ret);
        host.Source.Methods.Add(bump);
        return host;
    }

    /// <summary>
    /// Create a host which declares the instance method <c>U Touch&lt;U&gt;(int value)</c>, which declares a parameter
    /// of its own: a template which names that member through a delegate of its own carries no token of the parameter,
    /// because the delegate has no parameter of the member to write one for.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAMemberWhichDeclaresAParameterOfItsOwn(string assemblyName = "MethodInjectionUnnamedParameterAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        host.AddMethod(
            "Touch",
            typeof(M_0).ToGneedleType(),
            [new GenericParameterType("U")],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);

        return host;
    }

    /// <summary>
    /// Create a host which declares the method <c>int Touch&lt;U&gt;(int value)</c>, which declares a parameter of its own
    /// that stands nowhere in its signature: no argument of a call names it, and neither does the value which the method
    /// hands back, so no delegate describes the method.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAMemberWhoseParameterStandsNowhere(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        host.AddMethod(
            "Touch",
            typeof(int).ToGneedleType(),
            [new GenericParameterType("U")],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);

        return host;
    }

    [Test]
    public void InvokeWithAnArgumentWhichIsHandedByAddress_Rewrites_To_Direct_Call()
    {
        // A template which hands an argument of its own to a member by `ref` or `out` writes the address of the local
        // which holds it rather than the value itself, and the walk of the stack modelled neither of the instructions
        // which take an address: the call of the delegate popped as many arguments as the delegate declares where the
        // walk had left the stack holding fewer, and the weaving threw out of the walk rather than rewriting the call.
        var host = NewHostWithAnArgumentTakenByAddress();
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithAnOutArgument)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "TryHalf"), Is.True,
                    "the delegate was not rewritten to a direct call to TryHalf.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);
        Assert.That(ins.First(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "TryHalf").Operand,
                    Is.Not.InstanceOf<GenericInstanceMethod>(),
                    "the call stands on a method specification rather than on the member which it names, and the specification of a member which declares no parameter of its own names no argument.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [9]), Is.EqualTo(4));
    }

    [Test]
    public void InvokeWithTheAddressOfAnArgumentOfItsOwn_Rewrites_To_Direct_Call()
    {
        // The address which the template hands over is of an argument of its own here rather than of a local it holds,
        // which is another of the two instructions which take an address and another operand to read the type off. The
        // member is reached through `This`, so it is called on the instance which the member being woven belongs to,
        // and the argument of the template stands one slot higher in that member than it does in the template.
        var host = NewHostWithAnArgumentTakenByAddress("MethodInjectionByRefArgumentAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithARefArgument)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "BumpByRef"), Is.True,
                    "the delegate was not rewritten to a direct call to BumpByRef.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);
        Assert.That(ReceiverOf(ins, "BumpByRef", arguments: 1).OpCode, Is.EqualTo(OpCodes.Ldarg_0),
                    "the member is not called on the instance which the member being woven belongs to.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(42));
    }

    [Test]
    public void An_Instance_Member_Which_A_Static_Member_Reaches_Through_This_Is_Refused()
    {
        // The receiver of a member which a template reaches through `This` is the instance which the member being woven
        // belongs to, and a member which is static belongs to none: the first argument of it stands where that receiver
        // would be loaded from, which the template was handed for something else, so the call would be written on an
        // argument rather than on an instance. The instance which a static member reaches a member of is one it was
        // handed, which is what `Instance` names, so the weave is refused rather than written.
        var host = NewHostWithAnArgumentTakenByAddress("MethodInjectionStaticThisAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithARefArgument))));

        Assert.That(thrown!.Message, Does.Contain("is static and belongs to none"));
    }

    /// <summary>
    /// Create a host which declares the static method <c>T IdentityOfTheLater&lt;T&gt;(T value)</c>, which hands back the
    /// value it was given: the parameter of the member is declared with the parameter of the member itself, and a body
    /// which declares a parameter of that name at another position than it stands at is what the name of the two ties
    /// together, because the types of the parameters of a delegate are compared by name.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAMemberNamedByTheLaterParameter(string assemblyName = "MethodInjectionNamedParameterAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        var identity = new MethodDefinition("IdentityOfTheLater", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void)
        {
            DeclaringType = host.Source,
        };
        var own = new GenericParameter("TRes", identity);
        identity.GenericParameters.Add(own);
        identity.ReturnType = own;
        identity.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, own));
        var il = identity.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(identity);
        return host;
    }

    [Test]
    public void A_Member_Whose_Parameter_A_Later_Parameter_Of_The_Body_Names_Is_Called_With_That_Parameter()
    {
        // The member which the symbol names declares a parameter of its own, and the token of the delegate stands for a
        // parameter of the body which bears the name of that parameter and stands at another position of the body than
        // it stands at of the member: the member is looked up by the types of the parameters of the delegate, which are
        // compared by name, so the argument of the instantiation is the parameter of the body which the name ties it to
        // rather than the one which stands at the position of the parameter of the member, which is of another type.
        var host = NewHostWithAMemberNamedByTheLaterParameter();
        var method = (MethodHandler) host.AddMethod(
            "Run",
            typeof(M_1).ToGneedleType(),
            [new GenericParameterType("TKey"), new GenericParameterType("TRes")],
            [new Parameter(typeof(M_0).ToGneedleType()), new Parameter(typeof(M_1).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhichTheNameOfAParameterNames)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                         .FirstOrDefault(reference => reference.Name == "IdentityOfTheLater");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.That(call, Is.InstanceOf<GenericInstanceMethod>(),
                    "the call stands on the definition of the member rather than on an instantiation of it.");
        Assert.That(((GenericInstanceMethod) call!).GenericArguments.Single(), Is.SameAs(method.Source.GenericParameters[1]),
                    "the call does not name the parameter of the body which the name of the parameter of the member ties it to.");

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;

        Assert.That(type.GetMethod("Run")!.MakeGenericMethod(typeof(int), typeof(long)).Invoke(null, [1, 2L]), Is.EqualTo(2L),
                    "the woven assembly does not hand back the value of the parameter which the member is named by.");
    }

    /// <summary>
    /// Create a host which declares a parameter of its own and holds <c>T Identity&lt;T&gt;(T value)</c>, whose own
    /// parameter bears the name of the parameter of the type: a template which names that member through a delegate
    /// whose parameter is the token of the parameter of the type names the parameter of the member with it.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAMemberWhoseParameterTheTypeNames(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).WithGenericParameter("T").GetHandler();
        var module = host.Source.Module;

        var identity = new MethodDefinition("Identity", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Object)
        {
            DeclaringType = host.Source,
        };
        var own = new GenericParameter("T", identity);
        identity.GenericParameters.Add(own);
        identity.ReturnType = own;
        identity.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, own));
        var il = identity.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(identity);
        return host;
    }

    [Test]
    public void A_Member_Whose_Parameter_The_Type_Names_Is_Called_With_The_Parameter_Of_The_Type()
    {
        // The member which the symbol names declares a parameter of its own which the signature of the member names, and
        // the token of the delegate stands for the parameter of the type which the body is a member of, which bears the
        // name of that parameter: the body declares no parameter of its own, and the argument of the instantiation is
        // the parameter of the type, which the body names, rather than the parameter which would stand at the position
        // of the parameter of the member.
        var host = NewHostWithAMemberWhoseParameterTheTypeNames("ShadowedParameterAssembly");
        var method = (MethodHandler) host.AddMethod(
            "Run",
            typeof(T_0).ToGneedleType(),
            [],
            [new Parameter(typeof(T_0).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhoseParameterTheTypeNames)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                         .FirstOrDefault(reference => reference.Name == "Identity");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.That(call, Is.InstanceOf<GenericInstanceMethod>(),
                    "the call stands on the definition of the member rather than on an instantiation of it.");
        Assert.That(((GenericInstanceMethod) call!).GenericArguments.Single(), Is.SameAs(host.Source.GenericParameters[0]),
                    "the call does not name the parameter of the type which named the parameter of the member, which is "
                    + "the one the parameter of the member bears the name of rather than the one the member declares.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41),
                    "the woven assembly does not hand back the value which the member was called with.");
    }

    [Test]
    public void A_Member_Which_Declares_A_Parameter_The_Template_Names_None_Of_Is_Refused()
    {
        // The member which the template names declares a parameter of its own which no token of the template stands for,
        // and neither the body which is woven nor the type which declares it holds a parameter which the name of that
        // parameter ties it to, nor one which stands at its position: the call would stand on the definition of the
        // member with the parameter of it left open, which is a body the runtime refuses to run rather than one which
        // names the member, and the weave is refused instead.
        var host = NewHostWithAMemberWhoseParameterStandsNowhere("MethodInjectionUnnamedParameterAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhichDeclaresAParameterOfItsOwn))));

        Assert.That(thrown!.Message, Does.Contain("declares a generic parameter of its own which no parameter of the member being woven stands for"));
    }

    [Test]
    public void A_Member_Which_Declares_A_Parameter_Of_Its_Own_Which_The_Value_It_Hands_Back_Names_Is_Called_With_It()
    {
        // The member which the template names declares a parameter of its own which stands in no argument of the call,
        // and the value which the member hands back is what names it: the delegate hands back the type of the value which
        // the member hands back, so the member is instantiated with the type of its own argument and that value at once.
        var host = NewHostWithAMemberWhichDeclaresAParameterOfItsOwn("MethodInjectionParameterNamedByTheValueAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhichDeclaresAParameterOfItsOwn)));

        Assert.That(InstantiationOfTheCall(method, "Touch"), Is.EqualTo(typeof(int).FullName),
                    "the member was not instantiated with the type which the delegate describes the value it hands back with.");
    }

    /// <summary>
    /// Create a host which declares a real static method <c>long Widen(long)</c>, which is <c>value + 1</c>, so that a
    /// template which hands an argument of another type to it has one to be rewritten to and a body which runs.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which runs its host gives one of its own.</param>
    private static TypeHandler NewHostWithWiden(string assemblyName = "MethodInjectionConversionAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        var widen = new MethodDefinition("Widen", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Int64)
        {
            DeclaringType = host.Source,
        };
        widen.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int64));
        var il = widen.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(widen);
        return host;
    }

    [Test]
    public void InvokeWithAConvertedArgument_Rewrites_To_Direct_Call()
    {
        // The argument is computed as an int and the member takes a long, so the call is written with the conversion of
        // it: the walk of the stack used to leave the value which was converted where the conversion had left another,
        // so the argument was compared as the type it was before the call and the call was left as a call of the
        // delegate, which reaches the placeholder rather than the member when the woven body runs.
        var host = NewHostWithWiden();
        var method = host.AddMethod(
            "Run",
            typeof(long).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithAConvertedArgument)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "Widen"), Is.True,
                    "the delegate was not rewritten to a direct call to Widen.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(null, [3]), Is.EqualTo(7L));
    }

    [Test]
    public void ThisMethod_Of_A_Static_Member_Which_Is_Handed_Back_Is_Built_With_A_Null_Target()
    {
        // The delegate is built rather than called, and the constructor of a delegate takes the pointer of the member
        // together with the instance which it is called on, which a member of no instance has none of: the body was
        // written with the pointer alone, which is a stack the constructor cannot be called with at all.
        var host = NewHostWithWiden("MethodInjectionStaticDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(ThisMethodTemplates.LongOp).ToGneedleType(),
            [],
            [],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.GetStaticMethodDelegate)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var pointer = Array.FindIndex(ins, instruction => instruction.OpCode == OpCodes.Ldftn);
        Assert.That(pointer, Is.GreaterThanOrEqualTo(0), "the delegate was not built out of the pointer of the member.");
        Assert.That(pointer > 0 && ins[pointer - 1].OpCode == OpCodes.Ldnull, Is.True,
                    "the constructor of the delegate was not given the target which a member of no instance takes.");

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        var widen = (ThisMethodTemplates.LongOp) type.GetMethod("Run")!.Invoke(null, null)!;

        Assert.That(widen(3), Is.EqualTo(4L));
    }

    [Test]
    public void ThisMethod_Of_A_Generic_Delegate_Which_Is_Handed_Back_Is_Built_Of_The_Type_It_Names()
    {
        // The delegate which is handed back is one of the framework's generic ones, which the template names as an
        // instantiation: the constructor was taken from the definition which that one is an instantiation of, and the
        // open type of a generic delegate is a type which no assembly declares, so the woven body could not be loaded.
        var host = NewHostWithWiden("MethodInjectionGenericDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(Func<long, long>).ToGneedleType(),
            [],
            [],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.GetGenericMethodDelegate)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var construction = ins.FirstOrDefault(i => i.OpCode == OpCodes.Newobj);
        Assert.That(construction, Is.Not.Null, "the delegate was not built at all.");
        Assert.That(((MethodReference) construction!.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                    "the constructor of the delegate was written on the definition rather than on the type which was named.");

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        var widen = (Func<long, long>) type.GetMethod("Run")!.Invoke(null, null)!;

        Assert.That(widen(3), Is.EqualTo(4L));
    }

    [Test]
    public void InvokeAHeldDelegate_Rewrites_To_Direct_Call()
    {
        // The symbol stands where the delegate is stored into a local rather than where it is invoked, and what invokes
        // it is a read of that local: the fold used to drop the call and the name alone, which left the second read
        // invoking a delegate which nothing had built and the call which stood in the place of the first one reading
        // the receiver of the member rather than the arguments it was written with.
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegate)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Count(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                   && ((MethodReference) i.Operand).Name == "Add"), Is.EqualTo(2),
                    "the reads of the local were not both rewritten to a direct call to Add.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        AddAnInstanceConstructor(host);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(64));
    }

    [Test]
    public void A_Held_Delegate_Which_Is_Invoked_With_The_Value_Of_Its_Own_Invocation_Rewrites_To_Direct_Calls()
    {
        // The invocation of the inner read stands among the arguments of the outer one, and the arguments of the outer
        // invocation match the ones which the inner invocation is made with: the inner instruction was answered for the
        // outer read as well, and the second answer wrote over the work of the first.
        var host = NewHostWithAdd(isVirtual: false, "MethodInjectionNestedHeldDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegateWithTheValueOfItsOwnInvocation)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Count(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                   && ((MethodReference) i.Operand).Name == "Add"), Is.EqualTo(2),
                    "the reads of the local were not both rewritten to a direct call to Add.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference { Name: "Invoke" }), Is.False,
                    "an invocation of the delegate was left standing rather than folded.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        AddAnInstanceConstructor(host);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(63));
    }

    [Test]
    public void A_Symbol_Which_Stands_Within_The_Arguments_Of_Another_Rewrites_To_Direct_Calls()
    {
        // Neither symbol is stored anywhere, so each of them is invoked where it stands and the inner invocation is the
        // first call of the delegate's type after the outer symbol: the outer symbol was answered for it as well, and
        // the invocation of the inner symbol was written twice.
        var host = NewHostWithAdd(isVirtual: false, "MethodInjectionNestedSymbolAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeASymbolWithTheValueOfAnother)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Count(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                   && ((MethodReference) i.Operand).Name == "Add"), Is.EqualTo(2),
                    "the symbols were not both rewritten to a direct call to Add.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference { Name: "Invoke" }), Is.False,
                    "an invocation of the delegate was left standing rather than folded.");

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        AddAnInstanceConstructor(host);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(63));
    }

    [Test]
    public void A_Held_Delegate_Which_Is_Handed_On_As_Well_Is_Built_Rather_Than_Folded()
    {
        // The local holds the delegate for a call of another member as well, so its reads are not the invocations of the
        // delegate alone: the delegate is built where the symbol stands and every read stands where it stood. The
        // invocation was answered for the read which hands the delegate on as well, which wrote the one instruction
        // twice, and the second read was left invoking a delegate which nothing had built.
        var host = NewHostWithAdd(isVirtual: false, "MethodInjectionHandedOnDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        Assert.DoesNotThrow(() => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegateWhichWasHandedOn))),
                            "the template which hands the delegate it holds on was refused rather than woven.");
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.True,
                    "the delegate was not built into the local which holds it.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference { Name: "Invoke" }), Is.EqualTo(1),
                    "the invocation was folded into a call of the member rather than left standing on the delegate of the local.");

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        AddAnInstanceConstructor(host);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(42));
    }

    [Test]
    public void A_Held_Delegate_Which_Is_Stored_Through_A_Value_It_Was_Handed_To_Is_Built_Rather_Than_Folded()
    {
        // The store of a field of an instance takes two values, the value it writes and the value it is read off, and the
        // walk counted the one it took away as the one which stood under the arguments of the invocation: the read which
        // handed the delegate over was answered for the invocation as well, which wrote the hand-over with the receiver of
        // the member rather than with the delegate which the local holds.
        ThisMethodTemplates.Held.Slot = null;
        ThisMethodTemplates.Held.Number = 0;
        var host = NewHostWithAdd(isVirtual: false, "MethodInjectionStoredOnItsWayDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegateWhichIsStoredThroughAValueItWasHandedTo)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.True,
                    "the delegate was not built into the local which holds it.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference { Name: "Invoke" }), Is.EqualTo(1),
                    "the invocation was folded into a call of the member rather than left standing on the delegate of the local.");

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        AddAnInstanceConstructor(host);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(42));
        Assert.That(ThisMethodTemplates.Held.Slot, Is.Not.Null, "the delegate which the template handed over was not held.");
        Assert.That(ThisMethodTemplates.Held.Number, Is.EqualTo(5), "the field which the template wrote on its way was not written.");
    }

    [Test]
    public void A_Held_Delegate_Which_A_Call_Took_Under_The_Arguments_Is_Built_Rather_Than_Folded()
    {
        // A read of the local is handed to a member which answers a value of another type, and that value is one of the
        // arguments of the invocation: the call takes the delegate the read left and leaves another value in its place,
        // which the walk counted as the read still standing, so the read was answered for the invocation as well and
        // every read of the local was written as the receiver of the member.
        var host = NewHostWithAdd(isVirtual: false, "MethodInjectionCountedDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegateWhichWasCountedFirst)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.True,
                    "the delegate was not built into the local which holds it.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference { Name: "Invoke" }), Is.EqualTo(1),
                    "the invocation was folded into a call of the member rather than left standing on the delegate of the local.");

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        AddAnInstanceConstructor(host);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(28));
    }

    [Test]
    public void InvokeAHeldDelegate_Of_A_Static_Member_Rewrites_To_Direct_Call()
    {
        // The store of the delegate is the whole of what the symbol stands for, and a member which belongs to no
        // instance is reached with no receiver: the store was left reading a stack which nothing had pushed, which is a
        // body the runtime refuses to run.
        var host = NewHostWithWiden("MethodInjectionHeldDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(long).ToGneedleType(),
            [],
            [new Parameter(typeof(long).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegateOfAStaticMember)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "Widen"), Is.True,
                    "the delegate was not rewritten to a direct call to Widen.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(null, [3L]), Is.EqualTo(4L));
    }

    [Test]
    public void ThisMethod_Of_A_Local_Which_Is_Read_Twice_Is_Rewritten()
    {
        // A local which is read twice is read once by the walk of the stack and once more by the body, and the walk
        // used to take the type of the local away at the first read: the second read put a value with no type on the
        // stack, and the arguments were compared against it with nothing to compare. The read leaves the local where
        // it is, so both reads are of the type which the store of the local recorded.
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithALocalReadTwice)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Add"), Is.True,
                    "the delegate was not rewritten to a direct call to Add.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);
    }

    [Test]
    public void InvokeViaGenericDelegate_Does_Not_Throw()
    {
        // Bug A: generic delegate (Func<>) used to throw NRE while extracting Invoke params.
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);

        Assert.DoesNotThrow(() => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeViaGenericDelegate))));
    }

    [Test]
    public void InvokeViaGenericDelegate_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeViaGenericDelegate)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Add"), Is.True);
    }

    /// <summary>
    /// Create a host which declares two members of one name, <c>T Filter&lt;T&gt;(T value)</c> first and
    /// <c>int Filter(int value)</c> after it, so that the order they are declared in is not what decides which of them
    /// a delegate of <c>Func&lt;int, int&gt;</c> names.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithTwoMembersOfOneName(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        host.AddMethod(
            "Filter",
            typeof(int).ToGneedleType(),
            [new GenericParameterType("T")],
            [new Parameter(new GenericParameterType("T"))],
            MethodFlags.Public);
        host.AddMethod(
            "Filter",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);

        return host;
    }

    /// <summary>
    /// Create a host which holds <c>TOut Make&lt;TIn, TOut&gt;(TIn value)</c>, which declares a parameter of its own
    /// which stands in the value it hands back rather than in an argument of the call.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAMake(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        var make = new MethodDefinition("Make", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void)
        {
            DeclaringType = host.Source,
        };
        var tIn = new GenericParameter("TIn", make);
        var tOut = new GenericParameter("TOut", make);
        make.GenericParameters.Add(tIn);
        make.GenericParameters.Add(tOut);
        make.ReturnType = tOut;
        make.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, tIn));
        make.Body.GetILProcessor().Emit(OpCodes.Ldarg_1);
        make.Body.GetILProcessor().Emit(OpCodes.Ret);
        host.Source.Methods.Add(make);
        return host;
    }

    /// <summary>
    /// Create a host which holds <c>List&lt;TOut&gt; Make&lt;TIn, TOut&gt;(TIn value)</c>, one of whose parameters stands
    /// inside the value which the member hands back rather than in an argument of the call.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAListReturn(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        var make = new MethodDefinition("Make", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void)
        {
            DeclaringType = host.Source,
        };
        var tIn = new GenericParameter("TIn", make);
        var tOut = new GenericParameter("TOut", make);
        make.GenericParameters.Add(tIn);
        make.GenericParameters.Add(tOut);
        var list = new GenericInstanceType(module.ImportReference(typeof(List<>)));
        list.GenericArguments.Add(tOut);
        make.ReturnType = list;
        make.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, tIn));
        make.Body.GetILProcessor().Emit(OpCodes.Ldnull);
        make.Body.GetILProcessor().Emit(OpCodes.Ret);
        host.Source.Methods.Add(make);
        return host;
    }

    /// <summary>
    /// Create a host which holds <c>T[] Echo&lt;T&gt;(T[] value)</c>, whose parameter is an array of the parameter which
    /// the member declares, so that the rank of that array is a type of its own rather than part of the element.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAnArrayEcho(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        var echo = new MethodDefinition("Echo", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void)
        {
            DeclaringType = host.Source,
        };
        var parameter = new GenericParameter("T", echo);
        echo.GenericParameters.Add(parameter);
        echo.ReturnType = new ArrayType(parameter);
        echo.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, new ArrayType(parameter)));
        echo.Body.GetILProcessor().Emit(OpCodes.Ldarg_1);
        echo.Body.GetILProcessor().Emit(OpCodes.Ret);
        host.Source.Methods.Add(echo);
        return host;
    }

    /// <summary>
    /// Create a host which declares a real instance method <c>T Identity&lt;T&gt;(T value)</c>, which hands its argument
    /// back, so that a template which reaches a member declaring a parameter of its own has one to be rewritten to.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithIdentity(string assemblyName = "MethodInjectionIdentityAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        var identity = new MethodDefinition("Identity", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void)
        {
            DeclaringType = host.Source,
        };
        var parameter = new GenericParameter("T", identity);
        identity.GenericParameters.Add(parameter);
        identity.ReturnType = parameter;
        identity.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, parameter));
        identity.Body.GetILProcessor().Emit(OpCodes.Ldarg_1);
        identity.Body.GetILProcessor().Emit(OpCodes.Ret);
        host.Source.Methods.Add(identity);
        return host;
    }

    /// <summary>
    /// Create a host which declares a real instance method <c>T Identity&lt;T&gt;(T value)</c> whose parameter accepts
    /// the types of one kind alone, so that a template which describes the member with a type of the other kind
    /// describes no member of it.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    /// <param name="kind">The kind which the parameter accepts, which is the types of references where the caller names
    /// none.</param>
    private static TypeHandler NewHostWithAConstrainedIdentity(
        string assemblyName, GenericParameterAttributes kind = GenericParameterAttributes.ReferenceTypeConstraint)
    {
        var host = NewHostWithIdentity(assemblyName);
        host.Source.Methods.First(method => method.Name == "Identity").GenericParameters[0].Attributes = kind;
        return host;
    }

    /// <summary>
    /// Weave the identity of a member whose constraint names the given type, hand it the given value, and run it: the
    /// value which comes back is the value which went in, which is what tells that the member was called at all rather
    /// than refused for the constraint it declares.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which each test gives one of its own.</param>
    /// <param name="constraint">The type which the constraint of the member names.</param>
    /// <param name="value">The type of the value which the member is handed and hands back.</param>
    /// <param name="template">Name of the template which is woven.</param>
    /// <param name="argument">The value itself.</param>
    /// <param name="message">What is reported when the member was not called.</param>
    private static void TheIdentityOfAConstrainedMemberIsCalled(string assemblyName, Type constraint, Type value, string template,
                                                                object argument, string message)
    {
        var host = NewHostWhoseIdentityIsConstrainedTo(assemblyName, constraint);
        var method = host.AddMethod("Run", value.ToGneedleType(), [], [new Parameter(value.ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), template));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [argument]), Is.SameAs(argument), message);
    }

    /// <summary>
    /// Create the same host with a constraint which names a type rather than a kind, which the types the instantiation
    /// is made of satisfy or do not.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    /// <param name="constraint">The type which the constraint of the parameter names.</param>
    private static TypeHandler NewHostWhoseIdentityIsConstrainedTo(string assemblyName, Type constraint)
    {
        var host = NewHostWithIdentity(assemblyName);
        host.Source.Methods.First(method => method.Name == "Identity")
            .GenericParameters[0]
            .SetConstraintFromType(host.AssemblyHandler, host.Source, Constraint.FromType(constraint));
        return host;
    }

    /// <summary>The name of the type which the call of a member of a woven body is instantiated with.</summary>
    /// <param name="method">The member which was woven.</param>
    /// <param name="name">The name of the member which the body calls.</param>
    /// <returns>The name of the type argument of the call, or null where the call names no instantiation.</returns>
    private static string? InstantiationOfTheCall(IMethodHandler method, string name)
        => ((MethodHandler) method).Source.Body.Instructions
                                    .Select(instruction => instruction.Operand)
                                    .OfType<GenericInstanceMethod>()
                                    .FirstOrDefault(reference => reference.Name == name)?
                                    .GenericArguments[0].FullName;

    /// <summary>The names of the types which the call of a member of a woven body is instantiated with, in order.</summary>
    /// <param name="method">The member which was woven.</param>
    /// <param name="name">The name of the member which the body calls.</param>
    /// <returns>The names of the type arguments of the call, or an empty list where the call names no instantiation.</returns>
    private static string[] InstantiationsOfTheCall(IMethodHandler method, string name)
        => ((MethodHandler) method).Source.Body.Instructions
                                    .Select(instruction => instruction.Operand)
                                    .OfType<GenericInstanceMethod>()
                                    .FirstOrDefault(reference => reference.Name == name)?
                                    .GenericArguments.Select(argument => argument.FullName).ToArray() ?? [];

    [Test]
    public void ThisMethod_Of_A_Name_Which_Two_Members_Share_Is_The_One_Which_The_Names_Of_The_Types_Describe()
    {
        // Two members of one name are told apart by the signature which the caller hands them, which is what a compiler
        // does with them: the member which the names of the types describe is answered with wherever there is one, and a
        // member which only the binding of the parameters describes - one which declares a parameter of its own - is
        // what those names leave open rather than what they say. So a type which declares both is called through the one
        // the names name, whether or not the other is declared first.
        var host = NewHostWithTwoMembersOfOneName("MethodInjectionTwoMembersAssembly");
        var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Filter_OfAnInt)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                         .FirstOrDefault(reference => reference.Name == "Filter");

        Assert.That(call, Is.Not.Null, "the member which the delegate describes was not called.");
        Assert.That(call, Is.Not.InstanceOf<GenericInstanceMethod>(),
                    "the member which declares a parameter of its own was called rather than the one the names describe.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Declares_A_Parameter_Of_Its_Own_Is_Instantiated_From_The_Delegate()
    {
        // A member which declares a parameter of its own is one which no bare signature names, so the delegate which
        // the template wrote is what says which instantiation of it is reached: two woven members which name the same
        // member are calls of two instantiations of it.
        var host = NewHostWithIdentity();
        var asInt = host.AddMethod("RunInt", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        asInt.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnInt)));
        var asString = host.AddMethod("RunString", typeof(string).ToGneedleType(), [], [new Parameter(typeof(string).ToGneedleType())], MethodFlags.Public);
        asString.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAString)));

        Assert.That(InstantiationOfTheCall(asInt, "Identity"), Is.EqualTo(typeof(int).FullName),
                    "the call was not one of the instantiation which the delegate named.");
        Assert.That(InstantiationOfTheCall(asString, "Identity"), Is.EqualTo(typeof(string).FullName),
                    "the call was not one of the instantiation which the delegate named.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type)!;

        Assert.That(type.GetMethod("RunInt")!.Invoke(instance, [5]), Is.EqualTo(5),
                    "the member which declares a parameter of its own was not called through This.");
        Assert.That(type.GetMethod("RunString")!.Invoke(instance, ["hi"]), Is.EqualTo("hi"),
                    "the call was not one of the instantiation which the delegate named.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Declares_A_Parameter_Of_Its_Own_Is_Instantiated_From_The_Token()
    {
        // The token of the template stands for the parameter of the member which is woven, so the member which
        // declares a parameter of its own is called with it, whatever the woven member is instantiated with.
        var host = NewHostWithIdentity("MethodInjectionIdentityByTokenAssembly");
        var call = host.AddMethod(
            "Call",
            typeof(M_0).ToGneedleType(),
            [new GenericParameterType("U")],
            [new Parameter(typeof(M_0).ToGneedleType())],
            MethodFlags.Public);
        call.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfTheMethod)));

        Assert.That(InstantiationOfTheCall(call, "Identity"), Is.EqualTo("U"),
                    "the call was not one of the member which declares one, instantiated with the parameter of the member.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type)!;

        Assert.That(type.GetMethod("Call")!.MakeGenericMethod(typeof(string)).Invoke(instance, ["hi"]), Is.EqualTo("hi"),
                    "the member which declares a parameter of its own was not called with the parameter of the member.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Declares_A_Parameter_Of_Its_Own_Refuses_A_Delegate_Which_Describes_Another()
    {
        // The arguments of a member which declares a parameter of its own are only read out of the delegate which names
        // it, so a delegate which hands back another type than the one which those arguments instantiate the member with
        // names no member: no candidate of that name is described, and the member is not found at all rather than being
        // found and refused by the rule of the call. The refusal names the member which was looked for either way, so
        // both are read.
        var host = NewHostWithIdentity("MethodInjectionIdentityMismatchAssembly");
        var call = host.AddMethod("Run", typeof(string).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => call.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Mismatched_Identity))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was not refused as one which no candidate of that name describes: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Declares_A_Parameter_Of_Its_Own_Refuses_A_Value_Of_Another_Type()
    {
        // The member is found by the name which its parameter bears, which is the name of the parameter of the body as
        // well, so the rule which names the parameters of a member by the parameters of the body would name it: the
        // delegate describes the argument of the call with that parameter and the value which the member hands back with
        // a type which the instantiation of it does not stand for, and the two disagree. The call is refused rather than
        // written as one of the parameter of the body, which would hand the member a value its own parameter does not
        // stand for, which is a body the runtime refuses to run.
        var host = NewHostWithIdentity("MethodInjectionIdentityOfTheTokenAssembly");
        var method = (MethodHandler) host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [new GenericParameterType("T")],
            [new Parameter(new GenericParameterType("T"))],
            MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.IdentityOfTheTokenWithAValueOfAnotherType))));

        Assert.That(thrown!.Message, Does.Contain("names no member"),
                    "the delegate was refused as one which names no member rather than by the rule of the call.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Is_Instantiated_With_A_Type_Its_Own_Constraint_Refuses_Is_Refused()
    {
        // The parameter of the member accepts the types of references alone and the delegate describes the member with
        // the type of an int. The delegate is what says which instantiation of a member of a parameter of its own is
        // reached, and the instantiation which it names here is one the runtime refuses to call: the woven assembly
        // holds a call the verifier rejects rather than failing the weave, so the mismatch of the constraint is what
        // the weave reads as well, just as the mismatch of the value which the member hands back already is.
        var host = NewHostWithAConstrainedIdentity("MethodInjectionConstrainedIdentityAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnIntWhichTheConstraintRefuses))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    "the member was called with a type which the constraint of its own parameter refuses.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_The_Instantiation_Fits_Is_Called()
    {
        // The same host, woven with a delegate which names a type of the kind which the parameter accepts: the kind is
        // what the constraint is read against, so the member is found and the woven assembly runs.
        var host = NewHostWithAConstrainedIdentity("MethodInjectionConstrainedIdentityOfAStringAssembly");
        var method = host.AddMethod("Run", typeof(string).ToGneedleType(), [], [new Parameter(typeof(string).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAString)));

        Assert.That(InstantiationOfTheCall(method, "Identity"), Is.EqualTo(typeof(string).FullName),
                    "the member was not called one of the instantiation which fits the constraint of its parameter.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), ["hi"]), Is.EqualTo("hi"),
                    "the member whose constraint the instantiation fits was not called.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Parameter_Accepts_The_Types_Of_Values_Alone_Is_Refused_A_Reference()
    {
        // The other kind which a constraint names, which is read the same way: a parameter which accepts the types of
        // values is one which the type of a string does not fit and one which the type of an int does.
        var host = NewHostWithAConstrainedIdentity("MethodInjectionConstrainedIdentityOfAValueAssembly",
                                                  GenericParameterAttributes.NotNullableValueTypeConstraint);
        var asInt = host.AddMethod("RunInt", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        asInt.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnInt)));
        var asString = host.AddMethod("RunString", typeof(string).ToGneedleType(), [], [new Parameter(typeof(string).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => asString.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAString))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    "the member was called with a type of a reference where its parameter accepts the types of values.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);

        Assert.That(InstantiationOfTheCall(asInt, "Identity"), Is.EqualTo(typeof(int).FullName),
                    "the member was not called one of the instantiation which fits the constraint of its parameter.");
        Assert.That(type.GetMethod("RunInt")!.Invoke(Activator.CreateInstance(type), [5]), Is.EqualTo(5),
                    "the member whose parameter accepts the types of values was not called with one.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Parameter_Accepts_The_Types_Of_Values_Alone_Is_Refused_A_Value_Which_May_Be_Absent()
    {
        // A value which may be absent is a value type, so the kind of it is the kind the parameter accepts, and it is
        // the one value of that kind which the constraint does not: the runtime refuses an instantiation of such a
        // parameter with it, so a delegate which names it describes no member rather than one which cannot be called.
        var host = NewHostWithAConstrainedIdentity("MethodInjectionConstrainedIdentityOfANullableAssembly",
                                                  GenericParameterAttributes.NotNullableValueTypeConstraint);
        var method = host.AddMethod("Run", typeof(int?).ToGneedleType(), [], [new Parameter(typeof(int?).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfANullable))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with a value which may be absent where its own parameter accepts the types of values: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Parameter_Accepts_The_Types_Of_References_Alone_Is_Refused_A_Value_Which_May_Be_Absent()
    {
        // The same value read against the other kind, which it is not: a value which may be absent is a value type
        // whatever the constraint of the parameter says, so the read of the type which pairs a value with the absence
        // of it may not turn that kind into the kind which the references are.
        var host = NewHostWithAConstrainedIdentity("MethodInjectionConstrainedIdentityOfAReferenceFromANullableAssembly");
        var method = host.AddMethod("Run", typeof(int?).ToGneedleType(), [], [new Parameter(typeof(int?).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfANullable))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with a value which may be absent where its own parameter accepts the types of references: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Type_The_Argument_Is_Not_Made_Of_Is_Refused()
    {
        // A constraint which names a type is satisfied by a type which is made of that type, which the walk of the base
        // types and the interfaces of the argument tells: the type of every value is made of neither an interface nor
        // anything which implements one, so the instantiation the delegate names is one the runtime refuses and the
        // delegate describes no member rather than one which cannot be called.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToATypeAssembly", typeof(IComparable));
        var method = host.AddMethod("Run", typeof(object).ToGneedleType(), [], [new Parameter(typeof(object).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnObject))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with a type which is made of nothing the constraint names: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Type_Which_The_Argument_Is_Made_Of_Is_Called()
    {
        // The same host, woven with a delegate which names a type which is made of the interface the constraint names:
        // the member is found, called on the instantiation, and the woven assembly runs.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToASatisfiedTypeAssembly", typeof(IComparable));
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnInt)));

        Assert.That(InstantiationOfTheCall(method, "Identity"), Is.EqualTo(typeof(int).FullName),
                    "the member was not called one of the instantiation which satisfies the constraint of its parameter.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [5]), Is.EqualTo(5),
                    "the member whose constraint the instantiation satisfies was not called.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Base_Type_Of_The_Argument_Is_Called()
    {
        // The type which the delegate names is not the type the constraint names, and it is made of it through the base
        // types it is declared with, which is what the walk reaches the constraint through.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToABaseTypeAssembly", typeof(HelperClass), typeof(DerivedOfAHelperClass),
                                               nameof(ThisMethodTemplates.Identity_OfADerived), new DerivedOfAHelperClass(),
                                               "the member whose constraint names a base type of the argument was not called.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_An_Interface_Of_The_Argument_Is_Called()
    {
        // A type is made of the interfaces which are declared where it stands, which the walk reads as well: the
        // interface which the constraint names is one of them, as the very instantiation which the constraint names.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToAnInterfaceAssembly", typeof(ICountedOfAnInstantiation<int>), typeof(CountedOfAnInstantiation),
                                               nameof(ThisMethodTemplates.Identity_OfACounted), new CountedOfAnInstantiation(),
                                               "the member whose constraint names an interface of the argument was not called.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_An_Interface_Of_Another_Instantiation_Is_Refused()
    {
        // The type is made of the interface, and not of the instantiation of it which the constraint names: the two are
        // different types, which is what the name of the interface holds, and the runtime refuses one for the other.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToAnotherInstantiationAssembly",
                                                      typeof(ICountedOfAnInstantiation<string>));
        var method = host.AddMethod(
            "Run",
            typeof(CountedOfAnInstantiation).ToGneedleType(),
            [],
            [new Parameter(typeof(CountedOfAnInstantiation).ToGneedleType())],
            MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfACounted))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with a type which is made of another instantiation of the constraint: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Type_Which_The_Array_Is_Not_Given_Is_Refused()
    {
        // An array is made of the types which the runtime gives it rather than of the types the metadata declares it
        // with, and the type which the constraint names is none of them: the instantiation is one the runtime refuses
        // and the delegate describes no member rather than one which cannot be called.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToAnUnnamedTypeOfAnArrayAssembly",
                                                      typeof(IComparable));
        var method = host.AddMethod("Run", typeof(int[]).ToGneedleType(), [], [new Parameter(typeof(int[]).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnArrayOfInt))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with an array which the constraint of the parameter refuses: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Type_Which_The_Array_Is_Given_Is_Called()
    {
        // The same array read against a type which the runtime does give it, which is what tells the refusal of the
        // types which no array is given from the refusal of an array which is given none of them.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToAGivenTypeOfAnArrayAssembly", typeof(IEnumerable<int>), typeof(int[]),
                                               nameof(ThisMethodTemplates.Identity_OfAnArrayOfInt), new[] { 1, 2, 3 },
                                               "the member whose constraint names a type which the array is given was not called.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Which_An_Array_Of_Two_Dimensions_Is_Not_Given_Is_Refused()
    {
        // The collections which name the element are given to an array of one dimension alone, which is the shape the
        // runtime calls a vector: the element which the constraint names stands in a type which the array of two
        // dimensions is not given, so the shape of the array is what the walk reads rather than the element alone.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToAVectorOfAnArrayAssembly", typeof(IList<int>));
        var method = host.AddMethod("Run", typeof(int[,]).ToGneedleType(), [], [new Parameter(typeof(int[,]).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnArrayOfTwoDimensions))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with an array of two dimensions where the constraint names a collection of one: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Type_Which_The_Array_Is_Given_Through_Its_Element_Is_Called()
    {
        // The runtime accepts an array for a collection of every type which the element of the array is accepted for,
        // which is the covariance of the arrays and of the sequences which name the element: the walk reads the
        // argument of the constraint with that variance rather than by its name alone.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToACovariantTypeOfAnArrayAssembly", typeof(IEnumerable<object>), typeof(string[]),
                                               nameof(ThisMethodTemplates.Identity_OfAnArrayOfString), new[] { "a", "b" },
                                               "the member whose constraint names a type which the array is given through its element was not called.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Type_Which_The_Argument_Is_Given_Through_Its_Element_Is_Called()
    {
        // The same, of an instance of a generic type which implements the interface of the constraint as an instance of
        // another element type: a list of strings is a sequence of the values of the framework.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToACovariantTypeOfAnInstanceAssembly", typeof(IEnumerable<object>), typeof(List<string>),
                                               nameof(ThisMethodTemplates.Identity_OfAListOfString), new List<string> { "a", "b" },
                                               "the member whose constraint names a type which the argument is given through its element was not called.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Type_Which_Accepts_The_Argument_Is_Called()
    {
        // A parameter which the declaration of an interface marks contravariant accepts the types of everything which
        // the argument of the constraint accepts, which is the other way the arguments of an instance are read.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToAContravariantTypeAssembly", typeof(IComparer<string>), typeof(Comparer<object>),
                                               nameof(ThisMethodTemplates.Identity_OfAComparerOfTheValuesOfTheFramework), Comparer<object>.Default,
                                               "the member whose constraint names a type which accepts the argument was not called.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_Another_Value_Than_The_Element_Of_An_Array_Is_Refused()
    {
        // The variance of an argument converts a reference of one type to a reference of another, and a value which is
        // boxed is no reference of the type which the argument of the constraint names: an array of ints is no sequence
        // of the values of the framework, which the runtime refuses the instantiation for.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToABoxedElementOfAnArrayAssembly",
                                                      typeof(IEnumerable<object>));
        var method = host.AddMethod("Run", typeof(int[]).ToGneedleType(), [], [new Parameter(typeof(int[]).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnArrayOfInt))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with an array whose element is another value than the one the constraint names: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_Another_Value_Than_The_Element_Of_An_Instance_Is_Refused()
    {
        // The same read of an instance of a generic type, whose argument is a value which is boxed as well.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToABoxedElementOfAnInstanceAssembly",
                                                      typeof(IEnumerable<object>));
        var method = host.AddMethod("Run", typeof(List<int>).ToGneedleType(), [], [new Parameter(typeof(List<int>).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAListOfAnInt))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with an instance whose element is another value than the one the constraint names: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Value_Which_The_Argument_Is_Not_Converted_To_Is_Refused()
    {
        // And the other way of the same conversion, which a parameter the declaration marks contravariant is read with:
        // a comparer of the values of the framework accepts the values which the argument of the constraint accepts,
        // and an int is no such value.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToABoxedArgumentAssembly",
                                                      typeof(IComparer<int>));
        var method = host.AddMethod(
            "Run",
            typeof(Comparer<object>).ToGneedleType(),
            [],
            [new Parameter(typeof(Comparer<object>).ToGneedleType())],
            MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAComparerOfTheValuesOfTheFramework))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with a comparer of a value which the constraint does not name: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Sequence_Of_An_Array_Is_Called()
    {
        // An array stands among the arguments of the instance as well, and the covariance of the arrays is what the
        // argument of the constraint is read with: a list of arrays of strings is a sequence of arrays of the values of
        // the framework.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToASequenceOfAnArrayAssembly", typeof(IEnumerable<object[]>), typeof(List<string[]>),
                                               nameof(ThisMethodTemplates.Identity_OfAListOfAnArrayOfString), new List<string[]> { new[] { "a" }, new[] { "b" } },
                                               "the member whose constraint names a sequence of an array was not called.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Sequence_Of_The_Values_Of_The_Framework_Is_Called_For_An_Interface()
    {
        // An interface is a reference of the type of every value as a class is, and the metadata of it declares no base
        // type: the walk of it reaches the type of every value through the declaration of the interface, and a sequence
        // of the values of the framework is a sequence of an interface.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToASequenceOfAnInterfaceAssembly", typeof(IEnumerable<object>), typeof(IEnumerable<ICountedOfAnInstantiation<int>>),
                                               nameof(ThisMethodTemplates.Identity_OfASequenceOfAnInterface), new List<ICountedOfAnInstantiation<int>>(),
                                               "the member whose constraint names a sequence of the values of the framework was not called for an interface.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_The_Values_Of_The_Framework_Is_Called_For_An_Array()
    {
        // The collections which name the element are given to an array for every type which a reference conversion takes
        // the element to, whatever the variance of the parameter which names the element is: an array of strings is a
        // collection of the values of the framework, which the runtime accepts and the walk reads as well.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToACollectionOfAnArrayAssembly", typeof(IList<object>), typeof(string[]),
                                               nameof(ThisMethodTemplates.Identity_OfAnArrayOfString), new[] { "a", "b" },
                                               "the member whose constraint names a collection of the values of the framework was not called for an array.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_Arrays_Is_Called_For_An_Array_Of_Arrays()
    {
        // The element of the array is an array as well, and the type which the collection of the constraint names is one
        // which the covariance of the arrays takes it to: an array of arrays of strings is a collection of arrays of the
        // values of the framework.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToACollectionOfArraysAssembly", typeof(IList<object[]>), typeof(string[][]),
                                               nameof(ThisMethodTemplates.Identity_OfAnArrayOfAnArrayOfString), new[] { new[] { "a" }, new[] { "b" } },
                                               "the member whose constraint names a collection of arrays was not called for an array of arrays.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_Sequences_Is_Called_For_An_Array_Of_Instances()
    {
        // The element of the array is an instance of a generic type whose conversion is the variance of the sequence it
        // implements: an array of lists of strings is a collection of sequences of the values of the framework.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToACollectionOfSequencesAssembly", typeof(IList<IEnumerable<object>>), typeof(List<string>[]),
                                               nameof(ThisMethodTemplates.Identity_OfAnArrayOfAListOfString), new[] { new List<string> { "a" } },
                                               "the member whose constraint names a collection of sequences was not called for an array of instances.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_The_Type_Under_An_Enumeration_Is_Called_For_An_Array_Of_It()
    {
        // The runtime reads an array of an enumeration as an array of the type under it, so a member whose parameter is
        // constrained to a collection of that type is one which such an array is called for: the read of the elements of
        // the two arrays is what tells it, which no reference conversion does.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToACollectionOfAnUnderlyingTypeAssembly", typeof(IList<int>), typeof(DayOfWeek[]),
                                               nameof(ThisMethodTemplates.Identity_OfAnArrayOfAnEnumeration), new[] { DayOfWeek.Monday },
                                               "the member whose constraint names a collection of the type under an enumeration was not called for an array of it.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_One_Width_Is_Called_For_An_Array_Of_Another()
    {
        // The values of one width are read as one another whatever the sign of each of them is, so a member whose
        // parameter is constrained to a collection of the values of one sign is one which an array of the other sign is
        // called for.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToACollectionOfOneWidthAssembly", typeof(IList<int>), typeof(uint[]),
                                               nameof(ThisMethodTemplates.Identity_OfAnArrayOfUnsignedValues), new[] { 1u, 2u },
                                               "the member whose constraint names a collection of one width was not called for an array of another.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_The_Values_Under_A_Byte_Of_An_Enumeration_Is_Called_For_An_Array_Of_Them()
    {
        // The width of the values of an enumeration is the width of the type under it, and it is read from the
        // enumeration rather than from the type which stands under it alone.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToACollectionOfAByteEnumerationAssembly", typeof(IList<ByteEnumOfTheTests>), typeof(byte[]),
                                               nameof(ThisMethodTemplates.Identity_OfAnArrayOfTheValuesUnderAByteEnumeration), new byte[] { 1, 2 },
                                               "the member whose constraint names a collection of the values under an enumeration of a byte was not called.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_The_Widest_Signed_Values_Is_Called_For_An_Array_Of_The_Unsigned_Ones()
    {
        // The same of the values of eight bytes, which is the other end of the widths which the integer family holds.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToACollectionOfTheWidestValuesAssembly", typeof(IList<long>), typeof(ulong[]),
                                               nameof(ThisMethodTemplates.Identity_OfAnArrayOfTheWidestUnsignedValues), new[] { 1ul, 2ul },
                                               "the member whose constraint names a collection of the widest signed values was not called for an array of the unsigned ones.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_The_Signed_Native_Values_Is_Called_For_An_Array_Of_The_Unsigned_Ones()
    {
        // The native values of the two signs are related to one another whatever the width the runtime holds them at,
        // which the width of the values of every other type of the integer family does not tell.
        TheIdentityOfAConstrainedMemberIsCalled("MethodInjectionConstrainedToACollectionOfTheNativeValuesAssembly", typeof(IList<IntPtr>), typeof(UIntPtr[]),
                                               nameof(ThisMethodTemplates.Identity_OfAnArrayOfTheUnsignedNativeValues), new[] { (UIntPtr) 1, (UIntPtr) 2 },
                                               "the member whose constraint names a collection of the signed native values was not called for an array of the unsigned ones.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_The_Native_Values_Is_Refused_An_Array_Of_One_Width()
    {
        // The native values are related to one another alone, and neither to the values of the width which the runtime
        // holds them at: the runtime refuses the instantiation as well.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToACollectionOfTheNativeValuesOfOneWidthAssembly",
                                                      typeof(IList<IntPtr>));
        var method = host.AddMethod("Run", typeof(long[]).ToGneedleType(), [], [new Parameter(typeof(long[]).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnArrayOfTheWidestSignedValues))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with an array of the width which the native values are held at: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_Arrays_Of_An_Enumeration_Is_Refused_An_Array_Of_Values()
    {
        // An array is a type of its own rather than the enumeration which it holds, so the width of the values of it is
        // told by nothing: the runtime refuses the instantiation for an array of the values alone, and the walk reads
        // the array as the type it is.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToACollectionOfArraysOfAnEnumerationAssembly",
                                                      typeof(IList<DayOfWeek[]>));
        var method = host.AddMethod("Run", typeof(int[]).ToGneedleType(), [], [new Parameter(typeof(int[]).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnArrayOfInt))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with an array of values where the constraint names an array of arrays of an enumeration: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Constraint_Names_A_Collection_Of_Another_Width_Is_Refused_An_Array()
    {
        // The widths of the elements are what relates them, and the values of two widths are related by nothing: the
        // runtime refuses the instantiation as well.
        var host = NewHostWhoseIdentityIsConstrainedTo("MethodInjectionConstrainedToACollectionOfAnotherWidthAssembly",
                                                      typeof(IList<float>));
        var method = host.AddMethod(
            "Run",
            typeof(DayOfWeek[]).ToGneedleType(),
            [],
            [new Parameter(typeof(DayOfWeek[]).ToGneedleType())],
            MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnArrayOfAnEnumeration))));

        Assert.That(thrown!.Message, Does.Contain("cannot be resolved").And.Contains("Identity"),
                    $"the member was called with an array whose element is of another width than the one the constraint names: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Declares_A_Parameter_Of_Its_Own_Refuses_Another_Where_The_Body_Names_One()
    {
        // The rule which names the parameters of a member by the parameters of the body is not the rule for a member
        // which the signature of the delegate describes the parameters of: the body of this test declares a parameter of
        // the name of the parameter of the member, so that rule would name it, and the call would be written as one of
        // the instantiation which the body names rather than as one of the instantiation which the delegate described -
        // a call which hands the member a value of a type which its parameter does not stand for, which is a body the
        // runtime refuses to run rather than one which calls the member.
        var host = NewHostWithIdentity("MethodInjectionIdentityMismatchNamedAssembly");
        var call = host.AddMethod("Run", typeof(string).ToGneedleType(), [new GenericParameterType("T")],
                                  [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => call.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Mismatched_Identity))));

        Assert.That(thrown!.Message, Does.Contain("Identity"));
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Names_A_Parameter_Of_Its_Own_By_The_Value_It_Hands_Back_Is_Instantiated_With_It()
    {
        // The member declares a parameter of its own which stands in no argument of the call, and the value which it
        // hands back is what names it: the delegate hands that value back, so the member is instantiated with the type
        // of its argument and the type of the value at once, in the order its parameters are declared in.
        var host = NewHostWithAMake("MethodInjectionMakeAssembly");
        var method = host.AddMethod("Run", typeof(string).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.MakeAString)));

        Assert.That(InstantiationsOfTheCall(method, "Make"), Is.EqualTo(new[] { typeof(int).FullName, typeof(string).FullName }),
                    "the member was not instantiated with the argument and the value which the delegate describes together.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Names_A_Parameter_Of_Its_Own_Is_Refused_Where_The_Delegate_Hands_Nothing_Back()
    {
        // A delegate which stands for a member that hands nothing back, just like `Action<int>`, hands back the type of
        // nothing, and the type of nothing describes no value: a parameter of the member which stands in no argument of
        // the call is named by nothing at all, so the delegate describes no member. Writing the call with the type of
        // nothing as the argument of the member is what naming it there would come to, and an assembly which names it is
        // one the runtime refuses to load, so the weave is refused instead.
        var host = NewHostWithAMake("MethodInjectionMakeOfNoValueAssembly");
        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.MakeOfNoValue))));

        Assert.That(thrown!.Message, Does.Contain("Make"));
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Hands_A_Value_Back_Is_Refused_A_Delegate_Which_Hands_None()
    {
        // The value which the call of the member leaves where the symbol stood is one which the body hands nowhere
        // where the delegate hands nothing back, so the body is one the runtime refuses to run rather than one which
        // calls the member: the delegate describes no member here, just as it describes none where it names a type
        // which no instantiation of the member stands for.
        var host = NewHostWithAnEchoAndASilence("MethodInjectionVoidDelegateAssembly");
        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.CallAVoidDelegate))));

        Assert.That(thrown!.Message, Does.Contain("The value which the delegate of the template hands back is not the one which the member hands back"),
                    $"the member which hands a value back was called through a delegate which hands nothing back: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Hands_Nothing_Back_Is_Refused_A_Delegate_Which_Hands_A_Value()
    {
        // The other direction of the same mismatch: a delegate which hands a value back reads one which no call of the
        // member leaves, and the woven body is one the runtime refuses to run for it as well.
        var host = NewHostWithAnEchoAndASilence("MethodInjectionValueDelegateAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.CallAValueDelegate))));

        Assert.That(thrown!.Message, Does.Contain("The value which the delegate of the template hands back is not the one which the member hands back"),
                    $"the member which hands nothing back was called through a delegate which hands a value back: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Hands_Back_A_Value_Of_Another_Type_Is_Refused()
    {
        // A member which declares no parameter of its own is found by the names of the types of its arguments, which
        // say nothing of the value it hands back: a delegate which hands back a value of another name describes no
        // member of the type either, and the value which the call of it leaves is read as one of another type.
        var host = NewHostWithAnEchoAndASilence("MethodInjectionAnotherValueAssembly");
        var method = host.AddMethod("Run", typeof(string).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.CallAStringDelegate))));

        Assert.That(thrown!.Message, Does.Contain("The value which the delegate of the template hands back is not the one which the member hands back"),
                    $"the member was called through a delegate which hands back a value of another type: {thrown.Message}");
    }

    [Test]
    public void InstanceMethod_Of_A_Member_Of_A_Generic_Type_Which_Hands_Back_Another_Type_Is_Refused()
    {
        // The member belongs to a type which declares a parameter of its own, so the value it hands back is named by
        // that parameter: the instantiation which the template named is what the parameter stands for here, which is
        // the type of the argument of the call, and the delegate hands back a value of another type than that one.
        var (_, _, method) = NewInstanceHost("InstanceMethodAnotherValueAssembly", [typeof(GenericHelper<int>), typeof(int)]);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethodOfAGenericTypeWhichHandsBackAnotherType))));

        Assert.That(thrown!.Message, Does.Contain("The value which the delegate of the template hands back is not the one which the member hands back"),
                    $"the member of the generic type was called through a delegate which hands back a value of another type: {thrown.Message}");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Hands_Back_A_Value_Of_The_Integer_Family_Is_Called_Through_Another_Of_It()
    {
        // The stack carries the values of the integer family as the same 4-byte value whatever the width of the type
        // which names them, so a member which hands back an int is one which a delegate which hands back a char
        // describes: the tolerance is the one which the arguments of a call are read with as well.
        var host = NewHostWithAnEchoAndASilence("MethodInjectionIntegerFamilyAssembly");
        var method = host.AddMethod("Run", typeof(char).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.CallACharDelegate)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [65]), Is.EqualTo('A'),
                    "the member which hands back a value of the integer family was not called through the delegate.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Hands_Back_A_Value_Under_An_Enumeration_Is_Called_Through_An_Int()
    {
        // An enumeration of the integer family is carried as the value under it, which is the value which the member
        // leaves and the value which the delegate hands back: there is no instruction which names the enumeration, so
        // a member which hands back one is described by a delegate which hands back the value under it.
        var host = NewHostWithAValueOfAnEnumeration("MethodInjectionEnumerationValueAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.CallAnIntDelegateForAnEnumeration)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [0]), Is.EqualTo((int) StringComparison.Ordinal),
                    "the member which hands back a value under an enumeration was not called through the delegate.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Which_Names_A_Parameter_Of_Its_Own_Inside_The_Value_It_Hands_Back_Is_Instantiated_With_It()
    {
        // The same, of a parameter which stands inside a type which the value the member hands back is an instance of:
        // List<string> is what List<TOut> is described by, so TOut is named by the argument of that value rather than by
        // the value itself, and the instantiation names that argument.
        var host = NewHostWithAListReturn("MethodInjectionListReturnAssembly");
        var method = host.AddMethod("Run", typeof(List<string>).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.MakeAList)));

        Assert.That(InstantiationsOfTheCall(method, "Make"),
                    Is.EqualTo(new[] { typeof(int).FullName, typeof(string).FullName }),
                    "the member was not instantiated with the argument and the type which stands in the value it hands back.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Parameter_Is_An_Array_Of_Its_Own_Is_Instantiated_From_The_Delegate()
    {
        // The parameter of the member stands in an array, and the delegate describes that array rather than the element
        // alone: Array<int> is what Array<T> is described by, so the parameter is bound to the element of the arguments
        // and the call is one of the instantiation which the delegate named.
        var host = NewHostWithAnArrayEcho("MethodInjectionArrayIdentityAssembly");
        var method = host.AddMethod("Run", typeof(int[]).ToGneedleType(), [], [new Parameter(typeof(int[]).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnArray)));

        Assert.That(InstantiationOfTheCall(method, "Echo"), Is.EqualTo(typeof(int).FullName),
                    "the call was not one of the instantiation which the delegate named.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type)!;
        var value = new[] { 1, 2, 3 };

        Assert.That(type.GetMethod("Run")!.Invoke(instance, [value]), Is.SameAs(value),
                    "the member whose parameter stands in an array was not called through the array it was handed.");
    }

    [Test]
    public void ThisMethod_Of_A_Member_Whose_Parameter_Is_An_Array_Of_Its_Own_Refuses_Another_Rank()
    {
        // The rank of an array belongs to the array rather than to the element which stands in it, and a member whose
        // parameter is an array of one rank is described by an array of any rank as well where the two elements are
        // read alone: the member is found, and the call of it is written with the element of the arguments where the
        // value which stands on the stack is a value of another rank, which is a body the runtime refuses to run. The
        // rank is read before the elements are, and the weave is refused.
        var host = NewHostWithAnArrayEcho("MethodInjectionArrayRankAssembly");
        var method = host.AddMethod("Run", typeof(int[,]).ToGneedleType(), [], [new Parameter(typeof(int[,]).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.Identity_OfAnArrayOfAnotherRank))));

        Assert.That(thrown!.Message, Does.Contain("Echo"));
    }

    [Test]
    public void InvokeCharLiteral_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithEcho(typeof(char));
        var method = host.AddMethod("Run", typeof(char).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeCharLiteral)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Echo"), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
    }

    [Test]
    public void InvokeBoolLiteral_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithEcho(typeof(bool));
        var method = host.AddMethod("Run", typeof(bool).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeBoolLiteral)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Echo"), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
    }
}
