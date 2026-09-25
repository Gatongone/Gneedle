using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// The type which a parameter of a generic type of these tests is constrained to, whose member a token reaches.
/// </summary>
public class AConstrainedBase
{
    /// <summary>The member which a token of the parameter reaches.</summary>
    public int Number;
}

/// <summary>
/// Tests for the token which stands for the type being woven, and for the placeholder which stands for the instance the
/// member being woven belongs to.<para/>
/// Both name what a template cannot otherwise name: the type it is woven into is not one it was compiled against, and
/// the instance of a member is not an argument of the template which reaches it.
/// </summary>
[TestFixture]
public class SelfTokenTests
{
    /// <summary>
    /// The templates, which live in the test assembly so that Cecil can resolve them from disk.
    /// </summary>
    public static class Templates
    {
        /// <summary>The type being woven, as the argument of a type of the framework.</summary>
        public static List<T_Self> ListOfSelf() => [];

        /// <summary>The same, of one which derives from a type of the framework: the token stands where a class is
        /// asked for as well.</summary>
        public static T_Self[] ArrayOfSelf() => [];

        /// <summary>The type being woven, as a value of the type of the framework which names a type.</summary>
        public static Type TypeOfSelf() => typeof(T_Self);

        /// <summary>A field of the type being woven, reached as a value of that same type.</summary>
        public static int ReadOwnField() => This.Field<T_Self>("Next")!.Get()!.GetHashCode();

        /// <summary>The instance which the member being woven belongs to, compared with a field of the type being
        /// woven.</summary>
        public static bool IsTheSame() => This.Field<T_Self>("s_Instance")!.Get() == This.Reference;

        /// <summary>The same, of a member which is static, which belongs to no instance and is refused for it.</summary>
        public static bool IsTheSameOfAStaticOne() => This.Reference == null;

        /// <summary>The type being woven, built: what the compiler writes for the token where an instance is made.</summary>
        public static int BuildASelf() => new T_Self().GetHashCode();

        /// <summary>The same, where the token is the type of an `is`.</summary>
        public static int IsASelf(object value) => value is T_Self ? 1 : 0;

        /// <summary>A member of the type being woven, reached through another instance of that type.</summary>
        public static int ReadThroughAnother(T_Self other) => other.Field<int>("Next").Get()!.GetHashCode();

        /// <summary>The same, of the instance which the member being woven belongs to, which is what the placeholder
        /// of the instance stands for.</summary>
        public static int ReadThroughOwnInstance() => This.Reference.Field<int>("Next").Get()!.GetHashCode();

        /// <summary>The same, of a token of a generic parameter: a value of one is a value of the constraint which the
        /// parameter holds, and what its members are is read there.</summary>
        public static int ReadThroughAParameter(T_0 other) => other.Field<int>("Number").Get()!.GetHashCode();

        /// <summary>The same, of a property rather than a field.</summary>
        public static int ReadAPropertyThroughAnother(T_Self other) => other.Property<int>("Next").Get()!.GetHashCode();

        /// <summary>The same, where the value of a default of the token is what is written.</summary>
        public static int NoSelf()
        {
            T_Self value = default!;
            GC.KeepAlive(value);
            return 1;
        }
    }

    private static (TypeHandler Host, MethodHandler Method) Weave(string templateName, bool isStatic = false, bool isGeneric = false)
    {
        var assembly = Assembly.Create("SelfTokenAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var declaration = handler.AddClass("Host", NS, ClassFlags.Public);
        var host = (TypeHandler) (isGeneric ? declaration.WithGenericParameter("T") : declaration).GetHandler();

        // The two are of the type being woven, which is what the templates reach through the token: a type which stands
        // for itself is the one thing a template cannot name, and a field of it can only be declared against the
        // definition the host is being built as.
        host.Source.Fields.Add(new FieldDefinition("s_Instance", FieldAttributes.Public | FieldAttributes.Static, host.Source));
        host.Source.Fields.Add(new FieldDefinition("Next", FieldAttributes.Public, host.Source));

        var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToIType(), [], [],
            MethodFlags.Public | (isStatic ? MethodFlags.Static : 0));
        method.SetBody(typeof(Templates).GetMethod(templateName)!);
        return (host, method);
    }

    [Test]
    public void T_Self_Stands_For_The_Type_Being_Woven_Where_A_Type_Is_Argumented()
    {
        var (_, method) = Weave(nameof(Templates.ListOfSelf));

        var named = method.Source.ReturnType;
        Assert.That(named, Is.InstanceOf<GenericInstanceType>(), "the woven body does not name an instantiation.");
        var argument = ((GenericInstanceType) named).GenericArguments[0];
        Assert.Multiple(() =>
        {
            Assert.That(argument.FullName, Is.EqualTo($"{NS}.Host"), "the argument is not the type being woven.");
            Assert.That(argument.Module, Is.SameAs(method.Source.Module), "the argument is not of the module which was woven.");
        });
    }

