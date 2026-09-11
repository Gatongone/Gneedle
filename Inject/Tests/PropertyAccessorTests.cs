using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Gneedle.Inject.Test;

/// <summary>
/// Templates for the bodies of a property's accessors, which live top level in the test assembly so that Cecil can
/// resolve them from disk.
/// </summary>
public static class PropertyBodyTemplates
{
    /// <summary>
    /// The field which <see cref="RecordValue"/> writes, so that the body copied into a setter is told apart from the
    /// body which the setter would otherwise hold.
    /// </summary>
    public static int Recorded;

    /// <summary>
    /// A body of the shape a getter has, which holds no call to an accessor.
    /// </summary>
    public static int GetConstant() => 42;

    /// <summary>
    /// A body of the shape a setter has, which holds no call to an accessor.
    /// </summary>
    public static void RecordValue(int value) => Recorded = value;
}

/// <summary>
/// Templates which weave around the body of an accessor, which the template reaches through <see cref="Proceed"/>.
/// </summary>
public static class PropertyAroundTemplates
{
    /// <summary>
    /// Proceed, and add one to what the accessor did with the value.
    /// </summary>
    public static int GetThenAddsOne() => Proceed.Method<Func<int>>("get_Value")() + 1;

    /// <inheritdoc cref="GetThenAddsOne"/>
    public static void SetThenAddsOne(int value) => Proceed.Method<Action<int>>("set_Value")(value + 1);
}

