using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// Tests for the member which the template names through `Instance`, which are the tests of <see cref="PointerTests"/> for that one placeholder.
/// </summary>
public partial class PointerTests
{
    [Test]
    public void InstanceMethod_With_NewInstance_Syntax_Rewrites_To_Direct_Call()
    {
        var asm = Assembly.Create("InstancePointerAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = AddAHost(handler);

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
            [new Parameter(typeof(HelperClass).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_NewSyntax)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            // Should rewrite to call/callvirt HelperClass::Calc, not call Instance::Method
            Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "Calc"), Is.True);
            Assert.That(ins.Any(i => i.Operand is MethodReference mr && mr.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
        });
    }

    [Test]
    public void InstanceField_Get_Rewrites_To_Ldfld()
    {
        var asm = Assembly.Create("InstanceFieldAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = AddAHost(handler);

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_Get)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference fr && fr.Name == "PublicField"), Is.True);
            Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
        });
    }

    [Test]
    public void InstanceField_Set_Rewrites_To_Stfld()
    {
        var asm = Assembly.Create("InstanceFieldAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = AddAHost(handler);

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference fr && fr.Name == "PublicField"), Is.True);
            Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
        });
    }

    [Test]
    public void InstanceProperty_Get_Rewrites_To_Call_Getter()
    {
        var asm = Assembly.Create("InstancePropertyAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = AddAHost(handler);

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceProperty_Get)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "get_PublicProperty"), Is.True);
            Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
        });
    }

    [Test]
    public void InstanceProperty_Set_Rewrites_To_Call_Setter()
    {
        var asm = Assembly.Create("InstancePropertyAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = AddAHost(handler);

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceProperty_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "set_PublicProperty"), Is.True);
            Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
        });
    }

    [Test]
    public void InstanceField_Of_An_Instance_Which_Is_Held_In_A_Local_Reads_That_Local()
    {
        // The instance which the template holds in a local stands where the template stored it, which is not where the
        // name of the field stands: the load of the local is what the field is read off, and the sequence which built the
        // array around the instance goes with the name.
        var (assembly, _, method) = NewInstanceHost("InstanceFieldInALocalAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_OfAnInstanceInALocal)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(instruction => instruction.Operand is MemberReference reference && reference.DeclaringType.FullName == Instance.TYPE_NAME), Is.False,
            "the array which built the instance of `Instance` was left in the body.");

        var receiver = ReceiverOf(ins, "PublicField");
        Assert.That(receiver.TryGetLdlocIndex(out _), Is.True,
            "the field is read off `this` rather than off the local which holds the instance.");

        var type = assembly.Load().GetType($"{NS}.Host")!;
        var helper = new HelperClass {PublicField = 21};

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [helper]), Is.EqualTo(21));
    }

    [Test]
    public void InstanceField_Of_An_Instance_Whose_Type_Is_Not_Named_Where_It_Is_Read_Throws()
    {
        // The type which the field is looked up on is named by the sequence which leads to the name of the field, and a
        // sequence which names no type names none at all. The name is not looked up on the member being woven instead,
        // which holds a field of that name of its own here: the field of the template is one of the instance the
        // template holds, and a name which is woven into another member than the one it names is worse than a name which
        // is refused.
        var host = NewHostWithField("PublicField", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [new Parameter(typeof(HelperClass[]).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_OfAnElementOfAnArray))));

        Assert.That(thrown!.Message, Does.Contain("PublicField"));
    }

    [Test]
    public void InstanceMethod_Of_A_Member_Whose_Signature_Names_The_Parameter_Of_The_Type_Is_Called()
    {
        // The signature of a member of a generic type is written where that type is declared, so the parameter which
        // stands in it is the one the instantiation which the template named holds an argument for: the member of
        // `GenericHelper<int>` which takes the parameter of the type is the one which takes an `int`, and the member
        // which the delegate describes is found and called on the instantiation rather than on the definition.
        var (assembly, _, method) = NewInstanceHost("InstanceMemberSignatureParameterAssembly",
            [typeof(GenericHelper<int>), typeof(int)]);

        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAMemberWhoseSignatureNamesTheParameter)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                         .FirstOrDefault(reference => reference.Name == "Echo");
        Assert.That(call, Is.Not.Null, "the member which the delegate describes was not called.");
        Assert.Multiple(() =>
        {
            Assert.That(call!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                "the member is called on the definition of the type which declares it rather than on the instantiation.");
            Assert.That(((GenericInstanceType) call.DeclaringType).GenericArguments.Single().FullName, Is.EqualTo(typeof(int).FullName),
                "the member is not called on the instantiation which the template named.");
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new GenericHelper<int>(), 41]), Is.EqualTo(41),
            "the woven assembly does not run the member which the signature names the parameter of the type with.");
    }

    [Test]
    public void InstanceMethod_Of_A_Constraint_Which_Is_An_Instantiation_Is_Called()
    {
        // The instance which the template named is the generic parameter of the body itself, and the constraint of that
        // parameter is an instantiation of a type which declares a parameter of its own: the member belongs to the
        // definition of the constraint, and the instantiation which the constraint names is what the parameter of the
        // member stands for, so the member is found and called on that instantiation rather than refused.
        var assembly = Assembly.Create("InstanceConstraintInstantiationAssembly");
        var host = AddAHost(assembly);
        var method = (MethodHandler) host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [new GenericParameterType("T", Constraint.FromType(typeof(ICountedOfAnInstantiation<int>)))],
            [new Parameter(typeof(M0).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAConstraintWhichIsAnInstantiation)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                         .FirstOrDefault(reference => reference.Name == "Echo");
        Assert.That(call, Is.Not.Null, "the member of the constraint was not called.");
        Assert.Multiple(() =>
        {
            Assert.That(call!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                "the member is called on the definition of the constraint rather than on the instantiation which it names.");
            Assert.That(((GenericInstanceType) call.DeclaringType).GenericArguments.Single().FullName, Is.EqualTo(typeof(int).FullName),
                "the member is not called on the instantiation which the constraint names.");
        });
        var type = LoadHostOf(assembly, host);

        Assert.That(type.GetMethod("Run")!.MakeGenericMethod(typeof(CountedOfAnInstantiation))
                        .Invoke(Activator.CreateInstance(type), [new CountedOfAnInstantiation(), 41]), Is.EqualTo(41),
            "the woven assembly does not run the member of the constraint which names the parameter of it.");
    }

    [Test]
    public void InstanceField_Of_A_Value_Which_A_Condition_Computed_Throws()
    {
        // The value which the instance was built around is read off the stack which the instructions ahead of the name
        // leave, and a branch is an instruction whose count the walk of that stack cannot read: the name is refused
        // rather than looked up on the member being woven, which holds a field of that name of its own here, because a
        // name which is woven into another member than the one it names is worse than a name which is refused.
        var host = NewHostWithField("PublicField", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [],
        [
            new Parameter(typeof(HelperClass).ToGneedleType()), new Parameter(typeof(HelperClass).ToGneedleType()),
            new Parameter(typeof(bool).ToGneedleType())
        ], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_OfAValueWhichAConditionComputed))));

        Assert.That(thrown!.Message, Does.Contain("PublicField"));
    }

    [Test]
    public void InstanceField_Of_A_Value_Which_The_Template_Computed_Reads_That_Value()
    {
        // The instance which the placeholder was built around is the value which another placeholder handed back, which
        // is a sequence of instructions rather than one load: the member is written off the value where the template left
        // it, and the array which carried it is dropped.
        var (assembly, host, method) = NewInstanceHost("InstanceComputedValueAssembly", []);
        AddHelperField(host);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_OfAValueWhichAMemberHandedBack)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(instruction => instruction.Operand is MemberReference reference && reference.DeclaringType.FullName == Instance.TYPE_NAME), Is.False,
                "the array which built the instance of `Instance` was left in the body.");
            Assert.That(ReceiverOf(ins, "PublicField"), Is.SameAs(ins.First(instruction => instruction.Operand is FieldReference {Name: "Helper"})),
                "the field is read off `this` rather than off the value which the template computed.");
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetField("Helper")!.SetValue(instance, new HelperClass {PublicField = 21});

        Assert.That(type.GetMethod("Run")!.Invoke(instance, null), Is.EqualTo(21));
    }

    [Test]
    public void InstanceMethod_Of_A_Field_Which_The_Template_Reads_Is_Reached_Through_That_Field()
    {
        // The value which the placeholder was built around is read off an instance which the template was handed, so the
        // instruction which leaves it takes one value as well: the walk which counts the values of the value read the
        // field as leaving one more than it does, so the sequence which the placeholder was built around was never
        // recognized and the template was refused rather than woven.
        var (assembly, _, method) = NewInstanceHost("InstanceFieldValueAssembly", [typeof(HelperClass), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAFieldOfAnInstance)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(instruction => instruction.Operand is MemberReference reference && reference.DeclaringType.FullName == Instance.TYPE_NAME), Is.False,
                "the array which built the instance of `Instance` was left in the body.");
            Assert.That(ReceiverOf(ins, "Calc", arguments: 1).OpCode, Is.EqualTo(OpCodes.Ldfld),
                "the member is called on a value which the template did not name.");
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;
        var outer = new HelperClass {Inner = new HelperClass()};

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [outer, 21]), Is.EqualTo(42));
    }

    [Test]
    public void InstanceStaticField_Of_A_Value_Which_The_Template_Computed_Is_Reached_Through_No_Receiver()
    {
        // The field which the instance of `Instance` names belongs to the type alone, so the value which the template
        // computed is read for nothing: it goes with the array which carried it, which is what leaves the body without a
        // value on the stack where the member takes none.
        var (assembly, host, method) = NewInstanceHost("InstanceComputedStaticValueAssembly", []);
        AddHelperField(host);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceStaticField_OfAValueWhichAMemberHandedBack)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && field.Name == "StaticField"), Is.True,
                "the static field was not read through the type which the template named.");
            Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False,
                "a receiver was written where the member being woven holds none.");
        });
        HelperClass.StaticField = 7;
        var type = assembly.Load().GetType($"{NS}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), null), Is.EqualTo(7));
    }

    [Test]
    public void InstanceProperty_Of_A_Value_Which_The_Template_Computed_Calls_The_Accessor()
    {
        var (assembly, host, method) = NewInstanceHost("InstanceComputedPropertyAssembly", []);
        AddHelperField(host);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceProperty_OfAValueWhichAMemberHandedBack)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Count(instruction => instruction.Operand is MethodReference {Name: "get_PublicProperty"}), Is.EqualTo(1), "the getter was not called exactly once.");
            Assert.That(ReceiverOf(ins, "get_PublicProperty"), Is.SameAs(ins.First(instruction => instruction.Operand is FieldReference {Name: "Helper"})),
                "the property is read off `this` rather than off the value which the template computed.");
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetField("Helper")!.SetValue(instance, new HelperClass {PublicProperty = 5});

        Assert.That(type.GetMethod("Run")!.Invoke(instance, null), Is.EqualTo(5));
    }

    [Test]
    public void InstanceMethod_Of_A_Value_Which_The_Template_Computed_Calls_The_Method()
    {
        var (assembly, host, method) = NewInstanceHost("InstanceComputedMethodAssembly", [typeof(int)]);
        AddHelperField(host);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAValueWhichAMemberHandedBack)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ReceiverOf(ins, "Calc", arguments: 1), Is.SameAs(ins.First(instruction => instruction.Operand is FieldReference {Name: "Helper"})),
            "the method is called on `this` rather than on the value which the template computed.");

        var type = assembly.Load().GetType($"{NS}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetField("Helper")!.SetValue(instance, new HelperClass());

        Assert.That(type.GetMethod("Run")!.Invoke(instance, [21]), Is.EqualTo(42));
    }

    [Test]
    public void InstanceMethod_Of_A_Value_Which_The_Template_Computed_Is_Handed_Back_As_A_Delegate()
    {
        // The pointer of the member is taken out of the value which the template computed where the value stands, so no
        // receiver is written where the delegate is built.
        var (assembly, host, method) = NewInstanceHost("InstanceComputedDelegateAssembly", []);
        AddHelperField(host);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAValueWhichAMemberHandedBackAsADelegate)));

        var type = assembly.Load().GetType($"{NS}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetField("Helper")!.SetValue(instance, new HelperClass());
        var calc = (InstanceStaticTemplates.IntOp) type.GetMethod("Run")!.Invoke(instance, null)!;

        Assert.That(calc(21), Is.EqualTo(42));
    }

    /// <summary>
    /// Create an assembly which holds a type of one method, which takes the arguments which the templates of
    /// <c>Instance</c> name and belongs to an instance unless it is asked not to.
    /// </summary>
    /// <param name="assemblyName">The name of the assembly, which is the identity the runtime loads it by.</param>
    /// <param name="parameters">The arguments of the member which is woven.</param>
    /// <param name="isStatic">Whether the member which is woven belongs to no instance.</param>
    private static (Assembly Assembly, TypeHandler Host, MethodHandler Method) NewInstanceHost(string assemblyName, Type[] parameters, bool isStatic = false)
    {
        var assembly = Assembly.Create(assemblyName);
        var host = AddAHost(assembly);
        var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToGneedleType(), [],
            [.. parameters.Select(type => new Parameter(type.ToGneedleType()))],
            MethodFlags.Public | (isStatic ? MethodFlags.Static : 0));

        if (isStatic) return (assembly, host, method);

        // A type which Cecil emits carries no constructor of its own, and one is needed to create an instance of it,
        // which is what the tests below do to run the member which they wove.
        AddAnInstanceConstructor(host);

        return (assembly, host, method);
    }

    /// <summary>
    /// The same host, of a static member <c>U Run&lt;U&gt;(GenericHelper&lt;int&gt; helper, U value)</c> which declares a
    /// parameter of its own, which is what a template that reaches a member declaring one as well is woven into: the
    /// delegate of such a template is written with the token of the parameter of the member, which the member of the
    /// type is matched by.<para/>
    /// The parameter is named <c>U</c> because the name is all which ties the two together: the token of the delegate
    /// stands for the parameter of the member being woven, and the member which is reached declares its own as
    /// <c>U</c>, so the names are what the lookup compares.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static (Assembly Assembly, TypeHandler Host, MethodHandler Method) NewInstanceHostOfAGenericMember(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        var host = AddAHost(assembly);
        var method = (MethodHandler) host.AddMethod(
            "Run",
            typeof(M0).ToGneedleType(),
            [new GenericParameterType("U")],
            [new Parameter(typeof(GenericHelper<int>).ToGneedleType()), new Parameter(typeof(M0).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);

        return (assembly, host, method);
    }

    /// <summary>
    /// The same host, of a body which declares a parameter of its own which no token of a template stands for: the
    /// delegate names the parameter of the member which the body declares first, and the body declares one beyond it
    /// which the member does not have.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static (Assembly Assembly, TypeHandler Host, MethodHandler Method) NewInstanceHostOfAGenericMemberOfABodyOfAGreaterArity(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        var host = AddAHost(assembly);
        var method = (MethodHandler) host.AddMethod(
            "Run",
            typeof(M0).ToGneedleType(),
            [new GenericParameterType("U"), new GenericParameterType("V")],
            [new Parameter(typeof(GenericHelper<int>).ToGneedleType()), new Parameter(typeof(M0).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);

        return (assembly, host, method);
    }

    /// <summary>
    /// Give a host a field which holds a <see cref="HelperClass"/>, which is what a template of <c>Instance</c> which
    /// computes its instance reaches one through.
    /// </summary>
    /// <param name="host">The host which the field is declared on.</param>
    private static void AddHelperField(TypeHandler host)
    {
        var fieldType = host.Source.Module.ImportReference(typeof(HelperClass));
        host.Source.Fields.Add(new FieldDefinition("Helper", FieldAttributes.Public, fieldType));
    }

    /// <summary>
    /// The instruction which was written ahead of the arguments of the one which reaches the member of the given name,
    /// which is the receiver of it.
    /// </summary>
    /// <param name="instructions">The body of the member which was woven.</param>
    /// <param name="member">The name of the member which the instruction reaches.</param>
    /// <param name="arguments">How many arguments the instruction which reaches the member reads, which stand between it and the receiver.</param>
    private static Instruction ReceiverOf(Instruction[] instructions, string member, int arguments = 0)
    {
        for (var index = 1; index < instructions.Length; index++)
        {
            if (instructions[index].Operand is MemberReference reference && reference.Name == member) return instructions[index - 1 - arguments];
        }

        Assert.Fail($"No instruction reaching '{member}' was written.");
        return null!;
    }

    [Test]
    public void InstanceField_Of_A_Parameter_Loads_The_Argument_Which_Holds_It()
    {
        // The instance which the template names is a parameter of the template, and the member being woven holds that
        // argument at a slot of its own: what stands ahead of the field access is the load of that argument. The load
        // of `this` which stood there instead is another object than the one the template named, which the runtime
        // refuses where the types of the two do not meet.
        var (_, _, method) = NewInstanceHost("InstanceFieldReceiverAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_Get)));

        Assert.That(ReceiverOf([.. method.Source.Body.Instructions], "PublicField").OpCode, Is.EqualTo(OpCodes.Ldarg_1),
            "the field is reached through `this` rather than through the instance which the template named.");
    }

    [Test]
    public void InstanceField_Of_A_Later_Parameter_Loads_That_Argument_By_Its_Slot()
    {
        // A slot which no macro opcode of the member being woven carries is written as the operand form, which names the
        // parameter rather than the slot, so what the receiver is read back off is the parameter the template named.
        var (_, _, method) = NewInstanceHost("InstanceLaterParameterAssembly", [typeof(object), typeof(object), typeof(object), typeof(object), typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_Get_OfALaterParameter)));

        var receiver = ReceiverOf([.. method.Source.Body.Instructions], "PublicField");
        Assert.Multiple(() =>
        {
            Assert.That(receiver.OpCode, Is.EqualTo(OpCodes.Ldarg));
            Assert.That(((ParameterReference) receiver.Operand).Index, Is.EqualTo(4),
                "the argument was loaded from the slot of another parameter.");
        });
    }

    [Test]
    public void InstanceProperty_Of_A_Parameter_Loads_The_Argument_Which_Holds_It()
    {
        var (_, _, method) = NewInstanceHost("InstancePropertyReceiverAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceProperty_Get)));

        Assert.That(ReceiverOf([.. method.Source.Body.Instructions], "get_PublicProperty").OpCode, Is.EqualTo(OpCodes.Ldarg_1),
            "the property is reached through `this` rather than through the instance which the template named.");
    }

    [Test]
    public void InstanceMethod_Of_A_Parameter_Loads_The_Argument_Which_Holds_It()
    {
        var (_, _, method) = NewInstanceHost("InstanceMethodReceiverAssembly", [typeof(HelperClass), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_NewSyntax)));

        Assert.That(ReceiverOf([.. method.Source.Body.Instructions], "Calc", arguments: 1).OpCode, Is.EqualTo(OpCodes.Ldarg_1),
            "the method is called on `this` rather than on the instance which the template named.");
    }

    [Test]
    public void InstanceField_Of_A_Static_Field_Is_Reached_Through_No_Receiver()
    {
        // The field which the instance of `Instance` names belongs to the type alone, so the member being woven holds no
        // receiver for it: the sequence which named the instance is dropped whole, and the load of a `this` written
        // where the member is static is a body which the runtime refuses to run.
        var (_, _, method) = NewInstanceHost("InstanceStaticFieldReceiverAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceStaticField_Get)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && field.Name == "StaticField"), Is.True,
                "the static field was not read through the type which the template named.");
            Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False,
                "a receiver was written where the member being woven holds none.");
        });
    }

    [Test]
    public void InstanceMethod_Of_A_Parameter_Reads_The_Instance_Which_Was_Given()
    {
        // What the tests above read out of the body, run: a receiver which is the load of `this` reads the member of
        // another object than the one which was given, which is what the runtime refuses.
        var (_, host, method) = NewInstanceHost("InstanceFieldReceiverRunAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_Get)));

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{NS}.Host")!;
        var helper = new HelperClass {PublicField = 21};

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [helper]), Is.EqualTo(21));
    }

    [Test]
    public void InstanceMethod_Which_Is_Handed_Back_As_A_Delegate_Is_Reached_Through_The_Instance_Which_Named_It()
    {
        // The method is not invoked where the template names it, so the instructions which named the type of it stand in
        // the body until the delegate is built from the pointer of it. The sequence which built the instance of `Instance`
        // is dropped with them, which it was not: what was left of it was an array on the stack of the woven member.
        var (_, host, method) = NewInstanceHost("InstanceDelegateReceiverAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_AsADelegate)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Newarr), Is.False,
                "the array which built the instance of `Instance` was left in the body.");
            Assert.That(ReceiverOf(ins, "Calc").OpCode, Is.EqualTo(OpCodes.Ldarg_1),
                "the pointer of the method was taken ahead of `this` rather than of the instance which the template named.");
        });
        var type = host.AssemblyHandler.Assembly.Load().GetType($"{NS}.Host")!;
        var calc = (InstanceStaticTemplates.IntOp) type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new HelperClass()])!;

        Assert.That(calc(21), Is.EqualTo(42));
    }

    [Test]
    public void InstanceMethod_Of_A_Body_Which_Holds_Many_Locals_Is_Reached_Through_The_Instance_Which_Named_It()
    {
        // The stack which the weaving carries along the body while it looks for the call of the delegate is balanced over
        // the locals of the template as well. A local beyond the third is stored and loaded in the operand form, whose
        // operand the reader of Cecil hands back as the variable itself, which is read as the slot of it rather than cast.
        var (_, host, method) = NewInstanceHost("InstanceManyLocalsAssembly", [typeof(HelperClass), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfABodyWhichHoldsManyLocals)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(instruction => instruction.Operand is MethodReference {Name: "Calc"}), Is.True,
                "the member which the template named was not called.");
            Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False,
                "the method is called on `this` rather than on the instance which the template named.");
        });
        var type = host.AssemblyHandler.Assembly.Load().GetType($"{NS}.Host")!;
        var helper = new HelperClass();

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [helper, 16]), Is.EqualTo(42),
            "the body which held the locals was not woven into the member which runs.");
    }

    [Test]
    public void InstanceMethod_Of_A_Generic_Type_Runs_The_Member()
    {
        // The instance is one of a type which declares a parameter of its own, and its member belongs to the definition
        // of that type: the call names the instantiation which the template declared, which is the type of the value
        // the member is reached through rather than a type of the body which is woven.
        var (_, host, method) = NewInstanceHost("InstanceGenericTypeAssembly", [typeof(GenericHelper<int>), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAGenericType)));

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{NS}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new GenericHelper<int>(), 21]), Is.EqualTo(42));
    }

    [Test]
    public void InstanceProperty_Of_A_Generic_Type_Runs_The_Accessor()
    {
        var (_, host, method) = NewInstanceHost("InstanceGenericPropertyAssembly", [typeof(GenericHelper<int>)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceProperty_OfAGenericType)));

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{NS}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new GenericHelper<int>()]), Is.EqualTo(42));
    }

    [Test]
    public void InstanceField_Of_A_Generic_Type_Runs_The_Read()
    {
        // The field belongs to the definition of a type which declares a parameter of its own, and the type of the field
        // is the value which is read rather than that parameter: the read names the instantiation which the template
        // declared, which is the type of the value the field is reached through rather than a type of the body woven.
        var (assembly, _, method) = NewInstanceHost("InstanceGenericFieldAssembly", [typeof(GenericHelper<int>)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_OfAGenericType)));

        var ldfld = method.Source.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldfld);
        Assert.Multiple(() =>
        {
            Assert.That(ldfld, Is.Not.Null, "the field was not read.");
            Assert.That(((FieldReference) ldfld!.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                "the field was read off the definition of the type rather than off the instantiation which was named.");
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;
        var helper = new GenericHelper<int> {PublicField = 42};

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [helper]), Is.EqualTo(42),
            "the woven assembly does not run.");
    }

    [Test]
    public void InstanceMethod_Of_A_Member_Whose_Parameter_Stands_In_A_Wrapper_Of_The_Parameter_Of_The_Type_Is_Called()
    {
        // The member belongs to the definition of a type which declares a parameter of its own, and its parameter
        // stands in a wrapper: the delegate describes an address of an `int`, and the signature of the member holds an
        // address of the parameter of the type, so the two describe one member only where the argument of the
        // instantiation is written inside the wrapper as well.
        var (assembly, _, method) = NewInstanceHost("InstanceGenericWrapperAssembly",
            [typeof(GenericHelper<int>), typeof(int)]);

        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAMemberWhoseParameterStandsInAWrapper)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                         .FirstOrDefault(reference => reference.Name == "Set");
        Assert.That(call, Is.Not.Null, "the member whose parameter stands in a wrapper was not called.");
        Assert.Multiple(() =>
        {
            Assert.That(call!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                "the member is called on the definition of the type which declares it rather than on the instantiation.");
            Assert.That(((GenericInstanceType) call.DeclaringType).GenericArguments.Single().FullName, Is.EqualTo(typeof(int).FullName),
                "the member is not called on the instantiation which the template named.");
            Assert.That(call.Parameters.Single().ParameterType, Is.InstanceOf<ByReferenceType>(),
                "the member is not called with the argument by address.");
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;
        var helper = new GenericHelper<int>();
        Assert.Multiple(() =>
        {
            Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [helper, 42]), Is.EqualTo(42),
                "the woven assembly does not run the member whose parameter stands in a wrapper.");
            Assert.That(helper.Held, Is.EqualTo(42), "the member was not handed the value which the template computed.");
        });
    }

    [Test]
    public void InstanceMethod_Of_A_Base_Of_A_Generic_Type_Which_Names_The_Parameter_And_One_Of_Its_Own_Runs_The_Member()
    {
        // The member declares a parameter of its own as well as naming the parameter of the type which declares it, and
        // the type is reached through an instance of a type which derives from an instantiation of it: the argument of
        // that instantiation reads the parameter of the type and the signature of the delegate binds the one the member
        // declares, so the member is read through the instantiation of the type which declares it rather than through
        // the type of the value which the template named.
        var (assembly, _, method) = NewInstanceHost("InstanceGenericBaseBothAssembly", [typeof(DerivedOfAGenericBase), typeof(int), typeof(string)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfABaseOfAGenericTypeWhichNamesTheParameterAndOneOfItsOwn)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                         .FirstOrDefault(reference => reference.Name == "EchoBoth");
        Assert.That(call, Is.Not.Null, "the member which the delegate describes was not called.");
        Assert.Multiple(() =>
        {
            Assert.That(call, Is.InstanceOf<GenericInstanceMethod>(),
                "the member which declares a parameter of its own was not called through an instantiation of it.");
            Assert.That(((GenericInstanceMethod) call!).GenericArguments.Single().FullName, Is.EqualTo(typeof(string).FullName),
                "the member is not called with the type which the delegate binds the parameter it declares to.");
            Assert.That(((GenericInstanceType) call!.DeclaringType).GenericArguments.Single().FullName, Is.EqualTo(typeof(int).FullName),
                "the member is not called on the instantiation which the chain of base types names.");
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new DerivedOfAGenericBase(), 41, "seed"]), Is.EqualTo("seed"),
            "the woven assembly does not run the member of the base.");
    }

    [Test]
    public void InstanceMethod_Of_A_Base_Of_A_Generic_Type_Which_Names_The_Parameter_Runs_The_Member()
    {
        // The member belongs to the definition of a base of the type which the template named the instance through, and
        // the signature of that member names the parameter which the base declares: the argument which the chain of base
        // types hands down to it is what that parameter stands for, so the member which the delegate describes is found
        // on the instantiation which the chain names and called there.
        var (assembly, _, method) = NewInstanceHost("InstanceGenericBaseSignatureAssembly", [typeof(DerivedOfAGenericBase), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfABaseOfAGenericTypeWhichNamesTheParameter)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                         .FirstOrDefault(reference => reference.Name == "Echo");
        Assert.That(call, Is.Not.Null, "the member which the delegate describes was not called.");
        Assert.Multiple(() =>
        {
            Assert.That(call!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                "the member is called on the definition of the base rather than on the instantiation of it.");
            Assert.That(((GenericInstanceType) call.DeclaringType).GenericArguments.Single().FullName, Is.EqualTo(typeof(int).FullName),
                "the member is not called on the instantiation which the chain of base types names.");
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new DerivedOfAGenericBase(), 41]), Is.EqualTo(41),
            "the woven assembly does not run the member which the signature of the base names the parameter of it with.");
    }

    [Test]
    public void InstanceMethod_Of_A_Base_Of_A_Generic_Type_Runs_The_Member()
    {
        // The type which the template named the instance through derives from an instantiation of the type which
        // declares the member: the member belongs to the definition of that base, and the call names the instantiation
        // which the type was handed where it was declared, which is the type of the value the member is reached through
        // rather than a type of the body which is woven.
        var (assembly, _, method) = NewInstanceHost("InstanceGenericBaseAssembly", [typeof(DerivedOfAGenericBase), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfABaseOfAGenericType)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().FirstOrDefault(reference => reference.Name == "Calc");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.That(call!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
            "the call names the definition of the base rather than the instantiation which the type was handed.");

        var type = assembly.Load().GetType($"{NS}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new DerivedOfAGenericBase(), 14]), Is.EqualTo(42),
            "the woven assembly does not run.");
    }

    [Test]
    public void InstanceMethod_Of_A_Base_Of_A_Base_Of_A_Generic_Type_Runs_The_Member()
    {
        // The base which declares the member is written where the type between it and the instance the template was
        // handed declares it, with a parameter of that type: the argument which reaches the base is the one the middle
        // instantiation was handed, which the walk has to carry down rather than read off the declaration it stands in.
        var (assembly, _, method) = NewInstanceHost("InstanceGenericBaseOfABaseAssembly", [typeof(DerivedOfAMiddleOfAGenericBase), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfABaseOfABaseOfAGenericType)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().FirstOrDefault(reference => reference.Name == "Calc");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.Multiple(() =>
        {
            Assert.That(call!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                "the call names the definition of the base rather than an instantiation of it.");
            Assert.That(((GenericInstanceType) call.DeclaringType).GenericArguments.Select(argument => argument.FullName),
                Is.EqualTo(new[] {method.Source.Module.TypeSystem.Int32.FullName}),
                "the call names the parameter which the base is written with rather than the argument which the chain handed down.");
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new DerivedOfAMiddleOfAGenericBase(), 14]), Is.EqualTo(42),
            "the woven assembly does not run.");
    }

    [Test]
    public void InstanceMethod_Of_A_Generic_Member_Of_A_Generic_Type_Runs_The_Member()
    {
        // The member declares a parameter of its own as well as belonging to a type which declares one, and the member
        // which is woven declares one too, which the delegate of the template stands for: the call is one of the member
        // instanced with the parameter of the body, so the reference which the call stands on has to declare the
        // parameter of the member, which the instantiation of the call is an argument of.
        var (assembly, _, method) = NewInstanceHostOfAGenericMember("InstanceGenericMemberOfAGenericTypeAssembly");
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethodOfAGenericMemberOfAGenericType)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().FirstOrDefault(reference => reference.Name == "Identity");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.That(call!.GetElementMethod().GenericParameters.Count, Is.EqualTo(1),
            "the reference which the call stands on does not declare the parameter of the member.");

        var type = assembly.Load().GetType($"{NS}.Host")!;

        Assert.That(type.GetMethod("Run")!.MakeGenericMethod(typeof(int)).Invoke(null, [new GenericHelper<int>(), 42]), Is.EqualTo(42),
            "the woven assembly does not run.");
    }

    [Test]
    public void InstanceMethod_Of_A_Generic_Member_Of_A_Body_Of_A_Greater_Arity_Runs_The_Member()
    {
        // The member which the symbol names declares a parameter of its own, and the body which is woven declares one
        // beyond it which no token of the delegate stands for: the arguments of an instantiation are the parameters of
        // the body which stand at the positions of the parameters of the member, so the call names as many of them as
        // the member declares rather than every parameter of the body, which would name the member with a parameter the
        // body declares for another purpose.
        var (assembly, _, method) = NewInstanceHostOfAGenericMemberOfABodyOfAGreaterArity("InstanceGenericMemberOfAGreaterArityAssembly");
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethodOfAGenericMemberOfABodyOfAGreaterArity)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().FirstOrDefault(reference => reference.Name == "Identity");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.Multiple(() =>
        {
            Assert.That(call, Is.InstanceOf<GenericInstanceMethod>(),
                "the call stands on the definition of the member rather than on an instantiation of it.");
            Assert.That(((GenericInstanceMethod) call!).GenericArguments.Single(), Is.SameAs(method.Source.GenericParameters[0]),
                "the call does not name the parameter of the body which the token of the delegate stands for, which is "
                + "the one it stands at the position of rather than the one beyond it.");
        });
        var type = assembly.Load().GetType($"{NS}.Host")!;

        Assert.That(type.GetMethod("Run")!.MakeGenericMethod(typeof(int), typeof(string)).Invoke(null, [new GenericHelper<int>(), 42]), Is.EqualTo(42),
            "the woven assembly does not run.");
    }
}