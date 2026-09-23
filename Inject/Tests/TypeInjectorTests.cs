using System.Runtime.CompilerServices;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// Tests for adding a type of each kind to the assembly through the handler which adds it, which is a class, a struct and
/// an enum, and for reading the handler of the kind which a type of the assembly is.
/// </summary>
[TestFixture]
public class TypeInjectorTests
{
    private static AssemblyHandler CreateHandler()
        => (AssemblyHandler) Assembly.Create("TestAssembly").Handler;

    #region Struct

    [Test]
    public void AddStruct_Creates_ValueType()
    {
        var handler = CreateHandler();
        var structHandler = handler.AddStruct("MyStruct", NS, StructFlags.Public).GetHandler();

        Assert.That(structHandler, Is.Not.Null);
        Assert.That(structHandler, Is.InstanceOf<IStructHandler>());
        Assert.Multiple(() =>
        {
            Assert.That(structHandler.Name, Is.EqualTo("MyStruct"));
            Assert.That(structHandler.Namespace, Is.EqualTo(NS));
        });
        var def = ((StructHandler) structHandler).Source;
        Assert.Multiple(() =>
        {
            Assert.That(def.IsValueType, Is.True);
            Assert.That(def.BaseType!.FullName, Is.EqualTo(typeof(ValueType).FullName));
        });
    }

    [Test]
    public void AddStruct_Appends_To_Module()
    {
        var handler = CreateHandler();
        handler.AddStruct("Appended", NS, StructFlags.Public).GetHandler();

        var found = handler.GetType($"{NS}.Appended");
        Assert.That(found, Is.Not.Null);
        Assert.That(found, Is.InstanceOf<IStructHandler>());
    }

    [Test]
    public void AddStruct_Redefined_Throws()
    {
        var handler = CreateHandler();
        handler.AddStruct("Dup", NS, StructFlags.Public).GetHandler();

        Assert.Throws<WeavingException>(() => handler.AddStruct("Dup", NS, StructFlags.Public));
    }

    [Test]
    public void AddStruct_Ref_Applies_IsByRefLike_And_Obsolete()
    {
        var handler = CreateHandler();
        var structHandler = handler.AddStruct("RefStruct", NS, StructFlags.Public | StructFlags.Ref).GetHandler();

        var def = ((StructHandler) structHandler).Source;
        Assert.Multiple(() =>
        {
            Assert.That(def.CustomAttributes.Any(a => a.AttributeType.Name == nameof(IsByRefLikeAttribute)), Is.True);
            Assert.That(def.CustomAttributes.Any(a => a.AttributeType.Name == nameof(ObsoleteAttribute)), Is.True);
        });
    }

    [Test]
    public void AddStruct_ReadOnly_Applies_IsReadOnly()
    {
        var handler = CreateHandler();
        var structHandler = handler.AddStruct("RoStruct", NS, StructFlags.Public | StructFlags.ReadOnly).GetHandler();

        var def = ((StructHandler) structHandler).Source;
        Assert.That(def.CustomAttributes.Any(a => a.AttributeType.Name == nameof(IsReadOnlyAttribute)), Is.True);
    }

    [Test]
    public void AddStruct_WithInterface_Type_Adds_Interface()
    {
        var handler = CreateHandler();
        var structHandler = handler.AddStruct("MyStruct", NS, StructFlags.Public)
                                   .WithInterface(typeof(IDisposable))
                                   .GetHandler();

        var def = ((StructHandler) structHandler).Source;
        Assert.That(def.Interfaces.Any(i => i.InterfaceType.FullName == typeof(IDisposable).FullName), Is.True);
    }

    [Test]
    public void AddStruct_WithInterface_IType_Adds_Interface()
    {
        var handler = CreateHandler();
        var structHandler = handler.AddStruct("MyStruct", NS, StructFlags.Public)
                                   .WithInterface(typeof(IComparable).ToGneedleType())
                                   .GetHandler();

        var def = ((StructHandler) structHandler).Source;
        Assert.That(def.Interfaces.Any(i => i.InterfaceType.FullName == typeof(IComparable).FullName), Is.True);
    }

    #endregion

    #region Class