    [Test]
    public void T_Self_Stands_For_The_Type_Being_Woven_Where_An_Array_Is_Declared()
    {
        var (_, method) = Weave(nameof(Templates.ArrayOfSelf));

        var named = method.Source.ReturnType;
        Assert.That(named, Is.InstanceOf<ArrayType>(), "the woven body does not name an array.");
        Assert.Multiple(() =>
        {
            Assert.That(((ArrayType) named).ElementType.FullName, Is.EqualTo($"{NS}.Host"),
                "the element of the array is not the type being woven.");
            Assert.That(((ArrayType) named).ElementType.Module, Is.SameAs(method.Source.Module),
                "the element of the array is not of the module which was woven.");
        });
    }

    [Test]
    public void T_Self_Of_A_Generic_Type_Is_An_Instantiation_Of_That_Type()
    {
        // What a member of an instantiation reaches is an instantiation: a list of the type being woven is a list of it
        // with the arguments it was declared with, rather than one of the definition which stands open.
        var (_, method) = Weave(nameof(Templates.ListOfSelf), isGeneric: true);

        var argument = ((GenericInstanceType) method.Source.ReturnType).GenericArguments[0];
        Assert.That(argument, Is.InstanceOf<GenericInstanceType>(), "the argument is not an instantiation of the type being woven.");

        var instance = (GenericInstanceType) argument;
        Assert.Multiple(() =>
        {
            Assert.That(instance.ElementType.FullName, Is.EqualTo($"{NS}.Host"),
                "the argument is not an instantiation of the type being woven.");
            Assert.That(instance.GenericArguments[0], Is.InstanceOf<GenericParameter>(),
                "the instantiation is not of the parameter which the type being woven declares.");
        });
    }

