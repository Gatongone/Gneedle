using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// The flags which a handler reads back off the definition which it handles, which is the inverse of what the flags
/// write into it: a class which was declared static is read back as <see cref="ClassFlags.Static"/> rather than as the
/// abstract and sealed shape which that flag is written as, and a struct which was declared a ref struct is read back
/// as <see cref="StructFlags.Ref"/> rather than as the attribute which marks it. The two directions are read together
/// here, by writing a definition with a set of flags and asking the handler of it what the definition declares.
/// </summary>
[TestFixture]
public class HandlerFlagsTests
{
    /// <summary>
    /// The handler of a class of the attributes named, declared at the top of an assembly of its own. Every test asks
    /// for an assembly which is named after what it builds, because two assemblies of one name are one assembly to a
    /// runtime which loads both.
    /// </summary>
    private static ClassHandler NewClass(string assemblyName, TypeAttributes attributes)
    {
        var assembly = Assembly.Create(assemblyName);
        var handler = (AssemblyHandler)assembly.Handler;
        var module = assembly.Source.MainModule;

        var type = new TypeDefinition(NS, "Host", attributes, module.TypeSystem.Object);
        module.Types.Add(type);

        return (ClassHandler)handler.GetType(type);
    }

    /// <summary>
    /// The handler of a class nested in a type of an assembly of its own, which is where the visibility of a type is
    /// declared as the nested shape of it rather than as the public one or the one which names no visibility at all.
    /// </summary>
    private static ClassHandler NewNestedClass(string assemblyName, TypeAttributes visibility)
    {
        var assembly = Assembly.Create(assemblyName);
        var handler = (AssemblyHandler)assembly.Handler;
        var module = assembly.Source.MainModule;

        var outer = new TypeDefinition(NS, "Outer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var inner = new TypeDefinition(NS, "Inner", visibility | TypeAttributes.Class, module.TypeSystem.Object) { DeclaringType = outer };
        outer.NestedTypes.Add(inner);
        module.Types.Add(outer);

        return (ClassHandler)handler.GetType(inner);
    }

    #region Class

    [TestCase(TypeAttributes.Public, ClassFlags.Public)]
    [TestCase(TypeAttributes.NotPublic, ClassFlags.Internal)]
    [TestCase(TypeAttributes.Public | TypeAttributes.Abstract, ClassFlags.Public | ClassFlags.Abstract)]
    [TestCase(TypeAttributes.Public | TypeAttributes.Sealed, ClassFlags.Public | ClassFlags.Sealed)]
    [TestCase(TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, ClassFlags.Public | ClassFlags.Static)]
    [TestCase(TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, ClassFlags.Internal | ClassFlags.Static)]
    public void Class_Flags_Name_What_The_Definition_Declares(TypeAttributes attributes, ClassFlags expected)
    {
        Assert.That(NewClass($"FlagsClass{(int)attributes}Assembly", attributes).Flags, Is.EqualTo(expected));
    }

    [TestCase(TypeAttributes.NestedPublic, ClassFlags.Public)]
    [TestCase(TypeAttributes.NestedAssembly, ClassFlags.Internal)]
    [TestCase(TypeAttributes.NestedFamily, ClassFlags.Protected)]
    [TestCase(TypeAttributes.NestedPrivate, ClassFlags.Private)]
    [TestCase(TypeAttributes.NestedFamORAssem, ClassFlags.Protected | ClassFlags.Internal)]
    [TestCase(TypeAttributes.NestedFamANDAssem, ClassFlags.Private | ClassFlags.Protected)]
    public void Nested_Class_Flags_Name_The_Visibility_Which_The_Definition_Declares(TypeAttributes visibility, ClassFlags expected)
    {
        Assert.That(NewNestedClass($"FlagsNestedClass{(int)visibility}Assembly", visibility).Flags, Is.EqualTo(expected));
    }

    [TestCase(ClassFlags.Public)]
    [TestCase(ClassFlags.Internal)]
    [TestCase(ClassFlags.Public | ClassFlags.Static)]
    [TestCase(ClassFlags.Public | ClassFlags.Abstract)]
    [TestCase(ClassFlags.Public | ClassFlags.Sealed)]
    public void Class_Flags_Are_Read_Back_Off_The_Definition_Which_They_Were_Written_Into(ClassFlags classFlags)
    {
        var handler = (AssemblyHandler)Assembly.Create($"FlagsReadBackClass{(int)classFlags}Assembly").Handler;

        Assert.That(handler.AddClass("Host", NS, classFlags).GetHandler().Flags, Is.EqualTo(classFlags));
    }

    #endregion

    #region Struct

    [TestCase(StructFlags.Public)]
    [TestCase(StructFlags.Internal)]
    [TestCase(StructFlags.Public | StructFlags.ReadOnly)]
    [TestCase(StructFlags.Public | StructFlags.Ref)]
    public void Struct_Flags_Are_Read_Back_Off_The_Definition_Which_They_Were_Written_Into(StructFlags structFlags)
    {
        var handler = (AssemblyHandler)Assembly.Create($"FlagsReadBackStruct{(int)structFlags}Assembly").Handler;

        Assert.That(handler.AddStruct("Host", NS, structFlags).GetHandler().Flags, Is.EqualTo(structFlags));
    }

    [Test]
    public void Nested_Struct_Flags_Name_The_Visibility_Which_The_Definition_Declares()
    {
        // The visibility of a nested type is declared as the nested shape of it, which is none of the shapes a struct
        // which is declared at the top of a module is written with.
        var assembly = Assembly.Create("FlagsNestedStructAssembly");
        var handler = (AssemblyHandler)assembly.Handler;
        var module = assembly.Source.MainModule;

        var outer = new TypeDefinition(NS, "Outer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var inner = new TypeDefinition(NS, "Inner", TypeAttributes.NestedAssembly | TypeAttributes.SequentialLayout, module.ImportReference(typeof(ValueType))) { DeclaringType = outer };
        outer.NestedTypes.Add(inner);
        module.Types.Add(outer);
        Assert.Multiple(() =>
        {
            Assert.That(inner.ToStructFlags(), Is.EqualTo(StructFlags.Internal));
            Assert.That(((IStructHandler) handler.GetType(inner)).Flags, Is.EqualTo(StructFlags.Internal));
        });
    }

    #endregion

    #region Enum

    [TestCase(EnumFlags.Public)]
    [TestCase(EnumFlags.Internal)]
    public void Enum_Flags_Are_Read_Back_Off_The_Definition_Which_They_Were_Written_Into(EnumFlags enumFlags)
    {
        var handler = (AssemblyHandler)Assembly.Create($"FlagsReadBackEnum{(int)enumFlags}Assembly").Handler;

        Assert.That(handler.AddEnum("Host", NS, enumFlags).GetHandler().Flags, Is.EqualTo(enumFlags));
    }

    [Test]
    public void Nested_Enum_Flags_Name_The_Visibility_Which_The_Definition_Declares()
    {
        var assembly = Assembly.Create("FlagsNestedEnumAssembly");
        var handler = (AssemblyHandler)assembly.Handler;
        var module = assembly.Source.MainModule;

        var outer = new TypeDefinition(NS, "Outer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var inner = new TypeDefinition(NS, "Inner", TypeAttributes.NestedPublic | TypeAttributes.Sealed, module.ImportReference(typeof(Enum))) { DeclaringType = outer };
        inner.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, module.TypeSystem.Int32));
        outer.NestedTypes.Add(inner);
        module.Types.Add(outer);
        Assert.Multiple(() =>
        {
            Assert.That(inner.ToEnumFlags(), Is.EqualTo(EnumFlags.Public));
            Assert.That(((IEnumHandler) handler.GetType(inner)).Flags, Is.EqualTo(EnumFlags.Public));
        });
    }

    #endregion

    #region Method

    [TestCase(MethodFlags.Public)]
    [TestCase(MethodFlags.Private)]
    [TestCase(MethodFlags.Internal)]
    [TestCase(MethodFlags.Protected)]
    [TestCase(MethodFlags.Public | MethodFlags.Static)]
    [TestCase(MethodFlags.Public | MethodFlags.Virtual)]
    [TestCase(MethodFlags.Public | MethodFlags.Abstract)]
    public void Method_Flags_Are_Read_Back_Off_The_Definition_Which_They_Were_Written_Into(MethodFlags methodFlags)
    {
        var (_, host, _) = NewHost($"FlagsReadBackMethod{(int)methodFlags}Assembly");

        Assert.That(host.AddMethod("Run", methodFlags).GetHandler().Flags, Is.EqualTo(methodFlags));
    }

    [Test]
    public void Method_Flags_Of_A_Definition_Which_Overrides_Are_Those_Of_A_Virtual_Method()
    {
        // An override is a virtual method which takes the slot of another one, which is a difference the flags do not
        // name: the flags say what a method is, not which of the shapes of it the metadata was written with.
        var (_, host, module) = NewHost("FlagsOverrideMethodAssembly");
        var overrideMethod = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, module.TypeSystem.Void);
        host.Source.Methods.Add(overrideMethod);

        Assert.That(host.GetMethod("Run")!.Flags, Is.EqualTo(MethodFlags.Public | MethodFlags.Virtual));
    }

