namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for a class of the metadata which is built.
/// </summary>
/// <param name="assemblyHandler">Handler of the assembly which declares the class.</param>
/// <param name="source">The class definition which is handled.</param>
internal class ClassHandler(AssemblyHandler assemblyHandler, TypeDefinition source) : TypeHandler(assemblyHandler, source), IClassHandler
{
    /// <inheritdoc/>
    public IClassHandler BaseType
    {
        get
        {
            var baseDefinition = AssemblyHandler.GetCecilType(Source.BaseType).Definition;
            return (IClassHandler) AssemblyHandler.GetType(baseDefinition);
        }
    }
}