    [Test]
    public void AddClass_Creates_ReferenceType()
    {
        var handler = CreateHandler();
        var classHandler = handler.AddClass("MyClass", NS, ClassFlags.Public).GetHandler();

        Assert.That(classHandler, Is.InstanceOf<IClassHandler>());
        Assert.That(classHandler.Name, Is.EqualTo("MyClass"));

        var def = ((ClassHandler) classHandler).Source;
        Assert.Multiple(() =>
        {
            Assert.That(def.IsClass, Is.True);
            Assert.That(def.IsValueType, Is.False);
            Assert.That(def.BaseType!.FullName, Is.EqualTo(typeof(object).FullName));
        });
    }

    [Test]
    public void AddClass_Redefined_Throws()
    {
        var handler = CreateHandler();
        handler.AddClass("Dup", NS, ClassFlags.Public).GetHandler();

        Assert.Throws<WeavingException>(() => handler.AddClass("Dup", NS, ClassFlags.Public));
    }

    #endregion

    #region Enum

    [Test]
    public void AddEnum_Creates_Enum_With_Int_Underlying_Type()
    {
        var handler = CreateHandler();
        var enumHandler = handler.AddEnum("MyEnum", NS, EnumFlags.Public).GetHandler();

        var def = ((EnumHandler) enumHandler).Source;
        Assert.Multiple(() =>
        {
            Assert.That(def.IsEnum, Is.True);
            Assert.That(def.IsValueType, Is.True);
            Assert.That(def.BaseType!.FullName, Is.EqualTo(typeof(Enum).FullName));
            Assert.That(def.Fields.Any(f => f.Name == "value__" && f.FieldType.FullName == typeof(int).FullName), Is.True);
        });
    }

    [Test]
    public void AddEnum_WithUnderlyingType_Sets_Value_Field_Type()
    {
        var handler = CreateHandler();
        var enumHandler = handler.AddEnum("MyEnum", NS, EnumFlags.Public)
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
        var enumHandler = handler.AddEnum("MyEnum", NS, EnumFlags.Public)
                                 .WithFlagsAttribute()
                                 .GetHandler();

        var def = ((EnumHandler) enumHandler).Source;
        Assert.That(def.CustomAttributes.Any(a => a.AttributeType.Name == nameof(FlagsAttribute)), Is.True);
    }

    [Test]
    public void AddEnum_Redefined_Throws()
    {
        var handler = CreateHandler();
        handler.AddEnum("Dup", NS, EnumFlags.Public).GetHandler();

        Assert.Throws<WeavingException>(() => handler.AddEnum("Dup", NS, EnumFlags.Public));
    }

    [Test]
    public void AddEnum_Appends_To_Module()
    {
        var handler = CreateHandler();
        handler.AddEnum("MyEnum", NS, EnumFlags.Public).GetHandler();

        var found = handler.GetType($"{NS}.MyEnum");
        Assert.That(found, Is.Not.Null);
    }

    #endregion

    #region GetType

    [Test]
    public void GetType_Returns_StructHandler_For_ValueType()
    {
        var handler = CreateHandler();
        handler.AddStruct("SomeStruct", NS, StructFlags.Public).GetHandler();

        var found = handler.GetType($"{NS}.SomeStruct");
        Assert.That(found, Is.InstanceOf<IStructHandler>());
        Assert.That(found, Is.Not.InstanceOf<IClassHandler>());
    }

    [Test]
    public void GetType_Returns_ClassHandler_For_ReferenceType()
    {
        var handler = CreateHandler();
        handler.AddClass("SomeClass", NS, ClassFlags.Public).GetHandler();

        var found = handler.GetType($"{NS}.SomeClass");
        Assert.That(found, Is.InstanceOf<IClassHandler>());
    }

    [Test]
    public void GetType_Returns_EnumHandler_For_Enum()
    {
        var handler = CreateHandler();
        handler.AddEnum("MyEnum", NS, EnumFlags.Public).WithUnderlyingType(typeof(int)).GetHandler();

        var found = handler.GetType($"{NS}.MyEnum");
        Assert.That(found, Is.InstanceOf<IEnumHandler>());
    }

    [Test]
    public void GetType_Unknown_Returns_Null()
    {
        var handler = CreateHandler();
        Assert.That(handler.GetType($"{NS}.DoesNotExist"), Is.Null);
    }

    #endregion
}