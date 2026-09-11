namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for a struct of the metadata which is built.
/// </summary>
/// <param name="assemblyHandler">Handler of the assembly which declares the struct.</param>
/// <param name="source">The struct definition which is handled.</param>
internal class StructHandler(AssemblyHandler assemblyHandler, TypeDefinition source) : TypeHandler(assemblyHandler, source), IStructHandler;
