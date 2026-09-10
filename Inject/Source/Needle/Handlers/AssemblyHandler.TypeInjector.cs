using System.Runtime.CompilerServices;

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

    public ITypeHandler GetType(TypeDefinition typeDefinition) => typeDefinition switch
    {
        {IsValueType: true, IsEnum: false} => new StructHandler(this, typeDefinition),
        {IsEnum     : true}                => new EnumHandler(this, typeDefinition, typeDefinition.Fields.First(f => f.Name == "value__").FieldType),
        {IsClass    : true}                => new ClassHandler(this, typeDefinition),
        _                                  => new TypeHandler(this, typeDefinition)
    };

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
    public ClassDecorator AddClass(string typeName, string typeNamespace, ClassFlags classFlags)
    {
        var fullName = $"{typeNamespace}.{typeName}";

        // Check type redefined.
        if (m_TypeCache.ContainsKey(fullName)) throw new ArgumentException(string.Format(ErrorMessages.TYPE_HAS_DEFINED, fullName));

        // Create type definition from context.
        var typeDef = new TypeDefinition(typeNamespace, typeName, classFlags.ToTypeAttributes());
        var context = new Implementation();
        return new ClassDecorator(this, typeDef, context, AddClassCallback);

        IClassHandler AddClassCallback(TypeDefinition type, Implementation impl)
        {
            // Inherits from base type.
            type.BaseType = impl.BaseType;

            // Add type to module.
            Assembly.Source.MainModule.Types.Add(type);

            // Track for redefinition check.
            m_TypeCache[fullName] = new CecilType(type, type);

            return new ClassHandler(this, type);
        }
    }

    /// <summary>
    /// Add a class to the assembly.
    /// </summary>
    public StructDecorator AddStruct(string typeName, string typeNamespace, StructFlags structFlags)
    {
        var fullName = $"{typeNamespace}.{typeName}";

        // Check type redefined.
        if (m_TypeCache.ContainsKey(fullName)) throw new ArgumentException(string.Format(ErrorMessages.TYPE_HAS_DEFINED, fullName));

        // Create type definition from context.
        var typeDef = new TypeDefinition(typeNamespace, typeName, structFlags.ToTypeAttributes());
        var context = new Implementation();

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

        return new StructDecorator(this, typeDef, context, AddStructCallback);

        IStructHandler AddStructCallback(TypeDefinition type, Implementation impl)
        {
            // Inherits from base type.
            type.BaseType = impl.BaseType;

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