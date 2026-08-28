namespace Gneedle.Inject;

internal class StructHandler(AssemblyHandler assemblyHandler, TypeDefinition source) : TypeHandler(assemblyHandler, source), IStructHandler;
