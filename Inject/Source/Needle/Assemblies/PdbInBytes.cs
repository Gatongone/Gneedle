namespace Gneedle.Inject;

/// <summary>
/// Reader of the symbols of an assembly which lie in this memory rather than beside the image.<para/>
/// The image of an assembly which is woven is read out of the bytes of it, and the symbols which were compiled with it
/// are other bytes: a reader which goes looking for them beside the image has nothing to look beside, because the image
/// was read out of a stream and names no file. The bytes of the symbols are handed over with the image instead, which is
/// what a build does with the program database which lies beside the assembly it wove.
/// </summary>
/// <param name="symbols">The bytes of the portable program database which describes the image.</param>
internal sealed class PdbInBytes(byte[] symbols) : ISymbolReaderProvider
{
    /// <inheritdoc/>
    /// <remarks>
    /// The name of a file is not read, because the one which was handed over is what the symbols are read out of: it is
    /// what this reader stands for, and the name of the image has no file to be found beside.
    /// </remarks>
    public ISymbolReader GetSymbolReader(ModuleDefinition module, string fileName) => GetSymbolReader(module, new MemoryStream(symbols));

    /// <inheritdoc/>
    public ISymbolReader GetSymbolReader(ModuleDefinition module, Stream stream) => Assembly.PortablePdbReaderProvider.GetSymbolReader(module, stream);
}
