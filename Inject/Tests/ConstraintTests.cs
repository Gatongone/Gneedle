using Mono.Cecil;
using GenericParameterAttributes = System.Reflection.GenericParameterAttributes;

namespace Gneedle.Inject.Test;

[TestFixture]
public class ConstraintTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static TypeHandler NewClass()
    {
        var handler = (AssemblyHandler) Assembly.Create("ConstraintTestAssembly").Handler;
        return (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
    }

    private static GenericParameter SingleGenericParameterOf(IMethodHandler method)
        => ((MethodHandler) method).Source.GenericParameters.Single();

    // region Flag presets (construction only)

    [Test]
    public void Preset_Class_Is_ReferenceTypeConstraint_Flag()
    {
        Assert.That(Constraint.Class.GenericParameterAttributes, Is.EqualTo(GenericParameterAttributes.ReferenceTypeConstraint));
        Assert.That(Constraint.Class.Type, Is.Null);
    }

    [Test]
    public void Preset_New_Is_DefaultConstructorConstraint_Flag()
    {
        Assert.That(Constraint.New.GenericParameterAttributes, Is.EqualTo(GenericParameterAttributes.DefaultConstructorConstraint));
        Assert.That(Constraint.New.Type, Is.Null);
    }

    [Test]
    public void Preset_Struct_Combines_ValueType_And_Ctor_Flags()
    {
        var expected = GenericParameterAttributes.NotNullableValueTypeConstraint | GenericParameterAttributes.DefaultConstructorConstraint;
        Assert.That(Constraint.Struct.GenericParameterAttributes, Is.EqualTo(expected));
    }

    // endregion

    // region FromType construction (currently broken — see Constraint.cs:46-52)

    [Test]
    public void FromType_NonGeneric_Type_Does_Not_Throw()
    {
        Assert.DoesNotThrow(() => Constraint.FromType(typeof(IDisposable)));
    }

    [Test]
    public void FromType_Generic_Overload_Sets_Type_And_Name()
    {
        var constraint = Constraint.FromType<IDisposable>();
        Assert.That(constraint.Type, Is.Not.Null);
        Assert.That(constraint.Name, Is.EqualTo("System.IDisposable"));
    }

    // endregion

    // region AddMethod integration

    [Test]
    public void AddMethod_Generic_With_Class_Constraint_Sets_ReferenceTypeConstraint()
    {
        var host = NewClass();
        var method = host.AddMethod(
            "Foo",
            typeof(void).ToGneedleType(),
            [new GenericParameterType("T", Constraint.Class)],
            [],
            MethodFlags.Public);

        Assert.That(SingleGenericParameterOf(method).HasReferenceTypeConstraint, Is.True);
    }

    [Test]
    public void AddMethod_Generic_With_Self_Constraint_Constrains_To_DeclaringType()
    {
        var host = NewClass();
        var method = host.AddMethod(
            "Foo",
            typeof(void).ToGneedleType(),
            [new GenericParameterType("T", Constraint.FromSelf())],
            [],
            MethodFlags.Public);

        var gp = SingleGenericParameterOf(method);
        Assert.That(gp.Constraints.Any(c => c.ConstraintType.FullName == host.Source.FullName), Is.True);
    }

    [Test]
    public void AddMethod_Generic_With_Type_Constraint_Adds_Constraint()
    {
        var host = NewClass();
        var method = host.AddMethod(
            "Foo",
            typeof(void).ToGneedleType(),
            [new GenericParameterType("T", Constraint.FromType<IDisposable>())],
            [],
            MethodFlags.Public);

        var gp = SingleGenericParameterOf(method);
        Assert.That(gp.Constraints.Any(c => c.ConstraintType.Name == nameof(IDisposable)), Is.True);
    }

    // endregion

    // region StructDecorator integration (shared SetConstraintFromType path)

    [Test]
    public void AddStruct_Generic_With_Type_Constraint_Adds_Constraint()
    {
        var handler = (AssemblyHandler) Assembly.Create("StructConstraintAssembly").Handler;
        var structHandler = handler
            .AddStruct("Wrapper", Ns, StructFlags.Public)
            .WithGenericParameter("T", Constraint.FromType<IDisposable>())
            .GetHandler();

        var gp = ((StructHandler) structHandler).Source.GenericParameters.Single();
        Assert.That(gp.Constraints.Any(c => c.ConstraintType.Name == nameof(IDisposable)), Is.True);
    }

    [Test]
    public void AddStruct_Generic_With_Class_Flag_Sets_ReferenceTypeConstraint()
    {
        var handler = (AssemblyHandler) Assembly.Create("StructFlagAssembly").Handler;
        var structHandler = handler
            .AddStruct("Wrapper", Ns, StructFlags.Public)
            .WithGenericParameter("T", Constraint.Class)
            .GetHandler();

        var gp = ((StructHandler) structHandler).Source.GenericParameters.Single();
        Assert.That(gp.HasReferenceTypeConstraint, Is.True);
    }

    // endregion
}
