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
    /// It stands for the operand should be parsed as the instance pointer which the template was given.
    /// </summary>
    Instance = 0b0100000,

    /// <summary>
    /// It stands for the operand should be parsed as other static pointer operation.
    /// </summary>
    Static = 0b1000000,

    /// <summary>
    /// It stands for the operand should be parsed as the implementation which the body being woven around holds.
    /// </summary>
    Proceed = 0b10000000
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
    /// <exception cref="InjectionNotEffectiveException">Thrown when the member is called where it is written rather than from a member which was woven.</exception>
    public object Get() => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Set the value to the field or property.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="ValuableMember.Get()"/>
    /// </remarks>
    /// <param name="value">The value need to set.</param>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the member is called where it is written rather than from a member which was woven.</exception>
    public void Set(object value) => throw new InjectionNotEffectiveException();
}

/// <summary>
/// The symbol of the field or property pointer, which reads and writes a value of the type which it stands for.<para/>
/// Use it rather than <see cref="ValuableMember"/> when the field or property is of a value type, which that one boxes.
/// </summary>
public sealed class ValuableMember<T>
{
    /// <summary>
    /// Get the value from the field or property.
    /// </summary>
    /// <returns>The value from the field or property.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the member is called where it is written rather than from a member which was woven.</exception>
    public T Get() => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Set the value to the field or property.
    /// </summary>
    /// <param name="value">The value need to set.</param>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the member is called where it is written rather than from a member which was woven.</exception>
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
    public static ValuableMember<Instance> Property(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the field from <c>this</c> pointer.
    /// </summary>
    /// <param name="name">Field name.</param>
    /// <returns>The symbol of the field.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static ValuableMember<Instance> Field(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the method from <c>this</c> pointer.
    /// </summary>
    /// <param name="name">Method name</param>
    /// <typeparam name="TMethod">Method signature without name.</typeparam>
    /// <returns>The symbol of the method.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static TMethod Method<TMethod>(string name) where TMethod : Delegate => throw new InjectionNotEffectiveException();

    /// <summary>
    /// The instance which the member being woven belongs to, which a template reaches the members of.<para/>
    /// What it is for is the instance itself rather than a member of it: a template which compares the instance with
    /// something, or hands it to a call of its own, reads it here. A member which is static belongs to no instance, and
    /// a template of one which reads this is refused by name.
    /// </summary>
    public static T_Self Reference => throw new InjectionNotEffectiveException();
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
    public static ValuableMember<Instance> Property(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the field from <c>base</c> pointer.
    /// </summary>
    /// <param name="name">Field name.</param>
    /// <returns>The symbol of the field.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static ValuableMember<Instance> Field(string name) => throw new InjectionNotEffectiveException();

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
/// The symbol of the implementation which the body being woven around holds.<para/>
/// A body which is set through <see cref="IMethodHandler.AroundBody"/> keeps the body which the method had, and the
/// template reaches that body through <see cref="Method{TMethod}"/>.
/// </summary>
public static class Proceed
{
    /// <summary>
    /// Full name of the class.
    /// </summary>
    internal const string TYPE_NAME = $"{nameof(Gneedle)}.{nameof(Inject)}.{nameof(Proceed)}";

    /// <summary>
    /// Get the method which holds the body of the method which is woven around.<para/>
    /// The member which the call proceeds into is the member being woven rather than one which is named, so the call
    /// takes no name: a template is woven around one member at a time, and that member is the one whose body was taken
    /// over. What the generic argument describes is the signature of that body, which is the signature of the member.
    /// </summary>
    /// <typeparam name="TMethod">Method signature without name.</typeparam>
    /// <returns>The symbol of the method.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static TMethod Method<TMethod>() where TMethod : Delegate => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Call the body which was taken over with the arguments which the template itself was given, and hand back what
    /// that body hands back.<para/>
    /// The template of an around body keeps the signature of the member which is woven, so its parameters are the
    /// arguments of that member, and a call which passes them on names no signature of its own: the type of the value
    /// which is handed back is what the call names, and the weaving writes the arguments of the template into the call.
    /// </summary>
    /// <typeparam name="TResult">Type of the value which the body hands back, which is the type of the member.</typeparam>
    /// <returns>The value which the body of the member hands back.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static TResult Invoke<TResult>() => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Call the body which was taken over with the arguments which the template itself was given, where the member
    /// which is woven hands nothing back.
    /// </summary>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public static void Invoke() => throw new InjectionNotEffectiveException();
}

/// <summary>
/// The symbol of the instance pointer.<para/>
/// It is named after the instance which it holds rather than after the type of the framework, which it would otherwise
/// shadow wherever a template is written among the usings of this library.
/// </summary>
public class Instance
{
    /// <summary>
    /// Full name of the class.
    /// </summary>
    internal const string TYPE_NAME = $"{nameof(Gneedle)}.{nameof(Inject)}.{nameof(Instance)}";

    /// <summary>
    /// Wrap the instance which the described member is read from or called on.
    /// </summary>
    /// <param name="args">The instance alone, so that the array which the compiler builds holds a single element.</param>
    public Instance(params object[] args) => throw new InjectionNotEffectiveException();

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
    public ValuableMember<Instance> Property(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the field from instance pointer.
    /// </summary>
    /// <param name="name">Field name.</param>
    /// <returns>The symbol of the field.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public ValuableMember<Instance> Field(string name) => throw new InjectionNotEffectiveException();

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
    public ValuableMember<Instance> Property(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the field from the static pointer.
    /// </summary>
    /// <param name="name">Field name.</param>
    /// <returns>The symbol of the field.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public ValuableMember<Instance> Field(string name) => throw new InjectionNotEffectiveException();

    /// <summary>
    /// Get the method from the static pointer.
    /// </summary>
    /// <param name="name">Method name</param>
    /// <typeparam name="TMethod">Method signature without name.</typeparam>
    /// <returns>The symbol of the method.</returns>
    /// <exception cref="InjectionNotEffectiveException">Thrown when the operand didn't be parsed.</exception>
    public TMethod Method<TMethod>(string name) where TMethod : Delegate => throw new InjectionNotEffectiveException();
}