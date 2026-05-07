namespace Gneedle.Inject;

internal class TypeHandler : ITypeHandler, IEquatable<TypeHandler>
{
    internal readonly TypeDefinition Source;

    internal readonly AssemblyHandler AssemblyHandler;

    IAssemblyHandler ITypeHandler.AssemblyHandler => AssemblyHandler;

    public string Name
    {
        get => Source.Name;
        set => Source.Name = value;
    }

    public string Namespace
    {
        get => Source.Namespace;
        set => Source.Namespace = value;
    }

    internal TypeHandler(AssemblyHandler assemblyHandler, TypeDefinition source)
    {
        AssemblyHandler = assemblyHandler;
        Source          = source;
    }

    public bool TryGetRuntimeType(out Type? type)
    {
        type = Type.GetType(new TypeName(Source));
        return type == null;
    }

    public IMethodHandler AddMethod(string methodName, IType returnType, Constraint[] genericArguments, IType[] parameterTypes, MethodFlags methodFlags)
    {
        var methodAttribute = methodName switch
        {
            ".ctor" =>
                methodFlags.HasFlag(MethodFlags.Static)
                    ? throw new ArgumentException("Instance constructor with static attribute.")
                    : methodFlags.HasFlag(MethodFlags.Abstract) || methodFlags.HasFlag(MethodFlags.Virtual)
                        ? throw new ArgumentException("Instance constructor with abstract or virtual attribute.")
                        : methodFlags.ToMethodAttributes() | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            ".cctor" => methodFlags.HasFlag(MethodFlags.Protected) || methodFlags.HasFlag(MethodFlags.Internal)
                ? throw new ArgumentException("Static constructor can only be private.")
                : !methodFlags.HasFlag(MethodFlags.Static)
                    ? throw new ArgumentException("Static constructor must have static attribute.")
                    : methodFlags.HasFlag(MethodFlags.Abstract) || methodFlags.HasFlag(MethodFlags.Virtual)
                        ? throw new ArgumentException("Static constructor with abstract or virtual attribute.")
                        : methodFlags.ToMethodAttributes() | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            _ => methodFlags.ToMethodAttributes()
        };

        var method = new MethodDefinition(methodName, methodAttribute, AssemblyHandler.GetCecilType(returnType).Reference)
        {
            DeclaringType = Source
        };
        foreach (var parameter in parameterTypes)
        {
            method.Parameters.Add(new ParameterDefinition(AssemblyHandler.GetCecilType(parameter).Reference));
        }
        Source.Methods.Add(method);
        return new MethodHandler(method, this);
    }

    public bool ContainsInterface(IType interfaceType) => Source.Interfaces.Any(implementation => TypeName.HasSameName(implementation.InterfaceType, interfaceType));

    public bool ContainsAttribute(IType attributeType) => Source.CustomAttributes.Any(attribute => TypeName.HasSameName(attribute.AttributeType, attributeType));

    public void AddAttribute(IType attributeType, params object[] arguments)
    {
        var typeDef = AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = typeDef.CreateCustomAttribute(AssemblyHandler.Assembly.Source.MainModule, arguments);
        typeDef.CustomAttributes.Add(attribute);
    }

    public IMethodHandler? GetMethod(string methodName, params IType[] parameterTypes)
    {
        var curType = Source;
        MethodDefinition? methodDef = null;
        if (parameterTypes is {Length: > 0})
        {
            while (curType != null &&
                methodDef == null)
            {
                methodDef = curType.Methods.FirstOrDefault(method => method.Name == methodName && method.Parameters.SameWith(parameterTypes));
                curType   = curType.BaseType == null ? null : AssemblyHandler.GetCecilType(curType.BaseType).Definition;
            }
        }
        else
        {
            methodDef = curType.Methods.FirstOrDefault(method => method.Name == methodName);
        }

        return methodDef == null ? null : new MethodHandler(methodDef, this);
    }

    public IMethodHandler AddMethod(string methodName, IType returnType, IType[] parameterTypes)
    {
        var methodDef = new MethodDefinition(methodName, MethodAttributes.Public | MethodAttributes.HideBySig, AssemblyHandler.GetCecilType(returnType).Reference);
        foreach (var parameter in parameterTypes)
        {
            methodDef.Parameters.Add(new ParameterDefinition(AssemblyHandler.GetCecilType(parameter).Reference));
        }

        Source.Methods.Add(methodDef);
        return new MethodHandler(methodDef, this);
    }

