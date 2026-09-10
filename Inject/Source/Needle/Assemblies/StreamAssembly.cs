namespace Gneedle.Inject;

/// <summary>
/// Assembly which reading and writing from local file.
/// </summary>
/// <param name="cache">Cache of assembly raw data.</param>
/// <param name="symbol">Assembly symbol file type.</param>
internal sealed class StreamAssembly(IAssemblyCache cache, AssemblySymbol symbol = AssemblySymbol.None) : Assembly(AssemblyDefinition.ReadAssembly(cache.Stream, GetReaderSymbolProvider(symbol)))
{
    /// <inheritdoc/>
    public override System.Reflection.Assembly Load()
    {
        if (cache is FileCache fileCache)
            return System.Reflection.Assembly.LoadFile(fileCache.Path);

        var buffer = new byte[cache.Stream.Length];
        cache.Stream.Write(buffer, 0, buffer.Length);
        return System.Reflection.Assembly.Load(buffer);
    }

    /// <summary>
    /// Get reader parameters with symbol reader provider.
    /// </summary>
    /// <param name="symbol">Assembly symbol.</param>
    /// <returns>ReaderParameters with target symbol reader provider.</returns>
    private static ReaderParameters GetReaderSymbolProvider(AssemblySymbol symbol) => symbol switch
    {
        AssemblySymbol.Pdb => new ReaderParameters(ReadingMode.Deferred) {SymbolReaderProvider = PdbSymbolReaderProvider},
        AssemblySymbol.Mdb => new ReaderParameters(ReadingMode.Deferred) {SymbolReaderProvider = MdbSymbolReaderProvider},

        // No symbol was asked for, so none is read. The default symbol reader provider throws when the assembly has no
        // symbol besides it, which an assembly which was just produced, or which was built without symbols, has not.
        _ => new ReaderParameters(ReadingMode.Deferred) {ReadSymbols = false}
    };
}