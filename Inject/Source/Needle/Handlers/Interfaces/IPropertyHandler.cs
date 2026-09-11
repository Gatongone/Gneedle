using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for a property, which reads its accessors and writes them.
/// </summary>
public interface IPropertyHandler : IAttributeContainer
{
    /// <summary>
    /// Name of the property.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Full name of the property, which holds the name of the type which declares it.
    /// </summary>
    string FullName { get; }

    /// <summary>
    /// Handler of the type which declares the property.
    /// </summary>
    ITypeHandler DeclaringTypeHandler { get; }

    /// <summary>
    /// Get the handler of the setter of the property, or null when it has none.
    /// </summary>
    /// <returns>The handler of the setter, or null.</returns>
    IMethodHandler? GetSetter();

    /// <summary>
    /// Get the handler of the getter of the property, or null when it has none.
    /// </summary>
    /// <returns>The handler of the getter, or null.</returns>
    IMethodHandler? GetGetter();

    /// <summary>
    /// Set the body of the setter of the property from the method which holds the IL to copy.
    /// </summary>
    /// <param name="body">The method which holds the body of the setter.</param>
    /// <exception cref="ArgumentException">Thrown when the parameters of the method do not match the property, which an indexer is read with.</exception>
    void SetSetter(MethodInfo body);

    /// <summary>
    /// Set the body of the getter of the property from the method which holds the IL to copy.
    /// </summary>
    /// <param name="body">The method which holds the body of the getter.</param>
    /// <exception cref="ArgumentException">Thrown when the parameters of the method do not match the property, which an indexer is read with.</exception>
    void SetGetter(MethodInfo body);

    /// <summary>
    /// Set the body of the setter of the property to the default body behavior.
    /// </summary>
    /// <param name="body">The default body of the setter.</param>
    /// <exception cref="ArgumentException">Thrown when the default body is the one with a field operation, which an indexer cannot be written with.</exception>
    void SetSetter(DefaultPropertyBody body);

    /// <summary>
    /// Set the body of the getter of the property to the default body behavior.
    /// </summary>
    /// <param name="body">The default body of the getter.</param>
    void SetGetter(DefaultPropertyBody body);
}