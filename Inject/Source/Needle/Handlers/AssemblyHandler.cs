namespace Gneedle.Inject;

/// <summary>
/// Assembly injector.
/// </summary>
internal sealed partial class AssemblyHandler : IAssemblyHandler
{
    /// <summary>
    /// Handling target assembly.
    /// </summary>
    public readonly Assembly Assembly;

    /// <summary>
    /// The assembly has referenced.
    /// </summary>
    private readonly Dictionary<string, AssemblyDefinition> m_AssemblyCache;

    /// <summary>
    /// The cecil types has imported.
    /// </summary>
    private readonly Dictionary<string, CecilType> m_TypeCache = new();

    /// <param name="assembly">Handled target assembly.</param>
    internal AssemblyHandler(Assembly assembly)
    {
        Assembly = assembly;

        // The cache is the one of the resolver of the module, because resolution goes through that resolver and it only
        // finds the assemblies which the cache holds. It is created with the module, which is why it is taken from there
        // rather than made here, and it is seeded with the target assembly before anything is resolved.
        m_AssemblyCache = (assembly.Source.MainModule.AssemblyResolver as CachedAssemblyResolver)?.Assemblies
                       ?? new Dictionary<string, AssemblyDefinition>();
        m_AssemblyCache[assembly.Source.FullName] = assembly.Source;

        AddDefaultTypes();
    }

    /// <inheritdoc cref="AssemblyHandler.AddReference(AssemblyDefinition)"/>
    public void AddReference(Assembly targetAssembly) => AddReference(targetAssembly.Source);

    /// <summary>
    /// Append assembly reference to current assembly.
    /// </summary>
    /// <param name="targetAssembly">The target assembly need to be appended.</param>
    /// <exception cref="ArgumentException">Thrown when there is assembly cycle referenced (target assembly referenced current assembly).</exception>
    internal void AddReference(AssemblyDefinition targetAssembly)
    {
        // Check repeat.
        VerifyReferenceCycle(targetAssembly);

        // Append target reference to collections.
        var references = Assembly.Source.MainModule.AssemblyReferences;
        if (!references.Any(nameRef => nameRef.FullName.Equals(targetAssembly.FullName)))
        {
            references.Add(targetAssembly.Name);
        }
    }

    /// <summary>
    /// Verify that referencing <paramref name="targetAssembly"/> does not make the assemblies reference each other,
    /// which cannot be represented in metadata.
    /// </summary>
    /// <param name="targetAssembly">The assembly need to be referenced.</param>
    /// <exception cref="ArgumentException">Thrown when the target assembly references current assembly.</exception>
    private void VerifyReferenceCycle(AssemblyDefinition targetAssembly)
    {
        if (targetAssembly.MainModule.AssemblyReferences.Any(nameRef => nameRef.FullName.Equals(Assembly.Source.FullName)))
        {
            throw new ArgumentException(string.Format(ErrorMessages.ASSEMBLY_CYCLE_REFERENCE, Assembly.Source.FullName, targetAssembly.FullName));
        }
    }

    /// <summary>
    /// Add default types to type cache, which are commonly used in IL code, and we can directly get them from the module type system.
    /// </summary>
    private void AddDefaultTypes()
    {
        var module = Assembly.Source.MainModule;
        AddType(module.TypeSystem.Boolean);
        AddType(module.TypeSystem.Int16);
        AddType(module.TypeSystem.Int32);
        AddType(module.TypeSystem.Int64);
        AddType(module.TypeSystem.UInt16);
        AddType(module.TypeSystem.UInt32);
        AddType(module.TypeSystem.UInt64);
        AddType(module.TypeSystem.Byte);
        AddType(module.TypeSystem.SByte);
        AddType(module.TypeSystem.Char);
        AddType(module.TypeSystem.Double);
        AddType(module.TypeSystem.Single);
        AddType(module.TypeSystem.String);
        AddType(module.TypeSystem.IntPtr);
        AddType(module.TypeSystem.UIntPtr);
        AddType(module.TypeSystem.Void);
        AddType(module.TypeSystem.Object);
        AddType(module.ImportReference(typeof(decimal)));
        return;

        // Cache a type of the type system of the module, which is the kind the weaver writes the most of.
        void AddType(TypeReference typeReference)
        {
            // The type system of the module owns the reference, so it is a valid reference of the target assembly.
            m_TypeCache[new TypeName(typeReference).ToString()] = new CecilType(typeReference.Resolve(), typeReference);
        }
    }

    /// <summary>
    /// The definition which a type reference stands for, resolved without a reference of it being imported to the
    /// assembly which is woven.<para/>
    /// A walk up the base types asks for the definition alone, and the reference of a base type is one which the import
    /// refuses: a base type is written where the type which declares it stands, so the base of a generic declaration
    /// names the parameters of that declaration, and a module which held a reference of it would name a generic
    /// parameter which it does not declare.
    /// </summary>
    /// <param name="typeRef">The type reference which the definition is resolved from.</param>
    /// <returns>The definition of the reference, or null when it stands for no type which can be read.</returns>
    internal TypeDefinition? GetDefinition(TypeReference typeRef) => typeRef.ResolveDefinition(Assembly.Source.MainModule);

