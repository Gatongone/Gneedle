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
    /// The cache of the stream which the assembly was read from, which an assembly which was read from a file also
    /// keeps the path of, and which is released with it.
    /// </summary>
    private IAssemblyCache? m_AssemblyCache;

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
    public static Assembly Create(string assemblyName, byte[]? publicKey = null, byte[]? publicKeyToken = null, byte[]? hash = null, string? culture = null, ModuleKind moduleKind = ModuleKind.Dll)
    {
        return new MemoryAssembly(assemblyName, publicKey, publicKeyToken, hash, culture, moduleKind);
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
        // The file is read into bytes and the assembly is read out of those bytes rather than out of the file, so that
        // the read neither holds the file nor goes back to it: the body of a method is read out of the stream as it is
        // asked for, and the write which a read of a file is usually followed by - the woven image, written back over
        // the file it was read from - opens that file for writing and takes it to nothing, which a body read out of it
        // afterwards would find in place of the body. A file which is read alone is also one which a file that was
        // protected after it was written is, where the write access which the caller of a read has no need of is what
        // the file refuses. The path is kept with the stream, because an assembly of a file is one which is loaded from
        // that file.
        var cache = new FileCache(path, new MemoryStream(File.ReadAllBytes(path)));
        return new StreamAssembly(cache, symbol)
        {
            m_AssemblyCache = cache
        };
    }

    /// <summary>
    /// Read assembly to memory.
    /// </summary>
    /// <param name="stream">Stream that is a COFF-based image containing a managed assembly.</param>
    /// <param name="symbol">Assembly symbol file type.</param>
    /// <param name="searchDirectory">Directory which the assemblies the image refers to lie in, or null when the caller
    /// knows of none. It is the folder of the assembly which is woven where a build weaves the assembly it produced,
    /// whose references the build copied beside it.</param>
    /// <param name="symbols">Bytes of the portable program database which describes the image, or null for the symbols
    /// which <paramref name="symbol"/> names beside the image.</param>
    public static Assembly Read(Stream stream, AssemblySymbol symbol = AssemblySymbol.None, string? searchDirectory = null, byte[]? symbols = null)
    {
        var cache = new SimpleCache(stream);
        return new StreamAssembly(cache, symbol, searchDirectory, symbols)
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

        // The image is written through the writer of the assembly, which opens the file for itself: the assembly holds
        // no handle of the file it was read from, because the file lies in the memory which the assembly was read into
        // rather than behind a stream which the file was held open by.
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
            throw new WeavingException(string.Format(ErrorMessages.ARRAY_TOO_SMALL_FOR_ASSEMBLY, assemblyStream.Length), nameof(assemblyBytes));
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
    /// <exception cref="WeavingException">Thrown when the provided byte arrays are too small to hold the assembly or symbol data.</exception>
    public (long assemblyWrote, long symbolWrote) SaveTo(byte[] assemblyBytes, byte[] symbolBytes)
    {
        using var assemblyStream = new MemoryStream();
        using var symbolStream = new MemoryStream();
        SaveTo(assemblyStream, symbolStream);
        if (assemblyBytes.Length < assemblyStream.Length)
        {
            throw new WeavingException(string.Format(ErrorMessages.ARRAY_TOO_SMALL_FOR_ASSEMBLY, assemblyStream.Length), nameof(assemblyBytes));
        }

        if (symbolBytes.Length < symbolStream.Length)
        {
            throw new WeavingException(string.Format(ErrorMessages.ARRAY_TOO_SMALL_FOR_SYMBOLS, symbolStream.Length), nameof(symbolBytes));
        }

        var (assemblyWrote, symbolWrote) = (assemblyStream.Length, symbolStream.Length);
        Array.Copy(assemblyStream.GetBuffer(), assemblyBytes, assemblyWrote);
        Array.Copy(symbolStream.GetBuffer(), symbolBytes, symbolWrote);
        return (assemblyWrote, symbolWrote);
    }

    /// <summary>
    /// Release the stream which the assembly was read from, and the metadata which was read from it.<para/>
    /// The stream is the one the assembly lies in, so the assembly has to be saved before it is disposed of.
    /// </summary>
    public void Dispose()
    {
        m_AssemblyCache?.Dispose();
        Source.Dispose();
    }
}