using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Gneedle.Inject.Test;

[TestFixture]
public class AssemblyHandlerTests
{
    private const string Ns = "Gneedle.Test.Generated";

    [Test]
    public void GetType_Returns_Outer_Type()
    {
        var asm = Assembly.Create("NestedAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var module = asm.Source.MainModule;

        var outer = new TypeDefinition(Ns, "Outer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(outer);

        var result = handler.GetType($"{Ns}.Outer");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<IClassHandler>());
    }

    [Test]
    public void GetType_Returns_Nested_Type()
    {
        var asm = Assembly.Create("NestedAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var module = asm.Source.MainModule;

        var outer = new TypeDefinition(Ns, "Outer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var inner = new TypeDefinition(Ns, "Inner", TypeAttributes.NestedPublic | TypeAttributes.Class, module.TypeSystem.Object) { DeclaringType = outer };
        outer.NestedTypes.Add(inner);
        module.Types.Add(outer);

        // Nested type FullName format: "Namespace.Outer/Namespace.Inner"
        var result = handler.GetType($"{Ns}.Outer/{Ns}.Inner");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<IClassHandler>());
    }

    [Test]
    public void GetType_Returns_Null_For_Nonexistent_Type()
    {
        var asm = Assembly.Create("NestedAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var result = handler.GetType($"{Ns}.DoesNotExist");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetTypes_Includes_Nested_Types()
    {
        var asm = Assembly.Create("NestedAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var module = asm.Source.MainModule;

        var outer = new TypeDefinition(Ns, "Outer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var inner = new TypeDefinition(Ns, "Inner", TypeAttributes.NestedPublic | TypeAttributes.Class, module.TypeSystem.Object) { DeclaringType = outer };
        outer.NestedTypes.Add(inner);
        module.Types.Add(outer);

        var results = handler.GetTypes();

        // GetTypes() returns both top-level and nested types (plus the implicit <Module> type).
        Assert.That(results.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(results.Count(h => h is IClassHandler), Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void GetTypes_With_Filter_Includes_Nested_Types()
    {
        var asm = Assembly.Create("NestedAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var module = asm.Source.MainModule;

        var outer = new TypeDefinition(Ns, "Outer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var inner = new TypeDefinition(Ns, "Inner", TypeAttributes.NestedPublic | TypeAttributes.Class, module.TypeSystem.Object) { DeclaringType = outer };
        outer.NestedTypes.Add(inner);
        module.Types.Add(outer);

        var results = handler.GetTypes(t => t.Namespace == Ns);

        Assert.That(results.Length, Is.EqualTo(2)); // Outer + Inner
    }

    #region GetCecilType

    [Test]
    public void GetCecilType_ByIType_Resolves_A_Type_Of_Another_Assembly()
    {
        // A name alone cannot tell which assembly declares the type, so a type which neither the weaver nor the corlib
        // declares has to be resolved through the System.Type which the IType holds. TestBaseClass lives in the test
        // assembly, so it is neither of them.
        var asm = Assembly.Create("CecilLoaderAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var result = handler.GetCecilType(typeof(TestBaseClass).ToGneedleType());

        Assert.That(result.Definition.FullName, Is.EqualTo(typeof(TestBaseClass).FullName));
    }

    [Test]
    public void GetCecilType_ByFullName_Resolves_The_Types_Of_The_Target_Assembly()
    {
        // A type which the target assembly declares is not loadable by its name, so it is looked up in the module by its
        // full name. The nested types are looked up as well, just like GetType(string) looks them up.
        var asm = Assembly.Create("CecilLoaderAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var module = asm.Source.MainModule;

        var outer = new TypeDefinition(Ns, "Outer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var inner = new TypeDefinition(Ns, "Inner", TypeAttributes.NestedPublic | TypeAttributes.Class, module.TypeSystem.Object) { DeclaringType = outer };
        outer.NestedTypes.Add(inner);
        module.Types.Add(outer);

        Assert.That(handler.GetCecilType($"{Ns}.Outer").Definition.FullName, Is.EqualTo($"{Ns}.Outer"));
        Assert.That(handler.GetCecilType($"{Ns}.Outer/{Ns}.Inner").Definition.FullName, Is.EqualTo($"{Ns}.Outer/{Ns}.Inner"));
    }

    [Test]
    public void GetCecilType_Reference_Is_Owned_By_The_Target_Module()
    {
        // The reference is the one which may be assigned to the target assembly, whichever module declares the type.
        var asm = Assembly.Create("CecilLoaderAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var module = asm.Source.MainModule;

        foreach (var type in new[] { typeof(int), typeof(Guid), typeof(TestBaseClass) })
        {
            var result = handler.GetCecilType(type);

            Assert.That(result.Reference.Module, Is.SameAs(module), $"the reference of {type.FullName} is owned by another module");
            Assert.That(result.Definition.FullName, Is.EqualTo(type.FullName));
        }
    }

    [Test]
    public void GetCecilType_With_Type_Of_An_Assembly_Referencing_The_Target_Throws()
    {
        // A reference cycle cannot be represented in metadata, so it is rejected before the type is imported.
        // The test assembly references Mono.Cecil, so a target assembly which holds the identity of Mono.Cecil makes the
        // reference from the test assembly to the target cyclic.
        var cecilName = typeof(TypeReference).Assembly.GetName();
        var target = Assembly.Create(cecilName.Name!, cecilName.Version!, null, cecilName.GetPublicKeyToken());
        var handler = (AssemblyHandler) target.Handler;

        Assert.Throws<ArgumentException>(() => handler.GetCecilType(typeof(TestBaseClass)));
    }

    #endregion

    #region GetMethodFromType

    private static MethodDefinition AddMethod(TypeDefinition type, string name)
    {
        var method = new MethodDefinition(name, MethodAttributes.Public, type.Module.TypeSystem.Void) { DeclaringType = type };
        method.Body.GetILProcessor().Emit(OpCodes.Ret);
        type.Methods.Add(method);
        return method;
    }

    [Test]
    public void GetMethodFromType_Throws_When_The_Method_Is_Not_Found()
    {
        var asm = Assembly.Create("MethodLookupAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var type = asm.Source.MainModule.Types[0];

        Assert.Throws<ArgumentException>(() => handler.GetMethodFromType(type, "Missing", []));
    }

    [Test]
    public void GetMethodFromType_Returns_Null_When_The_Method_Is_Not_Found_And_It_Is_Told_Not_To_Throw()
    {
        // The constraint lookup walks the constraints of a generic parameter and has to be able to try the next one,
        // which is what the flag is for.
        var asm = Assembly.Create("MethodLookupAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var type = asm.Source.MainModule.Types[0];

        Assert.That(handler.GetMethodFromType(type, "Missing", [], false), Is.Null);
    }

    [Test]
    public void GetMethodFromType_Finds_A_Method_Of_A_Base_Type()
    {
        var asm = Assembly.Create("MethodLookupAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var module = asm.Source.MainModule;

        var baseType = new TypeDefinition(Ns, "Base", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        var derived = new TypeDefinition(Ns, "Derived", TypeAttributes.Public | TypeAttributes.Class, baseType);
        module.Types.Add(baseType);
        module.Types.Add(derived);
        var expected = AddMethod(baseType, "Ping");

        Assert.That(handler.GetMethodFromType(derived, "Ping", []), Is.SameAs(expected));
    }

    #endregion

    #region AddReference

    [Test]
    public void AddReference_With_Assembly_Referencing_The_Target_Throws()
    {
        // A reference cycle cannot be represented in metadata, so it is rejected.
        var target = Assembly.Create("CycleTargetAssembly");
        var other = Assembly.Create("CycleOtherAssembly");

        // Make the other assembly reference the target, which makes the reference cyclic.
        other.Source.MainModule.AssemblyReferences.Add(target.Source.Name);

        var handler = (AssemblyHandler) target.Handler;

        Assert.Throws<ArgumentException>(() => handler.AddReference(other));
    }

    [Test]
    public void AddReference_With_Unreferenced_Assembly_Appends_The_Reference()
    {
        var target = Assembly.Create("ReferenceTargetAssembly");
        var other = Assembly.Create("ReferenceOtherAssembly");

        var handler = (AssemblyHandler) target.Handler;
        handler.AddReference(other);

        Assert.That(target.Source.MainModule.AssemblyReferences.Any(reference => reference.FullName == other.Source.FullName), Is.True);
    }

    #endregion


    #region AddInterface

    [Test]
    public void AddInterface_Adds_The_Interface_To_The_Type()
    {
        var asm = Assembly.Create("HandlerInterfaceAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        host.AddInterface<ITestInterface>();

        Assert.That(host.Source.Interfaces.Count, Is.EqualTo(1));
        Assert.That(host.Source.Interfaces[0].InterfaceType.FullName, Is.EqualTo(typeof(ITestInterface).FullName));
    }

    [Test]
    public void AddInterface_With_An_IType_Adds_The_Interface_To_The_Type()
    {
        var asm = Assembly.Create("HandlerInterfaceITypeAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        host.AddInterface(typeof(ITestInterface).ToGneedleType());

        Assert.That(host.ContainsInterface<ITestInterface>(), Is.True);
    }

    [Test]
    public void AddInterface_Is_Reported_By_ContainsInterface()
    {
        var asm = Assembly.Create("HandlerInterfaceContainsAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        Assert.That(host.ContainsInterface<ITestInterface>(), Is.False);
        host.AddInterface(typeof(ITestInterface));
        Assert.That(host.ContainsInterface<ITestInterface>(), Is.True);
    }

    [Test]
    public void AddInterface_With_A_Type_Which_Is_Not_An_Interface_Throws()
    {
        // A class as the interface of a type is metadata which no loader reads, so it is refused where it is asked for
        // rather than where the assembly which holds it is loaded.
        var asm = Assembly.Create("HandlerInterfaceRefusedAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        Assert.Throws<ArgumentException>(() => host.AddInterface(typeof(TestBaseClass)));
    }

    [Test]
    public void AddInterface_Produces_An_Assembly_Which_Reads_Back()
    {
        // The interface is declared by the assembly which holds these tests, so the reference which is written has to
        // belong to the assembly which is built rather than to the one which declares the interface: a member of
        // another module is written through a reference to it alone.
        var asm = Assembly.Create("HandlerInterfaceReadableAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        host.AddInterface<ITestInterface>();

        using var stream = new MemoryStream();
        asm.SaveTo(stream);
        stream.Position = 0;

        var reread = AssemblyDefinition.ReadAssembly(stream);
        var type = reread.MainModule.GetType($"{Ns}.Host");
        Assert.That(type, Is.Not.Null);
        Assert.That(type!.Interfaces.Count, Is.EqualTo(1));
        Assert.That(type.Interfaces[0].InterfaceType.FullName, Is.EqualTo(typeof(ITestInterface).FullName));
    }

    #endregion
}
