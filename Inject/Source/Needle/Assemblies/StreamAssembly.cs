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
    /// <returns>ReaderParameters with target symbol reader provider.</returns>
    private static ReaderParameters GetReaderSymbolProvider(AssemblySymbol symbol)
    {
        var parameters = symbol switch
        {
            AssemblySymbol.Pdb => new ReaderParameters(ReadingMode.Deferred) {SymbolReaderProvider = PdbSymbolReaderProvider},
            AssemblySymbol.Mdb => new ReaderParameters(ReadingMode.Deferred) {SymbolReaderProvider = MdbSymbolReaderProvider},

            // No symbol was asked for, so none is read. The default symbol reader provider throws when the assembly has
            // no symbol besides it, which an assembly which was just produced, or which was built without symbols, has not.
            _ => new ReaderParameters(ReadingMode.Deferred) {ReadSymbols = false}
        };

        // The resolver holds the assemblies which are read into the module, so that one which only exists in memory, or
        // which was read from a stream, is resolvable. It is given here because the resolver of a module cannot be
        // replaced once it was created.
        parameters.AssemblyResolver = new CachedAssemblyResolver(new DefaultAssemblyResolver());

        // The type of the framework which is woven into an assembly is imported from the framework of whoever weaves,
        // which is not the framework of the assembly that is read: a build which runs on .NET hands the assembly it
        // writes a reference to the System.Private.CoreLib of that build, which neither .NET Framework nor Mono can
        // load. The importer which names the standard instead is the one the assemblies which are created are given.
        parameters.ReflectionImporterProvider = SPCLReflectionImporterProvider.Instance;
        return parameters;
    }
}