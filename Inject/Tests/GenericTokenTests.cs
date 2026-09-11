using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Gneedle.Inject.Test;

/// <summary>
/// Tests for the <c>Gneedle.Inject.T_[0-20]</c> / <c>Gneedle.Inject.M_[0-20]</c> tokens.
/// <para/>
/// The tokens are parsed by <c>CecilExtensions.TryGetParsedGenericParameter</c>, and consumed by
/// <see cref="MethodHandler.ParseReturnType(IType)"/> (rewrites the return type of the injected method)
/// and <c>MethodHandler.CopyVariables</c> (rewrites the local variable types of the injected method).
/// They are parsed as well when a type is declared through the public API, by
/// <c>AssemblyHandler.ResolveParameterType</c>, which covers the parameters and the return type of a method,
/// the field and property types, the base type, the interfaces and the generic constraints.
/// <para/>
/// <c>T_X</c> stands for the X-th generic parameter of the method's declaring type,
/// <c>M_X</c> stands for the X-th generic parameter of the method itself.
/// </summary>
/// <summary>
/// The constraint of the first generic parameter which the token receiver tests are looked up on.
/// It is a top level type, so that it could be resolved by its name from the assembly.
/// </summary>
public class NamedHelperBase
{
    public string Name() => "Gneedle";
}

/// <summary>
/// The constraint of the second generic parameter which the token receiver tests are looked up on.
/// It holds the same method as <see cref="NamedHelperBase"/>, so that the looked up one could tell which constraint was used.
/// </summary>
public class NamedSecondHelperBase
{
    public string Name() => "Gneedle";
}

[TestFixture]
public class GenericTokenTests
{
    private const string Ns = "Gneedle.Test.Generated";

    /// <summary>
    /// Template bodies live in the test assembly so Cecil can resolve them from disk.
    /// </summary>
    public static class Templates
    {
        // Return type templates.
        public static T_0 ReturnFirstTypeGeneric() => null!;
        public static T_1 ReturnSecondTypeGeneric() => null!;
        public static T_10 ReturnTenthTypeGeneric() => null!;
        public static T_11 ReturnEleventhTypeGeneric() => null!;
        public static M_0 ReturnFirstMethodGeneric() => null!;
        public static M_10 ReturnTenthMethodGeneric() => null!;
        public static M_1 ReturnSecondMethodGeneric() => null!;

        // Local variable templates. The local is kept alive with GC.KeepAlive so that the
        // compiler cannot fold it away and the stloc/ldloc instructions survive.
        public static bool LocalFirstTypeGeneric()
        {
            T_0 local = null!;
            GC.KeepAlive(local);
            return true;
        }

        public static bool LocalSecondTypeGeneric()
        {
            T_1 local = null!;
            GC.KeepAlive(local);
            return true;
        }

        public static bool LocalFirstMethodGeneric()
        {
            M_0 local = null!;
            GC.KeepAlive(local);
            return true;
        }

        // A return type and a local whose type hold the token as a generic argument.
        public static List<T_0> ReturnGenericInstance() => null!;

        public static bool LocalOfGenericInstance()
        {
            List<T_0> local = new();
            GC.KeepAlive(local);
            return true;
        }

        // A template with real control flow: two branches with targets inside the body.
        public static bool BranchingTemplate()
        {
            var flag = Environment.TickCount > 0;
            if (flag) return true;
            return false;
        }

        // The first four locals are addressed by macro opcodes (ldloc.0 ... ldloc.3) which carry no operand,
        // so a token typed local has to sit beyond index 3 to be addressed by an operand carrying instruction.
        public static bool LocalFifthTypeGeneric()
        {
            var first = 0;
            var second = 0;
            var third = 0;
            var fourth = 0;
            T_0 fifth = null!;
            GC.KeepAlive(first);
            GC.KeepAlive(second);
            GC.KeepAlive(third);
            GC.KeepAlive(fourth);
            GC.KeepAlive(fifth);
            return true;
        }

        // The Object.Method templates whose receiver is typed by the token. The receiver is wrapped by
        // `new Object(instance)`, which is the only way to place it before the `ldstr {method_name}`.
        public delegate string NameGetter();

