using System.Text.RegularExpressions;

namespace Gneedle.Inject;

/// <summary>
/// Extension methods for Mono.Cecil related types.
/// </summary>
internal static class CecilExtensions
{
    /// <param name="attributes">The attributes which need to convert.</param>
    extension(System.Reflection.GenericParameterAttributes attributes)
    {
        /// <summary>
        /// Convert the <see cref="System.Reflection.GenericParameterAttributes">System.Reflection.GenericParameterAttributes</see>
        /// to <see cref="Mono.Cecil.GenericParameterAttributes">Mono.Cecil.GenericParameterAttributes</see>
        /// </summary>
        /// <returns>Mono cecil generic parameter attributes.</returns>
        internal GenericParameterAttributes ToCecilAttribute()
            => (GenericParameterAttributes) attributes;
    }

    /// <param name="parameterDefs">Method parameter definitions.</param>
    extension(IList<ParameterDefinition> parameterDefs)
    {
        /// <summary>
        /// Check whether the parameters have same names with <c>targetTypes</c>.
        /// </summary>
        /// <param name="targetTypes"></param>
        internal bool SameWith(IList<TypeReference> targetTypes)
            => parameterDefs.Count == targetTypes.Count
                && !parameterDefs.Where((t, index) => !TypeName.HasSameName(t.ParameterType, targetTypes[index])).Any();

        /// <summary>
        /// Check whether the parameters have same names with <c>targetTypes</c>.
        /// </summary>
        /// <param name="targetTypes"></param>
        internal bool SameWith(IList<IType> targetTypes)
            => parameterDefs.Count == targetTypes.Count
                && !parameterDefs.Where((t, index) => !TypeName.HasSameName(t.ParameterType, targetTypes[index])).Any();

        /// <summary>
        /// Check whether the parameters have same names with <c>targetTypes</c>.
        /// </summary>
        /// <param name="targetTypes"></param>
        internal bool SameWith(IList<Type> targetTypes)
            => parameterDefs.Count == targetTypes.Count
                && !parameterDefs.Where((t, index) => !TypeName.HasSameName(t.ParameterType, targetTypes[index])).Any();
    }

    /// <param name="attributeDefinition">Type definition of attribute.</param>
    extension(TypeDefinition attributeDefinition)
    {
        /// <summary>
        /// Create a custom attribute from type definition and constructor arguments.
        /// </summary>
        /// <param name="module">The provider of importing argument type references.</param>
        /// <param name="arguments">Arguments of the attribute constructor calling.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when the attribute doesn't contain any constructor which has argument types match with <c>parameters</c>.
        /// </exception>
        /// <returns>
        /// The custom attribute from <c>attributeDefinition</c> with parameters.
        /// It would be null when the attribute definition not contains constructor with <c>parameters</c>.
        /// </returns>
        internal CustomAttribute CreateCustomAttribute(ModuleDefinition module, params object[] arguments)
        {
            // Get argument types.
            var argTypes = arguments.Select(parameter => parameter.GetType()).ToArray();

            // Get the constructor matches with argTypes.
            var method = attributeDefinition.Methods.FirstOrDefault(method => method.IsConstructor && method.Parameters.SameWith(argTypes));
            if (method == null) throw new ArgumentException(ErrorMessages.INVALID_PARAMETERS);

            // Create custom attribute and append arguments. The constructor is imported rather than used as it is, because
            // the attribute is declared by another assembly whenever the type which carries it is not the one which
            // declares the attribute: Cecil writes a member of another module only through a reference to it.
            var attribute = new CustomAttribute(module.ImportReference(method));
            for (var index = 0; index < argTypes.Length; index++)
            {
                var argument = new CustomAttributeArgument(module.ImportReference(argTypes[index]), arguments[index]);
                attribute.ConstructorArguments.Add(argument);
            }

            return attribute;
        }

        /// <summary>
        /// Get the <see cref="Type">System.Runtime.Type</see> from the name of the <c>typeDefinition</c>.
        /// </summary>
        /// <returns>Runtime type from the name of the <c>typeDefinition</c>.</returns>
        internal Type? GetRuntimeType() => Type.GetType(new TypeName(attributeDefinition));
    }

