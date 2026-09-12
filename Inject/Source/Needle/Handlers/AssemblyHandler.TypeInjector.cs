using System.Runtime.CompilerServices;

namespace Gneedle.Inject;

partial class AssemblyHandler
{
    /// <summary>
    /// Get all type handlers that match the given filter.
    /// </summary>
    /// <param name="filter">The filter to apply to the type definitions.</param>
    /// <returns>An array of type handlers that match the given filter.</returns>
    internal ITypeHandler[] GetTypes(Func<TypeDefinition, bool> filter)
    {
        var handlers = new List<ITypeHandler>();
        foreach (var type in Assembly.Source.Modules.SelectMany(module => module.Types))
        {
            // Append type definition.
            if (filter(type)) handlers.Add(GetType(type));
            // Append nested type definition.
            handlers.AddRange(type.NestedTypes.Where(filter).Select(GetType));
        }

        return handlers.ToArray<ITypeHandler>();
    }

    /// <summary>
    /// Get the type handler for the given type definition.
    /// </summary>
    /// <param name="typeDefinition">The type definition to get the handler for.</param>
    /// <returns>The type handler for the given type definition.</returns>
    internal ITypeHandler GetType(TypeDefinition typeDefinition) => typeDefinition switch
    {
        {IsValueType: true, IsEnum: false} => new StructHandler(this, typeDefinition),
        {IsEnum     : true}                => new EnumHandler(this, typeDefinition, typeDefinition.Fields.First(f => f.Name == "value__").FieldType),
        {IsClass    : true}                => new ClassHandler(this, typeDefinition),
        _                                  => new TypeHandler(this, typeDefinition)
    };

    /// <inheritdoc/>
    public ITypeHandler? GetType(string typeFullName)
    {
        foreach (var type in Assembly.Source.Modules.SelectMany(module => module.Types))
        {
            // Return type definition.
            if (type.FullName.Equals(typeFullName)) return GetType(type);
            // Return nested type definition.
            var nestedType = type.NestedTypes.FirstOrDefault(nestedType => nestedType.FullName.Equals(typeFullName));
            if (nestedType != null) return GetType(nestedType);
        }

        return null;
    }

    /// <summary>
    /// The handler of every type which the assembly declares, the nested ones included.
    /// </summary>
    /// <returns>The handlers of the types, in the order the metadata declares them.</returns>
    public ITypeHandler[] GetTypes()
    {
        var handlers = new List<ITypeHandler>();
        foreach (var type in Assembly.Source.Modules.SelectMany(module => module.Types))
        {
            // Append type definition.
            handlers.Add(GetType(type));
            // Append nested type definition.
            handlers.AddRange(type.NestedTypes.Select(GetType));
        }

        return handlers.ToArray<ITypeHandler>();
    }

    /// <inheritdoc/>
    public ITypeHandler GetType(Type type) => new TypeHandler(this, GetCecilType(type).Definition);

    /// <inheritdoc/>
    public ClassDecorator AddClass(string typeName, string typeNamespace, ClassFlags classFlags)
    {
        var fullName = $"{typeNamespace}.{typeName}";

        // Check type redefined.
        if (m_TypeCache.ContainsKey(fullName)) throw new ArgumentException(string.Format(ErrorMessages.TYPE_HAS_DEFINED, fullName));

        // Create type definition from context.
        var typeDef = new TypeDefinition(typeNamespace, typeName, classFlags.ToTypeAttributes());
        // The base type is described through the decorator before the class is appended, so none is held here.
        return new ClassDecorator(this, typeDef, null, AddClassCallback);

        /// <summary>
        /// Append the class to the module once the chain has described it, which is called by the decorator when the
        /// chain ends.
        /// </summary>
        /// <param name="type">The class which was described.</param>
        /// <param name="baseType">The base type which the chain asked for, or null when it asked for none.</param>
        /// <returns>The handler of the class which was appended.</returns>
        IClassHandler AddClassCallback(TypeDefinition type, TypeReference? baseType)
        {
            // Inherits from base type.
            type.BaseType = baseType;

            // Add type to module.
            Assembly.Source.MainModule.Types.Add(type);

            // Track for redefinition check.
            m_TypeCache[fullName] = new CecilType(type, type);

            return new ClassHandler(this, type);
        }
    }

