namespace Gneedle.Inject;

/// <summary>
/// Handler for a field definition.
/// </summary>
public interface IFieldHandler : IAttributeContainer
{
    /// <summary>
    /// Gets the name of the field.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the handler of the declaring type.
    /// </summary>
    ITypeHandler DeclaringTypeHandler { get; }
}
