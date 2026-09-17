using System.Linq;
using Mono.Cecil;

namespace Gneedle.Inject.Test;

/// <summary>
/// The queries which a type handler answers with the members of a type and with its base type. A member is asked for by
/// its name, which answers with one of them, or by the flags which it carries, which answers with every one of them: the
/// members which a type declares, in the order in which it declares them, which is what the queries of a type hold, while
/// a member of a base type is reached by its name alone. The base type itself is asked for as well, and a class which
/// derives from nothing is answered with null rather than with a base type of its own.
/// </summary>
[TestFixture]
public class MemberQueryTests
{
    private const string Ns = "Gneedle.Test.Generated";

    /// <summary>
    /// A type handler for a class of an assembly of its own, which is what the members of a test are declared on.
    /// </summary>
    private static (AssemblyHandler Handler, TypeHandler Host, ModuleDefinition Module) NewHost(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        return (handler, host, assembly.Source.MainModule);
    }

    /// <summary>
    /// A method of the type which carries the attributes named.
    /// </summary>
    private static MethodDefinition NewMethod(ModuleDefinition module, string name, MethodAttributes attributes)
        => new(name, attributes, module.TypeSystem.Void);

    /// <summary>
    /// A field of the type which carries the attributes named.
    /// </summary>
    private static FieldDefinition NewField(ModuleDefinition module, string name, FieldAttributes attributes)
        => new(name, attributes, module.TypeSystem.Int32);

    /// <summary>
    /// A property of the type whose accessors carry the attributes named, because the flags of a property are declared
    /// by the accessors rather than by the property.
    /// </summary>
    private static PropertyDefinition NewProperty(TypeHandler host, ModuleDefinition module, string name, MethodAttributes accessorAttributes)
    {
        var property = new PropertyDefinition(name, PropertyAttributes.None, module.TypeSystem.Int32);
        var getter = new MethodDefinition($"get_{name}", accessorAttributes, module.TypeSystem.Int32);
        host.Source.Methods.Add(getter);
        property.GetMethod = getter;
        host.Source.Properties.Add(property);
        return property;
    }

    [Test]
    public void Fields_Are_Answered_In_The_Order_Which_The_Type_Declares_Them()
    {
        var (_, host, module) = NewHost("MemberQueryFieldsAssembly");
        host.Source.Fields.Add(NewField(module, "First", FieldAttributes.Private));
        host.Source.Fields.Add(NewField(module, "Second", FieldAttributes.Public));
        host.Source.Fields.Add(NewField(module, "Third", FieldAttributes.Public | FieldAttributes.Static));

        Assert.That(host.GetFields().Select(field => field.Name), Is.EqualTo(new[] {"First", "Second", "Third"}));
    }

    [Test]
    public void Fields_Are_Filtered_By_The_Flags_Which_They_Carry()
    {
        var (_, host, module) = NewHost("MemberQueryFieldFlagsAssembly");
        host.Source.Fields.Add(NewField(module, "First", FieldAttributes.Private));
        host.Source.Fields.Add(NewField(module, "Second", FieldAttributes.Public));
        host.Source.Fields.Add(NewField(module, "Third", FieldAttributes.Public | FieldAttributes.Static));

        Assert.Multiple(() =>
        {
            // Every flag which is asked for has to be carried by the field, so the two flags below ask for the public
            // fields which belong to the type rather than for the fields which carry either of the two.
            Assert.That(host.GetFields(FieldFlags.Public).Select(field => field.Name), Is.EqualTo(new[] {"Second", "Third"}));
            Assert.That(host.GetFields(FieldFlags.Public | FieldFlags.Static).Select(field => field.Name), Is.EqualTo(new[] {"Third"}));
            Assert.That(host.GetFields(FieldFlags.Static).Select(field => field.Name), Is.EqualTo(new[] {"Third"}));
            Assert.That(host.GetFields(FieldFlags.Protected), Is.Empty);
        });
    }

    [Test]
    public void Methods_Are_Answered_In_The_Order_Which_The_Type_Declares_Them()
    {
        var (_, host, module) = NewHost("MemberQueryMethodsAssembly");
        host.Source.Methods.Add(NewMethod(module, "First", MethodAttributes.Public));
        host.Source.Methods.Add(NewMethod(module, "Second", MethodAttributes.Private));
        host.Source.Methods.Add(NewMethod(module, "First", MethodAttributes.Public | MethodAttributes.Static));

        Assert.Multiple(() =>
        {
            // The two methods of the one name are two methods, which is what the plural query answers with where the one
            // which reads a name alone has to pick one of them.
            Assert.That(host.GetMethods().Select(method => method.Name), Is.EqualTo(new[] {"First", "Second", "First"}));
            Assert.That(host.GetMethods(), Has.Length.EqualTo(3));
        });
    }

