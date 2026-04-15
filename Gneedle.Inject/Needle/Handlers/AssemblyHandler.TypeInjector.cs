namespace Gneedle.Inject;

partial class AssemblyHandler
{
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

    public ITypeHandler GetType(TypeDefinition typeDefinition)
    {
        if (typeDefinition.IsClass)
        {
            return new ClassHandler(this, typeDefinition);
        }
        // if (typeDefinition.IsValueType && !typeDefinition.IsEnum)
        // {
        //     return new StructHandler(this, typeDefinition);
        // }
        // if (typeDefinition.IsEnum)
        // {
        //     return new EnumHandler(this, typeDefinition);
        // }

        return new TypeHandler(this, typeDefinition);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="filter"></param>
    /// <returns></returns>
    public ITypeHandler[] GetTypes(Func<TypeDefinition, bool> filter)
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
    /// 
    /// </summary>
    /// <returns></returns>
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

    public ITypeHandler GetType(Type type) => new TypeHandler(this, GetCecilType(type).Definition);

    /// <summary>
    /// Add a class to the assembly.
    /// </summary>
    /// <param name="className"></param>
    /// <param name="typeNamespace"></param>
    /// <param name="classFlags"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public ClassDecorator AddClass(string className, string typeNamespace, ClassFlags classFlags)
    {
        var fullName = $"{typeNamespace}.{className}";

        // Check type redefined.
        if (m_TypeCache.ContainsKey(fullName)) throw new ArgumentException(string.Format(ErrorMessages.TYPE_HAS_DEFINED, fullName));

        // Create type definition from context.
        var typeDef = new TypeDefinition(className, typeNamespace, classFlags.ToTypeAttributes());
        var context = new Implementation();
        return new ClassDecorator(this, typeDef, context, AddClass);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="typeDef"></param>
    /// <param name="impl"></param>
    /// <returns></returns>
    private IClassHandler AddClass(TypeDefinition typeDef, Implementation impl)
    {
        // Inherits from base type.
        typeDef.BaseType = impl.BaseType;

        // Add type to module.
        Assembly.Source.MainModule.Types.Add(typeDef);

        return new ClassHandler(this, typeDef);
    }
}