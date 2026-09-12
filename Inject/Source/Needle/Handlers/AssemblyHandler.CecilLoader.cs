namespace Gneedle.Inject;

partial class AssemblyHandler
{
    /// <summary>
    /// Convert the parameter type to TypeReference from target type.
    /// </summary>
    /// <param name="target">The parameter type's owner.</param>
    /// <param name="parameterType">The type which need to convert to TypeReference.</param>
    /// <param name="methodGenericParameters">Generic parameters from method.</param>
    /// <returns>Resolved parameter type.</returns>
    internal TypeReference ResolveParameterType(TypeReference target, IType parameterType, IEnumerable<GenericParameter>? methodGenericParameters = null)
    {
        switch (parameterType)
        {
            case NongenericType nongenericType:
                // A token stands for a generic parameter of the target type or of the method, so it is parsed before
                // GetCecilType. GetCecilType would append a Gneedle.Inject assembly reference to the target module,
                // which leaves the produced assembly depending on the weaver even though the token itself is replaced.
                if (CecilExtensions.TryResolveGenericParameter(nongenericType.Type, target as IMemberDefinition, methodGenericParameters,
                    out var tokenParameter))
                {
                    return tokenParameter!;
                }

                // The reference is the one which may be assigned to the target assembly, while the definition is owned by
                // the module which declares the type and would make Cecil throw at write time. See CecilType.Reference.
                return GetCecilType(nongenericType.Type).Reference;
            case GenericParameterType genericParameterType:
                // Get generic parameter from method or target type.
                return methodGenericParameters?.FirstOrDefault(param => genericParameterType.TypeName.Equals(param.FullName))
                    ?? target.GenericParameters.FirstOrDefault(param => genericParameterType.TypeName.Equals(param.FullName))
                    ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_GENERIC_PARAMETER, genericParameterType.TypeName));
            case GenericType genericType:
                // Generic type should be recursively resolve its arguments.
                var parameterTypeDef = GetCecilType(genericType.Type);
                // Resolve all arguments.
                var arguments = genericType.GenericArguments
                                           .Select(argument => ResolveParameterType(target, argument, methodGenericParameters))
                                           .ToArray();
                // Create generic instance.
                return Assembly.Source.MainModule
                               .ImportReference(parameterTypeDef.Definition)
                               .MakeGenericInstanceType(arguments);
            default: throw new ArgumentOutOfRangeException(nameof(parameterType));
        }
    }

    /// <summary>
    /// Get cecil type from <see cref="IType"/> which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="type">The type which need to be converted.</param>
    /// <returns>The cecil type from current definition.</returns>
    internal CecilType GetCecilType(IType type) => type switch
    {
        // A type which is backed by a System.Type is resolved through the type rather than through its name, because a
        // name alone cannot tell which assembly declares the type.
        NongenericType nongenericType => GetCecilType(nongenericType.Type),
        GenericType genericType       => GetCecilType(genericType.Type),
        _                             => GetCecilType(type.GetTypeName())
    };

    /// <summary>
    /// Get cecil type from type name which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="typeName">Name of the type which need to be converted.</param>
    /// <exception cref="ArgumentException">Thrown when can't get from runtime type with the type name.</exception>
    /// <returns>The cecil type from current definition.</returns>
    internal CecilType GetCecilType(string typeName)
    {
        // Check assembly has be appended to cache.
        if (m_TypeCache.TryGetValue(typeName, out var cecilType)) return cecilType;

        // Type.GetType searches the assembly which calls it and the corlib only, so this resolves the types of the weaver
        // itself and of the corlib. A type of any other assembly is resolved through its System.Type instead, which the
        // callers which hold one pass in by GetCecilType(IType).
        var type = Type.GetType(typeName);

        if (type != null) return GetCecilType(type);

        // A type which the target assembly declares is not loadable by its name yet, so it is looked up by its full name.
        // It has to be the full name and the nested types as well, just like GetType(string) looks it up.
        foreach (var module in Assembly.Source.Modules)
        {
            foreach (var typeDefinition in module.Types)
            {
                if (typeDefinition.FullName == typeName) return GetCecilType(typeDefinition);

                var nestedType = typeDefinition.NestedTypes.FirstOrDefault(nested => nested.FullName == typeName);
                if (nestedType != null) return GetCecilType(nestedType);
            }
        }

        throw new ArgumentException(ErrorMessages.INVALID_TYPE_NAME);
    }

    /// <summary>
    /// Get cecil type from <see cref="Mono.Cecil.TypeReference"/> which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="typeRef">The type which need to be converted.</param>
    /// <returns>The cecil type from current definition</returns>
    internal CecilType GetCecilType(TypeReference typeRef)
    {
        // If has imported, then return from cache.
        if (m_TypeCache.TryGetValue(new TypeName(typeRef).ToString(), out var cecilType)) return cecilType;

        // A type which FromAssemblyAttribute marks stands for the real type of the same name which another assembly
        // declares. It is resolved before the assembly of the reference is appended, so that the assembly which only
        // declares the stub is not referenced by the produced assembly. The stub and the real type share a full name,
        // so they share the cache entry as well.
        if (typeRef.TryGetFromAssemblyDefinition(Assembly.Source.MainModule, out var fromAssemblyType))
        {
            cecilType                                     = new CecilType(fromAssemblyType!, Assembly.Source.MainModule.ImportReference(fromAssemblyType));
            m_TypeCache[new TypeName(typeRef).ToString()] = cecilType;
            return cecilType;
        }

        // Get assembly name.
        var assemblyName = typeRef.Module.Assembly.Name.FullName;

        // Check assembly has be appended to cache.
        if (!m_AssemblyCache.TryGetValue(assemblyName, out var assemblyDef))
        {
            // Get target assembly definition.
            assemblyDef = typeRef.Module.Assembly;
            // Append to cache.
            m_AssemblyCache[assemblyName] = assemblyDef;
            //Add to Reference.
            AddReference(assemblyDef);
        }

        // Import type ref into the current assembly definition, and add to type cache. A reference which belongs to
        // another module cannot be written to the produced assembly, so Reference must be owned by the current one.
        cecilType                                     = new CecilType(typeRef.Resolve(), Assembly.Source.MainModule.ImportReference(typeRef));
        m_TypeCache[new TypeName(typeRef).ToString()] = cecilType;
        return cecilType;
    }

    /// <summary>
    /// Get cecil type from <see cref="System.Type">System.Type</see> which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="type">The type which need to be converted.</param>
    /// <returns>The cecil type from current definition</returns>
    internal CecilType GetCecilType(Type type)
    {
        // If has imported, then return from cache.
        if (m_TypeCache.TryGetValue(new TypeName(type), out var cecilType)) return cecilType;

        // A type which the assembly being woven declares is taken from the module rather than imported from the runtime.
        // The import of a type names the assembly which declares it, and the name of the assembly which is being written
        // is a reference of an assembly to itself, which no loader reads back: the whole of the assembly which the
        // weaving produced would be discarded, declared to be referring to itself.
        if (type.Assembly.GetName().Name == Assembly.Source.Name.Name)
        {
            var declaredType = FindDeclaredType(type) ?? throw new ArgumentException(ErrorMessages.INVALID_TYPE_NAME);
            cecilType                                  = new CecilType(declaredType, declaredType);
            m_TypeCache[new TypeName(type).ToString()] = cecilType;
            return cecilType;
        }

        // A reference cycle cannot be represented in metadata, so it is rejected before the type is imported. The
        // assembly of the type does not have to be read for it, because the reflection type knows its references.
        if (type.Assembly.GetReferencedAssemblies().Any(name => name.FullName.Equals(Assembly.Source.FullName)))
        {
            throw new ArgumentException(string.Format(ErrorMessages.ASSEMBLY_CYCLE_REFERENCE, Assembly.Source.FullName, type.Assembly.FullName));
        }

        // A type which FromAssemblyAttribute marks stands for the real type of the same name which another assembly
        // declares. The real type is resolved before the stub is imported, so that the assembly which only declares the
        // stub is not appended as a reference of the produced assembly. The stub and the real type share a full name, so
        // they share the cache entry as well.
        if (Attribute.GetCustomAttribute(type, typeof(FromAssemblyAttribute)) is FromAssemblyAttribute fromAssembly)
        {
            var fromAssemblyDefinition = CecilExtensions.ResolveTypeFromAssembly(Assembly.Source.MainModule, fromAssembly.Name, type.FullName!);
            cecilType                                  = new CecilType(fromAssemblyDefinition, Assembly.Source.MainModule.ImportReference(fromAssemblyDefinition));
            m_TypeCache[new TypeName(type).ToString()] = cecilType;
            return cecilType;
        }

        // Import type ref into the current assembly definition. The import registers the assembly reference which the
        // type is resolved through as well, and that one may differ from the assembly which declares the type, because
        // the reflection importer maps the corlib to another assembly.
        var targetTypeRef = Assembly.Source.MainModule.ImportReference(type);

        // The definition is the one for looking the members up. An assembly which only exists in memory cannot be read
        // back, so it is not available for a type of such an assembly.
        cecilType                                  = new CecilType(targetTypeRef.Resolve(), targetTypeRef);
        m_TypeCache[new TypeName(type).ToString()] = cecilType;
        return cecilType;
    }

    /// <summary>
    /// Find the definition of a type which the assembly being woven declares.
    /// </summary>
    /// <param name="type">The type which is declared.</param>
    /// <returns>The definition of the type, or null when the assembly does not declare it.</returns>
    private TypeDefinition? FindDeclaredType(Type type)
    {
        var declaring = type.DeclaringType;

        // The name of a type which is declared by another one is qualified by the type which declares it, which Cecil
        // writes out itself, so only the name of the type is compared there.
        if (declaring == null)
        {
            return Assembly.Source.Modules.SelectMany(module => module.Types).FirstOrDefault(candidate => candidate.FullName == type.FullName);
        }

        return FindDeclaredType(declaring)?.NestedTypes.FirstOrDefault(nested => nested.Name == type.Name);
    }
}