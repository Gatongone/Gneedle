namespace Gneedle.Inject;

/// <summary>
/// Parsed member of the operand properties.
/// </summary>
[Flags]
internal enum MemberSymbols
{
    /// <summary>
    /// It stands for the operand is not a parsing symbol.
    /// </summary>
    None = 0b0000000,

    /// <summary>
    /// It stands for the operand should be parsed as field operation.
    /// </summary>
    Field = 0b0000001,

    /// <summary>
    /// It stands for the operand should be parsed as property operation.
    /// </summary>
    Property = 0b0000010,

    /// <summary>
    /// It stands for the operand should be parsed as method operation.
    /// </summary>
    Method = 0b0000100,

    /// <summary>
    /// It stands for the operand should be parsed as this pointer operation.
    /// </summary>
    This = 0b0001000,

    /// <summary>
    /// It stands for the operand should be parsed as base pointer operation.
    /// </summary>
    Base = 0b0010000,

    /// <summary>
    /// It stands for the operand should be parsed as other object pointer operation.
    /// </summary>
    Object = 0b0100000,

    /// <summary>
    /// It stands for the operand should be parsed as other static pointer operation.
    /// </summary>
    Static = 0b1000000
}

/// <summary>
/// The symbol of the field or property pointer.
/// </summary>
public sealed class ValuableMember
{
    /// <summary>
    /// Get the value from the field or property.
    /// </summary>
    /// <remarks>
    /// It would cause boxing when the field or property is ValueType.
    /// So you should use <see cref="ValuableMember{T}"/> if you is going to avoid it.
    /// </remarks>
    /// <returns>The value from the field or property.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the injection doesn't work.</exception>
    public object Get() => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Set the value to the field or property.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="ValuableMember.Get()"/>
    /// </remarks>
    /// <param name="value">The value need to set.</param>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the injection doesn't work.</exception>
    public void Set(object value) => throw new InjectionNotEffectiveException();
}

public sealed class ValuableMember<T>
{
    /// <summary>
    /// Get the value from the field or property.
    /// </summary>
    /// <returns>The value from the field or property.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the injection doesn't work.</exception>
    public T Get() => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Set the value to the field or property.
    /// </summary>
    /// <param name="value">The value need to set.</param>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the injection doesn't work.</exception>
    public void Set(T value) => throw new InjectionNotEffectiveException();
}

/// <summary>
/// The symbol of the <c>this</c> pointer.
/// </summary>
public static class This
{
    /// <summary>
    /// Full name of the class.
    /// </summary>
    internal const string TYPE_NAME = $"{nameof(Gneedle)}.{nameof(Inject)}.{nameof(This)}";

    /// <inheritdoc cref="Property(string)"/>
    /// <typeparam name="TProperty">Property type.</typeparam>
    public static ValuableMember<TProperty> Property<TProperty>(string name) => throw new InjectionNotEffectiveException();

