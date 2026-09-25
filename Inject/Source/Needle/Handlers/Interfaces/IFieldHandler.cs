namespace Gneedle.Inject;

/// <summary>
/// Handler for a field definition.
/// </summary>
public interface IFieldHandler : IAttributeContainer
{
    /// <summary>
    /// Flags of the field, which are the visibility and the modifiers which the definition declares, and the shape of
    /// belonging which is <see cref="FieldFlags.Static"/> or <see cref="FieldFlags.Instance"/>.
    /// </summary>
    FieldFlags Flags { get; }

    /// <summary>
    /// Gets the name of the field.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Type of the value which the field holds, which is described by <see cref="ReferencedType"/> where the assembly
    /// being woven declares the type, and by <see cref="GenericParameterType"/> where it stands for a generic parameter
    /// of the type which declares the field.<para/>
    /// The type is the one which the definition declares, so it is the description which a decorator describes the
    /// field with as well, and the two are read as one type by the names of the tree.
    /// </summary>
    IType FieldType { get; }

    /// <summary>
    /// Gets the handler of the declaring type.
    /// </summary>
    ITypeHandler DeclaringTypeHandler { get; }
}