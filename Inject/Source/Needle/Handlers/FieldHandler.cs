namespace Gneedle.Inject;

/// <summary>
/// Handler for a field definition, providing access to the underlying Cecil field.
/// </summary>
internal class FieldHandler(FieldDefinition fieldDef, TypeHandler declaringTypeHandler) : IFieldHandler, IAttributeContainer
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

    /// <inheritdoc/>
    public bool ContainsAttribute(IType attributeType) => Source.CustomAttributes.Any(attribute => TypeName.HasSameName(attribute.AttributeType, attributeType));

    /// <inheritdoc/>
    public void AddAttribute(IType attributeType, params object[] arguments)
    {
        var attributeDef = DeclaringTypeHandler.AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = attributeDef.CreateCustomAttribute(DeclaringTypeHandler.AssemblyHandler.Assembly.Source.MainModule, arguments);
        Source.CustomAttributes.Add(attribute);
    }
}
