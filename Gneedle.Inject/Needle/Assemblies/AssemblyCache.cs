namespace Gneedle.Inject;

internal interface IAssemblyCache : IDisposable
{
    Stream Stream { get; }
}

internal class FileCache(string path, Stream stream) : IAssemblyCache
{
    public string Path { get; } = path;
    public Stream Stream { get; } = stream;

    public void Dispose() => Stream?.Dispose();
}

internal class SimpleCache(Stream stream) : IAssemblyCache
{
    public Stream Stream { get; } = stream;
    public void Dispose() => Stream.Dispose();
}