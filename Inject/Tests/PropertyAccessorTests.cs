using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Gneedle.Inject.Test;

/// <summary>
/// Templates for the accessors of a property, which live top level in the test assembly so that Cecil can resolve them
/// from disk.<para/>
/// They are static, which gives the getter no parameter and the setter the value alone, and that is the shape the
/// accessors of the woven property have.
/// </summary>
public static class PropertyAroundTemplates
{
    /// <summary>
    /// Proceed, and add one to what the accessor did with the value.
    /// </summary>
    public static int GetThenAddsOne() => Proceed.Method<Func<int>>("get_Value")() + 1;

    /// <inheritdoc cref="GetThenAddsOne"/>
    public static void SetThenAddsOne(int value) => Proceed.Method<Action<int>>("set_Value")(value + 1);

    /// <summary>
    /// A body which holds no call to an accessor, so that the body of the accessor which was described and the body of
    /// the template are told apart by the instructions they hold.
    /// </summary>
    public static int GetConstant() => 42;

    /// <summary>
    /// A template whose return type does not match the property, which the around body refuses.
    /// </summary>
    public static long GetWithAnotherReturnType() => 0L;
}

/// <summary>
/// Tests for describing the bodies of a property's accessors through <see cref="PropertyDecorator"/>, which the chain
/// does the way <see cref="MethodDecorator"/> describes the body of a method.<para/>
/// The weaver itself needed nothing new for this: <see cref="IPropertyHandler.GetGetter"/> and
/// <see cref="IPropertyHandler.GetSetter"/> hand back an <see cref="IMethodHandler"/>, whose
/// <see cref="IMethodHandler.SetBody(MethodInfo)"/> and <see cref="IMethodHandler.AroundBody(MethodInfo)"/> have always
/// applied to an accessor. What these tests cover is the entry point which was missing from the chain, and the order in
/// which it has to be applied.
/// </summary>
[TestFixture]
public class PropertyAccessorTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static MethodInfo Template(string name) => typeof(PropertyAroundTemplates).GetMethod(name)!;

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

    private static bool Calls(MethodDefinition method, string name)
        => method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == name);

    private static bool Holds(MethodDefinition method, OpCode opcode)
        => method.Body.Instructions.Any(instruction => instruction.OpCode == opcode);

    #region The body which the chain describes

    [Test]
    public void PropertyDecorator_WithGetter_From_A_Template_Copies_The_Body()
    {
        var (_, host) = NewHost("PropertyGetterBodyAssembly");

        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithGetter(Template(nameof(PropertyAroundTemplates.GetConstant)))
            .GetHandler();

        // The body of the template, rather than the one which reads the backing field.
        Assert.That(Holds(GetterOf(host), OpCodes.Ldc_I4_S), Is.True);
        Assert.That(Holds(GetterOf(host), OpCodes.Ldfld), Is.False);
    }

    [Test]
    public void PropertyDecorator_WithAroundGetter_Proceeds_Into_The_Throwing_Body_It_Was_Added_With()
    {
        // Nothing was described for the getter to do, so it is created with a body which throws, which the template
        // proceeds into. A body which throws is used rather than one which returns a value, so that a getter which the
        // template never reaches is noticed at the call.
        var (_, host) = NewHost("PropertyAroundThrowingGetterAssembly");

        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithAroundGetter(Template(nameof(PropertyAroundTemplates.GetThenAddsOne)))
            .GetHandler();

        var proceed = ProceedOf(host, "get_Value");
        Assert.That(proceed, Is.Not.Null);
        Assert.That(Holds(proceed!, OpCodes.Newobj), Is.True);
        Assert.That(Calls(GetterOf(host), proceed!.Name), Is.True);
    }

    [Test]
    public void PropertyDecorator_WithAroundSetter_Proceeds_Into_The_Throwing_Body_It_Was_Added_With()
    {
        // The around body alone is enough to create the accessor, which is what this checks as much as the body.
        var (_, host) = NewHost("PropertyAroundThrowingSetterAssembly");

        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithAroundSetter(Template(nameof(PropertyAroundTemplates.SetThenAddsOne)))
            .GetHandler();

        var proceed = ProceedOf(host, "set_Value");
        Assert.That(proceed, Is.Not.Null);
        Assert.That(Holds(proceed!, OpCodes.Newobj), Is.True);
        Assert.That(Calls(SetterOf(host), proceed!.Name), Is.True);
    }

    [Test]
    public void PropertyDecorator_WithGetter_Then_WithAroundGetter_Weaves_Around_The_Body_Which_Was_Described()
    {
        var (_, host) = NewHost("PropertyAroundDescribedGetterAssembly");

        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .WithAroundGetter(Template(nameof(PropertyAroundTemplates.GetThenAddsOne)))
            .GetHandler();

        // The body which was described moved to the proceed method, so it is there rather than in the getter, and it is
        // that body rather than a throwing one.
        var proceed = ProceedOf(host, "get_Value");
        Assert.That(proceed, Is.Not.Null);
        Assert.That(Holds(proceed!, OpCodes.Ldfld), Is.True);
        Assert.That(Holds(proceed!, OpCodes.Newobj), Is.False);
        Assert.That(Calls(GetterOf(host), proceed!.Name), Is.True);
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
        Assert.That(ProceedOf(host, "get_Value"), Is.Null);
    }

    [Test]
    public void PropertyDecorator_WithAroundGetter_Before_WithGetter_Wraps_The_Body_Which_Was_Described_Afterwards()
    {
        // The around body is not one of the two which replace each other: it wraps whichever body the accessor holds, so
        // the order between the two kinds does not settle whether it is applied.
        var (_, host) = NewHost("PropertyAroundBeforeGetterAssembly");

        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithAroundGetter(Template(nameof(PropertyAroundTemplates.GetThenAddsOne)))
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .GetHandler();

        var proceed = ProceedOf(host, "get_Value");
        Assert.That(proceed, Is.Not.Null);
        Assert.That(Holds(proceed!, OpCodes.Ldfld), Is.True);
        Assert.That(Holds(proceed!, OpCodes.Newobj), Is.False);
    }

    [Test]
    public void PropertyDecorator_WithAroundGetter_Leaves_The_Setter_Alone()
    {
        // The bodies of the two accessors are described apart from each other, so the around body of the getter does not
        // reach the setter.
        var (_, host) = NewHost("PropertyAroundGetterAndSetterAssembly");

        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithSetter(DefaultPropertyBody.WithFieldOperation)
            .WithAroundGetter(Template(nameof(PropertyAroundTemplates.GetThenAddsOne)))
            .GetHandler();

        Assert.That(Holds(SetterOf(host), OpCodes.Stfld), Is.True);
        Assert.That(ProceedOf(host, "set_Value"), Is.Null);
    }

    #endregion

    #region What the weave refuses

    [Test]
    public void PropertyDecorator_WithAroundGetter_Of_An_Abstract_Property_Throws()
    {
        // The flags are applied before the weave, so an accessor which the flags make abstract is refused rather than
        // left holding a body and a proceed method of its own.
        var (_, host) = NewHost("PropertyAroundAbstractAssembly");

        Assert.Throws<ArgumentException>(() => host.AddProperty("Value", PropertyFlags.Public | PropertyFlags.Abstract)
                                                   .WithType(typeof(int))
                                                   .WithAroundGetter(Template(nameof(PropertyAroundTemplates.GetThenAddsOne)))
                                                   .GetHandler());
    }

    [Test]
    public void PropertyDecorator_WithAroundGetter_On_A_Virtual_Property_Generates_A_Not_Virtual_Proceed()
    {
        // The flags make the accessor virtual, and the method which the body is moved to is generated apart from them.
        var (_, host) = NewHost("PropertyAroundVirtualAssembly");

        host.AddProperty("Value", PropertyFlags.Public | PropertyFlags.Virtual)
            .WithType(typeof(int))
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .WithAroundGetter(Template(nameof(PropertyAroundTemplates.GetThenAddsOne)))
            .GetHandler();

        Assert.That(GetterOf(host).IsVirtual, Is.True);
        Assert.That(ProceedOf(host, "get_Value")!.IsVirtual, Is.False);
    }

    [Test]
    public void PropertyDecorator_WithAroundGetter_With_A_Template_Of_Another_Return_Type_Throws()
    {
        var (_, host) = NewHost("PropertyAroundMismatchAssembly");

        Assert.Throws<ArgumentException>(() => host.AddProperty("Value", PropertyFlags.Public)
                                                   .WithType(typeof(int))
                                                   .WithAroundGetter(Template(nameof(PropertyAroundTemplates.GetWithAnotherReturnType)))
                                                   .GetHandler());
    }

    #endregion

    #region The woven accessor runs

    [Test]
    public void PropertyDecorator_WithAroundGetter_Runs_The_Getter_And_Returns_What_The_Template_Chose()
    {
        var (assembly, host) = NewHost("PropertyAroundGetterRunsAssembly");
        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .WithAroundGetter(Template(nameof(PropertyAroundTemplates.GetThenAddsOne)))
            .GetHandler();

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetField("<Value>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, 5);

        // 5 is a getter which ran unchanged, 6 is one which the template wrapped, and 1 is one which never ran at all.
        Assert.That(type.GetProperty("Value")!.GetValue(instance), Is.EqualTo(6));
    }

    [Test]
    public void PropertyDecorator_WithAroundSetter_Runs_The_Setter_And_Writes_What_The_Template_Chose()
    {
        var (assembly, host) = NewHost("PropertyAroundSetterRunsAssembly");
        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .WithSetter(DefaultPropertyBody.WithFieldOperation)
            .WithAroundSetter(Template(nameof(PropertyAroundTemplates.SetThenAddsOne)))
            .GetHandler();

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetProperty("Value")!.SetValue(instance, 5);

        var written = type.GetField("<Value>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance);

        // 5 is a setter which ran unchanged, and 6 is one which the template wrapped: the value reached the proceed call
        // through the Action<int> which the template names.
        Assert.That(written, Is.EqualTo(6));
    }

    [Test]
    public void PropertyDecorator_WithAroundGetter_Survives_A_Save_And_ReRead()
    {
        var (assembly, host) = NewHost("PropertyAroundSaveAssembly");
        host.AddProperty("Value", PropertyFlags.Public)
            .WithType(typeof(int))
            .WithGetter(DefaultPropertyBody.WithFieldOperation)
            .WithAroundGetter(Template(nameof(PropertyAroundTemplates.GetThenAddsOne)))
            .GetHandler();

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
}
