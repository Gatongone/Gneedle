namespace Gneedle.Inject;

/// <summary>
/// Represents a container which carries the custom attributes of the metadata which it stands for.
/// </summary>
public interface IAttributeContainer
{
    /// <summary>
    /// Check whether the container carries an attribute of the given type.
    /// </summary>
    /// <param name="attributeType">The type of the attribute.</param>
    /// <returns>Whether the container carries the attribute.</returns>
    bool ContainsAttribute(IType attributeType);

    /// <summary>
    /// Append an attribute to the container.
    /// </summary>
    /// <param name="attributeType">The type of the attribute.</param>
    /// <param name="arguments">The arguments of the constructor which is called.</param>
    /// <exception cref="ArgumentException">Thrown when the attribute type holds no constructor which takes the arguments.</exception>
    void AddAttribute(IType attributeType, params object[] arguments);
}

/// <summary>
/// Extensions for <see cref="IAttributeContainer"/> which name the attribute by a runtime type.
/// </summary>
public static class AttributeExtensions
{
    extension(IAttributeContainer container)
    {
        /// <inheritdoc cref="IAttributeContainer.ContainsAttribute(IType)"/>
        public bool ContainsAttribute(Type attributeType) => container.ContainsAttribute(attributeType.ToGneedleType());

        /// <inheritdoc cref="IAttributeContainer.ContainsAttribute(IType)"/>
        /// <typeparam name="TAttribute">The type of the attribute.</typeparam>
        public bool ContainsAttribute<TAttribute>() where TAttribute : Attribute => container.ContainsAttribute(typeof(TAttribute));
    }
}