    /// <summary>
    /// Get field from target type, if the target type does not contain a matching field, recursively fetch it from its base type.
    /// </summary>
    /// <param name="target">The target type, or null when there is no type left to search.</param>
    /// <param name="fieldName">Name of the field.</param>
    /// <returns>The field from target type or its base type. Returns null if there is no matching field in the target type and its base types.</returns>
    internal FieldReference? GetFieldFromType(TypeDefinition? target, string fieldName)
    {
        var curType = target;
        FieldReference? fieldDef = null;
        while (fieldDef == null)
        {
            if (curType == null)
                break;
            fieldDef = curType.Fields.FirstOrDefault(field => field.Name.Equals(fieldName));
            curType  = curType.BaseType == null ? null : GetDefinition(curType.BaseType);
        }

        return fieldDef;
    }

    /// <summary>
    /// Get method from target type, if the target type does not contain a matching method, recursively fetch it from its base type.
    /// </summary>
    /// <param name="target">The target type, or null when there is no type left to search.</param>
    /// <param name="methodName">Name of the method.</param>
    /// <param name="parameters">Parameters of the method.</param>
    /// <param name="returnType">
    /// The type of the value which the member hands back, where the caller wrote it down, or null where the caller holds
    /// none: it names a parameter of the member which stands in no place of the arguments, so a member which is handed
    /// back a value of the type of one of its parameters is found by it.
    /// </param>
    /// <param name="instance">
    /// The instantiation of the type which is searched which the member is reached through, or null where it is reached
    /// through none. It is what the parameters of the signature of a member of a generic type stand for.
    /// </param>
    /// <param name="throwWhenNotFound">Whether to throw an exception when the method is not found. Default is true.</param>
    /// <returns>The method from target type or its base type. Returns null if there is no matching method in the target type and its base types, which only happens when <paramref name="throwWhenNotFound"/> is false.</returns>
    /// <exception cref="ArgumentException">Thrown when there is no matching method and <paramref name="throwWhenNotFound"/> is true.</exception>
    internal MethodDefinition? GetMethodFromType(TypeDefinition? target, string methodName, IReadOnlyList<TypeReference> parameters, TypeReference? returnType = null, bool throwWhenNotFound = true, TypeReference? instance = null)
    {
        var curType = target;

        // The instantiation which each type of the chain of base types was reached through: the signature of a member is
        // written where the type which declares it stands, so a parameter which stands in it is the argument which the
        // instantiation holds for it, and a base type is handed the arguments which the type above it hands down.
        var curInstance = instance;
        MethodDefinition? methodDef = null;
        while (methodDef == null)
        {
            if (curType == null) break;
            // A member which declares parameters of its own is one which no list of type names describes, so a candidate
            // is read as the signature which the caller hands it where that signature binds the parameters: the type
            // which stands in the place of a parameter of the member is the one the call of it is made with, and the
            // type of the value which the member hands back is the one the call names a parameter which stands in no
            // such place with. A candidate which the signature does not describe is still the one which the names of the
            // types name, which is what finds a member whose own parameters stand nowhere in its signature - the call of
            // that one is refused by the rule of the call rather than here, where the member it names is the one it names.
            methodDef = curType.Methods.FirstOrDefault(
                method => method.Name.Equals(methodName)
                          && (method.SameWith(parameters, returnType, out _, curInstance) || method.DescribedBy(parameters, curInstance)));

            if (curType.BaseType == null)
            {
                curType     = null;
                curInstance = null;
                continue;
            }

            // The base of a type is written where that type stands, so the arguments of the instance are handed down
            // to it: the base of `Middle<T>` is `Base<T>`, and the argument which `Middle<int>` holds for its own
            // parameter is the one which the `T` of that base stands for.
            curInstance = curInstance is null ? null : curType.BaseType.WithTheArgumentsOf(curType, curInstance);
            curType     = GetDefinition(curType.BaseType);
        }

        if (methodDef == null && throwWhenNotFound) throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, methodName));
        return methodDef;
    }

    /// <summary>
    /// Get property from target type, if the target type does not contain a matching property, recursively fetch it from its base type.
    /// </summary>
    /// <param name="target">The target type, or null when there is no type left to search.</param>
    /// <param name="propertyName">Name of the property.</param>
    /// <returns>The property from target type or its base type. Returns null if there is no matching property in the target type and its base types.</returns>
    internal PropertyDefinition? GetPropertyFromType(TypeDefinition? target, string propertyName)
    {
        var curType = target;
        PropertyDefinition? propertyDef = null;
        while (propertyDef == null)
        {
            if (curType == null)
                break;
            propertyDef = curType.Properties.FirstOrDefault(property => property.Name.Equals(propertyName));
            curType     = curType.BaseType == null ? null : GetDefinition(curType.BaseType);
        }

        return propertyDef;
    }
}