    [Test]
    public void T_Self_Stands_For_The_Type_Being_Woven_Where_A_Type_Is_Read()
    {
        var (_, method) = Weave(nameof(Templates.TypeOfSelf));

        var read = method.Source.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldtoken);
        Assert.That(read, Is.Not.Null, "the woven body reads no type.");
        Assert.That(((TypeReference) read!.Operand).FullName, Is.EqualTo($"{NS}.Host"),
            "the type which is read is not the type being woven.");
    }

    [Test]
    public void T_Self_Stands_For_The_Type_Being_Woven_Where_A_Member_Of_It_Is_Reached()
    {
        var (_, method) = Weave(nameof(Templates.ReadOwnField));

        var read = method.Source.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldfld);
        Assert.That(read, Is.Not.Null, "the woven body reads no field.");
        Assert.That(((FieldReference) read!.Operand).FieldType.FullName, Is.EqualTo($"{NS}.Host"),
            "the field which is read is not one of the type being woven.");
    }

    [Test]
    public void This_Reference_Stands_For_The_Instance_Of_The_Member_Being_Woven()
    {
        var (_, method) = Weave(nameof(Templates.IsTheSame));

        var instructions = method.Source.Body.Instructions;
        Assert.Multiple(() =>
        {
            Assert.That(instructions.Any(instruction => instruction.OpCode == OpCodes.Ldsfld
                && ((FieldReference) instruction.Operand).Name == "s_Instance"), Is.True,
                "the woven body does not read the field it is compared with.");
            Assert.That(instructions.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.True,
                "the woven body does not load the instance which the member belongs to.");
            Assert.That(instructions.Any(instruction => instruction.OpCode == OpCodes.Ceq), Is.True,
                "the woven body does not compare the two.");
        });
    }

    [Test]
    public void T_Self_Stands_For_The_Type_Being_Woven_Where_An_Instance_Is_Made()
    {
        var (_, method) = Weave(nameof(Templates.BuildASelf));

        var made = method.Source.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Newobj);
        Assert.That(made, Is.Not.Null, "the woven body makes no instance.");
        Assert.That(((MethodReference) made!.Operand).DeclaringType.FullName, Is.EqualTo($"{NS}.Host"),
            "the instance which is made is not one of the type being woven.");
    }

    [Test]
    public void T_Self_Stands_For_The_Type_Being_Woven_Where_A_Type_Is_Told_Of()
    {
        var (_, method) = Weave(nameof(Templates.IsASelf));

        var told = method.Source.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Isinst);
        Assert.That(told, Is.Not.Null, "the woven body tells no type.");
        Assert.That(((TypeReference) told!.Operand).FullName, Is.EqualTo($"{NS}.Host"),
            "the type which is told is not the type being woven.");
    }

    [Test]
    public void T_Self_Stands_For_An_Instance_Which_A_Member_Is_Reached_Through()
    {
        // What a template holds of the token is an instance of the type being woven, and a member of that type is
        // reached through it the way a member of any instance is: the receiver of the placeholder is the value itself,
        // and the type which the member is looked up on is the type of that value.
        var assembly = Assembly.Create("SelfTokenInstanceAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass("Host", NS, ClassFlags.Public).GetHandler();
        host.Source.Fields.Add(new FieldDefinition("Next", FieldAttributes.Public, host.Source));

        var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToIType(), [], [], MethodFlags.Public);
        method.Source.Parameters.Add(new ParameterDefinition("other", ParameterAttributes.None, host.Source));
        method.SetBody(typeof(Templates).GetMethod(nameof(Templates.ReadThroughAnother))!);

        var read = method.Source.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldfld);
        Assert.That(read, Is.Not.Null, "the woven body reads no field.");
        Assert.That(((FieldReference) read!.Operand).DeclaringType.FullName, Is.EqualTo($"{NS}.Host"),
            "the field which is read is not one of the type being woven.");
    }

    [Test]
    public void This_Reference_Stands_For_An_Instance_Which_A_Member_Is_Reached_Through()
    {
        var (_, method) = Weave(nameof(Templates.ReadThroughOwnInstance));

        var read = method.Source.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldfld);
        Assert.Multiple(() =>
        {
            Assert.That(read, Is.Not.Null, "the woven body reads no field.");
            Assert.That(((FieldReference) read!.Operand).DeclaringType.FullName, Is.EqualTo($"{NS}.Host"),
                "the field which is read is not one of the type being woven.");
            Assert.That(method.Source.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.True,
                "the woven body does not read the instance which the member belongs to.");
        });
    }

    [Test]
    public void A_Property_Of_The_Type_Being_Woven_Is_Reached_Through_A_Token()
    {
        var assembly = Assembly.Create("SelfTokenPropertyAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass("Host", NS, ClassFlags.Public).GetHandler();
        var property = new PropertyDefinition("Next", PropertyAttributes.None, host.Source);
        host.Source.Properties.Add(property);
        host.Source.Fields.Add(new FieldDefinition("_next", FieldAttributes.Private, host.Source));
        var getter = new MethodDefinition("get_Next", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, host.Source)
        {
            DeclaringType = host.Source
        };
        getter.Body.GetILProcessor().Emit(OpCodes.Ldnull);
        getter.Body.GetILProcessor().Emit(OpCodes.Ret);
        host.Source.Methods.Add(getter);
        property.GetMethod = getter;

        var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToIType(), [], [], MethodFlags.Public);
        method.Source.Parameters.Add(new ParameterDefinition("other", ParameterAttributes.None, host.Source));
        method.SetBody(typeof(Templates).GetMethod(nameof(Templates.ReadAPropertyThroughAnother))!);

        Assert.That(method.Source.Body.Instructions.Any(instruction => instruction.OpCode is { } opcode && (opcode == OpCodes.Call || opcode == OpCodes.Callvirt)
            && instruction.Operand is MethodReference reference && reference.Name == "get_Next"), Is.True,
            "the woven body does not call the accessor of the property of the type being woven.");
    }

    [Test]
    public void T_0_Stands_For_An_Instance_Which_A_Member_Is_Reached_Through()
    {
        // A value of a parameter of a type is a value of the constraint that parameter holds, and no member is declared
        // by the parameter itself: what the token stands for is read as the constraint, which is what the weaving takes
        // the definition of a value of a parameter to be.
        var assembly = Assembly.Create("SelfTokenParameterAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass("Host", NS, ClassFlags.Public)
                                       .WithGenericParameter("T", new Constraint(typeof(AConstrainedBase).ToIType()))
                                       .GetHandler();

        var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToIType(), [], [], MethodFlags.Public);
        method.Source.Parameters.Add(new ParameterDefinition("other", ParameterAttributes.None, host.Source.GenericParameters[0]));
        method.SetBody(typeof(Templates).GetMethod(nameof(Templates.ReadThroughAParameter))!);

        var read = method.Source.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldfld);
        Assert.That(read, Is.Not.Null, "the woven body reads no field.");
        Assert.That(((FieldReference) read!.Operand).DeclaringType.FullName, Is.EqualTo(typeof(AConstrainedBase).FullName),
            "the field which is read is not one of the constraint of the parameter.");
    }

    [Test]
    public void T_Self_Of_A_Default_Is_The_Null_Which_The_Compiler_Wrote_For_It()
    {
        // What the compiler writes for a default of the token is the default of the class which the token is: the type
        // is read where the type of the local stands, and the value which is put in it is a null, because a null is
        // what the default of a class is. A null is what the default of the type being woven is where that type is a
        // class, and is not where it is a struct, which is the one place the token does not stand for it.
        var (_, method) = Weave(nameof(Templates.NoSelf));

        Assert.That(method.Source.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldnull), Is.True,
            "the woven body does not hold the null which the compiler wrote for the default.");
        Assert.That(method.Source.Body.Instructions.Any(instruction => instruction.Operand is TypeReference reference && reference.FullName == $"{NS}.Host"), Is.False,
            "the woven body names the type being woven although the compiler wrote no token for it.");
    }

    [Test]
    public void This_Reference_Of_A_Member_Which_Is_Static_Is_Refused()
    {
        var thrown = Assert.Throws<WeavingException>(() => Weave(nameof(Templates.IsTheSameOfAStaticOne), isStatic: true));

        // What is refused is the reading of the instance rather than the reaching of a member of one, and it is
        // refused by the name of what was read: the message of the other refusal is one which a reader who has to tell
        // the two apart cannot use.
        Assert.That(thrown!.Message, Does.Contain("which a member that is static does not have"),
            "the refusal is not one which says that the instance itself was read.");
    }
}
