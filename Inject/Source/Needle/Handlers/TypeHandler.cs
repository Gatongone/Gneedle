namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for a type of the metadata which is built, which reads its members and adds new ones to it.
/// </summary>
internal class TypeHandler : ITypeHandler, IInterfaceContainer, IFieldContainer, IMethodContainer, IPropertyContainer, IEquatable<TypeHandler>
{
    /// <summary>
    /// The type definition which is handled.
    /// </summary>
    internal readonly TypeDefinition Source;

    /// <summary>
    /// Handler of the assembly which declares the type.
    /// </summary>
    internal readonly AssemblyHandler AssemblyHandler;

    /// <inheritdoc/>
    IAssemblyHandler ITypeHandler.AssemblyHandler => AssemblyHandler;

    /// <inheritdoc/>
    public string Name
    {
        get => Source.Name;
        set => Source.Name = value;
    }

    /// <inheritdoc/>
    public string Namespace
    {
        get => Source.Namespace;
        set => Source.Namespace = value;
    }

    /// <summary>
    /// Create a handler for a type definition.
    /// </summary>
    /// <param name="assemblyHandler">Handler of the assembly which declares the type.</param>
    /// <param name="source">The type definition which is handled.</param>
    internal TypeHandler(AssemblyHandler assemblyHandler, TypeDefinition source)
    {
        AssemblyHandler = assemblyHandler;
        Source          = source;
    }

    /// <inheritdoc/>
    public bool ContainsInterface(IType interfaceType) => Source.Interfaces.Any(implementation => TypeName.HasSameName(implementation.InterfaceType, interfaceType));

    /// <inheritdoc/>
    public bool ContainsAttribute(IType attributeType) => Source.CustomAttributes.Any(attribute => TypeName.HasSameName(attribute.AttributeType, attributeType));

