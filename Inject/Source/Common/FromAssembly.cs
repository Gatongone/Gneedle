namespace Gneedle.Inject;

/// <summary>
/// The real type which a reference of a stub stands for, which is the one of the assembly that the
/// <c>FromAssemblyAttribute</c> of the stub names.
/// </summary>
internal static class FromAssembly
{
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

    /// <param name="typeReference">The type reference which may stand for the type of another assembly, which the stub of it is declared for.</param>
    extension(TypeReference typeReference)
    {
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
}