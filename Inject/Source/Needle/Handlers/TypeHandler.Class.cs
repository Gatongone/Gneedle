namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for a class of the metadata which is built.
/// </summary>
/// <param name="assemblyHandler">Handler of the assembly which declares the class.</param>
/// <param name="source">The class definition which is handled.</param>
internal class ClassHandler(AssemblyHandler assemblyHandler, TypeDefinition source) : TypeHandler(assemblyHandler, source), IClassHandler
{
    /// <inheritdoc/>
    public ClassFlags Flags => Source.ToClassFlags();

    /// <inheritdoc/>
    public IClassHandler? BaseType
    {
        get
        {
            // A class which derives from nothing holds no base type to read, which is the root of a hierarchy rather
            // than a mistake, so none is what the query is answered with.
            if (Source.BaseType == null) return null;
            var baseDefinition = AssemblyHandler.GetCecilType(Source.BaseType).Definition;
            return (IClassHandler) AssemblyHandler.GetType(baseDefinition);
        }
    }
}