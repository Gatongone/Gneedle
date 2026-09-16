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

    /// <inheritdoc cref="IInterfaceContainer.ContainsInterface" />
    public bool ContainsInterface(IType interfaceType) => Source.Interfaces.Any(implementation => TypeName.HasSameName(implementation.InterfaceType, interfaceType));

    /// <inheritdoc/>
    public void AddInterface(IType interfaceType)
    {
        // The interface is resolved by the assembly handler, because the type which is handled is given a reference of
        // its own module to it, and the one which the caller named belongs to the module which declares it.
        var implementation = new InterfaceImplementation(AssemblyHandler.ResolveParameterType(Source, interfaceType));
        Source.Interfaces.Add(implementation);
    }

    /// <inheritdoc/>
    public bool ContainsAttribute(IType attributeType) => Source.CustomAttributes.Any(attribute => TypeName.HasSameName(attribute.AttributeType, attributeType));

    /// <inheritdoc/>
    public void AddAttribute(IType attributeType, params object[] arguments)
    {
        var attributeDef = AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = attributeDef.CreateCustomAttribute(AssemblyHandler.Assembly.Source.MainModule, arguments);
        Source.CustomAttributes.Add(attribute);
    }

    /// <inheritdoc cref="IMethodContainer.GetMethod(string, IType[])" />
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

    /// <inheritdoc cref="IMethodContainer.GetMethods()"/>
    public IMethodHandler[] GetMethods() => GetMethods(0);

    /// <inheritdoc cref="IMethodContainer.GetMethods(MethodFlags)"/>
    public IMethodHandler[] GetMethods(MethodFlags methodFlags)
        => Source.Methods
            .Where(method => method.ToMethodFlags().HasFlag(methodFlags))
            .Select(method => (IMethodHandler) new MethodHandler(method, this))
            .ToArray();

    /// <inheritdoc/>
    public MethodDecorator.IGenericParameterDecorator AddMethod(string methodName, MethodFlags methodFlags)
        => new MethodDecorator(this, methodName, methodFlags);

    /// <inheritdoc/>
    public FieldDecorator.IFieldTypeDecorator AddField(string fieldName, FieldFlags fieldFlags) => new FieldDecorator(this, fieldName, fieldFlags);

    /// <inheritdoc/>
    public PropertyDecorator.IPropertyTypeDecorator AddProperty(string propertyName, PropertyFlags propertyFlags)
        => new PropertyDecorator(this, propertyName, propertyFlags);

    /// <inheritdoc cref="IFieldContainer.GetField" />
    public IFieldHandler? GetField(string fieldName)
    {
        var fieldRef = AssemblyHandler.GetFieldFromType(Source, fieldName);
        return fieldRef == null ? null : new FieldHandler((FieldDefinition) fieldRef, this);
    }

    /// <inheritdoc cref="IFieldContainer.GetFields()" />
    public IFieldHandler[] GetFields() => GetFields(0);

    /// <inheritdoc cref="IFieldContainer.GetFields(FieldFlags)" />
    public IFieldHandler[] GetFields(FieldFlags fieldFlags)
        => Source.Fields
            .Where(field => field.ToFieldFlags().HasFlag(fieldFlags))
            .Select(IFieldHandler (field) => new FieldHandler(field, this))
            .ToArray();

    /// <inheritdoc cref="IPropertyContainer.GetProperty" />
    public IPropertyHandler? GetProperty(string propertyName)
    {
        var propDef = AssemblyHandler.GetPropertyFromType(Source, propertyName);
        return propDef == null ? null : new PropertyHandler(propDef, this);
    }

    /// <inheritdoc cref="IPropertyContainer.GetProperties()"/>
    public IPropertyHandler[] GetProperties() => GetProperties(0);

    /// <inheritdoc cref="IPropertyContainer.GetProperties(PropertyFlags)"/>
    public IPropertyHandler[] GetProperties(PropertyFlags propertyFlags)
        => Source.Properties
            .Where(property => property.ToPropertyFlags().HasFlag(propertyFlags))
            .Select(property => (IPropertyHandler) new PropertyHandler(property, this))
            .ToArray();

    /// <inheritdoc cref="IMethodContainer.AddMethod"/>
    public IMethodHandler AddMethod(string methodName, IType returnType, GenericParameterType[] genericParameters, Parameter[] parameters, MethodFlags methodFlags)
    {
        // Set method attributes, and check the validity of method attributes according to method name.
        var methodAttribute = methodName switch
        {
            ".ctor" => // Instance constructor cannot be static, abstract or virtual, and can only be public, private or protected.
                methodFlags.HasFlag(MethodFlags.Static)
                    ? throw new ArgumentException(string.Format(ErrorMessages.INSTANCE_CONSTRUCTOR_IS_STATIC, methodName))
                    : methodFlags.HasFlag(MethodFlags.Abstract) || methodFlags.HasFlag(MethodFlags.Virtual)
                        ? throw new ArgumentException(string.Format(ErrorMessages.INSTANCE_CONSTRUCTOR_IS_ABSTRACT_OR_VIRTUAL, methodName))
                        : methodFlags.ToMethodAttributes() | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            ".cctor" => // Static constructor must be static, and can only be private, and cannot be abstract or virtual.
                methodFlags.HasFlag(MethodFlags.Protected) || methodFlags.HasFlag(MethodFlags.Internal)
                    ? throw new ArgumentException(string.Format(ErrorMessages.STATIC_CONSTRUCTOR_IS_NOT_PRIVATE, methodName))
                    : !methodFlags.HasFlag(MethodFlags.Static)
                        ? throw new ArgumentException(string.Format(ErrorMessages.STATIC_CONSTRUCTOR_IS_NOT_STATIC, methodName))
                        : methodFlags.HasFlag(MethodFlags.Abstract) || methodFlags.HasFlag(MethodFlags.Virtual)
                            ? throw new ArgumentException(string.Format(ErrorMessages.STATIC_CONSTRUCTOR_IS_ABSTRACT_OR_VIRTUAL, methodName))
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
    /// Get the field which a name names, from this type or from a base type of it.<para/>
    /// A field which a base type declares is a field of the type as well, so the base types are walked for it, the
    /// nearest one first: the field of the type itself is the one which is answered with when it declares one of that
    /// name, and the one of the first base type which does otherwise.
    /// </summary>
    /// <param name="fieldName">Name of the field.</param>
    /// <returns>The field from this type or from a base type of it, or null when neither declares one of that name.</returns>
    internal FieldReference? GetFieldInThisOrABaseType(string fieldName)
        => AssemblyHandler.GetFieldFromType(Source, fieldName);

    /// <summary>
    /// Get the field which a name names, from the direct base type or from a base type of it.<para/>
    /// The walk starts at the direct base type rather than at the type itself, which is what the fields of a base type
    /// are reached through: a field which the type declares is not one which this query answers with.
    /// </summary>
    /// <param name="fieldName">Name of the field.</param>
    /// <returns>The field from a base type, or null when the type has no base type or none of them declares one of that name.</returns>
    internal FieldReference? GetFieldInBase(string fieldName)
        => Source.BaseType == null ? null : AssemblyHandler.GetFieldFromType(AssemblyHandler.GetCecilType(Source.BaseType).Definition, fieldName);

    /// <summary>
    /// Get the property which a name names, from this type or from a base type of it.<para/>
    /// A property which a base type declares is a property of the type as well, so the base types are walked for it, the
    /// nearest one first.
    /// </summary>
    /// <param name="propertyName">Name of the property.</param>
    /// <returns>The property from this type or from a base type of it, or null when neither declares one of that name.</returns>
    internal PropertyDefinition? GetPropertyInThisOrABaseType(string propertyName)
        => AssemblyHandler.GetPropertyFromType(Source, propertyName);

    /// <summary>
    /// Get the property which a name names, from the direct base type or from a base type of it.<para/>
    /// The walk starts at the direct base type rather than at the type itself, which is what the properties of a base
    /// type are reached through: a property which the type declares is not one which this query answers with.
    /// </summary>
    /// <param name="propertyName">Name of the property.</param>
    /// <returns>The property from a base type, or null when the type has no base type or none of them declares one of that name.</returns>
    internal PropertyDefinition? GetPropertyInBase(string propertyName)
        => Source.BaseType == null ? null : AssemblyHandler.GetPropertyFromType(AssemblyHandler.GetCecilType(Source.BaseType).Definition, propertyName);

    /// <summary>
    /// Get the method which a name and a signature name, from this type or from a base type of it.<para/>
    /// A method which a base type declares is a method of the type as well, so the base types are walked for it, the
    /// nearest one first.
    /// </summary>
    /// <param name="methodName">Name of the method.</param>
    /// <param name="parameters">Types of the parameters of the method.</param>
    /// <returns>The method from this type or from a base type of it, or null when neither declares one of that signature.</returns>
    internal MethodDefinition? GetMethodInThisOrABaseType(string methodName, IReadOnlyList<TypeReference> parameters)
        => AssemblyHandler.GetMethodFromType(Source, methodName, parameters, false);

    /// <summary>
    /// Get the method which a name and a signature name, from the direct base type or from a base type of it.<para/>
    /// The walk starts at the direct base type rather than at the type itself, which is what the methods of a base type
    /// are reached through: a method which the type declares is not one which this query answers with.
    /// </summary>
    /// <param name="methodName">Name of the method.</param>
    /// <param name="parameters">Types of the parameters of the method.</param>
    /// <returns>The method from a base type, or null when the type has no base type or none of them declares one of that signature.</returns>
    internal MethodDefinition? GetMethodInBase(string methodName, IReadOnlyList<TypeReference> parameters)
        => Source.BaseType == null ? null : AssemblyHandler.GetMethodFromType(AssemblyHandler.GetCecilType(Source.BaseType).Definition, methodName, parameters, false);

    /// <summary>
    /// Get the method which a signature names, which is the name of the method and the types of the parameters of it.<para/>
    /// A method of a base type is a method of the type as well, so the base types are walked for it. The signature is
    /// what tells two methods of one name apart, which a lookup by the name alone does not: a method which takes no
    /// parameter carries the empty signature rather than none at all.
    /// </summary>
    /// <param name="methodName">Name of the method.</param>
    /// <param name="parameterTypes">Types of the parameters of the method, which is empty for a method which takes none.</param>
    /// <returns>The method which the signature names, or null when the type and its base types hold none.</returns>
    internal IMethodHandler? GetMethodBySignature(string methodName, IList<IType> parameterTypes)
    {
        var curType = Source;
        while (curType != null)
        {
            var methodDef = curType.Methods.FirstOrDefault(method => method.Name == methodName && method.Parameters.SameWith(parameterTypes));
            if (methodDef != null) return new MethodHandler(methodDef, this);
            curType = curType.BaseType == null ? null : AssemblyHandler.GetCecilType(curType.BaseType).Definition;
        }

        return null;
    }
}