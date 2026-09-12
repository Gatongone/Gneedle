using System.Reflection;
using GenericParameterAttributes = Mono.Cecil.GenericParameterAttributes;

namespace Gneedle.Inject;

/// <summary>
/// Type used for describing class generic argument, method generic argument and method parameter type information.
/// </summary>
public interface IType;

/// <summary>
/// Generic parameter type.
/// </summary>
/// <param name="name">The generic parameter type name.</param>
/// <param name="constraints">Generic parameter type.</param>
public sealed class GenericParameterType(string name, params Constraint[] constraints) : IType
{
    /// <summary>
    /// The generic parameter type name.
    /// </summary>
    public readonly string TypeName = name;

    /// <summary>
    /// The generic parameter constraints.
    /// </summary>
    public readonly Constraint[] Constraints = constraints;

    /// <summary>
    /// Create a generic parameter type from name.
    /// </summary>
    /// <param name="name">The generic parameter type name.</param>
    public GenericParameterType(string name) : this(name, Array.Empty<Constraint>()) { }

    /// <summary>
    /// Get type name.
    /// </summary>
    /// <returns>Type name.</returns>
    public override string ToString() => TypeName;

    /// <summary>
    /// Get generic Parameter attributes from constraints.
    /// </summary>
    /// <returns>Generic parameter attributes for current generic parameter type.</returns>
    internal GenericParameterAttributes GetGenericParameterAttributes() => Constraints.Aggregate(new GenericParameterAttributes(), (current, constraint) => current | constraint.GenericParameterAttributes.ToCecilAttribute());
}

/// <summary>
/// Type without any generic arguments.
/// </summary>
public sealed class NongenericType : IType
{
    /// <summary>
    /// Type without any generic arguments.
    /// </summary>
    public readonly Type Type;

    /// <summary>
    /// Create a non-generic type from system type.
    /// </summary>
    /// <param name="type">System type with non generic.</param>
    /// <exception cref="ArgumentException">Thrown when the <c>type</c> is type.</exception>
    public NongenericType(Type type)
    {
        if (type.IsGenericType)
            throw new ArgumentException(string.Format(ErrorMessages.IS_NOT_NON_GENERIC_PARAMETER_TYPE, type.FullName));
        Type = type;
    }

    /// <summary>
    /// Create a non-generic type from a system type.
    /// </summary>
    /// <param name="type">The system type which is not generic.</param>
    public static implicit operator NongenericType(Type type) => new(type);

    /// <summary>
    /// Get type name.
    /// </summary>
    /// <returns>Type name.</returns>
    public override string ToString() => Type.FullName!;
}

/// <summary>
/// Type with generic arguments. Samples:
/// <list>
/// <item>√ MyClass&lt;T1, T2&gt;</item>
/// <item>√ MyClass&lt;int, string&gt;</item>
/// <item>√ MyClass&lt;T, string&gt;</item>
/// <item>× MyClass</item>
/// </list>
/// </summary>
public sealed class GenericType : IType
{
    /// <summary>
    /// Generic type definition.
    /// </summary>
    public readonly Type Type;

    /// <summary>
    /// The arguments for the generic type.
    /// </summary>
    public readonly IType[] GenericArguments;

    /// <summary>
    /// Create generic type with generic parameter type arguments.
    /// <example>
    /// If you ganna make <c>MyClass&lt;T1, T2&gt;</c>, you can use:
    /// <code>new Generic(typeof(MyClass&lt;,&gt;), "T1", "T2");</code>
    /// </example>
    /// </summary>
    /// <param name="type">Generic type definition.</param>
    /// <param name="argumentNames">Generic parameter type arguments</param>
    public GenericType(Type type, params string[] argumentNames) : this(type, argumentNames.Select<string, IType>(str => new GenericParameterType(str)).ToArray()) { }

    /// <summary>
    /// Create generic type with any generic arguments.
    /// <example>
    /// If you ganna make <c>MyClass&lt;int, string&gt;</c>, you can use:
    /// <code>new Generic(typeof(MyClass&lt;,&gt;), typeof(int), typeof(string));</code>
    /// </example>
    /// </summary>
    /// <param name="type">Generic type definition.</param>
    /// <param name="argumentTypes">Generic argument Types.</param>
    public GenericType(Type type, params Type[] argumentTypes) : this(type, argumentTypes.Select(p => p switch
    {
        {ContainsGenericParameters: false} and {GenericTypeArguments.Length: 0} => new NongenericType(p),
        {IsGenericParameter: true}                                              => new GenericParameterType(p.Name),
        _                                                                       => new GenericType(p, p.GetGenericArguments()) as IType
    }).ToArray()) { }

    /// <summary>
    /// Create generic type with any generic arguments.
    /// <example>
    /// If you ganna make <c>MyClass&lt;T, string&gt;</c>, you can use:
    /// <code>new Generic(typeof(MyClass&lt;,&gt;), new GenericParameterType("T"), new NongenericType(typeof(string)));</code>
    /// </example>
    /// </summary>
    /// <param name="type">Generic type definition.</param>
    /// <param name="genericArguments">Generic arguments.</param>
    /// <exception cref="ArgumentException">Thrown when the <c>type</c> is not generic type.</exception>
    public GenericType(Type type, params IType[] genericArguments)
    {
        if (!type.IsGenericType) throw new ArgumentException(string.Format(ErrorMessages.IS_NOT_PARAMETERIZED_GENERIC_TYPE, type.FullName));
        Type             = type.GetGenericTypeDefinition();
        GenericArguments = genericArguments;
    }

