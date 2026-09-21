namespace Gneedle.Inject;

/// <summary>
/// Assembly which reading and writing from local file.
/// </summary>
/// <param name="cache">Cache of assembly raw data.</param>
/// <param name="symbol">Assembly symbol file type.</param>
/// <param name="searchDirectory">Directory which the assemblies the image refers to lie in, or null when the caller knows
/// of none.</param>
/// <param name="symbols">Bytes of the portable program database which describes the image, or null for the symbols which
/// lie beside a file, whose format <paramref name="symbol"/> names.</param>
internal sealed class StreamAssembly(IAssemblyCache cache, AssemblySymbol symbol = AssemblySymbol.None, string? searchDirectory = null, byte[]? symbols = null) : Assembly(AssemblyDefinition.ReadAssembly(cache.Stream, GetReaderSymbolProvider(symbol, searchDirectory, symbols)))
{
    /// <inheritdoc/>
    public override System.Reflection.Assembly Load()
    {
        if (cache is FileCache fileCache)
            return System.Reflection.Assembly.LoadFile(fileCache.Path);

        // The stream was consumed by the reader which read the metadata out of it, so the image is read from the start
        // rather than from wherever the reader left it. The position is put back afterwards, so that the stream which
        // the caller handed over, and which the assembly is written back through, is left as it was found.
        var stream = cache.Stream;
        using var buffer = new MemoryStream();
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            stream.CopyTo(buffer);
        }
        finally
        {
            stream.Position = position;
        }

        buffer.Position = 0;
#if NET5_0_OR_GREATER
        return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(buffer);
#else
        // Assembly.Load reads the whole buffer, which does not depend on the position.
        return System.Reflection.Assembly.Load(buffer.ToArray());
#endif
    }

    /// <summary>
    /// Get reader parameters with symbol reader provider.
    /// </summary>
    /// <param name="symbol">Assembly symbol.</param>
    /// <param name="searchDirectory">Directory which the assemblies the image refers to lie in, or null when the caller
    /// knows of none.</param>
    /// <param name="symbols">Bytes of the symbols, which are read out of this memory rather than from beside a file, or
    /// null when they lie beside one.</param>
    /// <returns>ReaderParameters with target symbol reader provider.</returns>
    private static ReaderParameters GetReaderSymbolProvider(AssemblySymbol symbol, string? searchDirectory, byte[]? symbols = null)
    {
        var parameters = symbols != null
            ? new ReaderParameters(ReadingMode.Deferred) {SymbolReaderProvider = new PdbInBytes(symbols)}
            : symbol switch
            {
                AssemblySymbol.Pdb => new ReaderParameters(ReadingMode.Deferred) {SymbolReaderProvider = PdbSymbolReaderProvider},
                AssemblySymbol.Mdb => new ReaderParameters(ReadingMode.Deferred) {SymbolReaderProvider = MdbSymbolReaderProvider},

                // No symbol was asked for, so none is read. The default symbol reader provider throws when the assembly
                // has no symbol besides it, which an assembly which was just produced, or which was built without
                // symbols, has not.
                _ => new ReaderParameters(ReadingMode.Deferred) {ReadSymbols = false}
            };

        // The resolver holds the assemblies which are read into the module, so that one which only exists in memory, or
        // which was read from a stream, is resolvable, and it is given the folder which the assemblies the image refers
        // to lie in, which the caller knows and the resolver of the process does not. It is given here because the
        // resolver of a module cannot be replaced once it was created.
        parameters.AssemblyResolver = new CachedAssemblyResolver(new DefaultAssemblyResolver(), searchDirectory);

        // The type of the framework which is woven into an assembly is imported from the framework of whoever weaves,
        // which is not the framework of the assembly that is read: a build which runs on .NET hands the assembly it
        // writes a reference to the System.Private.CoreLib of that build, which neither .NET Framework nor Mono can
        // load. The importers which name the standard instead are the ones the assemblies which are created are given,
        // one for the types of the runtime and one for the members which are read out of it.
        parameters.ReflectionImporterProvider = SPCLReflectionImporterProvider.Instance;
        parameters.MetadataImporterProvider   = SPCLMetadataImporterProvider.Instance;
        return parameters;
    }
}