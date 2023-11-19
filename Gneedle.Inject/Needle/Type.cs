/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/18-11:47:36
 * Github: https://github.com/Gatongone
 */

namespace Gneedle.Inject;

/// <summary>
/// Type used for describing class generic argument, method generic argument and method parameter type information.
/// </summary>
public interface IType;

/// <summary>
/// Generic parameter type.
/// </summary>
/// <param name="name">The generic parameter type name.</param>
/// <param name="constraints">Generic parameter type </param>
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
    /// Create a non generic type from system type.
    /// </summary>
    /// <param name="type">System type with non generic.</param>
    /// <exception cref="ArgumentException">Throw when the <c>type</c> is type.</exception>
    public NongenericType(Type type)
    {
        if (type.IsGenericType)
            throw new ArgumentException(string.Format(ErrorResources.IS_NOT_NON_GENERIC_PARAMETER_TYPE, type.FullName));
        Type = type;
    }

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
    public GenericType(Type type, params string[] argumentNames) : this(type, argumentNames.Select(str => new GenericParameterType(str)).ToArray()) { }

    /// <summary>
    /// Create generic type with any generic arguments.
    /// <example>
    /// If you ganna make <c>MyClass&lt;int, string&gt;</c>, you can use:
    /// <code>new Generic(typeof(MyClass&lt;,&gt;), typeof(int), typeof(string));</code>
    /// </example>
    /// </summary>
    /// <param name="type">Generic type definition.</param>
    /// <param name="argumentTypes">Generic argument Types.</param>
    public GenericType(Type type, params Type[] argumentTypes) : this(type, argumentTypes.Select(p =>
    {
        if (!p.ContainsGenericParameters && p.GenericTypeArguments.Length == 0) return new NongenericType(p);
        if (p.IsGenericParameter) return new GenericParameterType(p.Name);
        return new GenericType(p, p.GetGenericArguments()) as IType;
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
    /// <exception cref="ArgumentException">Throw when the <c>type</c> is not generic type.</exception>
    public GenericType(Type type, params IType[] genericArguments)
    {
        if (!type.IsGenericType)
            throw new ArgumentException(string.Format(ErrorResources.IS_NOT_PARAMETERIZED_GENERIC_TYPE, type.FullName));
        Type = type.GetGenericTypeDefinition();
        GenericArguments = genericArguments;
    }

    /// <summary>
    /// Get type name.
    /// </summary>
    /// <returns>Type name.</returns>
    public override string ToString() => this.GetTypeName().ToString();
}

/// <summary>
/// Extension for <see cref="IType"/>.
/// </summary>
public static class TypeExtensions
{
    /// <summary>
    /// Create <see cref="IType"/> from System.Type.
    /// </summary>
    /// <param name="type">The type where create from.</param>
    /// <returns><see cref="IType"/></returns>
    public static IType ToGneedleType(this Type type)
    {
        if (type.IsGenericParameter)
            return new GenericParameterType(type.Name);
        if (!type.IsGenericType)
            return new NongenericType(type);
        return new GenericType(type, type
            .GetGenericArguments()
            .Select(argument => argument.ToGneedleType())
            .ToArray());
    }

    /// <summary>
    /// Get type's name.
    /// </summary>
    /// <param name="type">The name owner.</param>
    /// <returns>Type name.</returns>
    public static TypeName GetTypeName(this IType type) => new(type);
}