    /// <inheritdoc cref="Property(string)"/>
    /// <typeparam name="TField">Field type.</typeparam>
    public static ValuableMember<TField> Field<TField>(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the property from <c>this</c> pointer.
    /// </summary>
    /// <param name="name">Property name.</param>
    /// <returns>The symbol of the property.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operation didn't be parsed.</exception>
    public static ValuableMember<Object> Property(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the field from <c>this</c> pointer.
    /// </summary>
    /// <param name="name">Field name.</param>
    /// <returns>The symbol of the field.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static ValuableMember<Object> Field(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the method from <c>this</c> pointer.
    /// </summary>
    /// <param name="name">Method name</param>
    /// <typeparam name="TMethod">Method signature without name.</typeparam>
    /// <returns>The symbol of the method.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static TMethod Method<TMethod>(string name) where TMethod : Delegate => throw new InjectionNotEffectiveException();
}

/// <summary>
/// The symbol of the <c>base</c> pointer.
/// </summary>
public static class Base
{
    /// <summary>
    /// Full name of the class.
    /// </summary>
    internal const string TYPE_NAME = $"{nameof(Gneedle)}.{nameof(Inject)}.{nameof(Base)}";

    /// <inheritdoc cref="Property(string)"/>
    /// <typeparam name="TProperty">Property type.</typeparam>
    public static ValuableMember<TProperty> Property<TProperty>(string name) => throw new InjectionNotEffectiveException();

    /// <inheritdoc cref="Property(string)"/>
    /// <typeparam name="TField">Field type.</typeparam>
    public static ValuableMember<TField> Field<TField>(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the property from <c>base</c> pointer.
    /// </summary>
    /// <param name="name">Property name.</param>
    /// <returns>The symbol of the property.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operation didn't be parsed.</exception>
    public static ValuableMember<Object> Property(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the field from <c>base</c> pointer.
    /// </summary>
    /// <param name="name">Field name.</param>
    /// <returns>The symbol of the field.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static ValuableMember<Object> Field(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the method from <c>base</c> pointer.
    /// </summary>
    /// <param name="name">Method name</param>
    /// <typeparam name="TMethod">Method signature without name.</typeparam>
    /// <returns>The symbol of the method.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static TMethod Method<TMethod>(string name) where TMethod : Delegate => throw new InjectionNotEffectiveException();
}

/// <summary>
/// The symbol of the instance pointer.
/// </summary>
public class Object
{
    /// <summary>
    /// Full name of the class.
    /// </summary>
    internal const string TYPE_NAME = $"{nameof(Gneedle)}.{nameof(Inject)}.{nameof(Object)}";

    public Object(params object[] args) => throw new InjectionNotEffectiveException();

    /// <inheritdoc cref="Property(string)"/>
    /// <typeparam name="TProperty">Property type.</typeparam>
    public ValuableMember<TProperty> Property<TProperty>(string name) => throw new InjectionNotEffectiveException();

    /// <inheritdoc cref="Property(string)"/>
    /// <typeparam name="TField">Field type.</typeparam>
    public ValuableMember<TField> Field<TField>(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the property from instance pointer.
    /// </summary>
    /// <param name="name">Property name.</param>
    /// <returns>The symbol of the property.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operation didn't be parsed.</exception>
    public ValuableMember<Object> Property(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the field from instance pointer.
    /// </summary>
    /// <param name="name">Field name.</param>
    /// <returns>The symbol of the field.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public ValuableMember<Object> Field(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the method from instance pointer.
    /// </summary>
    /// <param name="name">Method name</param>
    /// <typeparam name="TMethod">Method signature without name.</typeparam>
    /// <returns>The symbol of the method.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public TMethod Method<TMethod>(string name) where TMethod : Delegate => throw new InjectionNotEffectiveException();
}

/// <summary>
/// The symbol of the static class pointer.
/// </summary>
public class Static
{
    /// <summary>
    /// Full name of the class.
    /// </summary>
    internal const string TYPE_NAME = $"{nameof(Gneedle)}.{nameof(Inject)}.{nameof(Static)}";

    /// <summary>
    /// Get static type from fullname.
    /// </summary>
    /// <param name="fullName">Static type full name.</param>
    /// <returns>Static type</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static Static From(string fullName) => throw new InjectionNotEffectiveException();

    /// <inheritdoc cref="Property(string)"/>
    /// <typeparam name="TProperty">Property type.</typeparam>
    public ValuableMember<TProperty> Property<TProperty>(string name) => throw new InjectionNotEffectiveException();

    /// <inheritdoc cref="Property(string)"/>
    /// <typeparam name="TField">Field type.</typeparam>
    public ValuableMember<TField> Field<TField>(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the property from the static pointer.
    /// </summary>
    /// <param name="name">Property name.</param>
    /// <returns>The symbol of the property.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operation didn't be parsed.</exception>
    public ValuableMember<Object> Property(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the field from the static pointer.
    /// </summary>
    /// <param name="name">Field name.</param>
    /// <returns>The symbol of the field.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public ValuableMember<Object> Field(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the method from the static pointer.
    /// </summary>
    /// <param name="name">Method name</param>
    /// <typeparam name="TMethod">Method signature without name.</typeparam>
    /// <returns>The symbol of the method.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public TMethod Method<TMethod>(string name) where TMethod : Delegate => throw new InjectionNotEffectiveException();
}