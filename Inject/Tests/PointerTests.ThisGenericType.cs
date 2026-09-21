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
/// Tests for a member of a generic type, woven and run, which are the tests of <see cref="PointerTests"/> for that one placeholder.
/// </summary>
public partial class PointerTests
{
    /// <summary>
    /// Create a host which is generic in one parameter, which declares <c>int Add(int a, int b)</c> and a property whose
    /// accessors read and write a field, so that a member of a generic type has one of every shape which a template
    /// reaches to be called and run.<para/>
    /// The members belong to the definition of the type, and the runtime refuses to run a call of a method of a type
    /// which stands open, which is what a test which loads the assembly sees and an assertion on the instructions alone
    /// does not.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly, which a test which runs its host gives one of its own.</param>
    private static TypeHandler NewRunnableGenericHost(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public)
                                        .WithGenericParameter("T")
                                        .GetHandler();
        var module = host.Source.Module;

        var add = new MethodDefinition("Add", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Int32) { DeclaringType = host.Source };
        add.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, module.TypeSystem.Int32));
        add.Parameters.Add(new ParameterDefinition("b", ParameterAttributes.None, module.TypeSystem.Int32));
        var addIl = add.Body.GetILProcessor();
        addIl.Emit(OpCodes.Ldarg_1); addIl.Emit(OpCodes.Ldarg_2); addIl.Emit(OpCodes.Add); addIl.Emit(OpCodes.Ret);
        host.Source.Methods.Add(add);

        var value = new FieldDefinition("m_Value", FieldAttributes.Private, module.TypeSystem.Int32);
        host.Source.Fields.Add(value);

        var accessorAttributes = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
        var getter = new MethodDefinition("get_Prop", accessorAttributes, module.TypeSystem.Int32) { DeclaringType = host.Source };
        var getterIl = getter.Body.GetILProcessor();
        getterIl.Emit(OpCodes.Ldarg_0); getterIl.Emit(OpCodes.Ldfld, value); getterIl.Emit(OpCodes.Ret);

        var setter = new MethodDefinition("set_Prop", accessorAttributes, module.TypeSystem.Void) { DeclaringType = host.Source };
        setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
        var setterIl = setter.Body.GetILProcessor();
        setterIl.Emit(OpCodes.Ldarg_0); setterIl.Emit(OpCodes.Ldarg_1); setterIl.Emit(OpCodes.Stfld, value); setterIl.Emit(OpCodes.Ret);

        host.Source.Methods.Add(getter);
        host.Source.Methods.Add(setter);
        host.Source.Properties.Add(new PropertyDefinition("Prop", PropertyAttributes.None, module.TypeSystem.Int32) { GetMethod = getter, SetMethod = setter });
        return host;
    }

    [Test]
    public void A_Call_Of_A_Member_Of_A_Generic_Type_Runs_The_Member()
    {
        var host = NewRunnableGenericHost("GenericMemberCallAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeInstanceMethod)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [3, 4]), Is.EqualTo(7));
    }

    [Test]
    public void A_Call_Whose_Argument_Holds_A_Conditional_Runs_The_Member()
    {
        // The argument of the call is computed along a branch, and the value which the symbol left stands under both of
        // the paths which the branch leaves for: the instructions between the two are walked as the graph which they
        // are rather than in a row, so the invocation which every path reaches with the two arguments of the delegate is
        // the one which the symbol stands for, and the call of the member is written in its place.
        var host = NewRunnableGenericHost("GenericMemberConditionalArgumentAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(bool).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithAConditionalArgument)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "Add"), Is.True,
                    "the member is not called where the delegate was invoked.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False,
                    "the member was built into a delegate rather than called.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [3, 4, false]), Is.EqualTo(7),
                    "the woven assembly does not run the member which the symbol stands for.");
    }

    [Test]
    public void A_Member_Which_A_Template_Invokes_Inside_A_Protected_Region_Is_Called_There()
    {
        // The symbol stands inside a region which the template protects, and the walk of the paths of the body reads
        // the region as well: the runtime hands the control to the beginning of the handler as well as to the beginning
        // of the body, and every path which reaches the invocation of the delegate passes through the symbol either
        // way, so the call of the member stands where the delegate was invoked rather than a delegate being built.
        var host = NewRunnableGenericHost("GenericMemberInsideAProtectedRegionAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberInsideAProtectedRegion)));

        var body = ((MethodHandler) method).Source.Body;
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "Add"), Is.True,
                    "the member is not called where the delegate was invoked.");
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldftn), Is.False,
                    "the member was built into a delegate rather than called.");
        Assert.That(body.ExceptionHandlers, Is.Not.Empty, "the region which the template protects was not carried.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [3, 4]), Is.EqualTo(7),
                    "the woven assembly does not run the member which the symbol stands for.");
    }

    [Test]
    public void A_Member_Which_A_Template_Invokes_In_The_Handler_Of_A_Region_Is_Called_There()
    {
        // The symbol stands in the handler rather than in the region, so the beginning of the handler is a place the
        // runtime hands the control to rather than one which a path of the body reaches: the path which begins there
        // passes through the symbol, so the call of the member stands where the delegate was invoked.
        var host = NewRunnableGenericHost("GenericMemberInTheHandlerAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberInTheHandler)));

        var body = ((MethodHandler) method).Source.Body;
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "Add"), Is.True,
                    "the member is not called where the delegate was invoked.");
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldftn), Is.False,
                    "the member was built into a delegate rather than called.");
        Assert.That(body.ExceptionHandlers, Is.Not.Empty, "the region which the template protects was not carried.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [3, 4]), Is.EqualTo(7),
                    "the woven assembly does not run the member which the symbol stands for.");
    }

    [Test]
    public void A_Held_Delegate_Which_A_Condition_Names_The_Member_Of_Runs_The_Member_Of_The_Arm_Which_Ran()
    {
        // The value which the local holds is the delegate which one of the two arms built, and the invocation which
        // stands after the join is made on it. The store of the local is reached by the path of either arm, so the value
        // it writes is not the one which either symbol left, and the delegate which each arm built is what the local
        // holds: the invocation is left as the invocation of that delegate, which runs the member of the arm which ran.
        var host = NewRunnableGenericHost("GenericMemberConditionalNameAssembly");
        AddASubtract(host);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(bool).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhichAConditionNames)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));
        var instance = Activator.CreateInstance(type);

        Assert.That(type.GetMethod("Run")!.Invoke(instance, [3, 4, true]), Is.EqualTo(7),
                    "the woven assembly does not run the member which the arm which ran names.");
        Assert.That(type.GetMethod("Run")!.Invoke(instance, [3, 4, false]), Is.EqualTo(-1),
                    "the woven assembly does not run the member which the arm which ran names.");
    }

    [Test]
    public void A_Member_Which_A_Condition_Names_Where_It_Stands_Runs_The_Member_Of_The_Arm_Which_Ran()
    {
        // The same of a symbol which stands where it is invoked, which both arms reach as well: the value which the
        // invocation is made on is the one which the arm which ran left, so neither symbol stands for it, and the
        // delegates which the two arms build are what it invokes.
        var host = NewRunnableGenericHost("GenericMemberConditionalNameWhereItStandsAssembly");
        AddASubtract(host);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(bool).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhichAConditionNamesWhereItStands)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));
        var instance = Activator.CreateInstance(type);

        Assert.That(type.GetMethod("Run")!.Invoke(instance, [3, 4, true]), Is.EqualTo(7),
                    "the woven assembly does not run the member which the arm which ran names.");
        Assert.That(type.GetMethod("Run")!.Invoke(instance, [3, 4, false]), Is.EqualTo(-1),
                    "the woven assembly does not run the member which the arm which ran names.");
    }

    /// <summary>
    /// Declare <c>int Subtract(int a, int b)</c> on the host which the conditional-name tests weave into, so that a
    /// template which names a member on each arm of a branch names two members of the same signature.
    /// </summary>
    /// <param name="host">The host which the member is declared on.</param>
    private static void AddASubtract(TypeHandler host)
    {
        var module = host.Source.Module;
        var subtract = new MethodDefinition("Subtract", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Int32) { DeclaringType = host.Source };
        subtract.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, module.TypeSystem.Int32));
        subtract.Parameters.Add(new ParameterDefinition("b", ParameterAttributes.None, module.TypeSystem.Int32));
        var il = subtract.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(subtract);
    }

    [Test]
    public void A_Delegate_Of_A_Member_Of_A_Generic_Type_Runs_The_Member()
    {
        var host = NewRunnableGenericHost("GenericMemberDelegateAssembly");
        var method = host.AddMethod("Run", typeof(ThisMethodTemplates.IntBinaryOp).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.GetInstanceMethodDelegate)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));
        var built = (Delegate) type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), null)!;

        Assert.That(built.DynamicInvoke(3, 4), Is.EqualTo(7));
    }

    [Test]
    public void A_Property_Of_A_Generic_Type_Reads_And_Writes_It()
    {
        var host = NewRunnableGenericHost("GenericMemberPropertyAssembly");
        var read = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        read.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceProperty)));

        var call = ((MethodHandler) read).Source.Body.Instructions.First(instruction => instruction.Operand is MethodReference { Name: "get_Prop" });
        Assert.That(((MethodReference) call.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                    "the accessor is called on the definition of the generic type rather than on the instantiation of it.");

        var write = host.AddMethod("Write", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        write.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteInstanceProperty)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));
        var instance = Activator.CreateInstance(type);
        type.GetMethod("Write")!.Invoke(instance, [41]);

        Assert.That(type.GetMethod("Read")!.Invoke(instance, null), Is.EqualTo(41));
    }

    [Test]
    public void A_Member_Of_A_Generic_Base_Type_Runs_On_The_Type_Which_Derives_From_It()
    {
        var asm = Assembly.Create("GenericBaseMemberAssembly");
        var mod = asm.Source.MainModule;
        var baseDef = new TypeDefinition(Ns, "BaseType", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        baseDef.GenericParameters.Add(new GenericParameter("T", baseDef));
        var calc = new MethodDefinition("Calc", MethodAttributes.Public | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
        calc.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, mod.TypeSystem.Int32));
        var calcIl = calc.Body.GetILProcessor();
        calcIl.Emit(OpCodes.Ldarg_1); calcIl.Emit(OpCodes.Ret);
        baseDef.Methods.Add(calc);
        mod.Types.Add(baseDef);

        var host = (TypeHandler) ((AssemblyHandler) asm.Handler).AddClass("Host", Ns, ClassFlags.Public)
                                                                .WithGenericParameter("T")
                                                                .GetHandler();
        // The host hands the parameter which it declares itself down to its base, which is what the body of a member of
        // it names where it reaches the base: the parameter of the base stands for the parameter of the host.
        var baseInstance = new GenericInstanceType(baseDef);
        baseInstance.GenericArguments.Add(host.Source.GenericParameters[0]);
        host.Source.BaseType = baseInstance;

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseMethod)));

        var type = LoadHostOf(asm, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41));
    }

    /// <summary>
    /// Create a host which derives from a type which declares a parameter of its own and derives from a generic type in
    /// turn, so that a member which a template reaches belongs to a base of a base of the type being woven.<para/>
    /// The member belongs to a declaration which stands two steps away from the body being woven, and the instantiation
    /// which the body can name for it is the one the chain of base types names rather than one which stands where the
    /// member is reached.
    /// </summary>
    /// <param name="assemblyName">The name of the assembly to build, which a test which runs its host gives one of its own.</param>
    /// <param name="addBaseMembers">Adds the members which a template reaches to the generic base type.</param>
    /// <param name="theHostDeclaresTheParameter">
    /// Whether the host declares the parameter which the chain hands down itself rather than naming a type where it hands
    /// one over, so that the instantiation of the base of the base holds the parameter of the body which reaches it.
    /// </param>
    private static TypeHandler NewHostWhichDerivesFromAGenericBaseOfAGenericBase(string assemblyName, Action<TypeDefinition, ModuleDefinition> addBaseMembers, bool theHostDeclaresTheParameter = false)
    {
        var asm = Assembly.Create(assemblyName);
        var mod = asm.Source.MainModule;
        var baseDef = new TypeDefinition(Ns, "BaseType", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        baseDef.GenericParameters.Add(new GenericParameter("T", baseDef));
        addBaseMembers(baseDef, mod);
        mod.Types.Add(baseDef);

        var middleDef = new TypeDefinition(Ns, "MiddleType", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        var middleParameter = new GenericParameter("T", middleDef);
        middleDef.GenericParameters.Add(middleParameter);
        var baseOfTheMiddle = new GenericInstanceType(baseDef);
        baseOfTheMiddle.GenericArguments.Add(middleParameter);
        middleDef.BaseType = baseOfTheMiddle;
        mod.Types.Add(middleDef);

        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) (theHostDeclaresTheParameter
            ? handler.AddClass("Host", Ns, ClassFlags.Public).WithGenericParameter("T").GetHandler()
            : handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler());
        // The middle type hands the argument over to its own base, so the base of the base of the host is reached through
        // an instantiation which is written where the middle type is declared rather than where the host is.
        var middleOfTheHost = new GenericInstanceType(middleDef);
        middleOfTheHost.GenericArguments.Add(theHostDeclaresTheParameter ? host.Source.GenericParameters[0] : mod.TypeSystem.Int32);
        host.Source.BaseType = middleOfTheHost;
        return host;
    }

    /// <summary>
    /// Add the method which the templates of the tests below call to the generic base type.
    /// </summary>
    /// <summary>
    /// Add the member <c>T Echo(T value)</c> to the type which the chain of base types ends at, which names the
    /// parameter that the type declares rather than a type.
    /// </summary>
    /// <param name="baseDef">The definition of the base type which the host derives from through a middle type.</param>
    /// <param name="mod">The module which the type belongs to.</param>
    private static void AddEchoToTheGenericBase(TypeDefinition baseDef, ModuleDefinition mod)
    {
        var parameter = baseDef.GenericParameters[0];
        var echo = new MethodDefinition("Echo", MethodAttributes.Public | MethodAttributes.HideBySig, parameter) { DeclaringType = baseDef };
        echo.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, parameter));
        echo.Body.GetILProcessor().Emit(OpCodes.Ldarg_1);
        echo.Body.GetILProcessor().Emit(OpCodes.Ret);
        baseDef.Methods.Add(echo);
    }

    private static void AddCalcToTheGenericBase(TypeDefinition baseDef, ModuleDefinition mod)
    {
        var calc = new MethodDefinition("Calc", MethodAttributes.Public | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
        calc.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, mod.TypeSystem.Int32));
        var calcIl = calc.Body.GetILProcessor();
        calcIl.Emit(OpCodes.Ldarg_1); calcIl.Emit(OpCodes.Ret);
        baseDef.Methods.Add(calc);
    }

    [Test]
    public void A_Member_Of_A_Generic_Base_Of_A_Generic_Base_Is_Called_On_The_Instantiation_Which_The_Chain_Names()
    {
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddleMemberAssembly", AddCalcToTheGenericBase);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseMethod)));

        var call = ((MethodHandler) method).Source.Body.Instructions
                                           .First(instruction => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                                                                 && ((MethodReference) instruction.Operand).Name == "Calc");
        var declaring = ((MethodReference) call.Operand).DeclaringType;

        Assert.That(declaring, Is.InstanceOf<GenericInstanceType>(),
                    "the member is called on the definition of the type which declares it, which stands open where the chain of base types names an instantiation of it.");
        Assert.That(((GenericInstanceType) declaring).GenericArguments.Select(argument => argument.FullName), Is.EqualTo(new[] { "System.Int32" }),
                    "the instantiation which the call names is not the one which the chain of base types hands down.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41));
    }

    [Test]
    public void A_Member_Of_A_Generic_Base_Of_A_Generic_Base_Of_An_Open_Type_Is_Called_On_The_Instantiation_Which_Names_The_Parameter()
    {
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddleOfAnOpenTypeAssembly", AddCalcToTheGenericBase, theHostDeclaresTheParameter: true);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseMethod)));

        var call = ((MethodHandler) method).Source.Body.Instructions
                                           .First(instruction => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                                                                 && ((MethodReference) instruction.Operand).Name == "Calc");
        var declaring = ((MethodReference) call.Operand).DeclaringType;

        Assert.That(declaring, Is.InstanceOf<GenericInstanceType>(),
                    "the member is called on the definition of the type which declares it, which stands open where the chain of base types hands the parameter of the body down to it.");
        Assert.That(((GenericInstanceType) declaring).GenericArguments.Single(), Is.SameAs(host.Source.GenericParameters[0]),
                    "the instantiation which the call names does not stand for the parameter of the type which is woven, "
                    + "which is the one the chain of base types hands down to the definition which declares the member.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41));
    }

    [Test]
    public void A_Member_Of_A_Generic_Base_Which_Names_The_Parameter_Of_It_Is_Called_Through_This()
    {
        // The body belongs to a type which derives from an instantiation of the type which declares the member, and the
        // member is reached through `This`: the walk of the chain of base types reads the member through the
        // instantiation which the chain names, which is what the argument of the instantiation is, so the member which
        // the delegate describes is found rather than refused.
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddleSignatureAssembly", AddEchoToTheGenericBase);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.ThisMethodOfABaseWhichNamesTheParameter)));

        var call = ((MethodHandler) method).Source.Body.Instructions
                                           .Select(instruction => instruction.Operand).OfType<MethodReference>()
                                           .FirstOrDefault(reference => reference.Name == "Echo");
        Assert.That(call, Is.Not.Null, "the member which the delegate describes was not called.");
        Assert.That(((GenericInstanceType) call!.DeclaringType).GenericArguments.Single().FullName, Is.EqualTo(typeof(int).FullName),
                    "the member is not called on the instantiation which the chain of base types names.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41),
                    "the woven assembly does not run the member of the base.");
    }

    [Test]
    public void A_Member_Of_A_Base_Which_Is_An_Instantiation_Is_Called_Through_Base()
    {
        // The base which the body's own type derives from is an instantiation of a type which declares a parameter of
        // its own, and the member which the delegate describes names that parameter: the member belongs to the
        // definition of the base, and the walk which reaches it through `Base` is handed the instantiation which the
        // type being woven derives from, so the member is found and called on that instantiation rather than refused.
        var host = NewHostWhichDerivesFromAnInstantiationOfAGenericType("BaseSignatureOfAnInstantiationAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseMethodOfABaseWhichNamesTheParameter)));

        var call = ((MethodHandler) method).Source.Body.Instructions
                                           .Select(instruction => instruction.Operand).OfType<MethodReference>()
                                           .FirstOrDefault(reference => reference.Name == "Echo");
        Assert.That(call, Is.Not.Null, "the member which the delegate describes was not called.");
        Assert.That(call!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                    "the member is called on the definition of the base rather than on the instantiation which the body's own type derives from.");
        Assert.That(((GenericInstanceType) call.DeclaringType).GenericArguments.Single().FullName, Is.EqualTo(typeof(int).FullName),
                    "the member is not called on the instantiation which the base was declared with.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41),
                    "the woven assembly does not run the member of the base which names the parameter of it.");
    }

    [Test]
    public void A_Default_Body_Which_Calls_The_Base_Calls_The_Member_On_The_Instantiation()
    {
        // The body of a member which is set to call the member of the base that carries its name and parameters is
        // written where the type which derives is declared, and the base of this type is an instantiation: the member
        // of the base which takes the parameter of it is the one which takes an `int`, so the member is found and the
        // call names that instantiation rather than the definition of the base, which stands open.
        var host = NewHostWhichDerivesFromAnInstantiationOfAGenericType("BaseCallOfAnInstantiationAssembly");
        var method = host.AddMethod("Echo", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.CallFromBase);

        var call = ((MethodHandler) method).Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                                           .FirstOrDefault(reference => reference.Name == "Echo");
        Assert.That(call, Is.Not.Null, "the member of the base was not called.");
        Assert.That(call!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                    "the member is called on the definition of the base rather than on the instantiation which the type derives from.");
        Assert.That(((GenericInstanceType) call.DeclaringType).GenericArguments.Single().FullName, Is.EqualTo(typeof(int).FullName),
                    "the member is not called on the instantiation which the base was declared with.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);

        Assert.That(type.GetMethod("Echo")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41),
                    "the woven assembly does not run the member of the base which the body calls.");
    }

    [Test]
    public void A_Member_Of_A_Generic_Base_Of_A_Generic_Base_Is_Called_On_The_Instantiation_Through_This()
    {
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddleThisMemberAssembly", AddCalcToTheGenericBase);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.ThisMethodOfABaseOfABase)));

        var call = ((MethodHandler) method).Source.Body.Instructions
                                           .First(instruction => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                                                                 && ((MethodReference) instruction.Operand).Name == "Calc");
        var declaring = ((MethodReference) call.Operand).DeclaringType;

        Assert.That(declaring, Is.InstanceOf<GenericInstanceType>(),
                    "the member is called on the definition of the type which declares it, which stands open where the chain of base types names an instantiation of it.");
        Assert.That(((GenericInstanceType) declaring).GenericArguments.Select(argument => argument.FullName), Is.EqualTo(new[] { "System.Int32" }),
                    "the instantiation which the call names is not the one which the chain of base types hands down.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41));
    }

    [Test]
    public void A_Field_Of_A_Generic_Base_Of_A_Generic_Base_Is_Read_Off_The_Instantiation_Which_The_Chain_Names()
    {
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddleFieldAssembly",
                                                                    (baseDef, mod) => baseDef.Fields.Add(new FieldDefinition("Value", FieldAttributes.Public, mod.TypeSystem.Int32)));
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseFieldGet)));

        var read = ((MethodHandler) method).Source.Body.Instructions.First(instruction => instruction.OpCode == OpCodes.Ldfld);
        var declaring = ((FieldReference) read.Operand).DeclaringType;

        Assert.That(declaring, Is.InstanceOf<GenericInstanceType>(),
                    "the field is read off the definition of the type which declares it, which stands open where the chain of base types names an instantiation of it.");
        Assert.That(((GenericInstanceType) declaring).GenericArguments.Select(argument => argument.FullName), Is.EqualTo(new[] { "System.Int32" }),
                    "the instantiation which the read names is not the one which the chain of base types hands down.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), null), Is.EqualTo(0));
    }

    [Test]
    public void A_Property_Of_A_Generic_Base_Of_A_Generic_Base_Is_Read_Off_The_Instantiation_Which_The_Chain_Names()
    {
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddlePropertyAssembly", (baseDef, mod) =>
        {
            var getter = new MethodDefinition("get_Prop", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
            var getterIl = getter.Body.GetILProcessor();
            getterIl.Emit(OpCodes.Ldc_I4_1); getterIl.Emit(OpCodes.Ret);
            baseDef.Methods.Add(getter);
            baseDef.Properties.Add(new PropertyDefinition("Prop", PropertyAttributes.None, mod.TypeSystem.Int32) { GetMethod = getter });
        });

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BasePropertyGet)));

        var call = ((MethodHandler) method).Source.Body.Instructions
                                           .First(instruction => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                                                                 && ((MethodReference) instruction.Operand).Name == "get_Prop");
        var declaring = ((MethodReference) call.Operand).DeclaringType;

        Assert.That(declaring, Is.InstanceOf<GenericInstanceType>(),
                    "the accessor is called on the definition of the type which declares it, which stands open where the chain of base types names an instantiation of it.");
        Assert.That(((GenericInstanceType) declaring).GenericArguments.Select(argument => argument.FullName), Is.EqualTo(new[] { "System.Int32" }),
                    "the instantiation which the call names is not the one which the chain of base types hands down.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), null), Is.EqualTo(1));
    }
}
