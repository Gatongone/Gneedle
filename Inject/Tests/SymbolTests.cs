using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Gneedle.Inject.Test;

/// <summary>
/// Tests for the symbols which are woven with an image, which are read with it and written beside it.<para/>
/// The image which is written holds the instructions of the template where the image which was read held the stub of
/// it, so the symbols which were compiled for the image which was read describe places which are no longer there: the
/// two are woven together, and the test below reads the woven pair back to tell that they describe one image.
/// </summary>
[TestFixture]
public class SymbolTests
{
    [Test]
    public void The_Symbols_Which_Were_Woven_With_An_Image_Describe_That_Image()
    {
        var location = typeof(SymbolTests).Assembly.Location;
        var image = File.ReadAllBytes(location);
        var symbols = File.ReadAllBytes(Path.ChangeExtension(location, ".pdb"));

        var (changed, woven, wovenSymbols) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image, symbols);
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True, "the assembly of these tests was woven by none of its injectors.");
            Assert.That(wovenSymbols, Is.Not.Null.And.Not.Empty, "the symbols which were read were not written back.");
        });

        // A reader which is handed both reads the image which the other describes: the database names the image it was
        // written for, and Cecil refuses one which was written for another, so a pair which was not woven together is
        // told from a pair which was.
        using var stream = new MemoryStream(woven);
        using var read = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters
        {
            ReadSymbols          = true,
            SymbolReaderProvider = new PdbInBytes(wovenSymbols!)
        });

        Assert.That(read.MainModule.HasSymbols, Is.True, "the image which was woven was read without the symbols which were written with it.");

        // A database which was written without the symbols which were read describes no instruction, so what tells the
        // one which carried them over is that the methods of the woven image are still described: the image which was
        // read held the places of its instructions, and the writer writes the ones the module holds.
        var described = read.MainModule.Types.SelectMany(type => type.Methods).Count(method => method.DebugInformation.HasSequencePoints);
        Assert.That(described, Is.GreaterThan(0), "no method of the image which was woven is described by the symbols which were written beside it.");
    }

    [Test]
    public void An_Image_Which_Was_Woven_Is_Not_Described_By_The_Symbols_Of_The_Image_It_Was_Woven_From()
    {
        // What is read here is the answer of the overload which is asked for the image alone, which is a pair no caller
        // can wear: what tells a database from a database which describes one image is the image it names, and a weaving
        // writes an image of its own, so a caller which hands the image it read the symbols of along with the image
        // which was woven hands over two things which do not describe one image, and a reader of the two says so rather
        // than reading places which are not there. It is the shape which the post processor of Unity was written in
        // rather than a fault of the weaver, which is why the test which answers for it lives beside that processor.
        var location = typeof(SymbolTests).Assembly.Location;
        var image = File.ReadAllBytes(location);
        var symbols = File.ReadAllBytes(Path.ChangeExtension(location, ".pdb"));

        var (changed, woven) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image);
        Assert.That(changed, Is.True, "the assembly of these tests was woven by none of its injectors.");

        using var stream = new MemoryStream(woven);
        Assert.Throws<SymbolsNotMatchingException>(() => AssemblyDefinition.ReadAssembly(stream, new ReaderParameters
        {
            ReadSymbols          = true,
            SymbolReaderProvider = new PdbInBytes(symbols)
        }), "the symbols of the image which was read describe the image which was written.");
    }
}