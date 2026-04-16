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
    /// <param name="typeName"></param>
    /// <param name="typeNamespace"></param>
    /// <param name="classFlags"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public ClassDecorator AddClass(string typeName, string typeNamespace, ClassFlags classFlags)
    {
        var fullName = $"{typeNamespace}.{typeName}";

        // Check type redefined.
        if (m_TypeCache.ContainsKey(fullName)) throw new ArgumentException(string.Format(ErrorMessages.TYPE_HAS_DEFINED, fullName));

        // Create type definition from context.
        var typeDef = new TypeDefinition(typeName, typeNamespace, classFlags.ToTypeAttributes());
        var context = new Implementation();
        return new ClassDecorator(this, typeDef, context, AddClassCallback);

        IClassHandler AddClassCallback(TypeDefinition type, Implementation impl)
        {
            // Inherits from base type.
            type.BaseType = impl.BaseType;

            // Add type to module.
            Assembly.Source.MainModule.Types.Add(type);

            return new ClassHandler(this, type);
        }
    }

    /// <summary>
    /// Add a class to the assembly.
    /// </summary>
    /// <param name="typeName"></param>
    /// <param name="typeNamespace"></param>
    /// <param name="structFlags"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public StructDecorator AddStruct(string typeName, string typeNamespace, StructFlags structFlags)
    {
        var fullName = $"{typeNamespace}.{typeName}";

        // Check type redefined.
        if (m_TypeCache.ContainsKey(fullName)) throw new ArgumentException(string.Format(ErrorMessages.TYPE_HAS_DEFINED, fullName));

        // Create type definition from context.
        var typeDef = new TypeDefinition(typeName, typeNamespace, structFlags.ToTypeAttributes())
        {
            IsValueType = true
        };
        var context = new Implementation();

        if (structFlags.HasFlag(StructFlags.Ref))
        {
            // Add IsByRefLike attribute.
            typeDef.CustomAttributes.Add(typeDef.CreateCustomAttribute(Assembly.Source.MainModule, typeof(IsByRefLikeAttribute)));
            // Add Obsolete attribute to prevent using this type in field, which is not allowed for ref struct.
            typeDef.CustomAttributes.Add(typeDef.CreateCustomAttribute(Assembly.Source.MainModule, typeof(ObsoleteAttribute), "This type is a ref struct and cannot be used as a field.", true));
        }
        if (structFlags.HasFlag(StructFlags.ReadOnly))
        {
            // Add IsReadOnly attribute.
            typeDef.CustomAttributes.Add(typeDef.CreateCustomAttribute(Assembly.Source.MainModule, typeof(IsReadOnlyAttribute)));
        }

        return new StructDecorator(this, typeDef, context, AddStructCallback);

        IStructHandler AddStructCallback(TypeDefinition type, Implementation impl)
        {
            // Inherits from base type.
            type.BaseType = impl.BaseType;

            // Add type to module.
            Assembly.Source.MainModule.Types.Add(type);

            // return new StructHandler(this, typeDef);
            return default;
        }
    }
}