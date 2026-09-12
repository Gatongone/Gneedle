using Mono.Cecil;

namespace Gneedle.Inject.Test;

/// <summary>
/// The attribute which the tests below put on the metadata which they build. It is declared by the test assembly rather
/// than by the assembly being built, so that the attribute class is one of another module.
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
internal sealed class MarkerAttribute : Attribute
{
    public string Name { get; }

    public MarkerAttribute(string name) => Name = name;
}

/// <summary>
/// Tests for <see cref="IAttributeContainer"/>, which reads and writes the custom attributes of the metadata which the
/// handlers stand for.
/// </summary>
[TestFixture]
public class AttributeTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static TypeHandler NewHost(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        return (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
    }

    /// <summary>
    /// Whether the definition carries the marker itself. The attribute class carries the attributes which the compiler
    /// writes as well, so the whole collection cannot be compared against an empty one.
    /// </summary>
    private static bool CarriesMarker(TypeDefinition definition)
        => definition.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(MarkerAttribute).FullName);

    #region Type

    [Test]
    public void AddAttribute_Puts_The_Attribute_On_The_Handled_Type()
    {
        var host = NewHost("AttributeTestAssembly");

        host.AddAttribute<MarkerAttribute>("hello");

        var attribute = host.Source.CustomAttributes.Single();
        Assert.That(attribute.AttributeType.FullName, Is.EqualTo(typeof(MarkerAttribute).FullName));
        Assert.That(attribute.ConstructorArguments[0].Value, Is.EqualTo("hello"));
    }

    [Test]
    public void AddAttribute_Does_Not_Put_The_Attribute_On_The_Attribute_Class()
    {
        // The type definition which the attribute is built from is the one of the attribute itself, so adding the
        // attribute to it would decorate the attribute class with itself and leave the handled type untouched.
        var assembly = Assembly.Create("AttributeSelfAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        host.AddAttribute<MarkerAttribute>("hello");

        var attributeDef = handler.GetCecilType(typeof(MarkerAttribute)).Definition;
        Assert.That(CarriesMarker(attributeDef), Is.False);
    }

    [Test]
    public void AddAttribute_With_Type_Of_Attribute_Puts_The_Attribute_On_The_Handled_Type()
    {
        var host = NewHost("AttributeTypeOverloadAssembly");

        host.AddAttribute(typeof(MarkerAttribute), "hello");

        var attribute = host.Source.CustomAttributes.Single();
        Assert.That(attribute.AttributeType.FullName, Is.EqualTo(typeof(MarkerAttribute).FullName));
    }

    [Test]
    public void AddAttribute_With_IType_Puts_The_Attribute_On_The_Handled_Type()
    {
        var host = NewHost("AttributeITypeAssembly");

        host.AddAttribute(typeof(MarkerAttribute).ToGneedleType(), "hello");

        var attribute = host.Source.CustomAttributes.Single();
        Assert.That(attribute.AttributeType.FullName, Is.EqualTo(typeof(MarkerAttribute).FullName));
    }

    [Test]
    public void AddAttribute_With_Arguments_Of_No_Matching_Constructor_Throws()
    {
        var host = NewHost("AttributeArgumentAssembly");

        // MarkerAttribute takes a single string, so an int names no constructor of it.
        Assert.Throws<ArgumentException>(() => host.AddAttribute<MarkerAttribute>(42));
    }

    [Test]
    public void AddAttribute_Produces_An_Assembly_Which_Reads_Back()
    {
        // The attribute class is declared by another module, so the constructor has to be imported before it can be
        // written. This is the test which proves the produced image is well formed with the attribute on it.
        var assembly = Assembly.Create("AttributeReadableAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        host.AddAttribute<MarkerAttribute>("hello");

        using var stream = new MemoryStream();
        assembly.SaveTo(stream);
        stream.Position = 0;

        var reread = AssemblyDefinition.ReadAssembly(stream);
        var type = reread.MainModule.GetType($"{Ns}.Host");
        Assert.That(type, Is.Not.Null);
        var attribute = type!.CustomAttributes.Single();
        Assert.That(attribute.AttributeType.FullName, Is.EqualTo(typeof(MarkerAttribute).FullName));
        Assert.That(attribute.ConstructorArguments[0].Value, Is.EqualTo("hello"));
    }

    #endregion

    #region Field

    [Test]
    public void AddAttribute_On_A_Field_Puts_The_Attribute_On_The_Field()
    {
        var assembly = Assembly.Create("FieldAttributeAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        host.Source.Fields.Add(new FieldDefinition("Value", FieldAttributes.Public, host.Source.Module.TypeSystem.Int32));
        var field = host.GetField("Value")!;

        field.AddAttribute(typeof(MarkerAttribute).ToGneedleType(), "hello");

        Assert.That(((FieldHandler) field).Source.CustomAttributes.Count, Is.EqualTo(1));
        Assert.That(CarriesMarker(handler.GetCecilType(typeof(MarkerAttribute)).Definition), Is.False);
    }

    [Test]
    public void ContainsAttribute_On_A_Field_Reports_The_Attribute_Which_Was_Added()
    {
        var host = NewHost("FieldAttributeContainsAssembly");
        host.Source.Fields.Add(new FieldDefinition("Value", FieldAttributes.Public, host.Source.Module.TypeSystem.Int32));
        var field = host.GetField("Value")!;

        Assert.That(field.ContainsAttribute<MarkerAttribute>(), Is.False);
        field.AddAttribute(typeof(MarkerAttribute).ToGneedleType(), "hello");
        Assert.That(field.ContainsAttribute<MarkerAttribute>(), Is.True);
    }

    #endregion

    #region Property

    [Test]
    public void AddAttribute_On_A_Property_Puts_The_Attribute_On_The_Property()
    {
        var assembly = Assembly.Create("PropertyAttributeAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        host.Source.Properties.Add(new PropertyDefinition("Prop", PropertyAttributes.None, host.Source.Module.TypeSystem.Int32));
        var property = host.GetProperty("Prop")!;

        property.AddAttribute(typeof(MarkerAttribute).ToGneedleType(), "hello");

        Assert.That(((PropertyHandler) property).Source.CustomAttributes.Count, Is.EqualTo(1));
        Assert.That(CarriesMarker(handler.GetCecilType(typeof(MarkerAttribute)).Definition), Is.False);
    }

    [Test]
    public void ContainsAttribute_On_A_Property_Reports_The_Attribute_Which_Was_Added()
    {
        var host = NewHost("PropertyAttributeContainsAssembly");
        host.Source.Properties.Add(new PropertyDefinition("Prop", PropertyAttributes.None, host.Source.Module.TypeSystem.Int32));
        var property = host.GetProperty("Prop")!;

        Assert.That(property.ContainsAttribute<MarkerAttribute>(), Is.False);
        property.AddAttribute(typeof(MarkerAttribute).ToGneedleType(), "hello");
        Assert.That(property.ContainsAttribute<MarkerAttribute>(), Is.True);
    }

    #endregion

    #region ContainsAttribute of a type

    [Test]
    public void ContainsAttribute_On_A_Type_Reports_The_Attribute_Which_Was_Added()
    {
        var host = NewHost("TypeAttributeContainsAssembly");

        Assert.That(host.ContainsAttribute<MarkerAttribute>(), Is.False);
        Assert.That(host.ContainsAttribute(typeof(MarkerAttribute)), Is.False);

        host.AddAttribute<MarkerAttribute>("hello");

        Assert.That(host.ContainsAttribute<MarkerAttribute>(), Is.True);
        Assert.That(host.ContainsAttribute(typeof(MarkerAttribute)), Is.True);
    }

    #endregion

    #region Method

    [Test]
    public void AddAttribute_On_A_Method_Puts_The_Attribute_On_The_Method()
    {
        var host = NewHost("MethodAttributeAssembly");
        var method = host.AddMethod("Run", MethodFlags.Public).GetHandler();

        method.AddAttribute<MarkerAttribute>("hello");

        var definition = ((MethodHandler) method).Source;
        Assert.That(definition.CustomAttributes.Single().AttributeType.FullName, Is.EqualTo(typeof(MarkerAttribute).FullName));
        // The attribute belongs to the method, so the type which declares it is left without it.
        Assert.That(host.Source.CustomAttributes.Any(carried => carried.AttributeType.FullName == typeof(MarkerAttribute).FullName), Is.False);
    }

    [Test]
    public void ContainsAttribute_On_A_Method_Reports_The_Attribute_Which_Was_Added()
    {
        var host = NewHost("MethodAttributeContainsAssembly");
        var method = host.AddMethod("Run", MethodFlags.Public).GetHandler();

        Assert.That(method.ContainsAttribute<MarkerAttribute>(), Is.False);
        method.AddAttribute(typeof(MarkerAttribute), "hello");
        Assert.That(method.ContainsAttribute<MarkerAttribute>(), Is.True);
    }

    #endregion
}