    /// <summary>
    /// Get type name.
    /// </summary>
    /// <returns>Type name.</returns>
    public override string ToString() => this.GetTypeName().ToString();
}

/// <summary>
/// Type for predefined type definition.
/// </summary>
/// <remarks>
/// If there is a constraint from the type definition self just like <code>class MyClass&lt;T&gt; where T : MyClass&lt;T&gt;</code>
/// How could we know the type detail from other IType implementation? 
/// So we got this type for marking the constraint is from the type itself.
/// </remarks>
internal sealed class SelfType : IType
{
    /// <summary>
    /// Generic arguments for self type.
    /// </summary>
    internal readonly IType[] GenericArguments;

    /// <summary>
    /// Create self type without generic arguments.
    /// </summary>
    internal SelfType() => GenericArguments = Array.Empty<IType>();

    /// <summary>
    /// Create self type with generic arguments.
    /// </summary>
    /// <param name="genericArguments">Generic argument types.</param>
    internal SelfType(IEnumerable<IType> genericArguments) => GenericArguments = genericArguments.ToArray();

    /// <summary>
    /// Create self type with generic parameters.
    /// </summary>
    /// <param name="genericParameterNames">Generic parameter names.</param>
    internal SelfType(IEnumerable<string> genericParameterNames) => GenericArguments = genericParameterNames.Select<string, IType>(name => new GenericParameterType(name)).ToArray();

    /// <summary>
    /// Create self type with generic arguments.
    /// </summary>
    /// <param name="genericArguments">Generic argument types.</param>
    internal SelfType(IEnumerable<Type> genericArguments) => GenericArguments = genericArguments.Select<Type, IType>(arg => new NongenericType(arg)).ToArray();
}

/// <summary>
/// Extension for <see cref="IType"/>.
/// </summary>
public static class TypeInfoExtensions
{
    /// <summary>
    /// Create <see cref="IType"/> from System.Type.
    /// </summary>
    /// <param name="type">The type where create from.</param>
    public static IType ToGneedleType(this Type type) => type switch
    {
        {IsGenericParameter: true}  => new GenericParameterType(type.Name),
        {IsGenericType     : false} => new NongenericType(type),
        _                           => new GenericType(type, type.GetGenericArguments().Select(ToGneedleType).ToArray())
    };

    /// <summary>
    /// Create <see cref="IType"/> array from <see cref="ParameterInfo"/> array.
    /// </summary>
    /// <param name="parameters">The parameter info array.</param>
    /// <returns>An array of <see cref="IType"/> representing the parameter types.</returns>
    public static IType[] GetITypes(this ParameterInfo[] parameters) => parameters.Select(p => p.ParameterType.ToGneedleType()).ToArray();

    /// <summary>
    /// Create Generic type definition from generic parameters.
    /// </summary>
    /// <example>
    /// If we try to describe System.Collections.Generic.Dictionary&lt;TKey,TValue&gt;, we can do this:
    /// <code>
    /// typeof(System.Collections.Generic.Dictionary&lt;,&gt;).WithGenericParameter("TKey","TValue");
    /// </code>
    /// </example>
    /// <param name="type">Raw type.</param>
    /// <param name="genericParameters">Generic parameter names.</param>
    /// <returns>Generic type definition.</returns>
    /// <exception cref="ArgumentException">Thrown when type is not generic.</exception>
    public static GenericType WithGenericParameter(this Type type, params string[] genericParameters) => !type.IsGenericType
        ? throw new ArgumentException(ErrorMessages.TYPE_IS_NOT_GENERIC)
        : new GenericType(type, genericParameters);

    /// <summary>
    /// Create Generic type definition from arguments.
    /// </summary>
    /// <example>
    /// If we try to describe System.Collections.Generic.Dictionary&lt;string,int&gt;, we can do this:
    /// <code>
    /// typeof(System.Collections.Generic.Dictionary&lt;,&gt;).WithGenericParameter(typeof(string), typeof(int));
    /// </code>
    /// </example>
    /// <param name="type">Raw type.</param>
    /// <param name="genericArguments">Generic argument types.</param>
    /// <returns>Generic type definition.</returns>
    /// <exception cref="ArgumentException">Thrown when type is not generic.</exception>
    public static GenericType WithGenericParameter(this Type type, params Type[] genericArguments) => !type.IsGenericType
        ? throw new ArgumentException(ErrorMessages.TYPE_IS_NOT_GENERIC)
        : new GenericType(type, genericArguments);

    /// <summary>
    /// Get type's name.
    /// </summary>
    /// <param name="type">The name owner.</param>
    /// <returns>Type name.</returns>
    public static TypeName GetTypeName(this IType type) => new(type);
}