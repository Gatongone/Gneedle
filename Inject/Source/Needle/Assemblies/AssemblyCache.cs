namespace Gneedle.Inject;

/// <summary>
/// Cache of the raw data of an assembly, which the assembly is read from and written back to.
/// </summary>
internal interface IAssemblyCache : IDisposable
{
    /// <summary>
    /// Stream which holds the raw data of the assembly.
    /// </summary>
    Stream Stream { get; }
}

/// <summary>
/// Cache which holds the raw data of an assembly which a file of the file system backs.
/// </summary>
/// <param name="path">Path of the file which backs the assembly.</param>
/// <param name="stream">Stream of that file.</param>
internal class FileCache(string path, Stream stream) : IAssemblyCache
{
    /// <summary>
    /// Path of the file which backs the assembly.
    /// </summary>
    public string Path { get; } = path;

    /// <inheritdoc/>
    public Stream Stream { get; } = stream;

    /// <inheritdoc/>
    public void Dispose() => Stream?.Dispose();
}

/// <summary>
/// Cache which holds the raw data of an assembly which a stream alone backs.
/// </summary>
/// <param name="stream">Stream which holds the raw data of the assembly.</param>
internal class SimpleCache(Stream stream) : IAssemblyCache
{
    /// <inheritdoc/>
    public Stream Stream { get; } = stream;

    /// <inheritdoc/>
    public void Dispose() => Stream.Dispose();
}
