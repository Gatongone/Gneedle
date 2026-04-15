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

            // Create custom attribute and append arguments.
            var attribute = new CustomAttribute(method);
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
            index        = 0;
            parameter    = default;
            isFromMethod = false;

            const string genericTypeNamePattern = @$"{nameof(Gneedle)}\.{nameof(Inject)}\.T_(1[1-9]|20|[0-9])";
            const string genericMethodNamePattern = @$"{nameof(Gneedle)}\.{nameof(Inject)}\.M_(1[1-9]|20|[0-9])";
            var typeName = typeReference.FullName;
            var matcher = Regex.Match(typeName, genericTypeNamePattern);

            // Match type.
            if (matcher.Success && int.TryParse(matcher.Groups[1].Value, out index))
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

                parameter    = typeParameters[index];
                isFromMethod = false;
                return true;
            }

            // Match method.
            matcher = Regex.Match(typeName, genericMethodNamePattern);
            if (!matcher.Success || !int.TryParse(matcher.Groups[1].Value, out index)) return false;
            if (provider is not MethodDefinition methodDef) return false;
            var methodParameters = methodDef.GenericParameters;
            if (index > methodParameters.Count - 1)
            {
                throw new IndexOutOfRangeException($"'M_{index}' index out of Generic parameters count from {methodDef.FullName}");
            }

            parameter    = methodParameters[index];
            isFromMethod = true;
            return true;
        }
    }
}