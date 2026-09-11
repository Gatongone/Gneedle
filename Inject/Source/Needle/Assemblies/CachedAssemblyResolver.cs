#if NETFRAMEWORK
using System.Reflection;
#endif

namespace Gneedle.Inject;

/// <summary>
/// Resolver which finds the assemblies which were read into a module, and defers to another resolver for the rest.<para/>
/// The resolver of a module searches the file system alone, so an assembly which only exists in memory, or which was read
/// from a stream which is not backed by a file, could not be found by it.
/// </summary>
/// <param name="fallback">Resolver which finds the assemblies which were not read into the module.</param>
internal sealed class CachedAssemblyResolver(IAssemblyResolver fallback) : IAssemblyResolver
{
#if NETFRAMEWORK
    /// <summary>
    /// Cache of the non public <c>GetRawBytes</c> method of <see cref="System.Reflection.Assembly"/>, which hands the
    /// bytes of the image of an assembly over.
    /// </summary>
    private static MethodInfo? s_GetRawBytes;
#endif

    /// <summary>
    /// Assemblies which were read into the module, keyed by the full name of each.
    /// </summary>
    internal Dictionary<string, AssemblyDefinition> Assemblies { get; } = new();

    /// <inheritdoc/>
    public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());

    /// <inheritdoc/>
    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        if (Assemblies.TryGetValue(name.FullName, out var assembly)) return assembly;

        // The version of a reference may differ from the version of the assembly which was read, and the full name holds
        // the version, so the simple name is the one which ties them together as well.
        var byName = Assemblies.Values.FirstOrDefault(assembly => assembly.Name.Name == name.Name);
        if (byName != null) return byName;

        try
        {
            return fallback.Resolve(name, parameters);
        }
        catch (AssemblyResolutionException)
        {
            // The assembly may be loaded in the process from a file which no search path of the resolver of that module
            // holds, so the file system could not find it.
        }

        var definition = ReadLoadedAssembly(name);

        // The assembly is kept, so that it is resolved without reading its image again.
        Assemblies[name.FullName] = definition ?? throw new AssemblyResolutionException(name);
        return definition;
    }

    /// <inheritdoc/>
    public void Dispose() => fallback.Dispose();

    /// <summary>
    /// Read the assembly of <paramref name="name"/> which is loaded in the process from the bytes of its image.
    /// </summary>
    /// <remarks>
    /// Nothing hands the image of an assembly over on .NET 5 and later, so an assembly which was loaded from bytes could
    /// only be read back on .NET Framework. An assembly which was loaded from a file which the resolver of the module
    /// could not search is read from that file, which is what the other runtimes cover.
    /// </remarks>
    /// <param name="name">Name of the assembly.</param>
    /// <returns>The assembly definition, or null when no assembly of that name is loaded, or its image can't be read.</returns>
    private AssemblyDefinition? ReadLoadedAssembly(AssemblyNameReference name)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == name.Name);
        if (assembly == null || !TryGetAssemblyRawBytes(assembly, out var rawBytes)) return null;

        using var memoryStream = new MemoryStream(rawBytes);
        return AssemblyDefinition.ReadAssembly(memoryStream, new ReaderParameters
        {
            InMemory         = true,
            ReadWrite        = false,
            ReadingMode      = ReadingMode.Deferred,
            AssemblyResolver = this
        });
    }

    /// <summary>
    /// Get the bytes of the image of <paramref name="assembly"/>, which no file necessarily backs.
    /// </summary>
    /// <param name="assembly">The assembly which need to get raw bytes.</param>
    /// <param name="rawBytes">Bytes that is a COFF-based image containing the assembly.</param>
    /// <returns>True when there is any way to get raw bytes.</returns>
    private static bool TryGetAssemblyRawBytes(System.Reflection.Assembly assembly, out byte[] rawBytes)
    {
        rawBytes = Array.Empty<byte>();

#if NETFRAMEWORK
        // .NET Framework holds the bytes of the image itself, and hands them over through a non public method. The
        // parameter types are named because the name alone matches the overloads of the base types as well, which makes
        // the lookup ambiguous.
        s_GetRawBytes ??= assembly.GetType().GetMethod("GetRawBytes", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (s_GetRawBytes?.Invoke(assembly, null) is byte[] {Length: > 0} bytes)
        {
            rawBytes = bytes;
            return true;
        }
#endif

        // An assembly which was loaded from a file holds the path of it. A dynamic assembly holds none, and asking for
        // the path of one throws, so the path is only read when the assembly was loaded from one.
        try
        {
            var location = assembly.Location;
            if (string.IsNullOrEmpty(location) || !File.Exists(location)) return false;

            rawBytes = File.ReadAllBytes(location);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
