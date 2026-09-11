using System.Diagnostics;
using GenericParameterAttributes = System.Reflection.GenericParameterAttributes;

namespace Gneedle.Inject;

/// <summary>
/// Constraints inform the compiler about the capabilities a type argument must have.<para/>
/// Without any constraints, the type argument could be any type.<para/>
/// The compiler can only assume the members of System.Object, which is the ultimate base class for any .NET type.<para/>
/// If client code uses a type that doesn't satisfy a constraint, the compiler issues an error. Constraints are specified by using the where contextual keyword.
/// </summary>
public struct Constraint
{
    /// <summary>
    /// The constraint name, it could be any type name type where constraint form or constraints informing.
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// The ultimate base class for the type.
    /// </summary>
    public readonly IType? Type;

    /// <summary>
    /// The constraints on a generic type parameter of a generic type or method.
    /// </summary>
    public readonly GenericParameterAttributes GenericParameterAttributes;

    /// <summary>
    /// Create a constraint type from any type.
    /// </summary>
    /// <param name="type">Type constraint to.</param>
    /// <param name="genericParameterAttributes">Generic parameter attributes.</param>
    public Constraint(IType type, GenericParameterAttributes genericParameterAttributes = GenericParameterAttributes.None)
    {
        Type = type;
        Name = type.ToString()!;
        Debug.Assert(type.ToString() != null);

        GenericParameterAttributes = genericParameterAttributes | type switch
        {
            GenericParameterType genericParameterType                     => genericParameterType.Constraints.Combined(),
            GenericType {Type.IsGenericParameter   : true} genericType    => genericType.Type.GenericParameterAttributes,
            NongenericType {Type.IsGenericParameter: true} nonGenericType => nonGenericType.Type.GenericParameterAttributes,
            _                                                             => GenericParameterAttributes.None
        };
    }

    /// <summary>
    /// Create a constraint which from generic parameter.
    /// </summary>
    /// <param name="name">Generic parameter name.</param>
    /// <param name="genericParameterAttributes">Generic parameter attributes.</param>
    public Constraint(string name, GenericParameterAttributes genericParameterAttributes)
    {
        Type = null;
        Name = name;
        GenericParameterAttributes = genericParameterAttributes;
    }

    /// <summary>
    /// Create a constraint which from a system type.
    /// </summary>
    /// <param name="name">Type name.</param>
    /// <param name="type">The type that constraint to.</param>
    /// <param name="genericParameterAttributes">Generic parameter attributes.</param>
    private Constraint(string name, IType type, GenericParameterAttributes genericParameterAttributes)
    {
        Type = type;
        Name = name;
        GenericParameterAttributes = genericParameterAttributes;
    }

    /// <summary>
    /// Create a constraint type from type itself.
    /// </summary>
    public static Constraint FromSelf() => new(new SelfType());

    /// <summary>
    /// Create a constraint type from type itself.
    /// </summary>
    /// <param name="genericParameterNames">Type's generic parameter names.</param>
    public static Constraint FromSelf(params string[] genericParameterNames) => new(new SelfType(genericParameterNames));

    /// <summary>
    /// Create a constraint type from type itself.
    /// </summary>
    /// <param name="genericArguments">Type's generic arguments.</param>
    public static Constraint FromSelf(params IType[] genericArguments) => new(new SelfType(genericArguments));

    /// <summary>
    /// Create a constraint type from type itself.
    /// </summary>
    /// <param name="genericArguments">Type's generic arguments.</param>
    public static Constraint FromSelf(params Type[] genericArguments) => new(new SelfType(genericArguments));

    /// <summary>
    /// Create a constraint type from type.
    /// </summary>
    public static Constraint FromType(Type type) => new(type.ToGneedleType());

    /// <summary>
    /// Create a constraint type from type.
    /// </summary>
    public static Constraint FromType<T>() => new(typeof(T).ToGneedleType());

    /// <summary>
    /// Create a constraint type from type.
    /// </summary>
    /// <param name="type"></param>
    public static Constraint FromType(IType type) => new(type);

    /// <summary>
    /// The type argument must have a public parameterless constructor.<para/>
    /// When used together with other constraints, the new() constraint must be specified last.<para/>
    /// The new() constraint can't be combined with the struct and unmanaged constraints.<para/>
    /// </summary>
    public static Constraint New = new("'new()'", GenericParameterAttributes.DefaultConstructorConstraint);

    /// <summary>
    /// The type argument must be a non-nullable value type.<para/>
    /// For information about nullable value types, see Nullable value types.<para/>
    /// Because all value types have an accessible parameterless constructor, the struct constraint implies the new() constraint and can't be combined with the new() constraint.<para/>
    /// You can't combine the struct constraint with the unmanaged constraint.
    /// </summary>
    public static Constraint Struct = new("'struct'", new NongenericType(typeof(ValueType)), GenericParameterAttributes.NotNullableValueTypeConstraint | GenericParameterAttributes.DefaultConstructorConstraint);

    /// <summary>
    /// The type argument must be a reference type.<para/>
    /// This constraint applies also to any class, interface, delegate, or array type.<para/>
    /// In a nullable context, T must be a non-nullable reference type.
    /// </summary>
    public static Constraint Class = new("'class'", GenericParameterAttributes.ReferenceTypeConstraint);

    /// <summary>
    /// The type argument must be a non-nullable unmanaged type.<para/>
    /// The unmanaged constraint implies the struct constraint and can't be combined with either the struct or new() constraints.
    /// </summary>
    public static Constraint Unmanaged = new("'unmanaged'", GenericParameterAttributes.NotNullableValueTypeConstraint | GenericParameterAttributes.DefaultConstructorConstraint);

    /// <summary>
    /// The type argument must be a non-nullable type.<para/>
    /// The argument can be a non-nullable reference type or a non-nullable value type.
    /// </summary>
    public static Constraint NotNull = new("'notnull'", GenericParameterAttributes.None);

    /// <summary>
    /// The generic type parameter is contravariant.<para/>
    /// A contravariant type parameter can appear as a parameter type in method signatures.
    /// </summary>
    public static Constraint In = new("'in'", GenericParameterAttributes.Contravariant);

    /// <summary>
    /// The generic type parameter is covariant.<para/>
    /// A covariant type parameter can appear as the result type of method, the type of read-only field, a declared base type, or an implemented interface.
    /// </summary>
    public static Constraint Out = new("'out'", GenericParameterAttributes.Covariant);
}

/// <summary>
/// Extensions for <see cref="Constraint"/>.
/// </summary>
internal static class ConstraintExtension
{
    /// <summary>
    /// Combined constraint's GenericParameterAttributes.
    /// </summary>
    internal static GenericParameterAttributes Combined(this IEnumerable<Constraint> constraints) => constraints.Aggregate(new GenericParameterAttributes(), (current, constraint) => current | constraint.GenericParameterAttributes);
}