using Mono.Cecil;

namespace Gneedle.Inject.Test;

[TestFixture]
public class MethodTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static IClassHandler NewClass()
    {
        var handler = (AssemblyHandler) Assembly.Create("MethodTestAssembly").Handler;
        return handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
    }

    private static MethodDefinition SourceOf(IMethodHandler handler) => ((MethodHandler) handler).Source;

    // region AddMethod

    [Test]
    public void AddMethod_Void_NoArgs_Instance()
    {
        var host = NewClass();
        var method = host.AddMethod("Foo", MethodFlags.Public).WithReturnType(typeof(void)).GetHandler();

        var def = SourceOf(method);
        Assert.That(def.Name, Is.EqualTo("Foo"));
        Assert.That(def.ReturnType.FullName, Is.EqualTo(typeof(void).FullName));
        Assert.That(def.IsStatic, Is.False);
        Assert.That(def.IsPublic, Is.True);
        Assert.That(def.Parameters, Is.Empty);
        Assert.That(((ClassHandler) host).Source.Methods, Does.Contain(def));
    }

    [Test]
    public void AddMethod_WithParameters_Static()
    {
        var host = NewClass();
        var method = host.AddMethod("Bar", MethodFlags.Public | MethodFlags.Static)
            .WithParameter(typeof(string))
            .WithParameter(typeof(int))
            .WithReturnType(typeof(int))
            .GetHandler();

        var def = SourceOf(method);
        Assert.That(def.IsStatic, Is.True);
        Assert.That(def.ReturnType.FullName, Is.EqualTo(typeof(int).FullName));
        Assert.That(def.Parameters.Count, Is.EqualTo(2));
        Assert.That(def.Parameters[0].ParameterType.FullName, Is.EqualTo(typeof(string).FullName));
        Assert.That(def.Parameters[1].ParameterType.FullName, Is.EqualTo(typeof(int).FullName));
    }

    [Test]
    public void AddMethod_Generic_Adds_Single_GenericParameter()
    {
        var host = NewClass();
        var method = host.AddMethod("Identity", MethodFlags.Public)
            .WithGenericParameter("T")
            .WithParameter(new GenericParameterType("T"))
            .WithReturnType(new GenericParameterType("T"))
            .GetHandler();

        var def = SourceOf(method);
        Assert.That(def.GenericParameters.Count, Is.EqualTo(1));
        Assert.That(def.GenericParameters[0].Name, Is.EqualTo("T"));
        Assert.That(def.ReturnType.Name, Is.EqualTo("T"));
        Assert.That(def.Parameters.Count, Is.EqualTo(1));
        Assert.That(def.Parameters[0].ParameterType.Name, Is.EqualTo("T"));
    }

    [Test]
    public void AddMethod_WithDeclaredTypes_Produces_Valid_Assembly()
    {
        // A declared type which the target module does not own has to be imported into it, otherwise Cecil asks the module
        // which declares it for its metadata token at write time and throws "Member ... is declared in another module and
        // needs to be imported". The return type is void, whose definition is owned by the corlib, and the parameter is a
        // type of another assembly, so both of the shapes which are not importable as-is are covered here.
        var assembly = Assembly.Create("DeclaredTypeAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new NongenericType(typeof(MethodTests))], MethodFlags.Public);

        using var stream = new MemoryStream();
        assembly.SaveTo(stream);

        stream.Position = 0;
        var reread = AssemblyDefinition.ReadAssembly(stream);
        var emitted = reread.MainModule.GetType($"{Ns}.Host")!.Methods.First(method => method.Name == "Run");
        Assert.That(emitted.ReturnType.FullName, Is.EqualTo(typeof(void).FullName));
        Assert.That(emitted.Parameters[0].ParameterType.FullName, Is.EqualTo(typeof(MethodTests).FullName));
    }

    // endregion

    // region Constructor validation

    [Test]
    public void AddMethod_InstanceCtor_Static_Throws()
    {
        var host = NewClass();
        Assert.Catch<ArgumentException>(() =>
            host.AddMethod(".ctor", MethodFlags.Public | MethodFlags.Static).WithReturnType(typeof(void)).GetHandler());
    }

    [Test]
    public void AddMethod_InstanceCtor_Virtual_Throws()
    {
        var host = NewClass();
        Assert.Catch<ArgumentException>(() =>
            host.AddMethod(".ctor", MethodFlags.Public | MethodFlags.Virtual).WithReturnType(typeof(void)).GetHandler());
    }

    [Test]
    public void AddMethod_StaticCtor_WithoutStatic_Throws()
    {
        var host = NewClass();
        Assert.Catch<ArgumentException>(() =>
            host.AddMethod(".cctor", MethodFlags.Private).WithReturnType(typeof(void)).GetHandler());
    }

    [Test]
    public void AddMethod_StaticCtor_NonPrivate_Throws()
    {
        var host = NewClass();
        Assert.Catch<ArgumentException>(() =>
            host.AddMethod(".cctor", MethodFlags.Static | MethodFlags.Internal).WithReturnType(typeof(void)).GetHandler());
    }

    [Test]
    public void AddMethod_StaticCtor_StaticPrivate_IsValid()
    {
        var host = NewClass();
        var method = host.AddMethod(".cctor", MethodFlags.Static | MethodFlags.Private).WithReturnType(typeof(void)).GetHandler();

        var def = SourceOf(method);
        Assert.That(def.IsStatic, Is.True);
        Assert.That(def.IsRuntimeSpecialName, Is.True);
        Assert.That(def.Name, Is.EqualTo(".cctor"));
    }

    // endregion

    // region MethodFlags conversion

    [TestCase(MethodFlags.Public, MethodAttributes.Public)]
    [TestCase(MethodFlags.Private, MethodAttributes.Private)]
    [TestCase(MethodFlags.Internal, MethodAttributes.Assembly)]
    [TestCase(MethodFlags.Protected, MethodAttributes.Family)]
    [TestCase(MethodFlags.Protected | MethodFlags.Internal, MethodAttributes.FamORAssem)]
    [TestCase(MethodFlags.Private | MethodFlags.Protected, MethodAttributes.FamANDAssem)]
    public void ToMethodAttributes_AccessLevel(MethodFlags flags, MethodAttributes expected)
    {
        var attributes = flags.ToMethodAttributes();
        Assert.That(attributes.HasFlag(expected), Is.True);
        Assert.That(attributes.HasFlag(MethodAttributes.HideBySig), Is.True);
    }

    [Test]
    public void ToMethodAttributes_Static()
        => Assert.That((MethodFlags.Public | MethodFlags.Static).ToMethodAttributes().HasFlag(MethodAttributes.Static), Is.True);

    [Test]
    public void ToMethodAttributes_Virtual_Adds_NewSlot()
    {
        var attributes = (MethodFlags.Public | MethodFlags.Virtual).ToMethodAttributes();
        Assert.That(attributes.HasFlag(MethodAttributes.Virtual), Is.True);
        Assert.That(attributes.HasFlag(MethodAttributes.NewSlot), Is.True);
    }

    // endregion
}