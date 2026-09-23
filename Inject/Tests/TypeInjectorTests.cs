using System.Runtime.CompilerServices;
using TypeAttributes = Mono.Cecil.TypeAttributes;

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

    #region A type which is nested in another

    [Test]
    public void AddNestedClass_Declares_The_Class_In_The_Type_And_It_Reads_Back_With_The_Flags_It_Was_Declared_With()
    {
        // A nested class is declared with the same flags a class at the top of a module is, and the two are written
        // into the metadata in forms which are not the same: what is read back of it here is what was declared, which
        // is what tells the two halves of that apart from each other.
        var handler = CreateHandler();
        var outer = (IClassHandler) handler.AddClass("Outer", NS, ClassFlags.Public).GetHandler();
        var inner = outer.AddNestedClass("Inner", ClassFlags.Private).GetHandler();

        var def = ((ClassHandler) inner).Source;
        Assert.Multiple(() =>
        {
            Assert.That(def.IsNested, Is.True, "the class was not declared in the type.");
            Assert.That(def.DeclaringType!.Name, Is.EqualTo("Outer"));
            Assert.That(def.Attributes & TypeAttributes.VisibilityMask, Is.EqualTo(TypeAttributes.NestedPrivate),
                "the visibility was not written in the nested form of it.");
            Assert.That(inner.Flags, Is.EqualTo(ClassFlags.Private), "the flags do not read back as the ones declared with.");
        });
    }

    [Test]
    public void AddNestedStruct_Declares_The_Struct_In_The_Type_And_It_Reads_Back_With_The_Flags_It_Was_Declared_With()
    {
        var handler = CreateHandler();
        var outer = (IClassHandler) handler.AddClass("Outer", NS, ClassFlags.Public).GetHandler();
        var inner = outer.AddNestedStruct("Inner", StructFlags.Internal).GetHandler();

        var def = ((StructHandler) inner).Source;
        Assert.Multiple(() =>
        {
            Assert.That(def.IsNested, Is.True, "the struct was not declared in the type.");
            Assert.That(def.IsValueType, Is.True);
            Assert.That(def.Attributes & TypeAttributes.VisibilityMask, Is.EqualTo(TypeAttributes.NestedAssembly),
                "the visibility was not written in the nested form of it.");
            Assert.That(inner.Flags, Is.EqualTo(StructFlags.Internal), "the flags do not read back as the ones declared with.");
        });
    }

    [Test]
    public void AddNestedEnum_Declares_The_Enum_In_The_Type_And_It_Reads_Back_With_The_Flags_It_Was_Declared_With()
    {
        var handler = CreateHandler();
        var outer = (IClassHandler) handler.AddClass("Outer", NS, ClassFlags.Public).GetHandler();
        var inner = outer.AddNestedEnum("Inner", EnumFlags.Public).GetHandler();

        var def = ((EnumHandler) inner).Source;
        Assert.Multiple(() =>
        {
            Assert.That(def.IsNested, Is.True, "the enum was not declared in the type.");
            Assert.That(def.IsEnum, Is.True);
            Assert.That(def.Attributes & TypeAttributes.VisibilityMask, Is.EqualTo(TypeAttributes.NestedPublic),
                "the visibility was not written in the nested form of it.");
            Assert.That(inner.Flags, Is.EqualTo(EnumFlags.Public), "the flags do not read back as the ones declared with.");
        });
    }

    [Test]
    public void AddNestedClass_Of_A_Name_Which_The_Type_Holds_Throws()
    {
        // The name a nested type is declared with is taken the same way the name of a type at the top of a module is,
        // because what a name is looked up by is what the whole of it is rather than what its last part is.
        var handler = CreateHandler();
        var outer = (IClassHandler) handler.AddClass("Outer", NS, ClassFlags.Public).GetHandler();
        outer.AddNestedClass("Inner").GetHandler();

        Assert.Throws<WeavingException>(() => outer.AddNestedClass("Inner"));
    }

    #endregion
    [Test]
    public void An_Enum_Handler_Is_Not_A_Container_Which_A_Type_May_Be_Declared_In()
    {
        // A class and a struct are the two which C# lets a type stand in, and an enum is not one of them: it holds
        // nothing but its values, and a nested type written inside one is refused by the compiler rather than written
        // by it. What the shape refuses here is refused where a caller reaches for it rather than in an assembly which
        // no C# consumer could have written.
        var handler = CreateHandler();
        var enumHandler = handler.AddEnum("E", NS, EnumFlags.Public).GetHandler();

        Assert.Multiple(() =>
        {
            Assert.That(enumHandler, Is.Not.InstanceOf<INestedTypeContainer>(),
                "the handler of an enum holds a shape which no enum can honour.");
            Assert.That((IClassHandler) handler.AddClass("C", NS, ClassFlags.Public).GetHandler(), Is.InstanceOf<INestedTypeContainer>(),
                "the handler of a class does not hold the shape which a class honours.");
        });
    }
}