    /// <param name="ins">Opcode provider.</param>
    extension(Instruction ins)
    {
        /// <summary>
        /// Get ldarg code index.
        /// </summary>
        /// <param name="index">Index of the ldarg target. It would be -1 when return false.</param>
        /// <returns>False when the instruction opcode is not a ldarg type.</returns>
        internal bool TryGetLdargIndex(out int index)
        {
            index = ins.OpCode.Code switch
            {
                Code.Ldarg_0 => 0,
                Code.Ldarg_1 => 1,
                Code.Ldarg_2 => 2,
                Code.Ldarg_3 => 3,
                Code.Ldarg   => (int) ins.Operand,
                _            => -1
            };
            return index != -1;
        }

        /// <summary>
        /// Get stloc code index.
        /// </summary>
        /// <param name="index">Index of the stloc target. It would be -1 when return false.</param>
        /// <returns>False when the instruction opcode is not a stloc type.</returns>
        internal bool TryGetStlocIndex(out int index)
        {
            index = ins.OpCode.Code switch
            {
                Code.Stloc_0 => 0,
                Code.Stloc_1 => 1,
                Code.Stloc_2 => 2,
                Code.Stloc_3 => 3,
                Code.Stloc_S => (int) ins.Operand,
                Code.Stloc   => (int) ins.Operand,
                _            => -1
            };
            return index != -1;
        }

        /// <summary>
        /// Get ldloc code index.
        /// </summary>
        /// <param name="index">Index of the ldloc target. It would be -1 when return false.</param>
        /// <returns>False when the instruction opcode is not a ldloc type.</returns>
        internal bool TryGetLdlocIndex(out int index)
        {
            index = ins.OpCode.Code switch
            {
                Code.Ldloc_0 => 0,
                Code.Ldloc_1 => 1,
                Code.Ldloc_2 => 2,
                Code.Ldloc_3 => 3,
                Code.Ldloc_S => (int) ins.Operand,
                Code.Ldloc   => (int) ins.Operand,
                _            => -1
            };
            return index != -1;
        }
    }

    /// <summary>
    /// The pattern of the <c>Gneedle.Inject.T_[0-20]</c> token, which stands for the generic parameter of the type.
    /// </summary>
    /// <remarks>
    /// The token must be the whole type name. Otherwise a type which merely contains a token as its generic argument,
    /// just like <c>List&lt;Gneedle.Inject.T_0&gt;</c>, would be taken as the token itself.
    /// </remarks>
    private static readonly Regex s_GenericTypeNamePattern = new(@$"^{nameof(Gneedle)}\.{nameof(Inject)}\.T_(1[1-9]|20|[0-9])$");

    /// <inheritdoc cref="s_GenericTypeNamePattern"/>
    private static readonly Regex s_GenericMethodNamePattern = new(@$"^{nameof(Gneedle)}\.{nameof(Inject)}\.M_(1[1-9]|20|[0-9])$");

    /// <summary>
    /// Try to parse the index which the Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20] token of <paramref name="typeName"/> holds.
    /// </summary>
    /// <param name="typeName">The full name of the type which could be a token.</param>
    /// <param name="index">The index held by the token.</param>
    /// <param name="isFromMethod">Whether the token stands for the generic parameter of the method rather than of the declaring type.</param>
    /// <returns>Whether the <paramref name="typeName"/> is a token.</returns>
    private static bool TryGetParsedTokenIndex(string typeName, out int index, out bool isFromMethod)
    {
        index        = 0;
        isFromMethod = false;

        // Match type.
        var matcher = s_GenericTypeNamePattern.Match(typeName);
        if (matcher.Success && int.TryParse(matcher.Groups[1].Value, out index)) return true;

        // Match method.
        matcher = s_GenericMethodNamePattern.Match(typeName);
        if (!matcher.Success || !int.TryParse(matcher.Groups[1].Value, out index)) return false;
        isFromMethod = true;
        return true;
    }

