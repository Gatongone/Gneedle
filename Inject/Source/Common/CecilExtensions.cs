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
        internal bool SameWith(IReadOnlyList<TypeReference> targetTypes)
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

    /// <param name="methodDef">Method definition.</param>
    extension(MethodDefinition methodDef)
    {
        /// <summary>
        /// Check whether the method is the one which the given signature describes, and read the arguments which the
        /// generic parameters it declares stand for out of that description.<para/>
        /// A method which declares a parameter of its own is one which no signature of named types describes, because
        /// the type of that parameter is the name of the method rather than the name of any type. The description is
        /// the signature which a caller hands the method, as the delegate of a template describes the member which it
        /// names, so such a parameter is bound to the type which stands where it stands rather than compared with it:
        /// the types of the arguments name the parameters which stand in the places of those arguments, and the type
        /// of the value which the method hands back names the ones which stand nowhere among them, which is the shape a
        /// member of the parameters of the caller has, just like <c>TOut Make&lt;TIn, TOut&gt;(TIn value)</c>.
        /// Every parameter of the method has to be described for the method to be named at all, because a call of a
        /// method which stands open is one which the runtime refuses to run.
        /// </summary>
        /// <param name="parameterTypes">The types of the arguments which the method is called with.</param>
        /// <param name="returnType">
        /// The type of the value which the method hands back, or null when the caller holds none - a caller which holds
        /// none describes the method with the type of nothing, just like the delegate of a member which hands nothing
        /// back. It names the parameters which stand in no position of the arguments, which is the only place left for
        /// one of them to be named at.
        /// </param>
        /// <param name="instance">
        /// The instantiation of the type which declares the method which the method is reached through, or null where it
        /// is reached through none: the signature of a member of a generic type is written where that type is declared,
        /// so the parameter which stands in it is the one the instantiation holds an argument for rather than a type
        /// which names an assembly. <c>int</c> is what the <c>T</c> of <c>GenericHelper&lt;int&gt;</c> is.
        /// </param>
        /// <param name="arguments">
        /// The types which the generic parameters of the method stand for, in the order they are declared, or null when
        /// the signature does not describe the method.
        /// </param>
        /// <returns>Whether the signature describes the method.</returns>
        internal bool SameWith(IReadOnlyList<TypeReference> parameterTypes, TypeReference? returnType, out IReadOnlyList<TypeReference>? arguments, TypeReference? instance = null)
        {
            var declaration = methodDef.DeclaringType;

            arguments = null;

            // A method which hands nothing back hands back no value, so a caller which describes one with the type of
            // nothing describes no value at all: a delegate which stands for a member that hands nothing back, just like
            // `Action<T>`, hands back `System.Void`, and a parameter of the member which that type named is one the call
            // would leave standing open - and the type of nothing is no argument of an instantiation at all, so the
            // assembly which named it is one the runtime refuses to load. The type of nothing describes nothing,
            // wherever the caller wrote it down.
            if (returnType is { MetadataType: MetadataType.Void })
            {
                returnType = null;
            }

            // A method which declares no parameter of its own names every type of its signature, so it is described by
            // the signature which holds those very names, and the call names the method itself.
            if (methodDef.GenericParameters.Count == 0)
            {
                return methodDef.DescribedBy(parameterTypes, instance);
            }

            if (methodDef.Parameters.Count != parameterTypes.Count)
            {
                return false;
            }

            var bound = new TypeReference?[methodDef.GenericParameters.Count];
            for (var index = 0; index < methodDef.Parameters.Count; index++)
            {
                if (!Bind(methodDef, methodDef.Parameters[index].ParameterType.WithTheArgumentsOf(declaration, instance), parameterTypes[index], bound))
                {
                    return false;
                }
            }

            // The type which the method hands back names a parameter of the method as well, because it is what tells one
            // instantiation of a method which declares parameters of its own from another: a parameter which no argument
            // of the call stands in the place of is one which the signature names by the value the method hands back,
            // where the caller wrote that value down, and the call of such a member is written with both arguments.
            if (returnType != null && !Bind(methodDef, methodDef.ReturnType.WithTheArgumentsOf(declaration, instance), returnType, bound))
            {
                return false;
            }

            // A parameter which no position of the signature binds is one which a call cannot name the argument of.
            var described = new TypeReference[bound.Length];
            for (var index = 0; index < bound.Length; index++)
            {
                if (bound[index] is not { } argument)
                {
                    return false;
                }

                described[index] = argument;
            }

            arguments = described;
            return true;
        }
    }

    extension(MethodDefinition methodDef)
    {
        /// <summary>
        /// Check whether the parameters of the method, read through the instance which it was reached through, are the
        /// types which the given signature names.<para/>
        /// A method which declares no parameter of its own names every type of its signature, and the signature of a
        /// member of a generic type is written where that type is declared: the parameter which stands in it is the one
        /// the instantiation of the type holds an argument for, so <c>int</c> is what the <c>T</c> of the parameter of
        /// <c>Echo</c> on <c>GenericHelper&lt;int&gt;</c> is.
        /// </summary>
        /// <param name="parameterTypes">The types of the arguments which the method is called with.</param>
        /// <param name="instance">The instantiation of the type which declares the method, or null where it was reached through none.</param>
        /// <returns>Whether the parameters describe the method.</returns>
        internal bool DescribedBy(IReadOnlyList<TypeReference> parameterTypes, TypeReference? instance)
            => methodDef.Parameters.Count == parameterTypes.Count
                && !methodDef.Parameters.Where((parameter, index) =>
                       !TypeName.HasSameName(parameter.ParameterType.WithTheArgumentsOf(methodDef.DeclaringType, instance),
                                             parameterTypes[index])).Any();
    }

    /// <summary>
    /// Bind the parameters which <paramref name="methodDef"/> declares in <paramref name="described"/> to the types
    /// which stand at the same positions of <paramref name="describing"/>, and tell whether the two describe the same
    /// type.
    /// </summary>
    /// <param name="methodDef">The method whose parameters are bound.</param>
    /// <param name="described">The type which holds the parameters, which is the one the method declares.</param>
    /// <param name="describing">The type which describes it, which is the one the caller wrote.</param>
    /// <param name="bound">The arguments which the parameters stand for so far, by position.</param>
    /// <returns>Whether <paramref name="describing"/> describes <paramref name="described"/>.</returns>
    private static bool Bind(MethodDefinition methodDef, TypeReference described, TypeReference describing, TypeReference?[] bound)
    {
        // A parameter of the method itself stands for whatever the signature holds in its place, and a parameter which
        // two positions bind is one which only the same type describes both times: a position which is reached again
        // tells whether it stands for the type which stands there, and a parameter which stands unbound is one which
        // this position names.
        if (described is GenericParameter parameter && ReferenceEquals(parameter.Owner, methodDef))
        {
            if (bound[parameter.Position] is { } boundArgument)
            {
                return TypeName.HasSameName(boundArgument, describing);
            }

            bound[parameter.Position] = describing;
            return true;
        }

        // A generic instance is described by an instance of the same type, and the arguments of it describe the
        // parameters which stand in the arguments of the definition: List<int> is what List<T> is described by.
        if (described is GenericInstanceType describedInstance && describing is GenericInstanceType describingInstance)
        {
            if (!TypeName.HasSameName(describedInstance.ElementType, describingInstance.ElementType)
                || describedInstance.GenericArguments.Count != describingInstance.GenericArguments.Count)
            {
                return false;
            }

            for (var index = 0; index < describedInstance.GenericArguments.Count; index++)
            {
                if (!Bind(methodDef, describedInstance.GenericArguments[index], describingInstance.GenericArguments[index], bound))
                {
                    return false;
                }
            }

            return true;
        }

        // A type which wraps another is described by one of the same kind, and the element is what describes the
        // parameter nested in it: int[] is what T[] is described by, and int[,] is not, because the rank belongs to
        // the array rather than to the element which stands in it.
        if (described is TypeSpecification describedSpecification && describing is TypeSpecification describingSpecification
            && describedSpecification.GetType() == describingSpecification.GetType())
        {
            // The two elements are one another's description whatever the rank of the arrays which hold them, so the
            // rank is read here: a signature which describes an element of an array of one rank describes the element
            // of the member which stands in an array of any rank as well, where the signature is a type of its own.
            if (describedSpecification is ArrayType describedArray && describingSpecification is ArrayType describingArray
                && describedArray.Rank != describingArray.Rank)
            {
                return false;
            }

            return Bind(methodDef, describedSpecification.ElementType, describingSpecification.ElementType, bound);
        }

        return TypeName.HasSameName(described, describing);
    }

    /// <param name="attributeDefinition">Type definition of attribute.</param>
    extension(TypeDefinition attributeDefinition)
    {
        /// <summary>
        /// Create a custom attribute from type definition and constructor arguments.
        /// </summary>
        /// <param name="module">The provider of importing argument type references.</param>
        /// <param name="arguments">Arguments of the attribute constructor calling.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <c>arguments</c> is null, which is a caller who left the arguments out rather than one who gave
        /// none of them.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when one of the arguments is null, which names no constructor, or when the attribute doesn't contain
        /// any constructor which has argument types match with <c>arguments</c>.
        /// </exception>
        /// <returns>
        /// The custom attribute from <c>attributeDefinition</c> with parameters.
        /// </returns>
        internal CustomAttribute CreateCustomAttribute(ModuleDefinition module, params object[] arguments)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));

            // Get argument types. The type of an argument is what the constructor is looked up by, so a null names no
            // constructor at all and is refused where it stands rather than read as a type of its own.
            var argTypes = new Type[arguments.Length];
            for (var index = 0; index < arguments.Length; index++)
            {
                if (arguments[index] == null)
                {
                    throw new ArgumentException(string.Format(ErrorMessages.NULL_ATTRIBUTE_ARGUMENT, attributeDefinition.FullName, index));
                }

                argTypes[index] = arguments[index].GetType();
            }

            // Get the constructor matches with argTypes.
            var method = attributeDefinition.Methods.FirstOrDefault(method => method.IsConstructor && method.Parameters.SameWith(argTypes));
            if (method == null) throw new ArgumentException(string.Format(ErrorMessages.INVALID_PARAMETERS, attributeDefinition.FullName));

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
    }

    /// <param name="ins">Opcode provider.</param>
    extension(Instruction ins)
    {
        /// <summary>
        /// Get the slot of the argument which a load of one names.
        /// </summary>
        /// <remarks>
        /// The slot is the position of the argument rather than that of the parameter, so the receiver of a method which
        /// belongs to an instance takes the first one and the first parameter of such a method follows it. The macro
        /// opcodes hold that slot in the opcode, while the long ones hold it as an operand, which Cecil resolves to the
        /// parameter itself wherever the body still holds the method the operand belongs to, and leaves as the slot
        /// wherever it does not. A parameter is named by its position among the parameters of its method, which is the
        /// slot only when the method belongs to no instance, so the receiver is added back where there is one.
        /// </remarks>
        /// <param name="hasReceiver">Whether the method which the instruction belongs to holds a receiver, which takes
        /// the first slot, ahead of the first parameter of it.</param>
        /// <param name="index">Slot of the ldarg target. It would be -1 when return false.</param>
        /// <returns>False when the instruction opcode is not a ldarg type.</returns>
        internal bool TryGetLdargIndex(bool hasReceiver, out int index)
        {
            index = ins.OpCode.Code switch
            {
                Code.Ldarg_0 => 0,
                Code.Ldarg_1 => 1,
                Code.Ldarg_2 => 2,
                Code.Ldarg_3 => 3,
                Code.Ldarg or Code.Ldarg_S => ins.Operand switch
                {
                    int slot                     => slot,
                    ParameterReference parameter => parameter.Index + (hasReceiver ? 1 : 0),
                    _                            => -1
                },
                _ => -1
            };
            return index != -1;
        }

        /// <summary>
        /// Get the slot of the local which a store names.
        /// </summary>
        /// <remarks>
        /// The slot is the position of the local among the variables of the body. The macro opcodes hold that slot in the
        /// opcode, while the long ones hold it as an operand, which Cecil resolves to the variable itself wherever the
        /// body still holds the method the operand belongs to, and leaves as the slot wherever it does not.
        /// </remarks>
        /// <param name="index">Index of the stloc target. It would be -1 when return false.</param>
        /// <returns>False when the instruction opcode is not a stloc type, or when its operand names no local.</returns>
        internal bool TryGetStlocIndex(out int index)
        {
            index = ins.OpCode.Code switch
            {
                Code.Stloc_0 => 0,
                Code.Stloc_1 => 1,
                Code.Stloc_2 => 2,
                Code.Stloc_3 => 3,
                Code.Stloc or Code.Stloc_S => ins.Operand switch
                {
                    int slot                   => slot,
                    VariableReference variable => variable.Index,
                    _                          => -1
                },
                _ => -1
            };
            return index != -1;
        }

        /// <summary>
        /// Get the slot of the local which a load names.
        /// </summary>
        /// <remarks>
        /// The operand is read the way the one of a store is, which is described there. Both forms hand back the slot
        /// rather than the variable, because a slot is what the caller addresses a local by.
        /// </remarks>
        /// <param name="index">Index of the ldloc target. It would be -1 when return false.</param>
        /// <returns>False when the instruction opcode is not a ldloc type, or when its operand names no local.</returns>
        internal bool TryGetLdlocIndex(out int index)
        {
            index = ins.OpCode.Code switch
            {
                Code.Ldloc_0 => 0,
                Code.Ldloc_1 => 1,
                Code.Ldloc_2 => 2,
                Code.Ldloc_3 => 3,
                Code.Ldloc or Code.Ldloc_S => ins.Operand switch
                {
                    int slot                   => slot,
                    VariableReference variable => variable.Index,
                    _                          => -1
                },
                _ => -1
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
        => new($@"^{nameof(Gneedle)}\.{nameof(Inject)}\.{token}_({string.Join("|", Enumerable.Range(0, GenericTokens.HighestIndex + 1))})$");

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
            TypeDefinition typeDefinition     => typeDefinition,
            MethodDefinition methodDefinition => methodDefinition.DeclaringType,
            _                                 => null
        };
        if (typeDef == null) return false;
        if (index > typeDef.GenericParameters.Count - 1)
        {
            throw new ArgumentException(string.Format(ErrorMessages.GENERIC_PARAMETER_OUT_OF_RANGE, $"'T_{index}'", typeDef.FullName));
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
        /// <exception cref="ArgumentException">Thrown when the <c>typeReference</c> names a generic parameter at a position which the <c>provider</c> does not declare.</exception>
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
        /// <exception cref="ArgumentException">Thrown when the <c>typeReference</c> names a generic parameter at a position which the <c>provider</c> does not declare.</exception>
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
        /// The type which a reference of a declaration stands for where that declaration is instantiated: every generic
        /// parameter of the declaration which stands in the reference is replaced by the argument of the instantiation
        /// at the same position.
        /// </summary>
        /// <remarks>
        /// A base type is written where the type which declares it stands, so the base of a generic declaration names
        /// the parameters of that declaration rather than the arguments of the instance which a body reaches: the base
        /// of <c>Middle&lt;T&gt;</c> is <c>Base&lt;T&gt;</c> where the base of <c>Middle&lt;int&gt;</c> is <c>Base&lt;int&gt;</c>,
        /// and only the arguments of the instantiation tell the two apart.
        /// </remarks>
        /// <param name="declaration">The type which declares the reference, whose parameters the arguments replace.</param>
        /// <param name="instance">The instantiation which the declaration was reached through, which holds the arguments.</param>
        /// <returns>The reference with the arguments of the instantiation in place of the parameters of the declaration. A parameter whose owner is not the declaration stands where it did.</returns>
        internal TypeReference WithTheArgumentsOf(TypeDefinition declaration, TypeReference instance)
        {
            var parameters = declaration.GenericParameters;

            // A declaration which names no parameter passes nothing down, and neither does an instance which holds no
            // argument for each of them: the reference stands as it is.
            if (parameters.Count == 0 || instance is not GenericInstanceType instantiation
                || instantiation.GenericArguments.Count != parameters.Count)
            {
                return typeReference;
            }

            return Replace(typeReference);

            TypeReference Replace(TypeReference type)
            {
                switch (type)
                {
                    // A parameter of another type, such as one which the base of a nested type names of the type it is
                    // nested in, is left standing: the caller refuses an instantiation which still holds one.
                    case GenericParameter parameter
                        when parameter.Owner is TypeReference owner && owner.FullName == declaration.FullName:
                        return instantiation.GenericArguments[parameter.Position];

                    // An argument is a type of its own, which may hold a parameter as well, just like Base<List<T>>.
                    case GenericInstanceType genericInstance:
                        var replacement = new GenericInstanceType(genericInstance.ElementType);
                        foreach (var argument in genericInstance.GenericArguments)
                        {
                            replacement.GenericArguments.Add(Replace(argument));
                        }

                        return replacement;

                    // And so is the element of an array, just like Base<T[]>.
                    case ArrayType arrayType:
                        return new ArrayType(Replace(arrayType.ElementType), arrayType.Rank);

                    default:
                        return type;
                }
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
        /// <exception cref="ArgumentException">Thrown when a token of the reference names a generic parameter at a position which the <c>provider</c> does not declare.</exception>
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
        /// <exception cref="ArgumentException">Thrown when a token of the reference names a generic parameter at a position which the <c>provider</c> does not declare.</exception>
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