/// <summary>
/// Tests for describing the bodies of a property's accessors, both through <see cref="PropertyDecorator"/> and through
/// the handlers which it hands back.<para/>
/// A body is described through the chain, and a body is woven around through the <see cref="IMethodHandler"/> which
/// <see cref="IPropertyHandler.GetGetter"/> and <see cref="IPropertyHandler.GetSetter"/> return. The chain describes
/// the members being added, of which a body is one part, while weaving around is an operation on a body which the
/// handler holds.
/// </summary>
[TestFixture]
public class PropertyAccessorTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static MethodInfo Template(Type holder, string name) => holder.GetMethod(name)!;

    /// <summary>
    /// Create a host which carries a constructor, so that an instance of it could be created once it is loaded.
    /// </summary>
    private static (Assembly Assembly, TypeHandler Host) NewHost(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        var module = assembly.Source.MainModule;
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        // A type which Cecil emits carries no constructor of its own, and one is needed to create an instance of it.
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

        return (assembly, host);
    }

    private static MethodDefinition GetterOf(TypeHandler host) => host.Source.Properties.Single().GetMethod!;

    private static MethodDefinition SetterOf(TypeHandler host) => host.Source.Properties.Single().SetMethod!;

    /// <summary>
    /// The method which the body of <paramref name="accessorName"/> was moved to, or null when the accessor is not
    /// woven around.
    /// </summary>
    private static MethodDefinition? ProceedOf(TypeHandler host, string accessorName)
        => host.Source.Methods.FirstOrDefault(method => method.Name == $"<{accessorName}>k__Proceed");

    private static bool Holds(MethodDefinition method, OpCode opcode)
        => method.Body.Instructions.Any(instruction => instruction.OpCode == opcode);

    #region The body which the chain describes

    [Test]
    public void PropertyDecorator_WithGetter_From_A_Template_Copies_The_Body()
    {
        var (_, host) = NewHost("PropertyGetterBodyAssembly");

        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithGetter(Template(typeof(PropertyBodyTemplates), nameof(PropertyBodyTemplates.GetConstant)))
            .GetHandler();

        // The body of the template, rather than the one which reads the backing field.
        Assert.That(Holds(GetterOf(host), OpCodes.Ldc_I4_S), Is.True);
        Assert.That(Holds(GetterOf(host), OpCodes.Ldfld), Is.False);
    }

    [Test]
    public void PropertyDecorator_WithSetter_From_A_Template_Copies_The_Body()
    {
        var (_, host) = NewHost("PropertySetterBodyAssembly");

        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithSetter(Template(typeof(PropertyBodyTemplates), nameof(PropertyBodyTemplates.RecordValue)))
            .GetHandler();

        // The body of the template, rather than the one which writes the backing field.
        Assert.That(Holds(SetterOf(host), OpCodes.Stsfld), Is.True);
        Assert.That(Holds(SetterOf(host), OpCodes.Stfld), Is.False);
    }

    [Test]
    public void PropertyDecorator_WithGetter_Twice_Keeps_The_Body_Which_Was_Asked_For_Last()
    {
        // The two ways of describing the body of an accessor replace each other, so the one which was asked for last is
        // the one which is applied.
        var (_, host) = NewHost("PropertyGetterTwiceAssembly");

        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithGetter(DefaultPropertyBody.WithDefaultReturn)
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .GetHandler();

        Assert.That(Holds(GetterOf(host), OpCodes.Ldfld), Is.True);
    }

    [Test]
    public void PropertyDecorator_WithFieldOperation_Of_A_Static_Property_Reaches_The_Field_Through_The_Type()
    {
        // A static accessor is created as one, so the body which reads or writes a field is emitted for that shape
        // rather than for the shape of an instance accessor, which the field it belongs to follows.
        var (_, host) = NewHost("PropertyStaticFieldAssembly");

        host.AddProperty("Value", PropertyFlags.Public | PropertyFlags.Static)
            .WithType(typeof(int))
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .WithSetter(DefaultPropertyBody.WithFieldOperation)
            .GetHandler();

        var field = host.Source.Fields.Single(f => f.Name == "<Value>k__BackingField");
        Assert.That(field.IsStatic, Is.True);
        Assert.That(Holds(GetterOf(host), OpCodes.Ldsfld), Is.True);
        Assert.That(Holds(GetterOf(host), OpCodes.Ldarg_0), Is.False);
        Assert.That(Holds(SetterOf(host), OpCodes.Stsfld), Is.True);
    }

    [Test]
    public void PropertyDecorator_Static_Property_With_Field_Operation_Runs()
    {
        // A static accessor whose body named `this` left the type unloadable rather than merely wrong, so the test which
        // tells the two apart is one which loads the type and uses the property.
        var (assembly, host) = NewHost("PropertyStaticFieldRunsAssembly");
        host.AddProperty("Value", PropertyFlags.Public | PropertyFlags.Static)
            .WithType(typeof(int))
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .WithSetter(DefaultPropertyBody.WithFieldOperation)
            .GetHandler();

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        type.GetProperty("Value")!.SetValue(null, 5);

        Assert.That(type.GetProperty("Value")!.GetValue(null), Is.EqualTo(5));
    }

    [Test]
    public void PropertyDecorator_WithGetter_Leaves_The_Setter_Alone()
    {
        // The bodies of the two accessors are described apart from each other.
        var (_, host) = NewHost("PropertyGetterAndSetterAssembly");

        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithSetter(DefaultPropertyBody.WithFieldOperation)
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .GetHandler();

        Assert.That(Holds(GetterOf(host), OpCodes.Ldfld), Is.True);
        Assert.That(Holds(SetterOf(host), OpCodes.Stfld), Is.True);
    }

    #endregion

    #region The body which the handler weaves around

    [Test]
    public void PropertyGetter_AroundBody_Moves_The_Body_To_A_Generated_Method()
    {
        var (_, host) = NewHost("PropertyGetterAroundAssembly");
        var property = host.AddProperty("Value", PropertyFlags.Public)
                           .WithType(typeof(int))
                           .WithGetter(DefaultPropertyBody.WithFieldOperation)
                           .GetHandler();

        property.GetGetter()!.AroundBody(Template(typeof(PropertyAroundTemplates), nameof(PropertyAroundTemplates.GetThenAddsOne)));

        var proceed = ProceedOf(host, "get_Value");
        Assert.That(proceed, Is.Not.Null);
        Assert.That(Holds(proceed!, OpCodes.Ldfld), Is.True);
    }

    [Test]
    public void PropertyDecorator_Of_An_Abstract_Property_Which_Describes_A_Body_Throws()
    {
        // The flags are given to the accessor as it is created, so an accessor which they make abstract is refused where
        // its body would be described, rather than left holding one or reaching Cecil without a body at all.
        var (_, host) = NewHost("PropertyAbstractAssembly");

        var thrown = Assert.Throws<ArgumentException>(() => host.AddProperty("Value", PropertyFlags.Public | PropertyFlags.Abstract)
                                                                .WithType(typeof(int))
                                                                .WithGetter(DefaultPropertyBody.WithFieldOperation)
                                                                .GetHandler());

        Assert.That(thrown!.Message, Does.Contain("abstract"));
    }

    [Test]
    public void PropertyGetter_AroundBody_On_A_Virtual_Property_Generates_A_Not_Virtual_Proceed()
    {
        // The flags make the accessor virtual, and the method which the body is moved to is generated apart from them.
        var (_, host) = NewHost("PropertyAroundVirtualAssembly");
        var property = host.AddProperty("Value", PropertyFlags.Public | PropertyFlags.Virtual)
                           .WithType(typeof(int))
                           .WithGetter(DefaultPropertyBody.WithFieldOperation)
                           .GetHandler();

        property.GetGetter()!.AroundBody(Template(typeof(PropertyAroundTemplates), nameof(PropertyAroundTemplates.GetThenAddsOne)));

        Assert.That(GetterOf(host).IsVirtual, Is.True);
        Assert.That(ProceedOf(host, "get_Value")!.IsVirtual, Is.False);
    }

    [Test]
    public void PropertyGetter_AroundBody_Survives_A_Save_And_ReRead()
    {
        var (assembly, host) = NewHost("PropertyAroundSaveAssembly");
        var property = host.AddProperty("Value", PropertyFlags.Public)
                           .WithType(typeof(int))
                           .WithGetter(DefaultPropertyBody.WithFieldOperation)
                           .GetHandler();
        property.GetGetter()!.AroundBody(Template(typeof(PropertyAroundTemplates), nameof(PropertyAroundTemplates.GetThenAddsOne)));

        using var stream = new MemoryStream();
        assembly.SaveTo(stream);
        stream.Position = 0;

        var reread = AssemblyDefinition.ReadAssembly(stream);
        var type = reread.MainModule.GetType($"{Ns}.Host")!;
        var proceed = type.Methods.FirstOrDefault(method => method.Name == "<get_Value>k__Proceed");

        Assert.That(proceed, Is.Not.Null);
        Assert.That(proceed!.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldfld), Is.True);
    }

    #endregion

    #region The woven accessor runs

    [Test]
    public void PropertyGetter_AroundBody_Runs_The_Getter_And_Returns_What_The_Template_Chose()
    {
        var (assembly, host) = NewHost("PropertyGetterRunsAssembly");
        var property = host.AddProperty("Value", PropertyFlags.Public)
                           .WithType(typeof(int))
                           .WithGetter(DefaultPropertyBody.WithFieldOperation)
                           .GetHandler();
        property.GetGetter()!.AroundBody(Template(typeof(PropertyAroundTemplates), nameof(PropertyAroundTemplates.GetThenAddsOne)));

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetField("<Value>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, 5);

        // 5 is a getter which ran unchanged, 6 is one which the template wrapped, and 1 is one which never ran at all.
        Assert.That(type.GetProperty("Value")!.GetValue(instance), Is.EqualTo(6));
    }

    [Test]
    public void PropertySetter_AroundBody_Runs_The_Setter_And_Writes_What_The_Template_Chose()
    {
        var (assembly, host) = NewHost("PropertySetterRunsAssembly");
        var property = host.AddProperty("Value", PropertyFlags.Public)
                           .WithType(typeof(int))
                           .WithGetter(DefaultPropertyBody.WithFieldOperation)
                           .WithSetter(DefaultPropertyBody.WithFieldOperation)
                           .GetHandler();
        property.GetSetter()!.AroundBody(Template(typeof(PropertyAroundTemplates), nameof(PropertyAroundTemplates.SetThenAddsOne)));

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetProperty("Value")!.SetValue(instance, 5);

        var written = type.GetField("<Value>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance);

        // 5 is a setter which ran unchanged, and 6 is one which the template wrapped: the value reached the proceed call
        // through the Action<int> which the template names.
        Assert.That(written, Is.EqualTo(6));
    }

    #endregion
}