    /// <summary>
    /// Try to parse the Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20] token of <paramref name="type"/> to the
    /// generic parameter of the type declared by <paramref name="typeProvider"/> or of the method held by <paramref name="methodParameters"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <c>TryGetParsedGenericParameter</c>, which reads the token of a type of a module, this one reads the token
    /// of an <see cref="IType"/> declared through the public API. The token is a <see cref="System.Type"/> there, and the
    /// generic parameters of the method are not held by a method definition yet.
    /// </remarks>
    /// <param name="type">The type which could be a token.</param>
    /// <param name="typeProvider">Holder of the generic parameters of the declaring type.</param>
    /// <param name="methodParameters">Generic parameters of the method.</param>
    /// <param name="parameter">The generic parameter which the token stands for.</param>
    /// <returns>Whether the <paramref name="type"/> is a token which could be resolved.</returns>
    /// <exception cref="IndexOutOfRangeException">Throw when the token index out of the generic parameters count.</exception>
    internal static bool TryResolveGenericParameter(Type type, IMemberDefinition? typeProvider, IEnumerable<GenericParameter>? methodParameters,
                                                    out GenericParameter? parameter)
    {
        parameter = null;
        if (type.FullName == null || !TryGetParsedTokenIndex(type.FullName, out var index, out var isFromMethod)) return false;

        // The token stands for the generic parameter of the method.
        if (isFromMethod)
        {
            var parameters = methodParameters?.ToArray();
            if (parameters == null) return false;
            if (index > parameters.Length - 1)
            {
                throw new IndexOutOfRangeException($"'M_{index}' index out of Generic parameters count from {type.FullName}");
            }

            parameter = parameters[index];
            return true;
        }

        // The token stands for the generic parameter of the declaring type.
        var typeDef = typeProvider switch
        {
            TypeDefinition typeDefinition     => typeDefinition,
            MethodDefinition methodDefinition => methodDefinition.DeclaringType,
            _                                 => null
        };
        if (typeDef == null) return false;
        if (index > typeDef.GenericParameters.Count - 1)
        {
            throw new IndexOutOfRangeException($"'T_{index}' index out of Generic parameters count from {typeDef.FullName}");
        }

        parameter = typeDef.GenericParameters[index];
        return true;
    }

    /// <summary>
    /// Get the name of the assembly which the <see cref="FromAssemblyAttribute"/> of <paramref name="definition"/> names.
    /// </summary>
    /// <param name="definition">The type definition which could carry the attribute.</param>
    /// <returns>The name of the assembly, or null when the definition does not carry the attribute.</returns>
    private static string? GetFromAssemblyName(TypeDefinition definition)
    {
        foreach (var attribute in definition.CustomAttributes)
        {
            if (attribute.AttributeType.FullName != FromAssemblyAttribute.TYPE_NAME) continue;
            if (attribute.ConstructorArguments.Count == 0) return null;

            return attribute.ConstructorArguments[0].Value as string;
        }

        return null;
    }

