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
        var method = host.AddMethod("Foo").WithReturnType(typeof(void)).WithFlags(MethodFlags.Public).GetHandler();

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
        var method = host.AddMethod("Bar")
            .WithReturnType(typeof(int))
            .WithParameter(typeof(string))
            .WithParameter(typeof(int))
            .WithFlags(MethodFlags.Public | MethodFlags.Static)
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
        var method = host.AddMethod("Identity")
            .WithReturnType(new GenericParameterType("T"))
            .WithGenericParameter("T")
            .WithParameter(new GenericParameterType("T"))
            .GetHandler();

        var def = SourceOf(method);
        Assert.That(def.GenericParameters.Count, Is.EqualTo(1));
        Assert.That(def.GenericParameters[0].Name, Is.EqualTo("T"));
        Assert.That(def.ReturnType.Name, Is.EqualTo("T"));
        Assert.That(def.Parameters.Count, Is.EqualTo(1));
        Assert.That(def.Parameters[0].ParameterType.Name, Is.EqualTo("T"));
    }

    // endregion

    // region Constructor validation

    [Test]
    public void AddMethod_InstanceCtor_Static_Throws()
    {
        var host = NewClass();
        Assert.Catch<ArgumentException>(() =>
            host.AddMethod(".ctor").WithReturnType(typeof(void)).WithFlags(MethodFlags.Public | MethodFlags.Static).GetHandler());
    }

    [Test]
    public void AddMethod_InstanceCtor_Virtual_Throws()
    {
        var host = NewClass();
        Assert.Catch<ArgumentException>(() =>
            host.AddMethod(".ctor").WithReturnType(typeof(void)).WithFlags(MethodFlags.Public | MethodFlags.Virtual).GetHandler());
    }

    [Test]
    public void AddMethod_StaticCtor_WithoutStatic_Throws()
    {
        var host = NewClass();
        Assert.Catch<ArgumentException>(() =>
            host.AddMethod(".cctor").WithReturnType(typeof(void)).WithFlags(MethodFlags.Private).GetHandler());
    }

    [Test]
    public void AddMethod_StaticCtor_NonPrivate_Throws()
    {
        var host = NewClass();
        Assert.Catch<ArgumentException>(() =>
            host.AddMethod(".cctor").WithReturnType(typeof(void)).WithFlags(MethodFlags.Static | MethodFlags.Internal).GetHandler());
    }

    [Test]
    public void AddMethod_StaticCtor_StaticPrivate_IsValid()
    {
        var host = NewClass();
        var method = host.AddMethod(".cctor").WithReturnType(typeof(void)).WithFlags(MethodFlags.Static | MethodFlags.Private).GetHandler();

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