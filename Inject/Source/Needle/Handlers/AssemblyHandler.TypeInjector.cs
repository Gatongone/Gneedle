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
    /// <exception cref="WeavingException">Thrown when the enum declares no field which holds the value of a member.</exception>
    private static TypeReference ValueFieldTypeOf(TypeDefinition typeDefinition)
        => typeDefinition.Fields.FirstOrDefault(field => field.Name == "value__")?.FieldType
            ?? throw new WeavingException(string.Format(ErrorMessages.ENUM_DECLARES_NO_VALUE_FIELD, typeDefinition.FullName));

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
            ModuleLock.Declare(Assembly.Source.MainModule, type);

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
    /// <exception cref="WeavingException">Thrown when a type of that name was declared already.</exception>
    private void Reserve(TypeDefinition typeDef, string fullName)
    {
        if (!m_TypeCache.TryAdd(new TypeName(typeDef).ToString(), new CecilType(typeDef, typeDef)))
        {
            throw new WeavingException(string.Format(ErrorMessages.TYPE_HAS_DEFINED, fullName));
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
            ModuleLock.Declare(Assembly.Source.MainModule, type);

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
    /// <exception cref="WeavingException">Thrown when the type has been defined.</exception>
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

    /// <summary>
    /// Declare a class which is nested in the type which is given, and hand back the decorator which describes it.<para/>
    /// The visibility of a nested class is written in the nested form of it, which is none of the two forms a class
    /// declared at the top of a module is written with, so what is given here is the same
    /// <see cref="ClassFlags"/> which <see cref="IAssemblyHandler.AddClass"/> takes and it is written as the nested
    /// form of it. What is declared is a type of the module like any other, so the name of it is taken the same way.
    /// </summary>
    /// <param name="declaring">The type which the class is declared in.</param>
    /// <param name="typeName">Name of the class.</param>
    /// <param name="classFlags">Flags of the class.</param>
    /// <returns>Decorator for describing the class.</returns>
    /// <exception cref="WeavingException">Thrown when the type has been defined.</exception>
    internal ClassDecorator AddNestedClass(TypeDefinition declaring, string typeName, ClassFlags classFlags)
    {
        var typeDef = new TypeDefinition("", typeName, classFlags.ToNestedTypeAttributes())
        {
            DeclaringType = declaring,
        };

        Reserve(typeDef, $"{new TypeName(declaring)}.{typeName}");

        // The base type is described through the decorator before the class is appended, as it is above.
        return new ClassDecorator(this, typeDef, null, AddClassCallback);

        IClassHandler AddClassCallback(TypeDefinition type, TypeReference? baseType)
        {
            type.BaseType = baseType;
            ModuleLock.DeclareNested(Assembly.Source.MainModule, declaring, type);

            return new ClassHandler(this, type);
        }
    }

    /// <summary>
    /// Declare a struct which is nested in the type which is given, and hand back the decorator which describes it.
    /// </summary>
    /// <param name="declaring">The type which the struct is declared in.</param>
    /// <param name="typeName">Name of the struct.</param>
    /// <param name="structFlags">Flags of the struct.</param>
    /// <returns>Decorator for describing the struct.</returns>
    /// <exception cref="WeavingException">Thrown when the type has been defined.</exception>
    internal StructDecorator AddNestedStruct(TypeDefinition declaring, string typeName, StructFlags structFlags)
    {
        var typeDef = new TypeDefinition("", typeName, structFlags.ToNestedTypeAttributes())
        {
            DeclaringType = declaring,
        };

        Reserve(typeDef, $"{new TypeName(declaring)}.{typeName}");

        // What the two kinds of struct are marked by is a custom attribute rather than a flag, so it is the same
        // whether the struct is nested or not: what is nested is the visibility alone.
        if (structFlags.HasFlag(StructFlags.Ref))
        {
            var module = Assembly.Source.MainModule;
            typeDef.CustomAttributes.Add(GetCecilType(typeof(IsByRefLikeAttribute)).Definition.CreateCustomAttribute(module));
            typeDef.CustomAttributes.Add(GetCecilType(typeof(ObsoleteAttribute)).Definition.CreateCustomAttribute(module,
                "This type is a ref struct and cannot be used as a field.", true));
        }

        if (structFlags.HasFlag(StructFlags.ReadOnly))
        {
            typeDef.CustomAttributes.Add(GetCecilType(typeof(IsReadOnlyAttribute)).Definition.CreateCustomAttribute(Assembly.Source.MainModule));
        }

        return new StructDecorator(this, typeDef, null, AddStructCallback);

        IStructHandler AddStructCallback(TypeDefinition type, TypeReference? baseType)
        {
            type.BaseType = baseType;
            ModuleLock.DeclareNested(Assembly.Source.MainModule, declaring, type);

            return new StructHandler(this, type);
        }
    }

    /// <summary>
    /// Declare an enum which is nested in the type which is given, and hand back the decorator which describes it.
    /// </summary>
    /// <param name="declaring">The type which the enum is declared in.</param>
    /// <param name="typeName">Name of the enum.</param>
    /// <param name="enumFlags">Flags of the enum.</param>
    /// <returns>Decorator for describing the enum.</returns>
    /// <exception cref="WeavingException">Thrown when the type has been defined.</exception>
    internal EnumDecorator AddNestedEnum(TypeDefinition declaring, string typeName, EnumFlags enumFlags)
    {
        var typeDef = new TypeDefinition("", typeName, enumFlags.ToNestedTypeAttributes())
        {
            DeclaringType = declaring,
        };

        Reserve(typeDef, $"{new TypeName(declaring)}.{typeName}");

        return new EnumDecorator(this, typeDef, Assembly.Source.MainModule.TypeSystem.Int32);
    }
}
