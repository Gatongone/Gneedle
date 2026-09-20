namespace Gneedle.Inject;

/// <summary>
/// The constraint which a <c>Constraint</c> of the model sets on a generic parameter, and the kinds which such a
/// constraint names.
/// </summary>
internal static class Constraints
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