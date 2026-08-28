using System.Runtime.CompilerServices;

namespace Gneedle.Inject.Test;

[TestFixture]
public class TypeInjectorTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static AssemblyHandler CreateHandler()
        => (AssemblyHandler) Assembly.Create("TestAssembly").Handler;

    [Test]
    public void AddStruct_Creates_ValueType()
    {
        var handler = CreateHandler();
        var structHandler = handler.AddStruct("MyStruct", Ns, StructFlags.Public).GetHandler();

        Assert.That(structHandler, Is.Not.Null);
        Assert.That(structHandler, Is.InstanceOf<IStructHandler>());
        Assert.Multiple(() =>
        {
            Assert.That(structHandler.Name, Is.EqualTo("MyStruct"));
            Assert.That(structHandler.Namespace, Is.EqualTo(Ns));
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
        handler.AddStruct("Appended", Ns, StructFlags.Public).GetHandler();

        var found = handler.GetType($"{Ns}.Appended");
        Assert.That(found, Is.Not.Null);
        Assert.That(found, Is.InstanceOf<IStructHandler>());
    }

    [Test]
    public void AddStruct_Redefined_Throws()
    {
        var handler = CreateHandler();
        handler.AddStruct("Dup", Ns, StructFlags.Public).GetHandler();

        Assert.Catch<ArgumentException>(() => handler.AddStruct("Dup", Ns, StructFlags.Public));
    }

    [Test]
    public void AddStruct_Ref_Applies_IsByRefLike_And_Obsolete()
    {
        var handler = CreateHandler();
        var structHandler = handler.AddStruct("RefStruct", Ns, StructFlags.Public | StructFlags.Ref).GetHandler();

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
        var structHandler = handler.AddStruct("RoStruct", Ns, StructFlags.Public | StructFlags.ReadOnly).GetHandler();

        var def = ((StructHandler) structHandler).Source;
        Assert.That(def.CustomAttributes.Any(a => a.AttributeType.Name == nameof(IsReadOnlyAttribute)), Is.True);
    }

    [Test]
    public void AddClass_Creates_ReferenceType()
    {
        var handler = CreateHandler();
        var classHandler = handler.AddClass("MyClass", Ns, ClassFlags.Public).GetHandler();

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
        handler.AddClass("Dup", Ns, ClassFlags.Public).GetHandler();

        Assert.Catch<ArgumentException>(() => handler.AddClass("Dup", Ns, ClassFlags.Public));
    }

    [Test]
    public void GetType_Returns_StructHandler_For_ValueType()
    {
        var handler = CreateHandler();
        handler.AddStruct("SomeStruct", Ns, StructFlags.Public).GetHandler();

        var found = handler.GetType($"{Ns}.SomeStruct");
        Assert.That(found, Is.InstanceOf<IStructHandler>());
        Assert.That(found, Is.Not.InstanceOf<IClassHandler>());
    }

    [Test]
    public void GetType_Returns_ClassHandler_For_ReferenceType()
    {
        var handler = CreateHandler();
        handler.AddClass("SomeClass", Ns, ClassFlags.Public).GetHandler();

        var found = handler.GetType($"{Ns}.SomeClass");
        Assert.That(found, Is.InstanceOf<IClassHandler>());
    }

    [Test]
    public void GetType_Unknown_Returns_Null()
    {
        var handler = CreateHandler();
        Assert.That(handler.GetType($"{Ns}.DoesNotExist"), Is.Null);
    }
}
