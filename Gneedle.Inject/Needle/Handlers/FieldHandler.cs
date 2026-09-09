namespace Gneedle.Inject;

internal class FieldHandler(FieldDefinition fieldDef, TypeHandler declaringTypeHandler) : IFieldHandler
{
    internal readonly FieldDefinition Source = fieldDef;
    internal readonly TypeHandler DeclaringTypeHandler = declaringTypeHandler;

    public string Name => Source.Name;

    ITypeHandler IFieldHandler.DeclaringTypeHandler => DeclaringTypeHandler;
}