    [Test]
    public void Constructor_Flags_Name_What_The_Definition_Declares()
    {
        var (_, host, _) = NewHost("FlagsConstructorAssembly");

        var staticConstructor = host.AddMethod(".cctor", MethodFlags.Private | MethodFlags.Static).GetHandler();
        var instanceConstructor = host.AddMethod(".ctor", MethodFlags.Public).GetHandler();

        Assert.Multiple(() =>
        {
            // The name and the static flag together are what tells the two constructors apart, both for a reader of the
            // metadata and for the two properties of <c>MethodExtensions</c> which ask which of the two a method is.
            // Those two are extension members, and a caller reads an extension member by being compiled with C# 14:
            // this assembly is built with the language the library is built with, which is the preview one. The flags
            // and the name are asserted alongside them as the two things which the properties are decided by.
            Assert.That(staticConstructor.Flags, Is.EqualTo(MethodFlags.Private | MethodFlags.Static));
            Assert.That(staticConstructor.Name, Is.EqualTo(".cctor"));
            Assert.That(staticConstructor.IsStaticCtor, Is.True);
            Assert.That(staticConstructor.IsInstanceCtor, Is.False);
            Assert.That(instanceConstructor.Flags, Is.EqualTo(MethodFlags.Public));
            Assert.That(instanceConstructor.Name, Is.EqualTo(".ctor"));
            Assert.That(instanceConstructor.IsInstanceCtor, Is.True);
            Assert.That(instanceConstructor.IsStaticCtor, Is.False);
        });
    }