    /// <inheritdoc/>
    public void AddAttribute(IType attributeType, params object[] arguments)
    {
        var attributeDef = AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = attributeDef.CreateCustomAttribute(AssemblyHandler.Assembly.Source.MainModule, arguments);
        Source.CustomAttributes.Add(attribute);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public MethodDecorator.IGenericParameterDecorator AddMethod(string methodName, MethodFlags methodFlags)
        => new MethodDecorator(this, methodName, methodFlags);

    /// <inheritdoc/>
    public FieldDecorator.IFieldTypeDecorator AddField(string fieldName, FieldFlags fieldFlags) => new FieldDecorator(this, fieldName, fieldFlags);

    /// <inheritdoc/>
    public PropertyDecorator.IPropertyTypeDecorator AddProperty(string propertyName, PropertyFlags propertyFlags)
        => new PropertyDecorator(this, propertyName, propertyFlags);

    /// <inheritdoc/>
    public IFieldHandler? GetField(string fieldName)
    {
        var fieldRef = AssemblyHandler.GetFieldFromType(Source, fieldName);
        return fieldRef == null ? null : new FieldHandler((FieldDefinition) fieldRef, this);
    }

    /// <inheritdoc/>
    public IPropertyHandler? GetProperty(string propertyName)
    {
        var propDef = AssemblyHandler.GetPropertyFromType(Source, propertyName);
        return propDef == null ? null : new PropertyHandler(propDef, this);
    }

    /// <inheritdoc/>
    public IMethodHandler AddMethod(string methodName, IType returnType, GenericParameterType[] genericParameters, Parameter[] parameters, MethodFlags methodFlags)
    {
        // Set method attributes, and check the validity of method attributes according to method name.
        var methodAttribute = methodName switch
        {
            ".ctor" => // Instance constructor cannot be static, abstract or virtual, and can only be public, private or protected.
                methodFlags.HasFlag(MethodFlags.Static)
                    ? throw new ArgumentException("Instance constructor with static attribute.")
                    : methodFlags.HasFlag(MethodFlags.Abstract) || methodFlags.HasFlag(MethodFlags.Virtual)
                        ? throw new ArgumentException("Instance constructor with abstract or virtual attribute.")
                        : methodFlags.ToMethodAttributes() | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            ".cctor" => // Static constructor must be static, and can only be private, and cannot be abstract or virtual.
                methodFlags.HasFlag(MethodFlags.Protected) || methodFlags.HasFlag(MethodFlags.Internal)
                    ? throw new ArgumentException("Static constructor can only be private.")
                    : !methodFlags.HasFlag(MethodFlags.Static)
                        ? throw new ArgumentException("Static constructor must have static attribute.")
                        : methodFlags.HasFlag(MethodFlags.Abstract) || methodFlags.HasFlag(MethodFlags.Virtual)
                            ? throw new ArgumentException("Static constructor with abstract or virtual attribute.")
                            : methodFlags.ToMethodAttributes() | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            _ => methodFlags.ToMethodAttributes()
        };

        // Use void as default return type, and set correct return type after processing generic parameters, because generic parameters may be used in return type.
        var methodReturnType = Source.Module.TypeSystem.Void;
        var method = new MethodDefinition(methodName, methodAttribute, methodReturnType)
        {
            DeclaringType = Source
        };
        // Add generic parameters.
        method.GenericParameters.AddRange(genericParameters.Select(genericParameter =>
        {
            var genericParams = new GenericParameter(genericParameter.TypeName, method)
            {
                // Append generic parameter attributes to constraint.
                Attributes = genericParameter.GetGenericParameterAttributes()
            };

            // Process every constraint from type.
            foreach (var constraint in genericParameter.Constraints)
            {
                genericParams.SetConstraintFromType(AssemblyHandler, Source, constraint);
            }

            return genericParams;
        }));

        // Add method parameters.
        method.Parameters.AddRange(parameters.Select(parameter => new ParameterDefinition(parameter.Name, ParameterAttributes.None,
            AssemblyHandler.ResolveParameterType(Source, parameter.Type, method.GenericParameters))));

        // Set method return type.
        method.ReturnType = AssemblyHandler.ResolveParameterType(Source, returnType, method.GenericParameters);

        Source.Methods.Add(method);
        var methodHandler = new MethodHandler(method, this);

        // A method which is not abstract has to carry a body, otherwise the produced assembly holds a method without an
        // RVA and cannot be loaded. A body which throws is used rather than one which returns a default value, so that a
        // method which is added without a body is noticed at the call instead of returning what was never meant.
        if (!method.IsAbstract) methodHandler.SetBody(DefaultMethodBody.ThrowException);

        return methodHandler;
    }

    /// <summary>
    /// Add custom attribute to this type definition.
    /// </summary>
    /// <param name="arguments">Arguments of the attribute constructor calling.</param>
    /// <typeparam name="TAttribute">Type of the attribute.</typeparam>
    public void AddAttribute<TAttribute>(params object[] arguments) where TAttribute : Attribute
    {
        var attributeDef = AssemblyHandler.GetCecilType(typeof(TAttribute)).Definition;
        var attribute = attributeDef.CreateCustomAttribute(AssemblyHandler.Assembly.Source.MainModule, arguments);
        Source.CustomAttributes.Add(attribute);
    }

    /// <summary>
    /// Add custom attribute to this type definition.
    /// </summary>
    /// <param name="attributeType">Type of the attribute.</param>
    /// <param name="arguments">Arguments of the attribute constructor calling.</param>
    /// <exception cref="ArgumentException">Thrown when the <c>attributeType</c> cannot assign to <see cref="System.Attribute"/>.</exception>
    public void AddAttribute(Type attributeType, params object[] arguments)
    {
        if (!typeof(Attribute).IsAssignableFrom(attributeType))
            throw new ArgumentException(string.Format(ErrorMessages.TYPE_CANNOT_ASSIGN_TO_TARGET_TYPE, typeof(Attribute)));
        var attributeDef = AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = attributeDef.CreateCustomAttribute(AssemblyHandler.Assembly.Source.MainModule, arguments);
        Source.CustomAttributes.Add(attribute);
    }

    /// <summary>
    /// Whether two handlers are of the same type of the metadata, which is what two handlers of one type are: the
    /// handlers are made as they are asked for rather than kept, so a caller which holds one of a type before asking
    /// for it again tells the two apart by this.
    /// </summary>
    /// <param name="other">The handler which is compared with this one.</param>
    /// <returns>Whether the two handle the same type.</returns>
    public bool Equals(TypeHandler? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Source.Equals(other.Source);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj.GetType() == GetType() && Equals((TypeHandler) obj);
    }

    /// <inheritdoc/>
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

    /// <summary>
    /// Get property from this type.
    /// </summary>
    /// <param name="propertyName">Name of the property.</param>
    /// <returns>The property from this type.</returns>
    internal PropertyDefinition? GetPropertyInThis(string propertyName)
        => AssemblyHandler.GetPropertyFromType(Source, propertyName);

    /// <summary>
    /// Get property from base type.
    /// </summary>
    /// <param name="propertyName">Name of the property.</param>
    /// <returns>The property from the base type.</returns>
    internal PropertyDefinition? GetPropertyInBase(string propertyName)
        => AssemblyHandler.GetPropertyFromType(AssemblyHandler.GetCecilType(Source.BaseType).Definition, propertyName);

    /// <summary>
    /// Get method from this type.
    /// </summary>
    /// <param name="methodName">Name of the method.</param>
    /// <param name="parameters">Types of the parameters of the method.</param>
    /// <returns>The method from this type.</returns>
    internal MethodDefinition? GetMethodInThis(string methodName, IReadOnlyList<TypeReference> parameters)
        => AssemblyHandler.GetMethodFromType(Source, methodName, parameters);

    /// <summary>
    /// Get method from base type.
    /// </summary>
    /// <param name="methodName">Name of the method.</param>
    /// <param name="parameters">Types of the parameters of the method.</param>
    /// <returns>The method from the base type.</returns>
    internal MethodDefinition? GetMethodInBase(string methodName, IReadOnlyList<TypeReference> parameters)
        => AssemblyHandler.GetMethodFromType(AssemblyHandler.GetCecilType(Source.BaseType).Definition, methodName, parameters);
}