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
    /// Get the handler of the setter of the property, or null when it has none.<para/>
    /// The handler stands for the accessor as the handler of any other method stands for its method, so the body of the
    /// accessor is set through <see cref="IMethodHandler.SetBody(MethodInfo)"/> and woven around through
    /// <see cref="IMethodHandler.AroundBody(MethodInfo)"/>. An accessor is created by the call which sets its body, so
    /// a property which has no setter returns null here until one is set.
    /// </summary>
    /// <returns>The handler of the setter, or null.</returns>
    IMethodHandler? GetSetter();

    /// <summary>
    /// Get the handler of the getter of the property, or null when it has none.<para/>
    /// The handler stands for the accessor as the handler of any other method stands for its method, so the body of the
    /// accessor is set through <see cref="IMethodHandler.SetBody(MethodInfo)"/> and woven around through
    /// <see cref="IMethodHandler.AroundBody(MethodInfo)"/>. An accessor is created by the call which sets its body, so
    /// a property which has no getter returns null here until one is set.
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

/// <summary>
/// Extensions for the decorator which describes a property, which name its accessor bodies by a delegate.
/// </summary>
public static class PropertyExtensions
{
    /// <summary>
    /// Set the body of the getter from the delegate which holds the IL to copy.
    /// </summary>
    /// <param name="decorator">The decorator which describes the property.</param>
    /// <param name="delegation">The delegate which holds the body.</param>
    /// <returns>Result for chains calling.</returns>
    public static PropertyDecorator.IAccessorDecorator WithGetter(this PropertyDecorator.IAccessorDecorator decorator, Delegate delegation)
        => decorator.WithGetter(delegation.Method);

    /// <summary>
    /// Set the body of the setter from the delegate which holds the IL to copy.
    /// </summary>
    /// <param name="decorator">The decorator which describes the property.</param>
    /// <param name="delegation">The delegate which holds the body.</param>
    /// <returns>Result for chains calling.</returns>
    public static PropertyDecorator.IAccessorDecorator WithSetter(this PropertyDecorator.IAccessorDecorator decorator, Delegate delegation)
        => decorator.WithSetter(delegation.Method);
}