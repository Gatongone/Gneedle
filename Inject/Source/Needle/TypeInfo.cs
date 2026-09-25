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
    public GenericParameterType(string name) : this(name, []) { }

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
    /// <exception cref="WeavingException">Thrown when the <c>type</c> is type.</exception>
    public NongenericType(Type type)
    {
        if (type.IsGenericType)
            throw new WeavingException(string.Format(ErrorMessages.IS_NOT_NON_GENERIC_PARAMETER_TYPE, type.FullName));
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
    /// Create generic type with the arguments which the type itself carries, which is what
    /// <see cref="TypeInfoExtensions.ToIType(Type)"/> reads out of the same type.
    /// <example>
    /// If you ganna make <c>MyClass&lt;T1, T2&gt;</c>, you can use:
    /// <code>new GenericType(typeof(MyClass&lt;,&gt;));</code>
    /// </example>
    /// </summary>
    /// <remarks>
    /// A call which names no argument is one which the two overloads below can each be read as, and the compiler refuses
    /// to read a call through either of two of them: this is the one it reads, because it is the one which is called
    /// without a list of arguments rather than with an empty one.
    /// </remarks>
    /// <param name="type">Generic type definition, or a generic type which holds the arguments of it.</param>
    /// <exception cref="WeavingException">Thrown when the <c>type</c> is not generic type.</exception>
    public GenericType(Type type) : this(type, [.. type.GetGenericArguments().Select(argument => argument.ToIType())]) { }

    /// <summary>
    /// Create generic type with generic parameter type arguments.
    /// <example>
    /// If you ganna make <c>MyClass&lt;T1, T2&gt;</c>, you can use:
    /// <code>new Generic(typeof(MyClass&lt;,&gt;), "T1", "T2");</code>
    /// </example>
    /// </summary>
    /// <param name="type">Generic type definition.</param>
    /// <param name="argumentNames">Generic parameter type arguments</param>
    public GenericType(Type type, params string[] argumentNames) : this(type, [.. argumentNames.Select<string, IType>(str => new GenericParameterType(str))]) { }

    /// <summary>
    /// Create generic type with any generic arguments.
    /// <example>
    /// If you ganna make <c>MyClass&lt;int, string&gt;</c>, you can use:
    /// <code>new Generic(typeof(MyClass&lt;,&gt;), typeof(int), typeof(string));</code>
    /// </example>
    /// </summary>
    /// <param name="type">Generic type definition.</param>
    /// <param name="argumentTypes">Generic argument Types.</param>
    public GenericType(Type type, params Type[] argumentTypes) : this(type, [
        .. argumentTypes.Select(p => p switch
        {
            {ContainsGenericParameters: false} and {GenericTypeArguments.Length: 0} => new NongenericType(p),
            {IsGenericParameter: true}                                              => new GenericParameterType(p.Name),
            _                                                                       => new GenericType(p, p.GetGenericArguments()) as IType
        })
    ]) { }

    /// <summary>
    /// Create generic type with any generic arguments.
    /// <example>
    /// If you ganna make <c>MyClass&lt;T, string&gt;</c>, you can use:
    /// <code>new Generic(typeof(MyClass&lt;,&gt;), new GenericParameterType("T"), new NongenericType(typeof(string)));</code>
    /// </example>
    /// </summary>
    /// <param name="type">Generic type definition.</param>
    /// <param name="genericArguments">Generic arguments.</param>
    /// <exception cref="WeavingException">Thrown when the <c>type</c> is not generic type.</exception>
    public GenericType(Type type, params IType[] genericArguments)
    {
        if (!type.IsGenericType) throw new WeavingException(string.Format(ErrorMessages.IS_NOT_PARAMETERIZED_GENERIC_TYPE, type.FullName));
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
/// The type which a reference of the metadata names, which is what a member of the assembly being woven is described
/// by.<para/>
/// The other descriptions of this tree are built from a <see cref="Type"/>, and an assembly which the weaver reads as
/// metadata holds none: its image may never have been loaded, so the type of a member of it cannot be asked of the
/// runtime, and what describes the type instead is the name which the reference of it writes. That name is what the
/// whole tree compares types by as well, because <see cref="TypeName"/> reads a description of either kind as the name
/// of it: a name and the description which was built from the type of that name are one type, so a type which was read
/// out of a member is the one which a caller named with <see cref="TypeInfoExtensions.ToIType(Type)"/>, and the
/// queries of a handler are asked with the two of them alike.
/// </summary>
/// <param name="typeName">The full name of the type, written as the metadata writes it: the types a type is nested in are separated by a plus, and the arguments of an instance stand in the brackets of it.</param>
public sealed class ReferencedType(string typeName) : IType
{
    /// <summary>
    /// Full name of the type.
    /// </summary>
    public readonly string TypeName = typeName;

    /// <summary>
    /// Get type name.
    /// </summary>
    /// <returns>Type name.</returns>
    public override string ToString() => TypeName;
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
    internal SelfType() => GenericArguments = [];

    /// <summary>
    /// Create self type with generic arguments.
    /// </summary>
    /// <param name="genericArguments">Generic argument types.</param>
    internal SelfType(IEnumerable<IType> genericArguments) => GenericArguments = [.. genericArguments];

    /// <summary>
    /// Create self type with generic parameters.
    /// </summary>
    /// <param name="genericParameterNames">Generic parameter names.</param>
    internal SelfType(IEnumerable<string> genericParameterNames) => GenericArguments = [.. genericParameterNames.Select<string, IType>(name => new GenericParameterType(name))];

    /// <summary>
    /// Create self type with generic arguments.
    /// </summary>
    /// <param name="genericArguments">Generic argument types.</param>
    internal SelfType(IEnumerable<Type> genericArguments) => GenericArguments = [.. genericArguments.Select<Type, IType>(arg => new NongenericType(arg))];
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
    public static IType ToIType(this Type type) => type switch
    {
        {IsGenericParameter: true}  => new GenericParameterType(type.Name),
        {IsGenericType     : false} => new NongenericType(type),
        _                           => new GenericType(type, [.. type.GetGenericArguments().Select(ToIType)])
    };

    /// <summary>
    /// Read the <see cref="Type"/> which a description names, where the runtime holds one.<para/>
    /// A description is built from a type as often as it is read out of metadata, and the two are the same type to the
    /// names of the tree, but only the first of them holds the type itself: the type of the assembly being woven lies in
    /// an image which may never have been loaded, and a parameter stands for whatever instantiates it, so neither is a
    /// type the runtime can hand over. What this answers with is the type which the description names where the runtime
    /// holds one, and nothing where it does not.
    /// </summary>
    /// <param name="type">The description which is read.</param>
    /// <param name="systemType">The type which the description names, or null where the runtime holds none.</param>
    /// <returns>Whether the runtime holds the type which the description names.</returns>
    public static bool TryGetSystemType(this IType type, out Type? systemType)
    {
        switch (type)
        {
            case NongenericType nongenericType:
                systemType = nongenericType.Type;
                return true;

            // The description of an instance holds the definition of the type and the arguments of the instance, so the
            // type the runtime holds is the one which that definition is made into by those arguments: an argument which
            // is no type of the runtime is one the definition cannot be made into, and one which the constraints of the
            // definition refuse is one the runtime refuses as well.
            case GenericType genericType:
                var arguments = new Type?[genericType.GenericArguments.Length];
                for (var index = 0; index < arguments.Length; index++)
                {
                    if (!genericType.GenericArguments[index].TryGetSystemType(out arguments[index]))
                    {
                        systemType = null;
                        return false;
                    }
                }

                try
                {
                    systemType = genericType.Type.MakeGenericType(arguments!);
                    return true;
                }
                catch (ArgumentException)
                {
                    systemType = null;
                    return false;
                }

            // The description names a type which this run reads as metadata alone, so the name is what tells which type
            // it is, and the runtime answers for that name the way it answers for a name of its own: the names of the
            // corlib and of the assembly which the reading is made in, and a name which the assembly of the type
            // qualifies. A name which cannot be read at all is one the runtime holds no type of, which is what is
            // answered here rather than the failure of the reading: the description itself is readable either way.
            case ReferencedType referencedType:
                try
                {
                    systemType = Type.GetType(referencedType.TypeName);
                    return systemType != null;
                }
                catch (Exception exception) when (exception is ArgumentException or TypeLoadException or FileLoadException or BadImageFormatException)
                {
                    systemType = null;
                    return false;
                }

            // A parameter of a method or of a type stands for whatever instantiates it, which is no type of this run
            // until that instantiation is written, and a kind which this tree does not build holds nothing to read a
            // type out of at all.
            default:
                systemType = null;
                return false;
        }
    }

    /// <summary>
    /// Create <see cref="IType"/> array from <see cref="ParameterInfo"/> array.
    /// </summary>
    /// <param name="parameters">The parameter info array.</param>
    /// <returns>An array of <see cref="IType"/> representing the parameter types.</returns>
    public static IType[] GetITypes(this ParameterInfo[] parameters) => [.. parameters.Select(p => p.ParameterType.ToIType())];

    /// <summary>
    /// Read the type which a reference of the metadata names as a description of this tree, which is the inverse of the
    /// reading which <c>AssemblyHandler.ResolveParameterType</c> does.<para/>
    /// The type of a member of the assembly being woven is described by the name of the reference alone, because the
    /// runtime holds no type of an assembly which lies there as metadata: see <see cref="ReferencedType"/>. A parameter
    /// of a method or of a type is the one exception, because it stands for the type which instantiates it rather than
    /// for a type of an assembly, and the name of it is what a caller writes in the place of it as well.
    /// </summary>
    /// <param name="typeReference">The reference which is read.</param>
    /// <returns>The description of the type which the reference names.</returns>
    internal static IType ToIType(this TypeReference typeReference) => typeReference switch
    {
        GenericParameter parameter => parameter.ToGenericParameterType(),
        _                          => new ReferencedType(new TypeName(typeReference).Name)
    };

    /// <summary>
    /// Read the parameter which a definition declares as a description of this tree, which holds the name of the
    /// parameter and the constraints which it declares.<para/>
    /// The kinds which a constraint names by an attribute of the parameter rather than by a type are written as the
    /// shapes of this tree which stand for them, because a caller reads them back the same way: <c>where T : class</c>,
    /// <c>where T : struct</c>, <c>where T : new()</c>, <c>in T</c> and <c>out T</c>. The value kind is one of them and
    /// it is written as a constraint on <c>System.ValueType</c> as well, which is the type the shape of it carries, so
    /// that constraint is left out here: it is written again from the shape, and the two would be two constraints of one
    /// type.
    /// </summary>
    /// <param name="parameter">The parameter of a method or of a type which is read.</param>
    /// <returns>The description of the parameter.</returns>
    internal static GenericParameterType ToGenericParameterType(this GenericParameter parameter)
    {
        // The value kind is written as a constraint on System.ValueType as well, which the shape of it carries, so that
        // constraint is left out of the list below where the kind is named: it is written again from the shape.
        var isValueKind = parameter.HasNotNullableValueTypeConstraint;

        var constraints = new List<Constraint>();

        if (parameter.HasReferenceTypeConstraint) constraints.Add(Constraint.Class);
        if (parameter.IsCovariant) constraints.Add(Constraint.Out);
        if (parameter.IsContravariant) constraints.Add(Constraint.In);
        if (isValueKind) constraints.Add(Constraint.Struct);
        else if (parameter.HasDefaultConstructorConstraint) constraints.Add(Constraint.New);

        constraints.AddRange(parameter.Constraints
            .Select(constraint => constraint.ConstraintType)
            .Where(constraintType => !(isValueKind && constraintType.FullName == typeof(ValueType).FullName))
            .Select(constraintType => Constraint.FromType(constraintType.ToIType())));

        return new GenericParameterType(parameter.Name, [.. constraints]);
    }

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
    /// <exception cref="WeavingException">Thrown when type is not generic.</exception>
    public static GenericType WithGenericParameter(this Type type, params string[] genericParameters) => !type.IsGenericType
        ? throw new WeavingException(ErrorMessages.TYPE_IS_NOT_GENERIC)
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
    /// <exception cref="WeavingException">Thrown when type is not generic.</exception>
    public static GenericType WithGenericParameter(this Type type, params Type[] genericArguments) => !type.IsGenericType
        ? throw new WeavingException(ErrorMessages.TYPE_IS_NOT_GENERIC)
        : new GenericType(type, genericArguments);

    /// <summary>
    /// Get type's name.
    /// </summary>
    /// <param name="type">The name owner.</param>
    /// <returns>Type name.</returns>
    public static TypeName GetTypeName(this IType type) => new(type);
}