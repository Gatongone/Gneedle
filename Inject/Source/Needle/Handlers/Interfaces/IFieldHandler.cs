namespace Gneedle.Inject;

/// <summary>
/// Handler for a field definition.
/// </summary>
public interface IFieldHandler : IAttributeContainer
{
    /// <summary>
    /// Flags of the field, which are the visibility and the modifiers which the definition declares.
    /// </summary>
    FieldFlags Flags { get; }

    /// <summary>
    /// Gets the name of the field.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the handler of the declaring type.
    /// </summary>
    ITypeHandler DeclaringTypeHandler { get; }
}