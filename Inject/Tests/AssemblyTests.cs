using System.Reflection;
using Mono.Cecil;

namespace Gneedle.Inject.Test;

using static Gneedle.Inject.Test.TestFixtures;

/// <summary>
/// Tests for the <see cref="Assembly"/> which is created, read and written back: the entry points which are backed by
/// <see cref="MemoryAssembly"/> and the ones which are backed by <see cref="StreamAssembly"/>.
/// </summary>
[TestFixture]
public class AssemblyTests
{

    /// <summary>
    /// Add a type which the assembly holds, and hand back the handler of it.
    /// </summary>
    private static TypeHandler NewHost(Assembly assembly)
        => (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();

    #region In memory

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

    #endregion

    #region From a stream

    /// <summary>
    /// Produce an assembly which holds a type of the given name, and hand its image over as a stream.
    /// </summary>
    private static (MemoryStream image, string name) NewImage(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        NewHost(assembly).AddMethod("Ping", typeof(void).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);

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

    #endregion

    #region From a file

    /// <summary>
    /// Write the image of an assembly which holds a type to a file of its own, and hand back the path of it. The file
    /// lies in a directory of its own, which the test that asked for it removes when it is done with it.
    /// </summary>
    private static string NewFile(string assemblyName)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Gneedle.Inject.Test.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{assemblyName}.dll");

        var assembly = Assembly.Create(assemblyName);
        NewHost(assembly);
        using var image = File.Create(path);
        assembly.SaveTo(image);
        return path;
    }

    [Test]
    public void Read_Of_A_Path_Reads_The_Assembly_Which_The_File_Holds()
    {
        // The reader reads the file rather than opening it for writing, so a file which was written once and protected
        // afterwards is read like any other: a file which is opened for writing is one which the write access of the
        // caller is asked for, which a file that is only read has no reason to grant.
        var path = NewFile("FileReadAssembly");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            // The module is read rather than the image loaded, because an image which was loaded from a path holds the
            // file until the process ends, and the file of this test is one which the test removes afterwards.
            using var assembly = Assembly.Read(path);
            var type = assembly.Source.MainModule.GetType($"{Ns}.Host");

            Assert.That(assembly.Source.MainModule.Assembly.Name.Name, Is.EqualTo("FileReadAssembly"));
            Assert.That(type, Is.Not.Null, "the type which the file holds is not in the assembly which was read from it.");
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Test]
    public void Read_Of_A_Path_Is_Written_Back_Over_The_File_It_Was_Read_From()
    {
        // An assembly which was read from a file is usually woven and written back over it, which is what the read of
        // the library is for: the file is opened for reading, and the write is the one the caller asks for when it
        // writes back, rather than the access the read takes for itself.
        var path = NewFile("FileWriteAssembly");

        try
        {
            using (var assembly = Assembly.Read(path))
            {
                ((AssemblyHandler) assembly.Handler).AddClass("Added", Ns, ClassFlags.Public).GetHandler();
                assembly.SaveTo(path);
            }

            // The file is read back with Cecil rather than loaded, because an image which was loaded from a path holds
            // the file until the process ends, and the file of this test is one which the test removes afterwards.
            using var reread = AssemblyDefinition.ReadAssembly(path);
            Assert.That(reread.MainModule.GetType($"{Ns}.Added"), Is.Not.Null,
                "the type which was described is not in the file which the assembly was written back to.");
            Assert.That(reread.MainModule.GetType($"{Ns}.Host"), Is.Not.Null,
                "the type which the file held is not in it after the assembly was written back over it.");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Test]
    public void Written_Back_Over_The_File_It_Was_Read_From_An_Assembly_Keeps_The_Bodies_Of_Its_Methods()
    {
        // The reader reads the body of a method out of the file as it is asked for rather than holding it, and the
        // write goes over the very file the assembly was read from: an image which is opened for writing is taken to
        // nothing where it is opened, so a body which is read afterwards is read out of the image which is being
        // written in place of the one which was woven, and what is written in its place is that.
        var directory = Path.Combine(Path.GetTempPath(), $"Gneedle.Inject.Test.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "WovenInPlace.dll");

        try
        {
            // The assembly of the tests is written as the file, because it is an assembly of methods which no test
            // wrote by hand, which is what tells a body which was read out of the file from one which was not.
            File.Copy(typeof(AssemblyTests).Assembly.Location, path);
            var before = Bodies(path);
            Assert.That(before, Is.Not.Empty, "the assembly which is written over holds no body for the read to lose.");

            using (var assembly = Assembly.Read(path))
            {
                ((AssemblyHandler) assembly.Handler).AddClass("Added", Ns, ClassFlags.Public).GetHandler();
                assembly.SaveTo(path);
            }

            using var reread = AssemblyDefinition.ReadAssembly(path, new ReaderParameters {ReadSymbols = false});
            Assert.That(reread.MainModule.GetType($"{Ns}.Added"), Is.Not.Null,
                        "the type which was described is not in the file which the assembly was written back to.");

            var after = Bodies(path);
            var changed = before.Where(body => !after.TryGetValue(body.Key, out var written) || written != body.Value).Select(body => body.Key).ToArray();
            Assert.That(changed, Is.Empty,
                        "the bodies of the methods which were not woven were read out of the file after the write took it to nothing.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The body of every method of the assembly which a file holds, by the name of the method, as the instructions
    /// which it is written as.
    /// </summary>
    /// <param name="path">Path of the file which holds the assembly.</param>
    /// <returns>The bodies of the methods of the assembly.</returns>
    private static Dictionary<string, string> Bodies(string path)
    {
        var bodies = new Dictionary<string, string>();
        using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters {ReadSymbols = false});
        foreach (var type in assembly.MainModule.GetTypes())
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                bodies[$"{type.FullName}.{method.Name}"] = string.Join(" ", method.Body.Instructions.Select(instruction => $"{instruction.OpCode}{instruction.Operand}"));
            }
        }

        return bodies;
    }

    #endregion
}