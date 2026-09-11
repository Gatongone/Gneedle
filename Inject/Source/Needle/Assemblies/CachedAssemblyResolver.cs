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
        return Assemblies.Values.FirstOrDefault(assembly => assembly.Name.Name == name.Name)
            ?? fallback.Resolve(name, parameters);
    }

    /// <inheritdoc/>
    public void Dispose() => fallback.Dispose();
}
