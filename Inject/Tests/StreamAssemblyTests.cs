using System.Reflection;

namespace Gneedle.Inject.Test;

/// <summary>
/// Tests for the <see cref="Assembly"/> entry points which are backed by <see cref="StreamAssembly"/>, which is both
/// <c>Assembly.Read(string)</c> and <c>Assembly.Read(Stream)</c>.
/// </summary>
[TestFixture]
public class StreamAssemblyTests
{
    private const string Ns = "Gneedle.Test.Generated";

    /// <summary>
    /// Produce an assembly which holds a type of the given name, and hand its image over as a stream.
    /// </summary>
    private static (MemoryStream image, string name) NewImage(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        host.AddMethod("Ping", typeof(void).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);

        var image = new MemoryStream();
        assembly.SaveTo(image);
        image.Position = 0;
        return (image, assemblyName);
    }

    [Test]
    public void Read_Of_A_Stream_Loads_The_Assembly_Which_The_Stream_Holds()
    {
        // The reader consumes the stream, so the bytes have to be read back out of it rather than off its end. Loading
        // what the stream holds at its end would load an empty image, which the runtime rejects.
        var (image, name) = NewImage("StreamReadAssembly");
        using var imageOwner = image;

        using var assembly = Assembly.Read(image);
        var loaded = assembly.Load();

        Assert.That(loaded.GetName().Name, Is.EqualTo(name));
        Assert.That(loaded.GetType($"{Ns}.Host"), Is.Not.Null);
    }

    [Test]
    public void Read_Of_A_Stream_Leaves_The_Stream_Where_The_Caller_Left_It()
    {
        // The stream is the one which the assembly is written back through, so reading the image must not disturb it.
        var (image, _) = NewImage("StreamPositionAssembly");
        using var imageOwner = image;

        using var assembly = Assembly.Read(image);
        image.Position = 7;
        assembly.Load();

        Assert.That(image.Position, Is.EqualTo(7));
    }

    [Test]
    public void Read_Of_A_Stream_Executes_The_Body_Of_A_Method_Which_Was_Added_Without_A_Body()
    {
        var (image, _) = NewImage("StreamExecutableAssembly");
        using var imageOwner = image;

        using var assembly = Assembly.Read(image);
        var ping = assembly.Load().GetType($"{Ns}.Host")!.GetMethod("Ping")!;

        var thrown = Assert.Throws<TargetInvocationException>(() => ping.Invoke(null, null));
        Assert.That(thrown!.InnerException, Is.InstanceOf<NotSupportedException>());
    }
}
