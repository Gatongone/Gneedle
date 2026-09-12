namespace Gneedle.Inject;

/// <summary>
/// Writing for <see cref="Mono.Cecil.AssemblyDefinition"/> to target file or memories.
/// </summary>
public abstract class Assembly : IDisposable
{
    /// <summary>
    /// Reader of the symbols of the Mono debug format.
    /// </summary>
    internal static readonly ISymbolReaderProvider MdbSymbolReaderProvider = new Mono.Cecil.Mdb.MdbReaderProvider();

    /// <summary>
    /// Writer of the symbols of the Mono debug format.
    /// </summary>
    internal static readonly ISymbolWriterProvider MdbSymbolWriterProvider = new Mono.Cecil.Mdb.MdbWriterProvider();

    /// <summary>
    /// Reader of the symbols of the Windows program database.
    /// </summary>
    internal static readonly ISymbolReaderProvider PdbSymbolReaderProvider = new Mono.Cecil.Pdb.PdbReaderProvider();

    /// <summary>
    /// Writer of the symbols of the Windows program database.
    /// </summary>
    internal static readonly ISymbolWriterProvider PdbSymbolWriterProvider = new Mono.Cecil.Pdb.PdbWriterProvider();

    /// <summary>
    /// Reader of the symbols of the portable program database.
    /// </summary>
    internal static readonly ISymbolReaderProvider PortablePdbReaderProvider = new PortablePdbReaderProvider();

    /// <summary>
    /// Writer of the symbols of the portable program database.
    /// </summary>
    internal static readonly ISymbolWriterProvider PortablePdbWriterProvider = new PortablePdbWriterProvider();

    /// <summary>
    /// Reader which tells the format of the symbols of an assembly by itself.
    /// </summary>
    internal static readonly ISymbolReaderProvider DefaultReaderProvider = new DefaultSymbolReaderProvider();

    /// <summary>
    /// Writer which tells the format of the symbols to write by itself.
    /// </summary>
    internal static readonly ISymbolWriterProvider DefaultWriterProvider = new DefaultSymbolWriterProvider();

    /// <summary>
    /// Assembly handler.
    /// </summary>
    public IAssemblyHandler Handler => m_Handler.Value;

    /// <summary>
    /// The cache of the stream which the assembly was read from, which the assembly is written back through when it is
    /// saved to the path it was read from, and which is released with it.
    /// </summary>
    private          IAssemblyCache?        m_AssemblyCache;

    /// <summary>
    /// The handler of the assembly, which is made when it is first asked for.
    /// </summary>
    /// <remarks>
    /// It is made lazily because an assembly which is only read is never handled: building the handler reads the
    /// assemblies which the module refers to and caches the types of the module, none of which a caller that reads an
    /// assembly to write it elsewhere has any use for.
    /// </remarks>
    private readonly Lazy<IAssemblyHandler> m_Handler;

    /// <summary>
    /// Target assembly definition.
    /// </summary>
    internal readonly AssemblyDefinition Source;

    /// <summary>
    /// Load the assembly as <see cref="System.Reflection.Assembly"/>.
    /// </summary>
    /// <returns>Reflection assembly.</returns>
    public abstract System.Reflection.Assembly Load();

    /// <param name="source">Source assembly definition which need to write.</param>
    internal Assembly(AssemblyDefinition source) => (Source, m_Handler) = (source, new Lazy<IAssemblyHandler>(() => new AssemblyHandler(this)));

    /// <summary>
    /// Create assembly.
    /// </summary>
    /// <inheritdoc cref="MemoryAssembly(string, byte[], byte[], byte[], string, ModuleKind)"/>
    public static Assembly Create(string assemblyName, byte[]? publicKey = null, byte[]? pubicKeyToken = null, byte[]? hash = null, string? culture = null, ModuleKind moduleKind = ModuleKind.Dll)
    {
        return new MemoryAssembly(assemblyName, publicKey, pubicKeyToken, hash, culture, moduleKind);
    }

    /// <summary>
    /// Create assembly.
    /// </summary>
    /// <inheritdoc cref="MemoryAssembly(string, Version, byte[], byte[], byte[], string, ModuleKind)"/>
    public static Assembly Create(string assemblyName, Version version, byte[]? publicKey = null, byte[]? publicKeyToken = null, byte[]? hash = null, string? culture = null, ModuleKind moduleKind = ModuleKind.Dll)
    {
        return new MemoryAssembly(assemblyName, version, publicKey, publicKeyToken, hash, culture, moduleKind);
    }