    /// <summary>
    /// Find the type named <paramref name="typeFullName"/> in the assembly which <paramref name="assemblyName"/> names.
    /// </summary>
    /// <param name="module">The module which the reference cycle is checked against.</param>
    /// <param name="assemblyName">Name of the assembly which declares the type.</param>
    /// <param name="typeFullName">Full name of the type, which names a nested type as <c>Namespace.Outer/Namespace.Inner</c>.</param>
    /// <returns>The definition of the type.</returns>
    /// <exception cref="ArgumentException">Thrown when the assembly or the type can't be resolved.</exception>
    internal static TypeDefinition ResolveTypeFromAssembly(ModuleDefinition module, string assemblyName, string typeFullName)
    {
        // The target assembly does not reference itself, so it is looked up in the module itself.
        if (IsSameAssembly(module.Assembly.Name, assemblyName))
        {
            return FindType(module, typeFullName)
                ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_FROM_ASSEMBLY_TYPE, typeFullName, assemblyName));
        }

        // The target may not reference the assembly yet, so a reference is made up when none matches and the resolver is
        // given the chance to find it. The reference is appended by the import of the resolved type which follows.
        var reference = module.AssemblyReferences.FirstOrDefault(reference => IsSameAssembly(reference, assemblyName))
                     ?? new AssemblyNameReference(assemblyName, new Version(0, 0, 0, 0));

        AssemblyDefinition assembly;
        try
        {
            assembly = module.AssemblyResolver?.Resolve(reference)
                ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_FROM_ASSEMBLY_ASSEMBLY, assemblyName));
        }
        catch (AssemblyResolutionException)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_FROM_ASSEMBLY_ASSEMBLY, assemblyName));
        }

        // A cycle reference cannot be represented in metadata, which is rejected as well when a reference is appended.
        if (assembly.MainModule.AssemblyReferences.Any(name => name.FullName.Equals(module.Assembly.FullName)))
        {
            throw new ArgumentException(string.Format(ErrorMessages.ASSEMBLY_CYCLE_REFERENCE, module.Assembly.FullName, assembly.FullName));
        }

        return FindType(assembly.MainModule, typeFullName)
            ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_FROM_ASSEMBLY_TYPE, typeFullName, assemblyName));
    }

    /// <summary>
    /// Check whether the name is the one of <paramref name="assemblyName"/>, which could be a simple or a full name.
    /// </summary>
    /// <param name="name">The name of an assembly.</param>
    /// <param name="assemblyName">The simple or full name which was named by the attribute.</param>
    /// <returns>Whether the name matches.</returns>
    private static bool IsSameAssembly(AssemblyNameReference name, string assemblyName)
        => name.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)
            || name.FullName.Equals(assemblyName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Find a type by its full name in <paramref name="module"/>, the nested types included.
    /// </summary>
    /// <param name="module">The module which declares the type.</param>
    /// <param name="fullName">Full name of the type.</param>
    /// <returns>The definition of the type, or null when the module does not declare it.</returns>
    private static TypeDefinition? FindType(ModuleDefinition module, string fullName)
    {
        // The full name of a nested type holds the full name of its declaring type, a slash and its own full name, so it
        // is the whole path rather than the last segment of it. The declaring type is walked down to reach it.
        var separator = fullName.IndexOf('/');
        if (separator < 0) return module.Types.FirstOrDefault(type => type.FullName == fullName);

        // The range operator is not used here, because System.Range is not a part of .NET Framework.
        var declaringFullName = fullName.Substring(0, separator);
        return FindType(module, declaringFullName)?.NestedTypes.FirstOrDefault(nested => nested.FullName == fullName);
    }

    /// <param name="typeReference">The type reference which could be Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20].</param>
    extension(TypeReference typeReference)
    {
        /// <summary>
        /// Try to parse Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20] to GenericParameter.
        /// </summary>
        /// <param name="provider">GenericParameters provider.</param>
        /// <param name="parameter">The generic parameter from type or method definition.</param>
        /// <returns>Whether the <c>typeReference</c> could passer as GenericParameter.</returns>
        /// <exception cref="IndexOutOfRangeException">Throw when the <c>typeReference</c> index out of the <c>provider</c>'s GenericParameters count.</exception>
        internal bool TryGetParsedGenericParameter(IMemberDefinition provider, out GenericParameter? parameter)
            => TryGetParsedGenericParameter(typeReference, provider, out parameter, out _, out _);

        /// <summary>
        /// Try to parse Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20] to GenericParameter.
        /// </summary>
        /// <param name="provider">GenericParameters provider.</param>
        /// <param name="parameter">The generic parameter from type or method definition.</param>
        /// <param name="index">The generic parameter index.</param>
        /// <param name="isFromMethod">Is the generic type from the method or from the method's declaring type.</param>
        /// <returns>Whether the <c>typeReference</c> could passer as GenericParameter.</returns>
        /// <exception cref="IndexOutOfRangeException">Throw when the <c>typeReference</c> index out of the <c>provider</c>'s GenericParameters count.</exception>
        internal bool TryGetParsedGenericParameter(IMemberDefinition provider, out GenericParameter? parameter, out int index, out bool isFromMethod)
        {
            parameter = default;
            if (!TryGetParsedTokenIndex(typeReference.FullName, out index, out isFromMethod)) return false;

            // Match type.
            if (!isFromMethod)
            {
                var typeDef = provider switch
                {
                    TypeDefinition typeDefinition     => typeDefinition,
                    MethodDefinition methodDefinition => methodDefinition.DeclaringType,
                    _                                 => null
                };
                if (typeDef == null) return false;

                var typeParameters = typeDef.GenericParameters;
                if (index > typeParameters.Count - 1)
                {
                    throw new IndexOutOfRangeException($"'T_{index}' index out of Generic parameters count from {typeDef.FullName}");
                }

                parameter = typeParameters[index];
                return true;
            }

            // Match method.
            if (provider is not MethodDefinition methodDef) return false;
            var methodParameters = methodDef.GenericParameters;
            if (index > methodParameters.Count - 1)
            {
                throw new IndexOutOfRangeException($"'M_{index}' index out of Generic parameters count from {methodDef.FullName}");
            }

            parameter = methodParameters[index];
            return true;
        }

        /// <summary>
        /// Parse the Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20] tokens in the type reference to the generic parameters of the <c>provider</c>.
        /// Unlike <c>TryGetParsedGenericParameter</c>, the tokens nested in the type are parsed as well,
        /// just like <c>List&lt;Gneedle.Inject.T_0&gt;</c> to <c>List&lt;T&gt;</c>.
        /// </summary>
        /// <param name="provider">GenericParameters provider.</param>
        /// <param name="module">The module which the returned type reference belongs to. The type reference is imported to it when it comes from another assembly.</param>
        /// <returns>
        /// The type reference without any token. It is the generic parameter of the <c>provider</c> itself when the type reference is a token.
        /// </returns>
        /// <exception cref="IndexOutOfRangeException">Throw when the token index out of the <c>provider</c>'s GenericParameters count.</exception>
        internal TypeReference ParseGenericTokens(IMemberDefinition provider, ModuleDefinition module)
        {
            // A type specification wraps another type, and the FullName of a wrapper which holds no affix
            // (just like `pinned T` or `modreq(InAttribute) T`) is the FullName of the wrapped type itself.
            // So the wrapped type is parsed below, otherwise the token of it would be taken as the token of the whole type.
            if (typeReference is not TypeSpecification)
            {
                // The token stands for the generic parameter of the provider itself. It is not a type of any module, so it doesn't need to be imported.
                if (typeReference.TryGetParsedGenericParameter(provider, out var parameter)) return parameter!;

                // A type which FromAssemblyAttribute marks stands for the real type of the same name which another
                // assembly declares, so the real type replaces it. The reference ownership is not a criterion here: the
                // reference of a member operand was imported to the module before it is parsed, even though the type it
                // denotes is the stub of the template assembly.
                if (typeReference.TryGetFromAssemblyDefinition(module, out var fromAssemblyType))
                {
                    return module.ImportReference(fromAssemblyType);
                }

                // Import the type reference, so that it could be used in the module even if it comes from another assembly.
                return module.ImportReference(typeReference);
            }

            // Import the type reference, so that it could be used in the module even if it comes from another assembly.
            var importedType = module.ImportReference(typeReference);
            switch (importedType)
            {
                // Parse the tokens nested in the generic arguments, just like List<Gneedle.Inject.T_0>.
                case GenericInstanceType genericInstanceType:
                    // The element of a generic instance is not one of its generic arguments, so the recursion below does
                    // not reach it. A type which FromAssemblyAttribute marks and stands there, just like Stub<int>, has
                    // to be replaced as well.
                    if (genericInstanceType.ElementType.TryGetFromAssemblyDefinition(module, out var realElement))
                    {
                        var replacement = new GenericInstanceType(module.ImportReference(realElement));
                        foreach (var argument in genericInstanceType.GenericArguments)
                        {
                            replacement.GenericArguments.Add(argument);
                        }

                        genericInstanceType = replacement;
                    }

                    var genericArguments = genericInstanceType.GenericArguments;
                    for (var index = 0; index < genericArguments.Count; index++)
                    {
                        genericArguments[index] = genericArguments[index].ParseGenericTokens(provider, module);
                    }

                    return genericInstanceType;

                // Parse the tokens nested in the return type and the parameters of the signature, just like delegate*<Gneedle.Inject.T_0>.
                // The element type of a function pointer type is its return type, so it is handled here instead of below.
                case FunctionPointerType functionPointerType:
                    functionPointerType.ReturnType = functionPointerType.ReturnType.ParseGenericTokens(provider, module);
                    foreach (var functionPointerParameter in functionPointerType.Parameters)
                    {
                        functionPointerParameter.ParameterType = functionPointerParameter.ParameterType.ParseGenericTokens(provider, module);
                    }

                    return functionPointerType;

                // Parse the tokens nested in the element type, just like Gneedle.Inject.T_0[].
                case ArrayType arrayType:
                    return new ArrayType(arrayType.ElementType.ParseGenericTokens(provider, module), arrayType.Rank);

                case ByReferenceType byReferenceType:
                    return new ByReferenceType(byReferenceType.ElementType.ParseGenericTokens(provider, module));

                case PointerType pointerType:
                    return new PointerType(pointerType.ElementType.ParseGenericTokens(provider, module));

                // The pinned and the sentinel type hold nothing but the element type, so they could be recreated with the parsed element type.
                case PinnedType pinnedType:
                    return new PinnedType(pinnedType.ElementType.ParseGenericTokens(provider, module));

                case SentinelType sentinelType:
                    return new SentinelType(sentinelType.ElementType.ParseGenericTokens(provider, module));

                case OptionalModifierType optionalModifierType:
                    return new OptionalModifierType(
                        optionalModifierType.ModifierType.ParseGenericTokens(provider, module),
                        optionalModifierType.ElementType.ParseGenericTokens(provider, module));

                case RequiredModifierType requiredModifierType:
                    return new RequiredModifierType(
                        requiredModifierType.ModifierType.ParseGenericTokens(provider, module),
                        requiredModifierType.ElementType.ParseGenericTokens(provider, module));

                default:
                    return importedType;
            }
        }

        /// <summary>
        /// Try to get the definition of the real type which <see cref="FromAssemblyAttribute"/> makes the reference stand for.
        /// </summary>
        /// <remarks>
        /// A stub is declared with the same full name as the real type which the assembly named by the attribute
        /// declares, so that a template could reference that type without referencing its assembly.
        /// </remarks>
        /// <param name="module">The module which the real type is looked up in.</param>
        /// <param name="definition">The definition of the real type.</param>
        /// <returns>Whether the reference stands for a type of another assembly.</returns>
        /// <exception cref="ArgumentException">Thrown when the assembly or the type which the attribute names can't be resolved.</exception>
        internal bool TryGetFromAssemblyDefinition(ModuleDefinition module, out TypeDefinition? definition)
        {
            definition = null;

            // A generic parameter is declared by its provider alone, so it could not carry the attribute.
            if (typeReference is GenericParameter) return false;

            // A type which can't be resolved is taken as one which does not stand for another type, because it may well
            // be a type of an assembly which the weaver can't reach, and importing it does not need that resolution.
            TypeDefinition? stub;
            try
            {
                stub = typeReference.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return false;
            }

            if (stub == null) return false;

            var assemblyName = GetFromAssemblyName(stub);
            if (assemblyName == null) return false;

            definition = ResolveTypeFromAssembly(module, assemblyName, stub.FullName);
            return true;
        }

        /// <summary>
        /// Get the definition which the reference points to, which is the one of the real type when the reference
        /// stands for a type of another assembly.
        /// </summary>
        /// <param name="module">The module which the real type is looked up in.</param>
        /// <returns>The definition which the reference points to.</returns>
        /// <exception cref="ArgumentException">Thrown when the assembly or the type which the attribute names can't be resolved.</exception>
        internal TypeDefinition ResolveDefinition(ModuleDefinition module)
            => typeReference.TryGetFromAssemblyDefinition(module, out var definition) ? definition! : typeReference.Resolve();
    }

    /// <param name="methodReference">The method reference which may hold tokens in the types of its declaring type, parameters or return value.</param>
    extension(MethodReference methodReference)
    {
        /// <summary>
        /// Parse the Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20] tokens in the signature of the method reference to the generic parameters of the <c>provider</c>.
        /// </summary>
        /// <remarks>
        /// The method reference is modified in place, so it must be a reference which belongs to the module of the <c>provider</c>.
        /// </remarks>
        /// <param name="provider">GenericParameters provider.</param>
        /// <param name="module">The module which the method reference belongs to.</param>
        /// <returns>The <c>methodReference</c> with all its tokens parsed.</returns>
        /// <exception cref="IndexOutOfRangeException">Throw when the token index out of the <c>provider</c>'s GenericParameters count.</exception>
        internal MethodReference ParseGenericTokens(IMemberDefinition provider, ModuleDefinition module)
        {
            methodReference.DeclaringType = methodReference.DeclaringType.ParseGenericTokens(provider, module);
            methodReference.ReturnType    = methodReference.ReturnType.ParseGenericTokens(provider, module);
            foreach (var parameter in methodReference.Parameters)
            {
                parameter.ParameterType = parameter.ParameterType.ParseGenericTokens(provider, module);
            }

            // Parse the tokens nested in the generic arguments when the method is a generic instance method.
            if (methodReference is GenericInstanceMethod genericInstanceMethod)
            {
                var genericArguments = genericInstanceMethod.GenericArguments;
                for (var index = 0; index < genericArguments.Count; index++)
                {
                    genericArguments[index] = genericArguments[index].ParseGenericTokens(provider, module);
                }
            }

            return methodReference;
        }
    }

    /// <param name="fieldReference">The field reference which may hold tokens in the types of its declaring type or its field.</param>
    extension(FieldReference fieldReference)
    {
        /// <summary>
        /// Parse the Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20] tokens in the signature of the field reference to the generic parameters of the <c>provider</c>.
        /// </summary>
        /// <remarks>
        /// The field reference is modified in place, so it must be a reference which belongs to the module of the <c>provider</c>.
        /// </remarks>
        /// <param name="provider">GenericParameters provider.</param>
        /// <param name="module">The module which the field reference belongs to.</param>
        /// <returns>The <c>fieldReference</c> with all its tokens parsed.</returns>
        /// <exception cref="IndexOutOfRangeException">Throw when the token index out of the <c>provider</c>'s GenericParameters count.</exception>
        internal FieldReference ParseGenericTokens(IMemberDefinition provider, ModuleDefinition module)
        {
            fieldReference.DeclaringType = fieldReference.DeclaringType.ParseGenericTokens(provider, module);
            fieldReference.FieldType     = fieldReference.FieldType.ParseGenericTokens(provider, module);
            return fieldReference;
        }
    }

    /// <param name="genericParameter">Constraint provider.</param>
    extension(GenericParameter genericParameter)
    {
        /// <summary>
        /// Set the generic parameter constraint which from type.
        /// </summary>
        /// <param name="typeDefinition">The type definition which the generic parameter belongs to. It is used for resolving constraint type reference.</param>
        /// <param name="assemblyHandler">Assembly handler for resolving constraint type reference.</param>
        /// <param name="constraint">Constraint witch from type.</param>
        internal void SetConstraintFromType(AssemblyHandler assemblyHandler, TypeDefinition typeDefinition, Constraint constraint)
        {
            if (constraint.Type == null) return;

            // Process parameter constraint.
            TypeReference constraintType;
            if (constraint.Type is SelfType selfType)
            {
                // If self type is generic type, then we make generic instance type.
                if (selfType.GenericArguments.Length > 0)
                {
                    var genericArguments = selfType.GenericArguments;
                    var resolvedArguments = new TypeReference[genericArguments.Length];
                    // Resolve every argument.
                    for (var index = 0; index < genericArguments.Length; index++)
                    {
                        resolvedArguments[index] = assemblyHandler.ResolveParameterType(typeDefinition, genericArguments[index]);
                    }

                    constraintType = typeDefinition.MakeGenericInstanceType(resolvedArguments);
                }
                // Or we just constrain to itself.
                else constraintType = typeDefinition;
            }
            // Resolve constraint type. ResolveParameterType returns a reference which is owned by the target module
            // already, so it can be appended as it is.
            else constraintType = assemblyHandler.ResolveParameterType(typeDefinition, constraint.Type);

            // Append to constraint collections.
            genericParameter.Constraints.Add(new GenericParameterConstraint(constraintType));
        }
    }
}