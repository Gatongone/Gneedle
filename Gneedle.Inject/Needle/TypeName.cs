/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/18-11:46:50
 * Github: https://github.com/Gatongone
 */

using System.Text;
using System.Text.RegularExpressions;

namespace Gneedle.Inject;

/// <summary>
/// Type name where read from <see cref="System.Type"/>, <see cref="Mono.Cecil.TypeReference"/> and <see cref="Gneedle.Inject.IType"/>.
/// The type name could be used for getting TypeReference by <see cref="Mono.Cecil.ModuleDefinition.GetType(string)">Mono.Cecil.ModuleDefinition.GetType(string)</see>.
/// or getting runtime type by <see cref="System.Type.GetType(string)">System.Type.GetType(string)</see><para/>
/// 
/// It will follow the following naming rules:
/// <code>
/// TypeName = Type.IsGenericType
/// ? "{Namespace}.{NestedTypeName}+{TypeName}"
/// : Type.ContainsGenericParameters &amp;&amp; !Type.ContainsGenericTypeArguments
///     ? "{Namespace}.{NestedTypeName}+{TypeName}`{GenericArgumentsCount}"
///     :  "{Namespace}.{NestedTypeName}+{TypeName}`{GenericArgumentsCount}[Argument1,Argument2...ArgumentN]"
/// </code>
/// <example>
/// <list>
/// <item><see cref="System.Collections.Generic.List{T}.Enumerator">List&lt;T&gt;.Enumerator</see> => System.Collections.Generic.List`1+Enumerator</item>
/// <item><see cref="System.Collections.Generic.List{T}"/> => System.Collections.Generic.List`1</item>
/// <item><see cref="System.Collections.Generic.List{T}">List&lt;int&gt;</see> => System.Collections.Generic.List`1[System.Int32]</item>
/// <item><see cref="System.Collections.Generic.List{T}">List&lt;List&lt;T&gt;&gt;</see> => System.Collections.Generic.List`1[System.Collections.Generic.List`1[T]]</item>
/// <item><see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> => System.Collections.Generic.Dictionary`2</item>
/// <item><see cref="System.Collections.Generic.Dictionary{TKey, TValue}">Dictionary&lt;string, int&gt;</see> => System.Collections.Generic.Dictionary`2[System.String, System.Int32]</item>
/// </list>
/// </example>
/// </summary>
public readonly struct TypeName : IEquatable<TypeName>
{
    /// <summary>
    /// Type name.
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// Create type name from <see cref="System.Type"/>.
    /// </summary>
    /// <param name="type">The name owner.</param>
    public TypeName(Type type)
    {
        if (type.IsGenericType)
        {
            Name = type.ContainsGenericParameters && type.GenericTypeArguments.Length == 0
                ? type.FullName ?? type.ToString()
                : Regex.Replace(type.ToString(), @", .*?]", "]");
        }
        else
        {
            Name = type.ToString();
        }
    }

    /// <summary>
    /// Create type name from <see cref="Mono.Cecil.TypeReference"/>.
    /// </summary>
    /// <param name="typeRef">The name owner.</param>
    public TypeName(TypeReference typeRef)
    {
        if (typeRef is GenericInstanceType)
        {
            var stringBuilder = new StringBuilder();
            var fullName = typeRef.FullName;
            for (var index = 0; index < fullName.Length; index++)
            {
                var c = fullName[index];
                switch (c)
                {
                    case '<':
                        stringBuilder.Append("[");
                        break;
                    case ',':
                        stringBuilder.Append(",");
                        break;
                    case '>':
                        stringBuilder.Append("]");
                        break;
                    default:
                        stringBuilder.Append(c);
                        break;
                }
            }

            Name = stringBuilder.ToString();
        }
        else
            Name = typeRef.FullName;
    }
    
    /// <inheritdoc cref="TypeName(IType,bool)"/>
    public TypeName(IType type) : this(type, true) { }

    /// <summary>
    /// Create type name from <see cref="Gneedle.Inject.IType"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The parameter type is not following type:
    /// <list>
    /// <item><see cref="GenericParameterType"/></item>
    /// <item><see cref="NongenericType"/></item>
    /// <item><see cref="GenericType"/></item>
    /// </list>
    /// </exception>
    /// <param name="type">The name owner.</param>
    /// <param name="isFirstIn">
    /// Flag for making the generic type drops first argument which not contains any generic parameters.<para/>
    /// Such like the type name of <c>MyClass&lt;T&gt;</c> should be <c>MyClass'1</c> but not <c>MyClass'1[T].</c><para/>
    /// And also like the type name of <c>MyClass&lt;MyClass&lt;T&gt;&gt;</c> should be <c>MyClass'1[MyClass'1[T]]</c> but not <c>MyClass'1[MyClass'1]</c> or <c>MyClass'1</c><para/>
    /// </param>
    private TypeName(IType type, bool isFirstIn)
    {
        switch (type)
        {
            case GenericParameterType genericParameterType:
                Name = genericParameterType.TypeName;
                break;
            case NongenericType nonGenericParameterType:
                Name = nonGenericParameterType.Type.FullName ?? nonGenericParameterType.Type.Name;
                break;
            case GenericType parameterizedGenericType:
            {
                var arguments = parameterizedGenericType.GenericArguments;
                Name = parameterizedGenericType.Type.FullName ?? parameterizedGenericType.Type.Name;
                
                // If first in, then check if contains any generic parameter.
                if (isFirstIn && parameterizedGenericType.GenericArguments.All(arg => arg is GenericParameterType)) return;
                
                // Else keep append arguments info to string builder.
                var stringBuilder = new StringBuilder();
                stringBuilder.Append(Name);
                stringBuilder.Append("[");
                for (var i = 0; i < arguments.Length; i++)
                {
                    // Recursive create typename.
                    stringBuilder.Append(new TypeName(arguments[i], false).ToString());
                    if (i != arguments.Length - 1)
                        stringBuilder.Append(",");
                }
                stringBuilder.Append("]");
                Name = stringBuilder.ToString();

                break;
            }
            default: throw new ArgumentException(string.Format(ErrorMessages.INVALID_TYPE_NAME));
        }
    }

    public static implicit operator TypeName(Type type) => new(type);
    public static implicit operator TypeName(TypeReference typeRef) => new(typeRef);
    public static implicit operator string(TypeName typeName) => typeName.Name;
    public static bool operator ==(TypeName typeName, TypeName other) => typeName.Equals(other);
    public static bool operator !=(TypeName typeName, TypeName other) => !(typeName == other);
    public static bool operator ==(TypeName typeName, object other) => typeName.Equals(other);
    public static bool operator !=(TypeName typeName, object other) => !(typeName == other);
    public bool Equals(TypeName other) => Name.Equals(other.Name);
    public override bool Equals(object? obj) => (obj is TypeName other && Equals(other)) || (obj is string str && str.Equals(Name));
    public override int GetHashCode() => Name.GetHashCode();
    public override string ToString() => Name;
}