    [Test]
    public void Methods_Are_Filtered_By_The_Flags_Which_They_Carry()
    {
        var (_, host, module) = NewHost("MemberQueryMethodFlagsAssembly");
        host.Source.Methods.Add(NewMethod(module, "Instance", MethodAttributes.Public));
        host.Source.Methods.Add(NewMethod(module, "Static", MethodAttributes.Public | MethodAttributes.Static));
        host.Source.Methods.Add(NewMethod(module, "Overridable", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot));
        host.Source.Methods.Add(NewMethod(module, "Abstract", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot));

        Assert.Multiple(() =>
        {
            Assert.That(host.GetMethods(MethodFlags.Static).Select(method => method.Name), Is.EqualTo(new[] {"Static"}));
            Assert.That(host.GetMethods(MethodFlags.Virtual).Select(method => method.Name), Is.EqualTo(new[] {"Overridable"}));
            Assert.That(host.GetMethods(MethodFlags.Abstract).Select(method => method.Name), Is.EqualTo(new[] {"Abstract"}));
            Assert.That(host.GetMethods(MethodFlags.Public).Select(method => method.Name), Is.EqualTo(new[] {"Instance", "Static", "Overridable", "Abstract"}));

            // An abstract method carries the virtual shape with it in the metadata, and the flags name it by the
            // narrower of the two shapes, which is the way in which the abstract and the sealed shape of a static class
            // are read back as the static flag: a query of the abstract methods answers with it, a query of the virtual
            // ones does not, and the two flags together name no method at all.
            Assert.That(host.GetMethods(MethodFlags.Virtual | MethodFlags.Abstract), Is.Empty);
            Assert.That(host.GetMethods(MethodFlags.Protected), Is.Empty);
        });
    }

