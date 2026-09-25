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
        => GetFromAssemblyAttribute(definition)?.ConstructorArguments[0].Value as string;

    /// <summary>
    /// Get the full name of the real type which the attribute of <paramref name="definition"/> names, which is the one
    /// it carries where it carries it, and the name of the stub itself where it names none: a stub is what it stands
    /// for by its own name where nothing else says what that is.
    /// </summary>
    /// <param name="definition">The type definition which could carry the attribute.</param>
    /// <returns>The full name of the real type.</returns>
    private static string GetFromAssemblyTypeName(TypeDefinition definition)
    {
        var named = GetFromAssemblyAttribute(definition)?.ConstructorArguments;
        if (named is not { Count: > 1 } || named[1].Value is not string typeFullName) return definition.FullName;

        // A type which declares generic parameters is named by the number of them wherever it is named, and a stub
        // declares the ones of the type it stands for: what the name is written without is that number, which is read
        // off the stub rather than written a second time.
        if (definition.GenericParameters.Count == 0) return typeFullName;

        var arity = "`" + definition.GenericParameters.Count;
        var last = typeFullName.LastIndexOf('/') + 1;
        return typeFullName.Substring(0, last) + typeFullName.Substring(last).Split('`')[0] + arity;
    }

    /// <summary>
    /// The attribute of <paramref name="definition"/> which stands for a type of another assembly, or null where it
    /// carries none.
    /// </summary>
    /// <param name="definition">The type definition which could carry the attribute.</param>
    /// <returns>The attribute, or null.</returns>
    private static CustomAttribute? GetFromAssemblyAttribute(TypeDefinition definition)
    {
        foreach (var attribute in definition.CustomAttributes)
        {
            if (attribute.AttributeType.FullName == FromAssemblyAttribute.TYPE_NAME && attribute.ConstructorArguments.Count > 0)
            {
                return attribute;
            }
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
    /// <exception cref="WeavingException">Thrown when the assembly or the type can't be resolved.</exception>
    internal static TypeDefinition ResolveTypeFromAssembly(ModuleDefinition module, string assemblyName, string typeFullName)
    {
        // The target assembly does not reference itself, so it is looked up in the module itself.
        if (IsSameAssembly(module.Assembly.Name, assemblyName))
        {
            return FindType(module, typeFullName)
                ?? throw new WeavingException(string.Format(ErrorMessages.INVALID_FROM_ASSEMBLY_TYPE, typeFullName, assemblyName));
        }

        // The target may not reference the assembly yet, so a reference is made up when none matches and the resolver is
        // given the chance to find it. The reference is appended by the import of the resolved type which follows.
        var reference = module.AssemblyReferences.FirstOrDefault(reference => IsSameAssembly(reference, assemblyName))
                     ?? new AssemblyNameReference(assemblyName, new Version(0, 0, 0, 0));

        AssemblyDefinition assembly;
        try
        {
            assembly = module.AssemblyResolver?.Resolve(reference)
                ?? throw new WeavingException(string.Format(ErrorMessages.INVALID_FROM_ASSEMBLY_ASSEMBLY, assemblyName));
        }
        catch (AssemblyResolutionException)
        {
            throw new WeavingException(string.Format(ErrorMessages.INVALID_FROM_ASSEMBLY_ASSEMBLY, assemblyName));
        }

        // A cycle reference cannot be represented in metadata, which is rejected as well when a reference is appended.
        if (assembly.MainModule.AssemblyReferences.Any(name => name.FullName.Equals(module.Assembly.FullName)))
        {
            throw new WeavingException(string.Format(ErrorMessages.ASSEMBLY_CYCLE_REFERENCE, module.Assembly.FullName, assembly.FullName));
        }

        return FindType(assembly.MainModule, typeFullName)
            ?? throw new WeavingException(string.Format(ErrorMessages.INVALID_FROM_ASSEMBLY_TYPE, typeFullName, assemblyName));
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
        /// <exception cref="WeavingException">Thrown when the assembly or the type which the attribute names can't be resolved.</exception>
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

            definition = ResolveTypeFromAssembly(module, assemblyName, GetFromAssemblyTypeName(stub));
            return true;
        }

        /// <summary>
        /// Get the definition which the reference points to, which is the one of the real type when the reference
        /// stands for a type of another assembly.<para/>
        /// Where the reference is a parameter of a type, what is answered with is the definition of the constraint which
        /// that parameter holds: a value of a parameter is a value of the type which the parameter is constrained to,
        /// and no member is declared by the parameter itself. What a member of such a value is looked up on is therefore
        /// the constraint, which is what tells a template which reaches a member through a token of a parameter.
        /// </summary>
        /// <param name="module">The module which the real type is looked up in.</param>
        /// <returns>The definition which the reference points to.</returns>
        /// <exception cref="WeavingException">Thrown when the assembly or the type which the attribute names can't be resolved.</exception>
        internal TypeDefinition ResolveDefinition(ModuleDefinition module)
        {
            var type = typeReference is GenericParameter parameter ? TheConstraintOf(parameter) : typeReference;
            return type.TryGetFromAssemblyDefinition(module, out var definition) ? definition! : type.Resolve();
        }

        /// <summary>
        /// The type which a value of a parameter of a type is a value of, which is the first of the constraints the
        /// parameter holds.<para/>
        /// The first of them is the one which a value of the parameter is read through: the language writes the class
        /// which a parameter is of ahead of the interfaces of it, and a parameter which holds interfaces alone is one
        /// whose members are the ones of those interfaces - which is what a template of it may reach without a cast, and
        /// so what a member which it names is looked for among.
        /// </summary>
        /// <param name="parameter">The parameter whose constraint is read.</param>
        /// <returns>The reference of the constraint, or of the type which everything is, where the parameter holds none.</returns>
        private static TypeReference TheConstraintOf(GenericParameter parameter)
            => parameter.Constraints.FirstOrDefault()?.ConstraintType ?? parameter.Module.TypeSystem.Object;
    }
}