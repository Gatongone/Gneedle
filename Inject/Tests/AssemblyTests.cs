using System.Reflection;

namespace Gneedle.Inject.Test;

/// <summary>
/// Tests for the <see cref="Assembly"/> entry points which are backed by <see cref="MemoryAssembly"/>.
/// </summary>
[TestFixture]
public class AssemblyTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static TypeHandler NewHost(Assembly assembly)
        => (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();

    [Test]
    public void Load_Returns_The_Produced_Assembly()
    {
        var assembly = Assembly.Create("LoadableAssembly");
        NewHost(assembly);

        var loaded = assembly.Load();

        Assert.That(loaded.GetName().Name, Is.EqualTo("LoadableAssembly"));
        Assert.That(loaded.GetType($"{Ns}.Host"), Is.Not.Null);
    }

    [Test]
    public void Load_Of_A_Type_With_A_Method_Returns_The_Produced_Assembly()
    {
        var assembly = Assembly.Create("LoadableMethodAssembly");
        NewHost(assembly).AddMethod("Ping", typeof(void).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);

        var loaded = assembly.Load();

        Assert.That(loaded.GetType($"{Ns}.Host")!.GetMethod("Ping"), Is.Not.Null);
    }

    [Test]
    public void Load_Executes_The_Body_Of_A_Method_Which_Was_Added_Without_A_Body()
    {
        // Calling the produced method proves that its body is valid IL and not merely metadata which got emitted.
        var assembly = Assembly.Create("ExecutableAssembly");
        NewHost(assembly).AddMethod("Ping", typeof(void).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);

        var ping = assembly.Load().GetType($"{Ns}.Host")!.GetMethod("Ping")!;

        var thrown = Assert.Throws<TargetInvocationException>(() => ping.Invoke(null, null));
        Assert.That(thrown!.InnerException, Is.InstanceOf<NotSupportedException>());
    }

    [Test]
    public void Load_Constructs_A_Type_Whose_Constructor_Was_Added_Without_A_Body()
    {
        var assembly = Assembly.Create("ConstructibleAssembly");
        NewHost(assembly).AddMethod(".ctor", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);

        var type = assembly.Load().GetType($"{Ns}.Host")!;

        var thrown = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type));
        Assert.That(thrown!.InnerException, Is.InstanceOf<NotSupportedException>());
    }
}