        public static string ObjectMethod_TokenReceiver(T_0 instance)
            => new Object(instance).Method<NameGetter>("Name")();

        public static string ObjectMethod_SecondTokenReceiver(T_1 instance)
            => new Object(instance).Method<NameGetter>("Name")();
    }

    private static TypeHandler NewHost(params string[] genericParameterNames)
    {
        var handler = (AssemblyHandler) Assembly.Create("GenericTokenAssembly").Handler;
        ClassDecorator.IGenericParametersDecorator decorator = handler.AddClass("Host", Ns, ClassFlags.Public);
        foreach (var name in genericParameterNames) decorator = decorator.WithGenericParameter(name);
        return (TypeHandler) decorator.GetHandler();
    }

    private static System.Reflection.MethodInfo Template(string name)
        => typeof(Templates).GetMethod(name)!;

    /// <summary>
    /// Add a public method to <c>host</c>, and set its body from the template.
    /// </summary>
    private static MethodHandler AddMethod(TypeHandler host, string methodName, IType returnType, GenericParameterType[] genericParameters, string template)
    {
        var method = (MethodHandler) host.AddMethod(methodName, returnType, genericParameters, [], MethodFlags.Public);
        method.SetBody(Template(template));
        return method;
    }

    #region T_X: the generic parameter of the declaring type

    [Test]
    public void ParseReturnType_Maps_T0_To_First_Type_Generic_Parameter()
    {
        var host = NewHost("T0");
        var method = AddMethod(host, "Get", typeof(void).ToGneedleType(), [], nameof(Templates.ReturnFirstTypeGeneric));

        Assert.That(method.Source.ReturnType, Is.SameAs(host.Source.GenericParameters[0]));
    }

    [Test]
    public void ParseReturnType_Maps_T1_To_Second_Type_Generic_Parameter()
    {
        var host = NewHost("T0", "T1");
        var method = AddMethod(host, "Get", typeof(void).ToGneedleType(), [], nameof(Templates.ReturnSecondTypeGeneric));

        Assert.That(method.Source.ReturnType, Is.SameAs(host.Source.GenericParameters[1]));
    }

    [Test]
    public void ParseReturnType_Maps_T10_To_Tenth_Type_Generic_Parameter()
    {
        // Guards the tens of the token pattern: the alternation has to cover '10', which is the one index of the two
        // tens which does not share its spelling with the single digit tokens.
        var host = NewHost("T0", "T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8", "T9", "T10");
        var method = AddMethod(host, "Get", typeof(void).ToGneedleType(), [], nameof(Templates.ReturnTenthTypeGeneric));

        Assert.That(method.Source.ReturnType, Is.SameAs(host.Source.GenericParameters[10]));
        Assert.That(method.Source.ReturnType.Name, Is.EqualTo("T10"));
    }

    [Test]
    public void ParseReturnType_With_T10_Token_But_Single_Type_Generic_Parameter_Throws()
    {
        // The token has to be parsed rather than taken as a type of its own, which is what throwing proves: an
        // unparsed token would be imported as an ordinary type and set as the return type without any complaint.
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        var token = host.Source.Module.ImportReference(typeof(T_10));

        Assert.Throws<IndexOutOfRangeException>(() => method.ParseReturnType(token));
    }

    [Test]
    public void ParseReturnType_Maps_T11_To_Eleventh_Type_Generic_Parameter()
    {
        // Guards the multi-digit alternation of the token pattern: 'T_11' must not be parsed as 'T_1'.
        var host = NewHost("T0", "T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8", "T9", "T10", "T11");
        var method = AddMethod(host, "Get", typeof(void).ToGneedleType(), [], nameof(Templates.ReturnEleventhTypeGeneric));

        Assert.That(method.Source.ReturnType, Is.SameAs(host.Source.GenericParameters[11]));
        Assert.That(method.Source.ReturnType.Name, Is.EqualTo("T11"));
    }

    [Test]
    public void ParseReturnType_With_T1_Token_But_Single_Type_Generic_Parameter_Throws()
    {
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        var token = host.Source.Module.ImportReference(typeof(T_1));

        Assert.Throws<IndexOutOfRangeException>(() => method.ParseReturnType(token));
    }

