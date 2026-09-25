using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// A stub which names the type it stands for rather than carrying the name of it, so that a template reaches a type of
/// another assembly without a type of its own of that name.
/// </summary>
[FromAssembly("NamedStubTargetAssembly", NS + ".Stub")]
public class AStubOfAnyName
{
    /// <summary>The member which the real type declares.</summary>
    public static int Field;

    /// <summary>The same, of a method.</summary>
    public static int Read() => 0;
}

/// <summary>
/// The same, of a generic type, which is named by the number of parameters the stub declares added to the name.
/// </summary>
[FromAssembly("NamedStubTargetAssembly", NS + ".GenericStub")]
public class AGenericStubOfAnyName<T>
{
    /// <summary>The member which the real type declares.</summary>
    public static int Count;
}

/// <summary>
/// Tests for a stub which names the type it stands for.<para/>
/// A stub is what it stands for by its own full name, which is the namespace of it and its name together, so a template
/// which reaches into a namespace which is not its own declares types in that namespace: what a stub names is the type
/// itself where its own name is not the name of that type, and the stubs of an assembly may then all stand in one
/// namespace of their own.
/// </summary>
[TestFixture]
public class NamedStubTests
{
    /// <summary>
    /// The templates, which live in the test assembly so that Cecil can resolve them from disk.
    /// </summary>
    public static class Templates
    {
        /// <summary>A member of the type which the stub names.</summary>
        public static int ReadThroughTheNamedStub() => AStubOfAnyName.Read();

        /// <summary>A member of the generic type which the stub names.</summary>
        public static int ReadThroughTheNamedGenericStub() => AGenericStubOfAnyName<int>.Count;
    }

    [Test]
    public void A_Stub_Which_Names_The_Type_It_Stands_For_Is_Resolved_To_It()
    {
        var method = Weave(nameof(Templates.ReadThroughTheNamedStub));

        var read = method.Source.Body.Instructions
                          .Select(instruction => instruction.Operand)
                          .OfType<MethodReference>()
                          .SingleOrDefault(reference => reference.Name == "Read");

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.Not.Null, "the woven body does not call the method of the type the stub names.");
            Assert.That(read!.DeclaringType.FullName, Is.EqualTo($"{NS}.Stub"),
                "the method which the woven body calls is not the one of the type the stub names.");
            Assert.That(read.DeclaringType.Module, Is.SameAs(method.Source.Module),
                "the type of the method which the woven body calls is not of the module which was woven.");
        });
    }

    [Test]
    public void A_Named_Stub_Of_A_Generic_Type_Is_Resolved_To_The_Instantiation_Of_It()
    {
        var method = Weave(nameof(Templates.ReadThroughTheNamedGenericStub));

        var read = method.Source.Body.Instructions
                          .Select(instruction => instruction.Operand)
                          .OfType<FieldReference>()
                          .SingleOrDefault(reference => reference.Name == "Count");

        Assert.That(read, Is.Not.Null, "the woven body does not read the field of the generic type the stub names.");
        Assert.That(read!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
            "the field which the woven body reads is not one of an instantiation.");
        Assert.That(((GenericInstanceType) read.DeclaringType).ElementType.FullName,
            Is.EqualTo($"{NS}.GenericStub`1"),
            "the instantiation which the woven body reads is not of the type the stub names.");
    }

    /// <summary>
    /// Weave a member of an assembly which declares the two types the stubs of these tests name.
    /// </summary>
    /// <param name="templateName">Name of the template which is woven.</param>
    /// <returns>The member which was woven.</returns>
    private static MethodHandler Weave(string templateName)
    {
        var assembly = Assembly.Create("NamedStubTargetAssembly");
        var module = assembly.Source.MainModule;

        var stub = new TypeDefinition(NS, "Stub", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        stub.Fields.Add(new FieldDefinition("Field", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32));
        var read = new MethodDefinition("Read", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Int32);
        read.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 41));
        read.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        stub.Methods.Add(read);
        module.Types.Add(stub);

        var generic = new TypeDefinition(NS, "GenericStub`1", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        generic.Fields.Add(new FieldDefinition("Count", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32));
        generic.GenericParameters.Add(new GenericParameter("T", generic));
        module.Types.Add(generic);

        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass("Host", NS, ClassFlags.Public).GetHandler();
        var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(typeof(Templates).GetMethod(templateName)!);
        return method;
    }
}