    /// <inheritdoc/>
    public StructDecorator AddStruct(string typeName, string typeNamespace, StructFlags structFlags)
    {
        var fullName = $"{typeNamespace}.{typeName}";

        // Check type redefined.
        if (m_TypeCache.ContainsKey(fullName)) throw new ArgumentException(string.Format(ErrorMessages.TYPE_HAS_DEFINED, fullName));

        // Create type definition from context.
        var typeDef = new TypeDefinition(typeNamespace, typeName, structFlags.ToTypeAttributes());

        if (structFlags.HasFlag(StructFlags.Ref))
        {
            var module = Assembly.Source.MainModule;
            // Add IsByRefLike attribute.
            var byRefLikeDef = GetCecilType(typeof(IsByRefLikeAttribute)).Definition;
            typeDef.CustomAttributes.Add(byRefLikeDef.CreateCustomAttribute(module));
            // Add Obsolete attribute to prevent using this type in field, which is not allowed for ref struct.
            var obsoleteDef = GetCecilType(typeof(ObsoleteAttribute)).Definition;
            typeDef.CustomAttributes.Add(obsoleteDef.CreateCustomAttribute(module, "This type is a ref struct and cannot be used as a field.", true));
        }

        if (structFlags.HasFlag(StructFlags.ReadOnly))
        {
            var module = Assembly.Source.MainModule;
            // Add IsReadOnly attribute.
            var readOnlyDef = GetCecilType(typeof(IsReadOnlyAttribute)).Definition;
            typeDef.CustomAttributes.Add(readOnlyDef.CreateCustomAttribute(module));
        }

        // The base type is described through the decorator before the struct is appended, so none is held here.
        return new StructDecorator(this, typeDef, null, AddStructCallback);

        /// <summary>
        /// Append the struct to the module once the chain has described it, which is called by the decorator when the
        /// chain ends.
        /// </summary>
        /// <param name="type">The struct which was described.</param>
        /// <param name="baseType">The base type which the chain asked for, or null when it asked for none.</param>
        /// <returns>The handler of the struct which was appended.</returns>
        IStructHandler AddStructCallback(TypeDefinition type, TypeReference? baseType)
        {
            // Inherits from base type.
            type.BaseType = baseType;

            // Add type to module.
            Assembly.Source.MainModule.Types.Add(type);

            // Track for redefinition check.
            m_TypeCache[fullName] = new CecilType(type, type);

            return new StructHandler(this, type);
        }
    }

    /// <summary>
    /// Add an enum to the assembly.
    /// </summary>
    /// <param name="typeName">Type name of the enum.</param>
    /// <param name="typeNamespace">Namespace of the enum.</param>
    /// <param name="enumFlags">Enum flags.</param>
    /// <returns>Decorator for describing the enum.</returns>
    /// <exception cref="ArgumentException">Thrown when the type has been defined.</exception>
    public EnumDecorator AddEnum(string typeName, string typeNamespace, EnumFlags enumFlags)
    {
        var fullName = $"{typeNamespace}.{typeName}";

        // Check type redefined.
        if (m_TypeCache.ContainsKey(fullName)) throw new ArgumentException(string.Format(ErrorMessages.TYPE_HAS_DEFINED, fullName));

        // Create type definition from context.
        var typeDef = new TypeDefinition(typeNamespace, typeName, enumFlags.ToTypeAttributes());

        // Default underlying type is int.
        var underlyingType = Assembly.Source.MainModule.TypeSystem.Int32;

        // Register the type in the cache immediately so the decorator can build on it.
        m_TypeCache[fullName] = new CecilType(typeDef, typeDef);

        return new EnumDecorator(this, typeDef, underlyingType);
    }
}