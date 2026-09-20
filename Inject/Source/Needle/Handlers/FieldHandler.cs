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
    public FieldFlags Flags => Source.ToFieldFlags();

    /// <inheritdoc/>
    public string Name => Source.Name;

    /// <summary>
    /// Get the string representation of the field, which is the declaration of it.
    /// </summary>
    /// <returns>The declaration of the field.</returns>
    public override string ToString() => ToString(0);

    /// <summary>
    /// The same, written at the given number of levels of indentation, which is what a member of a type is written at.
    /// </summary>
    /// <param name="level">The number of levels of indentation which the declaration stands at.</param>
    /// <returns>The declaration of the field, indented by that many levels.</returns>
    internal string ToString(int level)
        => $"{new string(' ', level * MethodHandler.Indentation)}.field {MethodHandler.TheAttributesOf(Source)} {Source.FieldType.FullName} {Source.Name}\n";

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