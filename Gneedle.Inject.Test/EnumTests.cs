using System.Linq;
using Mono.Cecil;

namespace Gneedle.Inject.Test;

[TestFixture]
public class EnumTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static AssemblyHandler CreateHandler()
        => (AssemblyHandler) Assembly.Create("EnumTestAssembly").Handler;

    [Test]
    public void AddEnum_Creates_Enum_With_Int_Underlying_Type()
    {
        var handler = CreateHandler();
        var enumHandler = handler.AddEnum("MyEnum", Ns, EnumFlags.Public).GetHandler();

        var def = ((EnumHandler) enumHandler).Source;
        Assert.That(def.IsEnum, Is.True);
        Assert.That(def.IsValueType, Is.True);
        Assert.That(def.BaseType!.FullName, Is.EqualTo(typeof(System.Enum).FullName));
        Assert.That(def.Fields.Any(f => f.Name == "value__" && f.FieldType.FullName == typeof(int).FullName), Is.True);
    }

    [Test]
    public void AddEnum_WithUnderlyingType_Sets_Value_Field_Type()
    {
        var handler = CreateHandler();
        var enumHandler = handler.AddEnum("MyEnum", Ns, EnumFlags.Public)
                                 .WithUnderlyingType(typeof(byte))
                                 .GetHandler();

        var def = ((EnumHandler) enumHandler).Source;
        var valueField = def.Fields.First(f => f.Name == "value__");
        Assert.That(valueField.FieldType.FullName, Is.EqualTo(typeof(byte).FullName));
    }

    [Test]
    public void AddEnum_WithFlagsAttribute_Adds_FlagsAttribute()
    {
        var handler = CreateHandler();
        var enumHandler = handler.AddEnum("MyEnum", Ns, EnumFlags.Public)
                                 .WithFlagsAttribute()
                                 .GetHandler();

        var def = ((EnumHandler) enumHandler).Source;
        Assert.That(def.CustomAttributes.Any(a => a.AttributeType.Name == nameof(FlagsAttribute)), Is.True);
    }

    [Test]
    public void AddEnum_Redefined_Throws()
    {
        var handler = CreateHandler();
        handler.AddEnum("Dup", Ns, EnumFlags.Public).GetHandler();

        Assert.Catch<System.ArgumentException>(() => handler.AddEnum("Dup", Ns, EnumFlags.Public));
    }

    [Test]
    public void GetType_Returns_EnumHandler_For_Enum()
    {
        var handler = CreateHandler();
        handler.AddEnum("MyEnum", Ns, EnumFlags.Public).WithUnderlyingType(typeof(int)).GetHandler();

        var found = handler.GetType($"{Ns}.MyEnum");
        Assert.That(found, Is.InstanceOf<IEnumHandler>());
    }

    [Test]
    public void AddEnum_Appends_To_Module()
    {
        var handler = CreateHandler();
        handler.AddEnum("MyEnum", Ns, EnumFlags.Public).GetHandler();

        var found = handler.GetType($"{Ns}.MyEnum");
        Assert.That(found, Is.Not.Null);
    }
}