    [Test]
    public void Properties_Are_Filtered_By_The_Flags_Of_Their_Accessors()
    {
        var (_, host, module) = NewHost("MemberQueryPropertyFlagsAssembly");
        NewProperty(host, module, "Count", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
        NewProperty(host, module, "Total", MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig);

        Assert.Multiple(() =>
        {
            Assert.That(host.GetProperties().Select(property => property.Name), Is.EqualTo(new[] {"Count", "Total"}));
            Assert.That(host.GetProperties(PropertyFlags.Public).Select(property => property.Name), Is.EqualTo(new[] {"Count"}));
            Assert.That(host.GetProperties(PropertyFlags.Static).Select(property => property.Name), Is.EqualTo(new[] {"Total"}));
            Assert.That(host.GetProperties(PropertyFlags.Public | PropertyFlags.Static), Is.Empty);
        });
    }

    [Test]
    public void A_Type_Which_Declares_No_Member_Is_Answered_With_None()
    {
        var (_, host, _) = NewHost("MemberQueryEmptyAssembly");

        Assert.Multiple(() =>
        {
            Assert.That(host.GetFields(), Is.Not.Null);
            Assert.That(host.GetFields(), Is.Empty);
            Assert.That(host.GetMethods(), Is.Not.Null);
            Assert.That(host.GetMethods(), Is.Empty);
            Assert.That(host.GetProperties(), Is.Not.Null);
            Assert.That(host.GetProperties(), Is.Empty);
        });
    }

    [Test]
    public void The_Members_Which_A_Base_Type_Declares_Are_Found_By_Name_And_Left_Out_Of_The_Plural_Queries()
    {
        // The two ways of asking for a member mean different things, which one test reads together: a lookup of a name
        // walks the base types and answers with what it finds there, while a plural query answers with what the type
        // itself declares, which is the fields and the properties it holds rather than the ones it inherits.
        var (handler, _, module) = NewHost("MemberQueryInheritedAssembly");
        var baseType = new TypeDefinition(Ns, "Base", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        baseType.Fields.Add(NewField(module, "InheritedField", FieldAttributes.Private));
        baseType.Properties.Add(new PropertyDefinition("InheritedProperty", PropertyAttributes.None, module.TypeSystem.Int32));
        var derived = new TypeDefinition(Ns, "Derived", TypeAttributes.Public | TypeAttributes.Class, baseType);
        derived.Fields.Add(NewField(module, "OwnField", FieldAttributes.Private));
        module.Types.Add(baseType);
        module.Types.Add(derived);

        var host = (TypeHandler) handler.GetType(derived);

        Assert.Multiple(() =>
        {
            Assert.That(host.GetField("InheritedField"), Is.Not.Null);
            Assert.That(host.GetProperty("InheritedProperty"), Is.Not.Null);
            Assert.That(host.GetFields().Select(field => field.Name), Is.EqualTo(new[] {"OwnField"}));
            Assert.That(host.GetProperties(), Is.Empty);
        });
    }

    [Test]
    public void The_Base_Type_Of_A_Class_Is_Answered_With_A_Handler_Of_It()
    {
        var (handler, _, module) = NewHost("MemberQueryBaseTypeAssembly");
        var baseType = new TypeDefinition(Ns, "Base", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var derived = new TypeDefinition(Ns, "Derived", TypeAttributes.Public | TypeAttributes.Class, baseType);
        module.Types.Add(baseType);
        module.Types.Add(derived);

        var host = (IClassHandler) handler.GetType(derived);

        Assert.That(host.BaseType, Is.Not.Null);
        Assert.That(host.BaseType!.Name, Is.EqualTo("Base"), "the handler is not one of the type which the class derives from.");
    }

    [Test]
    public void The_Base_Type_Of_A_Class_Which_Has_None_Is_Answered_With_Null()
    {
        // A class which derives from nothing is a hierarchy of its own rather than a mistake, so the query answers with
        // null for it: the type which a module declares without a base type is one, and so is the type which the runtime
        // declares as the root of every hierarchy.
        var (handler, _, module) = NewHost("MemberQueryNoBaseTypeAssembly");
        var root = new TypeDefinition(Ns, "Root", TypeAttributes.Public | TypeAttributes.Class);
        module.Types.Add(root);

        var host = (IClassHandler) handler.GetType(root);

        Assert.Multiple(() =>
        {
            Assert.That(host.BaseType, Is.Null, "a class of the module which derives from nothing is not answered with null.");
            Assert.That(((IClassHandler) handler.GetType(typeof(object))).BaseType, Is.Null, "the root of every hierarchy is not answered with null.");
        });
    }

    [Test]
    public void The_Plural_Queries_Are_Of_The_Type_Handler_And_Of_The_Container_Of_The_Member()
    {
        // The same query is declared by the type handler and by the container of the kind of member which it names, so a
        // caller which holds either of them asks for the members of the type, and both answer with the same ones.
        var (handler, host, module) = NewHost("MemberQueryContainersAssembly");
        host.Source.Fields.Add(NewField(module, "First", FieldAttributes.Private));
        host.Source.Methods.Add(NewMethod(module, "Run", MethodAttributes.Public));
        NewProperty(host, module, "Count", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);

        var typeHandler = (ITypeHandler) host;

        Assert.Multiple(() =>
        {
            Assert.That(typeHandler.GetFields().Select(field => field.Name), Is.EqualTo(new[] {"First"}));
            Assert.That(((IFieldContainer) handler.GetType(host.Source)).GetFields().Select(field => field.Name), Is.EqualTo(new[] {"First"}));
            Assert.That(typeHandler.GetMethods().Select(method => method.Name), Is.EqualTo(new[] {"Run", "get_Count"}));
            Assert.That(((IMethodContainer) host).GetMethods().Select(method => method.Name), Is.EqualTo(new[] {"Run", "get_Count"}));
            Assert.That(typeHandler.GetProperties().Select(property => property.Name), Is.EqualTo(new[] {"Count"}));
            Assert.That(((IPropertyContainer) host).GetProperties().Select(property => property.Name), Is.EqualTo(new[] {"Count"}));
        });
    }

    [Test]
    public void The_Queries_Are_Answered_To_A_Caller_Which_Holds_The_Shape_Of_The_Type()
    {
        // The shape of a class is the type handler and the container of every kind of member at once, so a caller which
        // holds that shape asks the type for the members of every kind without naming the kind first. The queries of the
        // two shapes are one declaration which both paths reach rather than a declaration of each of them, because a
        // member which is declared twice is a call which cannot be bound to either, and the shape of the handler which a
        // caller holds is not something a query of a type should depend on.
        var (handler, host, module) = NewHost("MemberQueryHandlerShapeAssembly");
        host.Source.Fields.Add(NewField(module, "First", FieldAttributes.Private));
        host.Source.Methods.Add(NewMethod(module, "Run", MethodAttributes.Public));
        NewProperty(host, module, "Count", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);

        var classHandler = (IClassHandler) handler.GetType(host.Source);

        Assert.Multiple(() =>
        {
            Assert.That(classHandler.GetFields().Select(field => field.Name), Is.EqualTo(new[] {"First"}));
            Assert.That(classHandler.GetField("First"), Is.Not.Null);
            Assert.That(classHandler.GetMethods().Select(method => method.Name), Is.EqualTo(new[] {"Run", "get_Count"}));
            Assert.That(classHandler.GetMethod("Run"), Is.Not.Null);
            Assert.That(classHandler.GetProperties().Select(property => property.Name), Is.EqualTo(new[] {"Count"}));
            Assert.That(classHandler.GetProperty("Count"), Is.Not.Null);
            Assert.That(classHandler.ContainsInterface(typeof(IDisposable).ToGneedleType()), Is.False);
        });
    }
}