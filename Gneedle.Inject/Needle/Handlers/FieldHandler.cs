namespace Gneedle.Inject;

/// <summary>
/// Handler for a field definition, providing access to the underlying Cecil field.
/// </summary>
internal class FieldHandler(FieldDefinition fieldDef, TypeHandler declaringTypeHandler) : IFieldHandler
{
    /// <summary>
    /// The underlying Cecil field definition.
    /// </summary>
    internal readonly FieldDefinition Source = fieldDef;

    /// <summary>
    /// The handler of the declaring type.
    /// </summary>
    internal readonly TypeHandler DeclaringTypeHandler = declaringTypeHandler;

    /// <inheritdoc/>
    public string Name => Source.Name;

    /// <inheritdoc/>
    ITypeHandler IFieldHandler.DeclaringTypeHandler => DeclaringTypeHandler;
}
