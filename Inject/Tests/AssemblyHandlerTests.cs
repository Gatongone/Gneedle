using Mono.Cecil;

namespace Gneedle.Inject.Test;

public interface ITestInterface { }
public class TestBaseClass { }

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

    #region ClassDecorator chain combinations

    [Test]
    public void AddClass_WithGenericParameter_Creates_Generic_Class()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("GenericClass", Ns, ClassFlags.Public)
                                                 .WithGenericParameter("T")
                                                 .GetHandler();

        Assert.That(classHandler.Source.GenericParameters.Count, Is.EqualTo(1));
        Assert.That(classHandler.Source.GenericParameters[0].Name, Is.EqualTo("T"));
    }

    [Test]
    public void AddClass_WithBaseType_Sets_Correct_BaseType()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("DerivedClass", Ns, ClassFlags.Public)
                                                 .WithBaseType(typeof(TestBaseClass))
                                                 .GetHandler();

        Assert.That(classHandler.Source.BaseType.FullName, Is.EqualTo(typeof(TestBaseClass).FullName));
    }

    [Test]
    public void AddClass_WithInterface_Adds_Interface_Implementation()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("ImplClass", Ns, ClassFlags.Public)
                                                 .WithInterface(typeof(ITestInterface))
                                                 .GetHandler();

        Assert.That(classHandler.Source.Interfaces.Count, Is.GreaterThan(0));
        Assert.That(classHandler.Source.Interfaces.Any(i => i.InterfaceType.FullName == typeof(ITestInterface).FullName), Is.True);
    }

    [Test]
    public void AddClass_WithGenericParameter_And_BaseType_Creates_Generic_Derived_Class()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("GenericDerived", Ns, ClassFlags.Public)
                                                 .WithGenericParameter("T")
                                                 .WithBaseType(typeof(TestBaseClass))
                                                 .GetHandler();

        Assert.That(classHandler.Source.GenericParameters.Count, Is.EqualTo(1));
        Assert.That(classHandler.Source.BaseType.FullName, Is.EqualTo(typeof(TestBaseClass).FullName));
    }

    [Test]
    public void AddClass_WithGenericParameter_And_Interface_Creates_Generic_Class_With_Interface()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("GenericImpl", Ns, ClassFlags.Public)
                                                 .WithGenericParameter("T")
                                                 .WithInterface(typeof(ITestInterface))
                                                 .GetHandler();

        Assert.That(classHandler.Source.GenericParameters.Count, Is.EqualTo(1));
        Assert.That(classHandler.Source.Interfaces.Any(i => i.InterfaceType.FullName == typeof(ITestInterface).FullName), Is.True);
    }

    [Test]
    public void AddClass_WithBaseType_And_Interface_Creates_Derived_Class_With_Interface()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("DerivedImpl", Ns, ClassFlags.Public)
                                                 .WithBaseType(typeof(TestBaseClass))
                                                 .WithInterface(typeof(ITestInterface))
                                                 .GetHandler();

        Assert.That(classHandler.Source.BaseType.FullName, Is.EqualTo(typeof(TestBaseClass).FullName));
        Assert.That(classHandler.Source.Interfaces.Any(i => i.InterfaceType.FullName == typeof(ITestInterface).FullName), Is.True);
    }

    [Test]
    public void AddClass_WithAllDecorators_Creates_Generic_Derived_Class_With_Interface()
    {
        var asm = Assembly.Create("DecoratorAssembly");
        var handler = (AssemblyHandler) asm.Handler;

        var classHandler = (ClassHandler) handler.AddClass("FullyDecoratedClass", Ns, ClassFlags.Public)
                                                 .WithGenericParameter("T")
                                                 .WithGenericParameter("U")
                                                 .WithBaseType(typeof(TestBaseClass))
                                                 .WithInterface(typeof(ITestInterface))
                                                 .GetHandler();

        Assert.That(classHandler.Source.GenericParameters.Count, Is.EqualTo(2));
        Assert.That(classHandler.Source.BaseType.FullName, Is.EqualTo(typeof(TestBaseClass).FullName));
        Assert.That(classHandler.Source.Interfaces.Any(i => i.InterfaceType.FullName == typeof(ITestInterface).FullName), Is.True);
    }

    #endregion
}
