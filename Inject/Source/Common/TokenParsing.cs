using System.Text.RegularExpressions;

namespace Gneedle.Inject;

/// <summary>
/// The tokens which name a generic parameter of the type or of the member which a template is woven into, and the
/// parsing of them: a token stands for a parameter by its position, read against the declaration which the body
/// is a member of.
/// </summary>
internal static class TokenParsing
{
    /// <summary>
    /// The pattern of the <c>Gneedle.Inject.T_[0-20]</c> token, which stands for the generic parameter of the type.
    /// </summary>
    /// <remarks>
    /// The token must be the whole type name. Otherwise a type which merely contains a token as its generic argument,
    /// just like <c>List&lt;Gneedle.Inject.T_0&gt;</c>, would be taken as the token itself.
    /// </remarks>
    private static readonly Regex s_GenericTypeNamePattern = BuildTokenPattern("T");

    /// <inheritdoc cref="s_GenericTypeNamePattern"/>
    private static readonly Regex s_GenericMethodNamePattern = BuildTokenPattern("M");

    /// <summary>
    /// Build the pattern which the token of a kind is read by, which is the name of the token and the number of it.<para/>
    /// The numbers which the pattern reads are the ones which this library declares a token for, so the bound of them is
    /// the one which the tokens are declared with rather than a second one written out here: a token which is declared
    /// beyond it would be read as a type of this library rather than as the generic parameter it stands for.
    /// </summary>
    /// <param name="token">The letter which tells the token of the type from the token of the method.</param>
    /// <returns>The pattern of the token.</returns>
    private static Regex BuildTokenPattern(string token)
        => new($@"^{nameof(Gneedle)}\.{nameof(Inject)}\.{token}_({string.Join("|", Enumerable.Range(0, GenericTokens.HIGHEST_INDEX + 1))})$");

    /// <summary>
    /// Try to parse the index which the Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20] token of <paramref name="typeName"/> holds.
    /// </summary>
    /// <param name="typeName">The full name of the type which could be a token.</param>
    /// <param name="index">The index held by the token.</param>
    /// <param name="isFromMethod">Whether the token stands for the generic parameter of the method rather than of the declaring type.</param>
    /// <returns>Whether the <paramref name="typeName"/> is a token.</returns>
    private static bool TryGetParsedTokenIndex(string typeName, out int index, out bool isFromMethod)
    {
        index = 0;
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
    /// <exception cref="ArgumentException">Thrown when the token names a generic parameter at a position which neither the type being woven nor the method declares.</exception>
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
                throw new ArgumentException(string.Format(ErrorMessages.GENERIC_PARAMETER_OUT_OF_RANGE, $"'M_{index}'", type.FullName));
            }

