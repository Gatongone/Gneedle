namespace Gneedle.Inject;

/// <summary>
/// Handler for an enum type definition, providing access to the underlying Cecil type.
/// </summary>
internal class EnumHandler(AssemblyHandler assemblyHandler, TypeDefinition source) : TypeHandler(assemblyHandler, source), IEnumHandler;