    /// <summary>
    /// Read assembly to memory.
    /// </summary>
    /// <param name="path">Assembly path.</param>
    /// <param name="symbol">Assembly symbol file type.</param>
    public static Assembly Read(string path, AssemblySymbol symbol = AssemblySymbol.None)
    {
        var cache = new FileCache(path, new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
        return new StreamAssembly(cache, symbol)
        {
            m_AssemblyCache = cache
        };
    }

    /// <summary>
    /// Read assembly to memory.
    /// </summary>
    public static Assembly Read(Stream stream, AssemblySymbol symbol = AssemblySymbol.None)
    {
        var cache = new SimpleCache(stream);
        return new StreamAssembly(cache, symbol)
        {
            m_AssemblyCache = cache
        };
    }

    /// <summary>
    /// Create assembly at target path.
    /// </summary>
    /// <param name="path">Target assembly path.</param>
    /// <param name="assemblySymbol"></param>
    public void SaveTo(string path, AssemblySymbol assemblySymbol = AssemblySymbol.None)
    {
        var writeParameters = assemblySymbol switch
        {
            AssemblySymbol.Pdb => new WriterParameters
            {
                WriteSymbols         = true,
                SymbolWriterProvider = PdbSymbolWriterProvider
            },
            AssemblySymbol.Mdb => new WriterParameters
            {
                WriteSymbols         = true,
                SymbolWriterProvider = MdbSymbolWriterProvider
            },
            _ => null
        };

        // Reusing the same stream.
        if (m_AssemblyCache is FileCache fileCache && fileCache.Path == path)
        {
            if (writeParameters == null)
            {
                Source.Write(fileCache.Stream);
            }
            else
            {
                Source.Write(fileCache.Stream, writeParameters);
            }

            return;
        }

        if (writeParameters == null)
        {
            Source.Write(path);
        }
        else
        {
            Source.Write(path, writeParameters);
        }
    }

    /// <summary>
    ///  Write assembly to stream.
    /// </summary>
    /// <param name="assemblyStream">Stream that is a COFF-based image containing a managed assembly.</param>
    public void SaveTo(Stream assemblyStream)
    {
        Source.Write(assemblyStream);
        assemblyStream.Flush();
    }

    /// <summary>
    ///  Write assembly and symbol to stream.
    /// </summary>
    /// <param name="assemblyStream">Stream that is a COFF-based image containing a managed assembly.</param>
    /// <param name="symbolStream">Stream that contains the raw bytes representing the portable pdb symbols for the assembly.</param>
    public void SaveTo(Stream assemblyStream, Stream symbolStream)
    {
        Source.Write(assemblyStream, new WriterParameters
        {
            WriteSymbols         = true,
            SymbolWriterProvider = PortablePdbWriterProvider,
            SymbolStream         = symbolStream
        });
        assemblyStream.Flush();
        symbolStream.Flush();
    }

    /// <summary>
    /// Write assembly and symbol to bytes.
    /// </summary>
    /// <param name="assemblyBytes">Byte array to hold the assembly data.</param>
    /// <returns>The number of bytes written to the assembly byte array.</returns>
    public long SaveTo(byte[] assemblyBytes)
    {
        using var assemblyStream = new MemoryStream();
        SaveTo(assemblyStream);
        if (assemblyBytes.Length < assemblyStream.Length)
        {
            throw new ArgumentException(string.Format(ErrorMessages.ARRAY_TOO_SMALL_FOR_ASSEMBLY, assemblyStream.Length), nameof(assemblyBytes));
        }

        var length = assemblyStream.Length;
        Array.Copy(assemblyStream.GetBuffer(), assemblyBytes, length);
        assemblyStream.Flush();
        return length;
    }

    /// <summary>
    ///  Write assembly and symbol to bytes.
    /// </summary>
    /// <param name="assemblyBytes">Byte array to hold the assembly data.</param>
    /// <param name="symbolBytes">Byte array to hold the symbol data.</param>
    /// <returns>A tuple containing the number of bytes written to the assembly and symbol byte arrays, respectively.</returns>
    /// <exception cref="ArgumentException">Thrown when the provided byte arrays are too small to hold the assembly or symbol data.</exception>
    public (long assemblyWrote, long symbolWrote) SaveTo(byte[] assemblyBytes, byte[] symbolBytes)
    {
        using var assemblyStream = new MemoryStream();
        using var symbolStream = new MemoryStream();
        SaveTo(assemblyStream, symbolStream);
        if (assemblyBytes.Length < assemblyStream.Length)
        {
            throw new ArgumentException(string.Format(ErrorMessages.ARRAY_TOO_SMALL_FOR_ASSEMBLY, assemblyStream.Length), nameof(assemblyBytes));
        }

        if (symbolBytes.Length < symbolStream.Length)
        {
            throw new ArgumentException(string.Format(ErrorMessages.ARRAY_TOO_SMALL_FOR_SYMBOLS, symbolStream.Length), nameof(symbolBytes));
        }

        var (assemblyWrote, symbolWrote) = (assemblyStream.Length, symbolStream.Length);
        Array.Copy(assemblyStream.GetBuffer(), assemblyBytes, assemblyWrote);
        Array.Copy(symbolStream.GetBuffer(), symbolBytes, symbolWrote);
        return (assemblyWrote, symbolWrote);
    }

    /// <summary>
    /// Release the stream which the assembly was read from, and the metadata which was read from it.<para/>
    /// The assembly is written back through the very stream it was read from, so it has to be saved before it is
    /// disposed of.
    /// </summary>
    public void Dispose()
    {
        m_AssemblyCache?.Dispose();
        Source.Dispose();
    }
}