namespace Gneedle.Inject;

internal class ClassHandler(AssemblyHandler assemblyHandler, TypeDefinition source) : TypeHandler(assemblyHandler, source), IClassHandler
{
    public IClassHandler BaseType
    {
        get
        {
            var baseDefinition = AssemblyHandler.GetCecilType(Source.BaseType).Definition;
            return (IClassHandler) AssemblyHandler.GetType(baseDefinition);
        }
    }
}