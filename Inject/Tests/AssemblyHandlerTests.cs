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

    // region ClassDecorator chain combinations

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

    // endregion
}