    [Test]
    public void ParseReturnType_With_T20_Token_But_Single_Type_Generic_Parameter_Throws()
    {
        // Guards the upper bound of the token pattern.
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        var token = host.Source.Module.ImportReference(typeof(T_20));

        Assert.Throws<IndexOutOfRangeException>(() => method.ParseReturnType(token));
    }

    #endregion

    #region M_X: the generic parameter of the method itself

    [Test]
    public void ParseReturnType_Maps_M0_To_First_Method_Generic_Parameter()
    {
        var host = NewHost();
        var method = AddMethod(host, "Get", typeof(void).ToGneedleType(), [new GenericParameterType("U")], nameof(Templates.ReturnFirstMethodGeneric));

        Assert.That(method.Source.ReturnType, Is.SameAs(method.Source.GenericParameters[0]));
        Assert.That(method.Source.ReturnType.Name, Is.EqualTo("U"));
    }

    [Test]
    public void ParseReturnType_Maps_M1_To_Second_Method_Generic_Parameter()
    {
        var host = NewHost();
        var method = AddMethod(host, "Get", typeof(void).ToGneedleType(), [new GenericParameterType("V"), new GenericParameterType("U")],
                               nameof(Templates.ReturnSecondMethodGeneric));

        Assert.That(method.Source.ReturnType, Is.SameAs(method.Source.GenericParameters[1]));
        Assert.That(method.Source.ReturnType.Name, Is.EqualTo("U"));
    }

    [Test]
    public void ParseReturnType_With_M0_Token_But_NonGeneric_Method_Throws()
    {
        var host = NewHost();
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        var token = host.Source.Module.ImportReference(typeof(M_0));

        Assert.Throws<IndexOutOfRangeException>(() => method.ParseReturnType(token));
    }

    [Test]
    public void ParseReturnType_Maps_M10_To_Tenth_Method_Generic_Parameter()
    {
        // The method pattern is an expression of its own rather than the type one, so the tens are guarded here as well.
        var host = NewHost();
        var genericParameters = Enumerable.Range(0, 11).Select(index => new GenericParameterType($"U{index}")).ToArray();
        var method = AddMethod(host, "Get", typeof(void).ToGneedleType(), genericParameters, nameof(Templates.ReturnTenthMethodGeneric));

        Assert.That(method.Source.ReturnType, Is.SameAs(method.Source.GenericParameters[10]));
        Assert.That(method.Source.ReturnType.Name, Is.EqualTo("U10"));
    }

    [Test]
    public void ParseReturnType_With_M10_Token_But_Single_Method_Generic_Parameter_Throws()
    {
        // An unparsed token would be imported as an ordinary type and set as the return type without any complaint.
        var host = NewHost();
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [new GenericParameterType("U")], [], MethodFlags.Public);
        var token = host.Source.Module.ImportReference(typeof(M_10));

