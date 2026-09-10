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
        Assembly        = assembly;
        m_AssemblyCache = new Dictionary<string, AssemblyDefinition> {{assembly.Source.FullName, assembly.Source}};
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

        void AddType(TypeReference typeReference)
        {
            // The type system of the module owns the reference, so it is a valid reference of the target assembly.
            m_TypeCache[new TypeName(typeReference).ToString()] = new CecilType(typeReference.Resolve(), typeReference);
        }
    }

    /// <summary>
    /// Get field from target type, if the target type does not contain a matching field, recursively fetch it from its base type.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="fieldName">Name of the field.</param>
    /// <returns>The field from target type or its base type. Returns null if there is no matching field in the target type and its base types.</returns>
    internal FieldReference? GetFieldFromType(TypeDefinition target, string fieldName)
    {
        var curType = target;
        FieldReference? fieldDef = null;
        while (fieldDef == null)
        {
            if (curType == null)
                break;
            fieldDef = curType.Fields.FirstOrDefault(field => field.Name.Equals(fieldName));
            curType  = curType.BaseType == null ? null : GetCecilType(curType.BaseType).Definition;
        }

        return fieldDef;
    }

    /// <summary>
    /// Get method from target type, if the target type does not contain a matching method, recursively fetch it from its base type.
    /// </summary>
    /// <param name="target">The target type.</param>
    /// <param name="methodName">Name of the method.</param>
    /// <param name="parameters">Parameters of the method.</param>
    /// <param name="throwWhenNotFound">Whether to throw an exception when the method is not found. Default is true.</param>
    /// <returns>The method from target type or its base type. Returns null if there is no matching method in the target type and its base types.</returns>
    internal MethodDefinition? GetMethodFromType(TypeDefinition target, string methodName, IReadOnlyList<TypeReference> parameters, bool throwWhenNotFound = true)
    {
        var curType = target;
        MethodDefinition? methodDef = null;
        while (methodDef == null)
        {
            if (curType == null) break;
            methodDef = curType.Methods.FirstOrDefault(method => method.Name.Equals(methodName) && method.Parameters.SameWith(parameters.ToArray()));
            curType   = curType.BaseType == null ? null : GetCecilType(curType.BaseType).Definition;
        }

        return methodDef == null && throwWhenNotFound ? null : methodDef!;
    }

    /// <summary>
    /// Get property from target type, if the target type does not contain a matching property, recursively fetch it from its base type.
    /// </summary>
    /// <param name="target">The target type.</param>
    /// <param name="propertyName">Name of the property.</param>
    /// <returns>The property from target type or its base type. Returns null if there is no matching property in the target type and its base types.</returns>
    internal PropertyDefinition? GetPropertyFromType(TypeDefinition target, string propertyName)
    {
        var curType = target;
        PropertyDefinition? propertyDef = null;
        while (propertyDef == null)
        {
            if (curType == null)
                break;
            propertyDef = curType.Properties.FirstOrDefault(property => property.Name.Equals(propertyName));
            curType     = curType.BaseType == null ? null : GetCecilType(curType.BaseType).Definition;
        }

        return propertyDef;
    }
}