    public IMethodHandler AddMethod(string methodName, IType returnType, Constraint[] genericArguments, IType[] parameterTypes)
    {
        var methodDef = new MethodDefinition(methodName, MethodAttributes.Public | MethodAttributes.HideBySig, AssemblyHandler.GetCecilType(returnType).Reference);
        foreach (var parameter in parameterTypes)
        {
            methodDef.Parameters.Add(new ParameterDefinition(AssemblyHandler.GetCecilType(parameter).Reference));
        }

        foreach (var genericArgument in genericArguments)
        {
            methodDef.GenericParameters.Add(new GenericParameter(genericArgument.Name, methodDef));
        }

        Source.Methods.Add(methodDef);
        return new MethodHandler(methodDef, this);
    }

    /// <summary>
    /// Add custom attribute to type definition.
    /// </summary>
    /// <param name="arguments">Arguments of the attribute constructor calling.</param>
    /// <typeparam name="TAttribute">Type of the attribute.</typeparam>
    public void AddAttribute<TAttribute>(params object[] arguments) where TAttribute : Attribute
    {
        var typeDef = AssemblyHandler.GetCecilType(typeof(TAttribute)).Definition;
        var attribute = typeDef.CreateCustomAttribute(AssemblyHandler.Assembly.Source.MainModule, arguments);
        typeDef.CustomAttributes.Add(attribute);
    }

    /// <summary>
    /// Add custom attribute to type definition.
    /// </summary>
    /// <param name="attributeType">Type of the attribute.</param>
    /// <param name="arguments">Arguments of the attribute constructor calling.</param>
    /// <exception cref="ArgumentException">Thrown when the <c>attributeType</c> cannot assign to <see cref="System.Attribute"/>.</exception>
    public void AddAttribute(Type attributeType, params object[] arguments)
    {
        if (!typeof(Attribute).IsAssignableFrom(attributeType))
            throw new ArgumentException(string.Format(ErrorMessages.TYPE_CANNOT_ASSIGN_TO_TARGET_TYPE, typeof(Attribute)));
        var typeDef = AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = typeDef.CreateCustomAttribute(AssemblyHandler.Assembly.Source.MainModule, arguments);
        typeDef.CustomAttributes.Add(attribute);
    }

    public bool Equals(TypeHandler? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Source.Equals(other.Source);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj.GetType() == GetType() && Equals((TypeHandler) obj);
    }

    public override int GetHashCode() => Source.GetHashCode();

    /// <summary>
    /// Get field from this type.
    /// </summary>
    /// <param name="fieldName">Name of the field.</param>
    /// <returns>The field from this type.</returns>
    internal FieldReference? GetFieldInThis(string fieldName)
        => AssemblyHandler.GetFieldFromType(Source, fieldName);

    /// <summary>
    /// Get field from base type.
    /// </summary>
    /// <param name="fieldName">Name of the field.</param>
    /// <returns>The field from this type.</returns>
    internal FieldReference? GetFieldInBase(string fieldName)
        => AssemblyHandler.GetFieldFromType(AssemblyHandler.GetCecilType(Source.BaseType).Definition, fieldName);

    internal PropertyDefinition? GetPropertyInThis(string propertyName)
        => AssemblyHandler.GetPropertyFromType(Source, propertyName);

    internal PropertyDefinition? GetPropertyInBase(string propertyName)
        => AssemblyHandler.GetPropertyFromType(AssemblyHandler.GetCecilType(Source.DeclaringType.BaseType).Definition, propertyName);

    internal MethodDefinition? GetMethodInThis(string methodName, IReadOnlyList<TypeReference> parameters)
        => AssemblyHandler.GetMethodFromType(Source, methodName, parameters);

    internal MethodDefinition? GetMethodInBase(string methodName, IReadOnlyList<TypeReference> parameters)
        => AssemblyHandler.GetMethodFromType(AssemblyHandler.GetCecilType(Source.DeclaringType.BaseType).Definition, methodName, parameters);
}