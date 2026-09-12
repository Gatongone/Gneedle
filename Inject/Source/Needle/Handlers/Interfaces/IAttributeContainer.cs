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
/// Extensions for <see cref="IAttributeContainer"/> which name the attribute by a runtime type.<para/>
/// A type of the runtime cannot be given to the members of the interface, because a <see cref="System.Type"/> is not an
/// <see cref="IType"/> and is not converted to one on the way to a parameter of it: what is read and written by the
/// name of a type is reached through here, and a container of any kind answers to it.
/// </summary>
public static class AttributeExtensions
{
    /// <param name="container">The container which the attributes are read from and written to.</param>
    extension(IAttributeContainer container)
    {
        /// <inheritdoc cref="IAttributeContainer.ContainsAttribute(IType)"/>
        public bool ContainsAttribute(Type attributeType) => container.ContainsAttribute(attributeType.ToGneedleType());

        /// <summary>
        /// Check whether the container carries an attribute of the given type.
        /// </summary>
        /// <typeparam name="TAttribute">The type of the attribute.</typeparam>
        /// <returns>Whether the container carries the attribute.</returns>
        public bool ContainsAttribute<TAttribute>() where TAttribute : Attribute => container.ContainsAttribute(typeof(TAttribute));

        /// <inheritdoc cref="IAttributeContainer.AddAttribute(IType,object[])"/>
        /// <exception cref="ArgumentException">Thrown when the type is not an attribute, or when it holds no constructor which takes the arguments.</exception>
        public void AddAttribute(Type attributeType, params object[] arguments)
        {
            if (!typeof(Attribute).IsAssignableFrom(attributeType))
                throw new ArgumentException(string.Format(ErrorMessages.TYPE_CANNOT_ASSIGN_TO_TARGET_TYPE, typeof(Attribute)));
            container.AddAttribute(attributeType.ToGneedleType(), arguments);
        }

        /// <summary>
        /// Append an attribute to the container.
        /// </summary>
        /// <param name="arguments">The arguments of the constructor which is called.</param>
        /// <typeparam name="TAttribute">The type of the attribute.</typeparam>
        /// <exception cref="ArgumentException">Thrown when the attribute type holds no constructor which takes the arguments.</exception>
        public void AddAttribute<TAttribute>(params object[] arguments) where TAttribute : Attribute => container.AddAttribute(typeof(TAttribute), arguments);
    }
}