        Assert.Throws<IndexOutOfRangeException>(() => method.ParseReturnType(token));
    }

    #endregion

    #region Non token types

    [Test]
    public void ParseReturnType_With_NonToken_Type_Sets_ReturnType_To_It()
    {
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Get", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        var target = host.Source.Module.ImportReference(typeof(string));

        method.ParseReturnType(target);

        // The return type of the injected method follows the template, no matter it holds a token or not.
        Assert.That(method.Source.ReturnType.FullName, Is.EqualTo(typeof(string).FullName));
    }

    [Test]
    public void SetBody_With_Unresolvable_Token_Throws()
    {
        // A host without generic parameters cannot hold a 'T_1' token in its return type.
        var host = NewHost();
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);

        Assert.Throws<IndexOutOfRangeException>(() => method.SetBody(Template(nameof(Templates.ReturnSecondTypeGeneric))));
    }

    #endregion

    #region Local variables

    [Test]
    public void CopyVariables_Maps_T0_Local_To_First_Type_Generic_Parameter()
    {
        var host = NewHost("T0");
        var method = AddMethod(host, "Get", typeof(bool).ToGneedleType(), [], nameof(Templates.LocalFirstTypeGeneric));

        Assert.That(method.Source.Body.Variables, Is.Not.Empty);
        Assert.That(method.Source.Body.Variables[0].VariableType, Is.SameAs(host.Source.GenericParameters[0]));
    }

    [Test]
    public void CopyVariables_Maps_T1_Local_To_Second_Type_Generic_Parameter()
    {
        var host = NewHost("T0", "T1");
        var method = AddMethod(host, "Get", typeof(bool).ToGneedleType(), [], nameof(Templates.LocalSecondTypeGeneric));

        Assert.That(method.Source.Body.Variables, Is.Not.Empty);
        Assert.That(method.Source.Body.Variables[0].VariableType, Is.SameAs(host.Source.GenericParameters[1]));
    }

    [Test]
    public void CopyVariables_Maps_M0_Local_To_First_Method_Generic_Parameter()
    {
        var host = NewHost();
        var method = AddMethod(host, "Get", typeof(bool).ToGneedleType(), [new GenericParameterType("U")], nameof(Templates.LocalFirstMethodGeneric));

        Assert.That(method.Source.Body.Variables, Is.Not.Empty);
        Assert.That(method.Source.Body.Variables[0].VariableType, Is.SameAs(method.Source.GenericParameters[0]));
    }

    [Test]
    public void CopyVariables_Keeps_Macro_Local_Instructions_Of_First_Variables()
    {
        // ldloc.0/stloc.0 carry no operand, so they address the source variables by index only.
        // CopyVariables must therefore copy the variables without changing their order.
        var host = NewHost("T0");
        var method = AddMethod(host, "Get", typeof(bool).ToGneedleType(), [], nameof(Templates.LocalFirstTypeGeneric));

        var codes = method.Source.Body.Instructions.Select(i => i.OpCode.Code).ToArray();
        Assert.That(codes, Does.Contain(Code.Stloc_0));
        Assert.That(codes, Does.Contain(Code.Ldloc_0));
    }

    [Test]
    public void CopyVariables_Remaps_Local_Instructions_To_Remapped_Variables()
    {
        var host = NewHost("T0");
        var method = AddMethod(host, "Get", typeof(bool).ToGneedleType(), [], nameof(Templates.LocalFifthTypeGeneric));

        // The fifth local is the token typed one, and is addressed by ldloc.s/stloc.s with an operand.
        Assert.That(method.Source.Body.Variables[4].VariableType, Is.SameAs(host.Source.GenericParameters[0]));
        Assert.That(method.Source.Body.Instructions.Any(i => ReferenceEquals(i.Operand, method.Source.Body.Variables[4])), Is.True);

        // No instruction may still refer to a variable of the template body.
        var sourceVariables = method.Source.Body.Variables.Cast<object>().ToArray();
        var localInstructions = method.Source.Body.Instructions.Where(i => i.Operand is VariableDefinition).ToArray();
        Assert.That(localInstructions, Is.Not.Empty);
        Assert.That(localInstructions.All(i => sourceVariables.Any(v => ReferenceEquals(v, i.Operand))), Is.True);
    }

    [Test]
    public void SetBody_Remaps_Branch_Targets_To_Source_Instructions()
    {
        var host = NewHost();
        var method = AddMethod(host, "Check", typeof(bool).ToGneedleType(), [], nameof(Templates.BranchingTemplate));

        var body = method.Source.Body;
        var branches = body.Instructions.Where(i => i.Operand is Instruction).ToArray();
        Assert.That(branches, Is.Not.Empty, "The template must contain at least one branch.");
        Assert.That(branches.All(i => body.Instructions.Contains((Instruction) i.Operand)), Is.True,
                    "Every branch target must be an instruction of the injected body.");
    }

    #endregion

    #region Tokens nested in generic instances

    [Test]
    public void ParseReturnType_Maps_Token_Nested_In_Generic_Instance()
    {
        var host = NewHost("T0");
        var method = AddMethod(host, "Get", typeof(void).ToGneedleType(), [], nameof(Templates.ReturnGenericInstance));

        var returnType = method.Source.ReturnType;
        Assert.That(returnType, Is.InstanceOf<GenericInstanceType>());
        Assert.That(((GenericInstanceType) returnType).GenericArguments[0], Is.SameAs(host.Source.GenericParameters[0]));
    }

    [Test]
    public void CopyVariables_Maps_Token_Nested_In_Generic_Instance()
    {
        var host = NewHost("T0");
        var method = AddMethod(host, "Get", typeof(bool).ToGneedleType(), [], nameof(Templates.LocalOfGenericInstance));

        var variableType = method.Source.Body.Variables[0].VariableType;
        Assert.That(variableType, Is.InstanceOf<GenericInstanceType>());
        Assert.That(((GenericInstanceType) variableType).GenericArguments[0], Is.SameAs(host.Source.GenericParameters[0]));
    }

    [Test]
    public void SetBody_With_Token_Nested_In_Generic_Instance_Produces_Valid_Assembly()
    {
        var assembly = Assembly.Create("GenericTokenNestedAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler)
                                 .AddClass("Host", Ns, ClassFlags.Public)
                                 .WithGenericParameter("T0")
                                 .GetHandler();
        AddMethod(host, "Get", typeof(bool).ToGneedleType(), [], nameof(Templates.LocalOfGenericInstance));

        // The runtime loader can't load this net5.0-targeted image here, but Cecil
        // re-reading the emitted bytes proves the produced image is well-formed.
        using var stream = new MemoryStream();
        assembly.SaveTo(stream);
        stream.Position = 0;

        var reread = AssemblyDefinition.ReadAssembly(stream);
        var emitted = reread.MainModule.GetType($"{Ns}.Host")!.Methods.First(m => m.Name == "Get");
        Assert.That(emitted.Body.Variables[0].VariableType.FullName, Is.EqualTo("System.Collections.Generic.List`1<T0>"));

        // The token types themselves must not be referenced by the produced assembly at all.
        Assert.That(reread.MainModule.GetTypeReferences().Any(type => type.FullName.Contains("Gneedle.Inject.T_")), Is.False);
        Assert.That(reread.MainModule.GetTypeReferences().Any(type => type.FullName.Contains("Gneedle.Inject.M_")), Is.False);
    }

    [Test]
    public void SetBody_Does_Not_Leak_Token_Types()
    {
        var host = NewHost("T0");
        var method = AddMethod(host, "Get", typeof(bool).ToGneedleType(), [], nameof(Templates.LocalOfGenericInstance));

        foreach (var instruction in method.Source.Body.Instructions) AssertNoTokenType(instruction);
        foreach (var variable in method.Source.Body.Variables) AssertNoTokenType(variable.VariableType);
    }

    #endregion

    #region Tokens nested in other type shapes

    // The shapes below can't be written as a local variable or a return type of a C# template,
    // so they are built by hand instead of being compiled.

    [Test]
    public void ParseReturnType_Maps_Token_In_Function_Pointer()
    {
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        var module = host.Source.Module;

        var pointer = new FunctionPointerType { ReturnType = module.TypeSystem.Void };
        pointer.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(T_0))));

        method.ParseReturnType(pointer);

        var returnType = method.Source.ReturnType;
        Assert.That(returnType, Is.InstanceOf<FunctionPointerType>());
        Assert.That(((FunctionPointerType) returnType).Parameters[0].ParameterType, Is.SameAs(host.Source.GenericParameters[0]));
    }

    [Test]
    public void ParseReturnType_Maps_Token_In_Required_Modifier()
    {
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        var module = host.Source.Module;

        // The shape of an `in` parameter: T& modreq(InAttribute).
        var inAttribute = module.ImportReference(typeof(System.Runtime.InteropServices.InAttribute));
        var modified = new RequiredModifierType(inAttribute, new ByReferenceType(module.ImportReference(typeof(T_0))));

        method.ParseReturnType(modified);

        var returnType = method.Source.ReturnType;
        Assert.That(returnType, Is.InstanceOf<RequiredModifierType>());
        // The modifier itself must survive the parsing, otherwise the `in` semantic of the parameter is lost.
        Assert.That(((RequiredModifierType) returnType).ModifierType.FullName, Is.EqualTo(inAttribute.FullName));
        var byReference = ((RequiredModifierType) returnType).ElementType;
        Assert.That(byReference, Is.InstanceOf<ByReferenceType>());
        Assert.That(((ByReferenceType) byReference).ElementType, Is.SameAs(host.Source.GenericParameters[0]));
    }

    [Test]
    public void ParseReturnType_Maps_Token_In_Optional_Modifier()
    {
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        var module = host.Source.Module;

        var outAttribute = module.ImportReference(typeof(System.Runtime.InteropServices.OutAttribute));
        var modified = new OptionalModifierType(outAttribute, module.ImportReference(typeof(T_0)));

        method.ParseReturnType(modified);

        var returnType = method.Source.ReturnType;
        Assert.That(returnType, Is.InstanceOf<OptionalModifierType>());
        Assert.That(((OptionalModifierType) returnType).ModifierType.FullName, Is.EqualTo(outAttribute.FullName));
        Assert.That(((OptionalModifierType) returnType).ElementType, Is.SameAs(host.Source.GenericParameters[0]));
    }

    [Test]
    public void ParseReturnType_Maps_Token_In_Pinned_Type()
    {
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        var pinned = new PinnedType(host.Source.Module.ImportReference(typeof(T_0)));

        method.ParseReturnType(pinned);

        var returnType = method.Source.ReturnType;
        Assert.That(returnType, Is.InstanceOf<PinnedType>());
        Assert.That(((PinnedType) returnType).ElementType, Is.SameAs(host.Source.GenericParameters[0]));
    }

    [Test]
    public void ParseReturnType_Maps_Token_In_Sentinel_Type()
    {
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Get", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        var sentinel = new SentinelType(host.Source.Module.ImportReference(typeof(T_0)));

        method.ParseReturnType(sentinel);

        var returnType = method.Source.ReturnType;
        Assert.That(returnType, Is.InstanceOf<SentinelType>());
        Assert.That(((SentinelType) returnType).ElementType, Is.SameAs(host.Source.GenericParameters[0]));
    }

    #endregion

    #region Tokens on the Object.Method path

    // The host is constrained so that the generic parameters have members to look up, which is the only way a member
    // call on a generic parameter typed instance could be produced as valid IL. The two constraints hold the same
    // method, so that the looked up one tells which generic parameter the token was mapped to.
    private static TypeHandler NewConstrainedHost()
    {
        var handler = (AssemblyHandler) Assembly.Create("ObjectTokenAssembly").Handler;
        return (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public)
                                       .WithGenericParameter("T0", Constraint.FromType<NamedHelperBase>())
                                       .WithGenericParameter("T1", Constraint.FromType<NamedSecondHelperBase>())
                                       .GetHandler();
    }

    [Test]
    public void ObjectMethod_With_Token_Receiver_Is_Looked_Up_On_The_Constraint_Of_The_First_Generic_Parameter()
    {
        var host = NewConstrainedHost();
        var method = (MethodHandler) host.AddMethod("Run", typeof(string).ToGneedleType(), [], [new Parameter(new GenericParameterType("T0"))], MethodFlags.Public);

        method.SetBody(Template(nameof(Templates.ObjectMethod_TokenReceiver)));

        var instructions = method.Source.Body.Instructions;
        // T_0 is the first generic parameter of the declaring type, so the method has to come from the first constraint.
        var call = instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                               .FirstOrDefault(reference => reference.Name == nameof(NamedHelperBase.Name));
        Assert.That(call, Is.Not.Null);
        Assert.That(call!.DeclaringType.Name, Is.EqualTo(nameof(NamedHelperBase)));
        Assert.That(instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.DeclaringType.FullName == Object.TYPE_NAME), Is.False);
        foreach (var instruction in instructions) AssertNoTokenType(instruction);
    }

    [Test]
    public void ObjectMethod_With_Second_Token_Receiver_Is_Looked_Up_On_The_Constraint_Of_The_Second_Generic_Parameter()
    {
        var host = NewConstrainedHost();
        var method = (MethodHandler) host.AddMethod("Run", typeof(string).ToGneedleType(), [], [new Parameter(new GenericParameterType("T1"))], MethodFlags.Public);

        method.SetBody(Template(nameof(Templates.ObjectMethod_SecondTokenReceiver)));

        // The very same method name is held by both constraints, so the declaring type tells which one was used.
        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                             .FirstOrDefault(reference => reference.Name == nameof(NamedSecondHelperBase.Name));
        Assert.That(call, Is.Not.Null);
        Assert.That(call!.DeclaringType.Name, Is.EqualTo(nameof(NamedSecondHelperBase)));
    }

    #endregion

    #region Tokens on the public API path
    //
    // A token passed to the public API stands for a generic parameter just like it does in a template, even though the
    // parameter is a System.Type there. Parsing it must happen before the type is imported, because importing a token
    // type appends a Gneedle.Inject assembly reference to the target module and leaves the produced assembly depending
    // on the weaver.

    [Test]
    public void AddMethod_Parses_The_Token_Of_The_Parameter_Type()
    {
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(T_0).ToGneedleType())], MethodFlags.Public);

        // The token parameter has to be the generic parameter of the host, just like the one from `new GenericParameterType("T")`.
        Assert.That(method.Source.Parameters[0].ParameterType, Is.SameAs(host.Source.GenericParameters[0]));
    }

    [Test]
    public void AddMethod_Parses_The_Token_Of_The_Method_Generic_Parameter()
    {
        var host = NewHost();
        var method = (MethodHandler) host.AddMethod("Run", typeof(void).ToGneedleType(), [new GenericParameterType("U")],
                                                    [new Parameter(typeof(M_0).ToGneedleType())], MethodFlags.Public);

        Assert.That(method.Source.Parameters[0].ParameterType, Is.SameAs(method.Source.GenericParameters[0]));
    }

    [Test]
    public void AddMethod_Parses_The_Token_Nested_In_The_Parameter_Type()
    {
        var host = NewHost("T0");
        var method = (MethodHandler) host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(new GenericType(typeof(List<>), typeof(T_0)))],
                                                    MethodFlags.Public);

        var parameterType = method.Source.Parameters[0].ParameterType;
        Assert.That(parameterType, Is.InstanceOf<GenericInstanceType>());
        Assert.That(((GenericInstanceType) parameterType).GenericArguments[0], Is.SameAs(host.Source.GenericParameters[0]));
    }

    [Test]
    public void AddMethod_With_Unresolvable_Token_Parameter_Throws()
    {
        var host = NewHost("T0");

        Assert.Throws<IndexOutOfRangeException>(() => host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(T_1).ToGneedleType())],
                                                                    MethodFlags.Public));
    }

    [Test]
    public void AddMethod_With_Token_Parameter_Does_Not_Reference_The_Weaver_Assembly()
    {
        var assembly = Assembly.Create("GenericTokenParameterAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler)
                                 .AddClass("Host", Ns, ClassFlags.Public)
                                 .WithGenericParameter("T0")
                                 .GetHandler();
        var method = (MethodHandler) host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(T_0).ToGneedleType())], MethodFlags.Public);

        // Resolving the token through GetCecilType would load the assembly which declares Gneedle.Inject.T_0, and
        // hence append a Gneedle.Inject reference to the target module. The produced assembly would then depend on
        // the weaver at runtime even though the token itself is replaced.
        Assert.That(host.Source.Module.AssemblyReferences.Any(reference => reference.Name.StartsWith(nameof(Gneedle))), Is.False);
        Assert.That(method.Source.Parameters[0].ParameterType, Is.SameAs(host.Source.GenericParameters[0]));
    }

    #endregion

    /// <summary>
    /// Assert that neither the operand of the instruction nor the types it refers to hold a token type.
    /// </summary>
    private static void AssertNoTokenType(Instruction instruction)
    {
        TypeReference[] types = instruction.Operand switch
        {
            TypeReference typeReference     => [typeReference],
            FieldReference fieldReference   => [fieldReference.DeclaringType, fieldReference.FieldType],
            MethodReference methodReference => [methodReference.DeclaringType, methodReference.ReturnType, .. methodReference.Parameters.Select(p => p.ParameterType)],
            VariableDefinition variable     => [variable.VariableType],
            _                               => []
        };
        foreach (var type in types) AssertNoTokenType(type);
    }

    private static void AssertNoTokenType(TypeReference? type)
    {
        if (type == null) return;
        Assert.That(type.FullName, Does.Not.Contain("Gneedle.Inject.T_"), $"The token type leaked into: {type.FullName}");
        Assert.That(type.FullName, Does.Not.Contain("Gneedle.Inject.M_"), $"The token type leaked into: {type.FullName}");
    }
}
