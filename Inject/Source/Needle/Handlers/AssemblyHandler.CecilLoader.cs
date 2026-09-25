using System.Reflection;

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
            // A null names no type, and it is read before the kinds are, because the reading of a kind which is none of
            // them names the type of the value it was given: a null has no type for that to name.
            case null: throw new WeavingException(string.Format(ErrorMessages.TYPE_IS_NULL, nameof(parameterType)));
            case NongenericType nongenericType:
                // A token stands for a generic parameter of the target type or of the method, so it is parsed before
                // GetCecilType. GetCecilType would append a Gneedle.Inject assembly reference to the target module,
                // which leaves the produced assembly depending on the weaver even though the token itself is replaced.
                if (TokenParsing.TryResolveGenericParameter(nongenericType.Type, target as IMemberDefinition, methodGenericParameters, out var tokenParameter))
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
                    ?? throw new WeavingException(string.Format(ErrorMessages.INVALID_GENERIC_PARAMETER, genericParameterType.TypeName));
            case GenericType genericType:
                // Generic type should be recursively resolve its arguments.
                var parameterTypeDef = GetCecilType(genericType.Type);
                // Resolve all arguments.
                var arguments = genericType.GenericArguments
                                           .Select(argument => ResolveParameterType(target, argument, methodGenericParameters))
                                           .ToArray();
                // Create generic instance.
                var module = Assembly.Source.MainModule;
                return ModuleLock.Import(module, parameterTypeDef.Definition).MakeGenericInstanceType(arguments);
            case ReferencedType referencedType:
                // The description holds the name of the type and nothing else, so the type is resolved by that name, as
                // the name which every other reading of a type of the tree is written with is resolved: a name which
                // the assembly being woven declares is found in its module, and one of an assembly which lies beside
                // it is found by the runtime. A name which neither holds is refused where it is read.
                return GetCecilType(referencedType.TypeName).Reference;
            // The four kinds of a type are the ones this library builds and the only ones anything here reads, and the
            // interface is one a caller can implement: a kind which is none of them is refused by name rather than
            // reported as the fault of the framework, which is what an argument out of range is read as.
            default: throw new WeavingException(string.Format(ErrorMessages.TYPE_IS_OF_A_KIND_WHICH_IS_NOT_ONE, parameterType.GetType().FullName));
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
    /// <exception cref="WeavingException">Thrown when can't get from runtime type with the type name.</exception>
    /// <returns>The cecil type from current definition.</returns>
    internal CecilType GetCecilType(string typeName)
    {
        // The name which a caller writes is the one of the metadata, which separates the types a type is nested in with a
        // slash, or the one which a System.Type spells, which separates them with a plus: the cache is keyed by one name,
        // so the two are read as one name here.
        if (m_TypeCache.TryGetValue(typeName.Replace('/', '+'), out var cecilType)) return cecilType;

        // Type.GetType searches the assembly which calls it and the corlib only, so this resolves the types of the weaver
        // itself and of the corlib. A type of any other assembly is resolved through its System.Type instead, which the
        // callers which hold one pass in by GetCecilType(IType).
        var type = Type.GetType(typeName);

        if (type != null) return GetCecilType(type);

        // A type which the target assembly declares is not loadable by its name yet, so it is looked up by its full name.
        // It has to be the full name and the nested types as well, just like GetType(string) looks it up, and a type
        // which a nested type declares is named by every level of the nesting which names it.
        foreach (var module in Assembly.Source.Modules)
        {
            var typeDefinition = InjectorInterfaces.AllTypes(module).FirstOrDefault(type => type.FullName == typeName);
            if (typeDefinition != null) return GetCecilType(typeDefinition);
        }

        throw new WeavingException(ErrorMessages.INVALID_TYPE_NAME);
    }

    /// <summary>
    /// Get cecil type from <see cref="Mono.Cecil.TypeReference"/> which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="typeRef">The type which need to be converted.</param>
    /// <returns>The cecil type from current definition</returns>
    internal CecilType GetCecilType(TypeReference typeRef)
    {
        var name = new TypeName(typeRef).ToString();

        // If has imported, then return from cache.
        if (m_TypeCache.TryGetValue(name, out var cecilType)) return cecilType;

        // What the lookup which missed writes is the module itself, which is written under the lock of that module, and
        // the lookup is made again there because another thread may have imported the same type while this one waited.
        var module = Assembly.Source.MainModule;
        lock (ModuleLock.Of(module))
        {
            if (m_TypeCache.TryGetValue(name, out cecilType)) return cecilType;

            // A type which FromAssemblyAttribute marks stands for the real type of the same name which another assembly
            // declares. It is resolved before the assembly of the reference is appended, so that the assembly which only
            // declares the stub is not referenced by the produced assembly. The stub and the real type share a full
            // name, so they share the cache entry as well.
            if (typeRef.TryGetFromAssemblyDefinition(module, out var fromAssemblyType))
            {
                cecilType         = new CecilType(fromAssemblyType!, ModuleLock.Import(module, fromAssemblyType!));
                m_TypeCache[name] = cecilType;
                return cecilType;
            }

            // Get assembly name.
            var assemblyName = typeRef.Module.Assembly.Name.FullName;

            // Check assembly has be appended to cache.
            if (!m_AssemblyCache.TryGetValue(assemblyName, out var assemblyDef))
            {
                // Get target assembly definition.
                assemblyDef                   = typeRef.Module.Assembly;
                // Append to cache.
                m_AssemblyCache[assemblyName] = assemblyDef;
                //Add to Reference.
                AddReference(assemblyDef);
            }

            // Import type ref into the current assembly definition, and add to type cache. A reference which belongs to
            // another module cannot be written to the produced assembly, so Reference must be owned by the current one.
            // The definition is what the members of the type are read from, and a reference which names a type that the
            // assembly it was asked of does not hold has none: the type is refused where it is read rather than being
            // carried about as a type of nothing, whose every query would throw from somewhere the caller cannot see the
            // reason of.
            var definition = typeRef.Resolve()
                ?? throw new WeavingException(string.Format(ErrorMessages.TYPE_CANNOT_BE_READ, typeRef.FullName));
            cecilType         = new CecilType(definition, ModuleLock.Import(module, typeRef));
            m_TypeCache[name] = cecilType;
            return cecilType;
        }
    }

    /// <summary>
    /// Get cecil type from <see cref="System.Type">System.Type</see> which was imported from current assembly definition.
    /// If the type's assembly was never been referenced, then it would be appended.
    /// </summary>
    /// <param name="type">The type which need to be converted.</param>
    /// <returns>The cecil type from current definition</returns>
    internal CecilType GetCecilType(Type type)
    {
        var name = new TypeName(type).ToString();

        // If has imported, then return from cache.
        if (m_TypeCache.TryGetValue(name, out var cecilType)) return cecilType;

        // What the lookup which missed writes is the module itself, which is written under the lock of that module, and
        // the lookup is made again there because another thread may have imported the same type while this one waited.
        var module = Assembly.Source.MainModule;
        lock (ModuleLock.Of(module))
        {
            if (m_TypeCache.TryGetValue(name, out cecilType)) return cecilType;

            // A type which the assembly being woven declares is taken from the module rather than imported from the
            // runtime. The import of a type names the assembly which declares it, and the name of the assembly which is
            // being written is a reference of an assembly to itself, which no loader reads back: the whole of the
            // assembly which the weaving produced would be discarded, declared to be referring to itself.
            if (type.Assembly.GetName().Name == Assembly.Source.Name.Name)
            {
                var declaredType = FindDeclaredType(type) ?? throw new WeavingException(ErrorMessages.INVALID_TYPE_NAME);
                cecilType = new CecilType(declaredType, declaredType);
                m_TypeCache[name] = cecilType;
                return cecilType;
            }

            // A reference cycle cannot be represented in metadata, so it is rejected before the type is imported. The
            // assembly of the type does not have to be read for it, because the reflection type knows its references.
            if (type.Assembly.GetReferencedAssemblies().Any(reference => reference.FullName.Equals(Assembly.Source.FullName)))
            {
                throw new WeavingException(string.Format(ErrorMessages.ASSEMBLY_CYCLE_REFERENCE, Assembly.Source.FullName, type.Assembly.FullName));
            }

            // A type which FromAssemblyAttribute marks stands for the real type of the same name which another assembly
            // declares. The real type is resolved before the stub is imported, so that the assembly which only declares
            // the stub is not appended as a reference of the produced assembly. The stub and the real type share a full
            // name, so they share the cache entry as well.
            if (Attribute.GetCustomAttribute(type, typeof(FromAssemblyAttribute)) is FromAssemblyAttribute fromAssembly)
            {
                var fromAssemblyDefinition = FromAssembly.ResolveTypeFromAssembly(module, fromAssembly.Name, type.FullName!);
                cecilType = new CecilType(fromAssemblyDefinition, ModuleLock.Import(module, fromAssemblyDefinition));
                m_TypeCache[name] = cecilType;
                return cecilType;
            }

            // Import type ref into the current assembly definition. The import registers the assembly reference which the
            // type is resolved through as well, and that one may differ from the assembly which declares the type,
            // because the reflection importer maps the corlib to another assembly.
            var targetTypeRef = ModuleLock.Import(module, type);

            // The definition is the one for looking the members up, and a type which the assembly it was asked of does
            // not hold has none: the type is refused here rather than being carried about as a type of nothing.
            var definition = targetTypeRef.Resolve() ?? throw new WeavingException(string.Format(ErrorMessages.TYPE_CANNOT_BE_READ, type.FullName));
            cecilType = new CecilType(definition, targetTypeRef);
            m_TypeCache[name] = cecilType;
            return cecilType;
        }
    }

    /// <summary>
    /// Get the definition of the method which a template names, which is read out of the module when the assembly being
    /// woven declares it and imported from the runtime otherwise.
    /// </summary>
    /// <remarks>
    /// A template is given as the reflection of a method, and the import of one names the assembly which declares it.
    /// The name of the assembly which is being written is a reference of an assembly to itself, which no loader reads
    /// back, so a template of the assembly itself is found in the module rather than imported: a project which writes
    /// its templates where it writes the members they are woven into is the case, which is every weaving of an assembly
    /// by the build which produced it.
    /// </remarks>
    /// <param name="template">The method which holds the body to weave.</param>
    /// <returns>The definition of the template.</returns>
    /// <exception cref="WeavingException">Thrown when the assembly being woven declares the template and its module does not hold it.</exception>
    internal MethodDefinition ResolveTemplate(MethodInfo template)
    {
        if (template.DeclaringType == null || template.DeclaringType.Assembly.GetName().Name != Assembly.Source.Name.Name)
        {
            var module = Assembly.Source.MainModule;
            return ModuleLock.Import(module, template).Resolve();
        }

        // The name of a method alone does not tell two of one name apart, so the signature is what the template is
        // looked for by, as the reflection names it: the parameters of the definition are matched with the types of the
        // parameters of the template, of which a template that takes none holds the empty signature.
        var parameterTypes = template.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var declaredMethod = FindDeclaredType(template.DeclaringType)?.Methods
                                                                     .FirstOrDefault(method => method.Name == template.Name && method.Parameters.SameWith(parameterTypes));
        return declaredMethod ?? throw new WeavingException(string.Format(ErrorMessages.INVALID_METHOD, $"{template.DeclaringType.FullName}.{template.Name}"));
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
        return declaring == null
            ? Assembly.Source.Modules.SelectMany(module => module.Types).FirstOrDefault(candidate => candidate.FullName == type.FullName)
            : FindDeclaredType(declaring)?.NestedTypes.FirstOrDefault(nested => nested.Name == type.Name);
    }
}