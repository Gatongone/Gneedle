namespace Gneedle.Inject;

/// <summary>
/// Resolver which finds the assemblies which were read into a module, and defers to another resolver for the rest.<para/>
/// The resolver of a module searches the file system alone, so an assembly which only exists in memory, or which was read
/// from a stream which is not backed by a file, could not be found by it.
/// </summary>
/// <param name="fallback">Resolver which finds the assemblies which were not read into the module.</param>
internal sealed class CachedAssemblyResolver(IAssemblyResolver fallback) : IAssemblyResolver
{
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
    /// Read the assembly of <paramref name="name"/> which is loaded in the process from the file which it was loaded from.
    /// </summary>
    /// <remarks>
    /// Nothing hands the image of an assembly over on .NET 5 and later, so an assembly which was loaded from bytes, or
    /// which belongs to no file at all, cannot be read here. The file of one which was loaded from a file outside the
    /// directories which the resolver of the module searches can be read though, and that is the case this covers.
    /// </remarks>
    /// <param name="name">Name of the assembly.</param>
    /// <returns>The assembly definition, or null when no assembly of that name is loaded, or its file can't be read.</returns>
    private AssemblyDefinition? ReadLoadedAssembly(AssemblyNameReference name)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == name.Name);
        if (assembly == null) return null;

        // Asking a dynamic assembly for its location throws, and one which was loaded from bytes holds an empty one.
        string location;
        try
        {
            location = assembly.Location;
        }
        catch (NotSupportedException)
        {
            return null;
        }

        if (string.IsNullOrEmpty(location) || !File.Exists(location)) return null;

        using var memoryStream = new MemoryStream(File.ReadAllBytes(location));
        return AssemblyDefinition.ReadAssembly(memoryStream, new ReaderParameters
        {
            InMemory         = true,
            ReadWrite        = false,
            ReadingMode      = ReadingMode.Deferred,
            AssemblyResolver = this
        });
    }
}
