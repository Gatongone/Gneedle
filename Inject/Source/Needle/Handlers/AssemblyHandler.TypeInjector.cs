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
        =>
        [
            .. Assembly.Source.Modules
                       .SelectMany(InjectorInterfaces.AllTypes)
                       .Where(filter)
                       .Select(GetType)
        ];

    /// <summary>
    /// Get the type handler for the given type definition.
    /// </summary>
    /// <param name="typeDefinition">The type definition to get the handler for.</param>
    /// <returns>The type handler for the given type definition.</returns>
    internal ITypeHandler GetType(TypeDefinition typeDefinition) => typeDefinition switch
    {
        {IsValueType: true, IsEnum: false} => new StructHandler(this, typeDefinition),
        {IsEnum     : true}                => new EnumHandler(this, typeDefinition, ValueFieldTypeOf(typeDefinition)),
        {IsClass    : true}                => new ClassHandler(this, typeDefinition),
        _                                  => new TypeHandler(this, typeDefinition)
    };

    /// <summary>
    /// The type which the values of an enum are read as, which is the type of the field which holds one of them.<para/>
    /// An enum which declares no such field is not an enum which anything can be read out of, and the refusal names it:
    /// the lookup of the field on its own answers with a sequence which holds nothing, which says nothing of the type
    /// which was asked about.
    /// </summary>
    /// <param name="typeDefinition">The enum which is read.</param>
    /// <returns>The type which the values of the enum are read as.</returns>
    /// <exception cref="ArgumentException">Thrown when the enum declares no field which holds the value of a member.</exception>
    private static TypeReference ValueFieldTypeOf(TypeDefinition typeDefinition)
        => typeDefinition.Fields.FirstOrDefault(field => field.Name == "value__")?.FieldType
            ?? throw new ArgumentException(string.Format(ErrorMessages.ENUM_DECLARES_NO_VALUE_FIELD, typeDefinition.FullName));

    /// <inheritdoc/>
    public ITypeHandler? GetType(string typeFullName)
    {
        // A type which a nested type declares is a type which the assembly declares, and its name is as long as the
        // nesting is deep, so the types of a module are read with the ones which the types themselves declare.
        var typeDefinition = Assembly.Source.Modules
                                     .SelectMany(InjectorInterfaces.AllTypes)
                                     .FirstOrDefault(type => type.FullName.Equals(typeFullName));

        return typeDefinition == null ? null : GetType(typeDefinition);
    }

    /// <summary>
    /// The handler of every type which the assembly declares, the nested ones included.
    /// </summary>
    /// <returns>The handlers of the types, in the order the metadata declares them.</returns>
    public ITypeHandler[] GetTypes() => GetTypes(_ => true);

    /// <inheritdoc/>
    public ITypeHandler GetType(Type type)
    {
        // The handler is the one of the kind which the definition is, as it is for a definition and for a name, because
        // the kind is what decides which of the injectors of a type applies to it: a handler which every type is
        // answered with alike holds no kind, which refuses an injector that names one for a type of every kind there is.
        return GetType(GetCecilType(type).Definition);
    }

    /// <inheritdoc/>
    public ClassDecorator AddClass(string typeName, string typeNamespace, ClassFlags classFlags)
    {
        var fullName = $"{typeNamespace}.{typeName}";

        // Create type definition from context.
        var typeDef = new TypeDefinition(typeNamespace, typeName, classFlags.ToTypeAttributes());

        // The name is taken before the chain which describes the type runs, because the chain runs at the end of it: two
        // chains of one name would both find the name free where the reservation stood last, and the second would append
        // a definition which is taken for the first wherever a name is looked up.
        Reserve(typeDef, fullName);

        // The base type is described through the decorator before the class is appended, so none is held here.
        return new ClassDecorator(this, typeDef, null, AddClassCallback);

        // Append the class to the module once the chain has described it, which is called by the decorator when the
        // chain ends, with the class which was described and the base type which the chain asked for, or null when it
        // asked for none; the handler of the class which was appended is answered.
        IClassHandler AddClassCallback(TypeDefinition type, TypeReference? baseType)
        {
            // Inherits from base type.
            type.BaseType = baseType;

            // Add type to module.
            Assembly.Source.MainModule.Types.Add(type);

            return new ClassHandler(this, type);
        }
    }

    /// <summary>
    /// Take the name which a type is declared with, where no definition of that name is held yet, which is what a name
    /// is looked up by: the definition itself is held, because it is the same one which the chain which describes the
    /// type appends to the module, and the name of the type which is described first is the one which stands.
    /// </summary>
    /// <param name="typeDef">The definition of the type which is declared.</param>
    /// <param name="fullName">The name which the type is declared with, which the refusal names.</param>
    /// <exception cref="ArgumentException">Thrown when a type of that name was declared already.</exception>
    private void Reserve(TypeDefinition typeDef, string fullName)
    {
        if (!m_TypeCache.TryAdd(new TypeName(typeDef).ToString(), new CecilType(typeDef, typeDef)))
        {
            throw new ArgumentException(string.Format(ErrorMessages.TYPE_HAS_DEFINED, fullName));
        }
    }

    /// <inheritdoc/>
    public StructDecorator AddStruct(string typeName, string typeNamespace, StructFlags structFlags)
    {
        var fullName = $"{typeNamespace}.{typeName}";

        // Create type definition from context.
        var typeDef = new TypeDefinition(typeNamespace, typeName, structFlags.ToTypeAttributes());

        // The name is taken before the chain which describes the type runs, which is described where the class above is.
        Reserve(typeDef, fullName);

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

        // Append the struct to the module once the chain has described it, which is called by the decorator when the
        // chain ends, with the struct which was described and the base type which the chain asked for, or null when it
        // asked for none; the handler of the struct which was appended is answered.
        IStructHandler AddStructCallback(TypeDefinition type, TypeReference? baseType)
        {
            // Inherits from base type.
            type.BaseType = baseType;

            // Add type to module.
            Assembly.Source.MainModule.Types.Add(type);

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

        // Create type definition from context.
        var typeDef = new TypeDefinition(typeNamespace, typeName, enumFlags.ToTypeAttributes());

        // Default underlying type is int.
        var underlyingType = Assembly.Source.MainModule.TypeSystem.Int32;

        // The name is taken before the enum is described, which is the same reservation the class and the struct above
        // take, so that the chain of every kind of type which is added is guarded by one rule.
        Reserve(typeDef, fullName);

        return new EnumDecorator(this, typeDef, underlyingType);
    }
}