            parameter = parameters[index];
            return true;
        }

        // The token stands for the generic parameter of the declaring type.
        var typeDef = typeProvider switch
        {
            TypeDefinition typeDefinition => typeDefinition,
            MethodDefinition methodDefinition => methodDefinition.DeclaringType,
            _ => null
        };
        if (typeDef == null) return false;
        if (index > typeDef.GenericParameters.Count - 1)
        {
            throw new ArgumentException(string.Format(ErrorMessages.GENERIC_PARAMETER_OUT_OF_RANGE, $"'T_{index}'", typeDef.FullName));
        }

        parameter = typeDef.GenericParameters[index];
        return true;
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
        /// <exception cref="ArgumentException">Thrown when the <c>typeReference</c> names a generic parameter at a position which the <c>provider</c> does not declare.</exception>
        internal bool TryGetParsedGenericParameter(IMemberDefinition provider, out GenericParameter? parameter)
            =>
                typeReference.TryGetParsedGenericParameter(provider, out parameter, out _, out _);

        /// <summary>
        /// Try to parse Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20] to GenericParameter.
        /// </summary>
        /// <param name="provider">GenericParameters provider.</param>
        /// <param name="parameter">The generic parameter from type or method definition.</param>
        /// <param name="index">The generic parameter index.</param>
        /// <param name="isFromMethod">Is the generic type from the method or from the method's declaring type.</param>
        /// <returns>Whether the <c>typeReference</c> could passer as GenericParameter.</returns>
        /// <exception cref="ArgumentException">Thrown when the <c>typeReference</c> names a generic parameter at a position which the <c>provider</c> does not declare.</exception>
        internal bool TryGetParsedGenericParameter(IMemberDefinition provider, out GenericParameter? parameter, out int index, out bool isFromMethod)
        {
            parameter = null;
            if (!TryGetParsedTokenIndex(typeReference.FullName, out index, out isFromMethod)) return false;

            // Match type.
            if (!isFromMethod)
            {
                var typeDef = provider switch
                {
                    TypeDefinition typeDefinition => typeDefinition,
                    MethodDefinition methodDefinition => methodDefinition.DeclaringType,
                    _ => null
                };
                if (typeDef == null) return false;

                var typeParameters = typeDef.GenericParameters;
                if (index > typeParameters.Count - 1)
                {
                    throw new ArgumentException(string.Format(ErrorMessages.GENERIC_PARAMETER_OUT_OF_RANGE, $"'T_{index}'", typeDef.FullName));
                }

                parameter = typeParameters[index];
                return true;
            }

            // Match method.
            if (provider is not MethodDefinition methodDef) return false;
            var methodParameters = methodDef.GenericParameters;
            if (index > methodParameters.Count - 1)
            {
                throw new ArgumentException(string.Format(ErrorMessages.GENERIC_PARAMETER_OUT_OF_RANGE, $"'M_{index}'", methodDef.FullName));
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
        /// <exception cref="ArgumentException">Thrown when a token of the type names a generic parameter at a position which the <c>provider</c> does not declare.</exception>
        internal TypeReference ParseGenericTokens(IMemberDefinition provider, ModuleDefinition module)
        {
            // A type specification wraps another type, and the FullName of a wrapper which holds no affix
            // (just like `pinned T` or `modreq(InAttribute) T`) is the FullName of the wrapped type itself.
            // So the wrapped type is parsed below, otherwise the token of it would be taken as the token of the whole type.
            if (typeReference is not TypeSpecification)
            {
                // The token stands for the generic parameter of the provider itself. It is not a type of any module, so it doesn't need to be imported.
                if (typeReference.TryGetParsedGenericParameter(provider, out var parameter)) return parameter!;

                return typeReference.TryGetFromAssemblyDefinition(module, out var fromAssemblyType)
                    // A type which FromAssemblyAttribute marks stands for the real type of the same name which another
                    // assembly declares, so the real type replaces it. The reference ownership is not a criterion here: the
                    // reference of a member operand was imported to the module before it is parsed, even though the type it
                    // denotes is the stub of the template assembly.
                    ? module.ImportReference(fromAssemblyType) :
                    // Import the type reference, so that it could be used in the module even if it comes from another assembly.
                    module.ImportReference(typeReference);
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
        /// <exception cref="ArgumentException">Thrown when a token of the reference names a generic parameter at a position which the <c>provider</c> does not declare.</exception>
        internal MethodReference ParseGenericTokens(IMemberDefinition provider, ModuleDefinition module)
        {
            // The declaring type of an instantiation of a method belongs to the method which it instantiates rather than
            // to the reference, which holds that method alone: reading the declaring type of a specification is what
            // finds the method, and writing it is refused, so a call which names a method through an instantiation of it
            // was refused before the rest of the reference was read. Both the reading and the writing go through the
            // method which the specification instantiates, and what is written back is what was read, so a reference
            // whose declaring type holds no token of the provider is left as it was.
            var declared = methodReference is MethodSpecification specification ? specification.ElementMethod : methodReference;
            declared.DeclaringType = declared.DeclaringType.ParseGenericTokens(provider, module);
            methodReference.ReturnType = methodReference.ReturnType.ParseGenericTokens(provider, module);
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
        /// <exception cref="ArgumentException">Thrown when a token of the reference names a generic parameter at a position which the <c>provider</c> does not declare.</exception>
        internal FieldReference ParseGenericTokens(IMemberDefinition provider, ModuleDefinition module)
        {
            fieldReference.DeclaringType = fieldReference.DeclaringType.ParseGenericTokens(provider, module);
            fieldReference.FieldType = fieldReference.FieldType.ParseGenericTokens(provider, module);
            return fieldReference;
        }
    }
}