    [Test]
    public void Constructor_Flags_Of_A_Definition_Which_Was_Compiled_Name_The_Modifiers_Of_It()
    {
        // The attributes below are the ones a compiler writes for a constructor, which are none of the shapes the
        // decorator writes: the static constructor of a type is private and belongs to the type, while the one of an
        // instance is public and belongs to it, and the two names are the ones which tell them apart.
        var (_, host, module) = NewHost("FlagsCompiledConstructorAssembly");
        host.Source.Methods.Add(new MethodDefinition(".cctor", MethodAttributes.Private | MethodAttributes.Static |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void));
        host.Source.Methods.Add(new MethodDefinition(".ctor", MethodAttributes.Public |
            MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void));

        Assert.Multiple(() =>
        {
            Assert.That(host.GetMethod(".cctor")!.Flags, Is.EqualTo(MethodFlags.Private | MethodFlags.Static));
            Assert.That(host.GetMethod(".ctor")!.Flags, Is.EqualTo(MethodFlags.Public));
        });
    }

    #endregion

    #region Field

    [Test]
    public void Field_Flags_Name_What_The_Definition_Declares()
    {
        var (_, host, module) = NewHost("FlagsFieldAssembly");
        host.Source.Fields.Add(new FieldDefinition("Counter", FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly, module.TypeSystem.Int32));
        host.Source.Fields.Add(new FieldDefinition("Value", FieldAttributes.Public, module.TypeSystem.Int32));

        Assert.Multiple(() =>
        {
            Assert.That(host.GetField("Counter")!.Flags, Is.EqualTo(FieldFlags.Private | FieldFlags.Static | FieldFlags.ReadOnly));
            Assert.That(host.GetField("Value")!.Flags, Is.EqualTo(FieldFlags.Public));
        });
    }

    #endregion

    #region Property

    /// <summary>
    /// A property of the host whose accessor carries the attributes named, which is where the flags of a property are
    /// declared: a property holds no attributes of its own.
    /// </summary>
    private static IPropertyHandler NewProperty(TypeHandler host, ModuleDefinition module, string name, MethodAttributes accessorAttributes, bool withGetter = true, bool withSetter = true)
    {
        var property = new PropertyDefinition(name, PropertyAttributes.None, module.TypeSystem.Int32);
        if (withGetter)
        {
            var getter = new MethodDefinition($"get_{name}", accessorAttributes, module.TypeSystem.Int32);
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            host.Source.Methods.Add(getter);
            property.GetMethod = getter;
        }

        if (withSetter)
        {
            var setter = new MethodDefinition($"set_{name}", accessorAttributes, module.TypeSystem.Void);
            setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
            setter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            host.Source.Methods.Add(setter);
            property.SetMethod = setter;
        }

        host.Source.Properties.Add(property);
        return host.GetProperty(name)!;
    }

    [Test]
    public void Property_Flags_Name_What_The_Accessor_Declares()
    {
        var (_, host, module) = NewHost("FlagsPropertyAssembly");

        var property = NewProperty(host, module, "Count", MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig);

        Assert.That(property.Flags, Is.EqualTo(PropertyFlags.Internal | PropertyFlags.Static));
    }

    [Test]
    public void Property_Flags_Are_Read_Off_The_Setter_When_The_Property_Holds_No_Getter()
    {
        var (_, host, module) = NewHost("FlagsSetterOnlyPropertyAssembly");

        var property = NewProperty(host, module, "Value", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, withGetter: false);

        Assert.That(property.Flags, Is.EqualTo(PropertyFlags.Public));
    }

    [Test]
    public void Property_Flags_Are_Read_Back_Off_The_Definition_Which_They_Were_Written_Into()
    {
        var (_, host, _) = NewHost("FlagsReadBackPropertyAssembly");

        var property = host.AddProperty("Count", PropertyFlags.Internal | PropertyFlags.Static)
                           .WithType(typeof(int))
                           .WithGetter(() => 0)
                           .GetHandler();

        Assert.That(property.Flags, Is.EqualTo(PropertyFlags.Internal | PropertyFlags.Static));
    }

    #endregion
}