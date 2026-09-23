namespace Gneedle.Inject;

/// <summary>
/// Represents a container which a type may be declared in: a class and a struct are the two which C# lets one stand in,
/// and an enum is not one, because an enum holds nothing but its values.<para/>
/// The visibility of a nested type is written in the nested form of it, which is none of the two forms a type declared
/// at the top of a module is written with. What is taken here is the same <see cref="ClassFlags"/> which
/// <see cref="IAssemblyHandler.AddClass"/> takes, written as that form, so that what <see cref="IClassHandler.Flags"/>
/// reads back of it is what it was declared with.
/// </summary>
public interface INestedTypeContainer
{
    /// <summary>
    /// Declare a class which is nested in this type, and hand back the decorator which describes it.
    /// </summary>
    /// <param name="typeName">Name of the class.</param>
    /// <param name="flags">Flags of the class.</param>
    /// <returns>Decorator for describing the class.</returns>
    /// <exception cref="WeavingException">Thrown when the type has been defined.</exception>
    ClassDecorator AddNestedClass(string typeName, ClassFlags flags = ClassFlags.Public);

    /// <inheritdoc cref="AddNestedClass(string, ClassFlags)"/>
    StructDecorator AddNestedStruct(string typeName, StructFlags flags = StructFlags.Public);

    /// <inheritdoc cref="AddNestedClass(string, ClassFlags)"/>
    EnumDecorator AddNestedEnum(string typeName, EnumFlags flags = EnumFlags.Public);
}

/// <summary>
/// Represents a container that holds a base type for a class or struct.
/// </summary>
public interface IBaseTypeContainer
{
    /// <summary>
    /// Gets the base type handler for the class or struct, which is null for a type which has no base type.<para/>
    /// A type which derives from nothing is the root of a hierarchy rather than a type whose base type could not be
    /// read, so a caller which walks the hierarchy has reached the top of it where the query answers with null.
    /// </summary>
    IClassHandler? BaseType { get; }
}

/// <summary>
/// Represents the query which reads the interfaces which a type implements.<para/>
/// The query is declared apart from the container of the kind of member which it names, and the type handler reaches it
/// as well, because both of the two shapes answer with the same thing and which one a caller holds is no part of what is
/// asked: a member which is declared by each of the two is a member which neither of them can be called with.
/// </summary>
public interface IInterfaceQuery
{
    /// <summary>
    /// Checks if the type which is handled contains the specified interface type.
    /// </summary>
    /// <param name="interfaceType">The interface type to check for.</param>
    /// <returns>True if the container contains the specified interface type; otherwise, false.</returns>
    bool ContainsInterface(IType interfaceType);
}

/// <summary>
/// Represents a container that holds interfaces for a class or struct.
/// </summary>
public interface IInterfaceContainer : IInterfaceQuery
{
    /// <summary>
    /// Append an interface to the type which is handled.<para/>
    /// The type which is handled is given the reference to the interface and nothing else: the members of the interface
    /// are not read, and nothing is woven into the type for it, so an interface is added whole or not at all.
    /// </summary>
    /// <param name="interfaceType">The interface which is added.</param>
    void AddInterface(IType interfaceType);
}

/// <summary>
/// Represents a handler for a type, providing access to its assembly, name, namespace, and attributes.<para/>
/// The queries which ask for the members of the type are declared by the query of the kind of member which each of them
/// names, which this shape reaches along with the container of that kind: a caller which holds the type handler asks the
/// type for the members of every kind, and a caller which holds the container of one kind asks for the members of that
/// kind, and both calls are bound to the one declaration which the query holds.
/// </summary>
public interface ITypeHandler : IAttributeContainer, IInterfaceQuery, IFieldQuery, IMethodQuery, IPropertyQuery
{
    /// <summary>
    /// Handler of the assembly which declares the type.
    /// </summary>
    IAssemblyHandler AssemblyHandler { get; }

    /// <summary>
    /// Name of the type. Setting it renames the type which is handled.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Namespace of the type. Setting it moves the type which is handled into another namespace.
    /// </summary>
    string Namespace { get; set; }
}

/// <summary>
/// Represents the queries which read the fields which a type declares.<para/>
/// The queries are declared apart from the container of the kind of member which they name, and the type handler reaches
/// them as well, because both of the two shapes answer with the same fields and which one a caller holds is no part of
/// what is asked: a query which is declared by each of the two is a query which neither of them can be called with.
/// </summary>
public interface IFieldQuery
{
    /// <summary>
    /// Gets a field handler for the specified field name, or null when no field of that name is found.<para/>
    /// A field which a base type declares is a field of the type as well, so the lookup walks the base types, the
    /// nearest one first, and answers with the first field of that name which it finds: the two ways of asking for a
    /// field mean different things, because a plural query answers with what the type itself declares rather than with
    /// what it inherits.
    /// </summary>
    /// <param name="fieldName">The name of the field to retrieve.</param>
    /// <returns>An <see cref="IFieldHandler"/> for the specified field, or null if the field is not found.</returns>
    IFieldHandler? GetField(string fieldName);

    /// <summary>
    /// Get the handlers of every field which the type declares, in the order in which it declares them.<para/>
    /// The fields of a base type are left out, because they are not the ones which the type declares.
    /// </summary>
    /// <returns>The handlers of the fields.</returns>
    IFieldHandler[] GetFields();

    /// <summary>
    /// Get the handlers of the fields which carry every flag which is named, in the order in which the type declares
    /// them. A query which names no flag answers with every field which the type declares.
    /// </summary>
    /// <param name="fieldFlags">The flags which the fields carry.</param>
    /// <returns>The handlers of the fields which carry the flags.</returns>
    IFieldHandler[] GetFields(FieldFlags fieldFlags);
}

/// <summary>
/// Represents a container that holds fields for a class or struct.
/// </summary>
public interface IFieldContainer : IFieldQuery
{
    /// <summary>
    /// Start building a field through a chainable <see cref="FieldDecorator"/>.
    /// </summary>
    /// <param name="fieldName">Name of the field.</param>
    /// <param name="fieldFlags">Flags of the field.</param>
    /// <returns>Result for chains calling.</returns>
    FieldDecorator.IFieldTypeDecorator AddField(string fieldName, FieldFlags fieldFlags);
}

/// <summary>
/// Represents the queries which read the methods which a type declares.<para/>
/// The queries are declared apart from the container of the kind of member which they name, and the type handler reaches
/// them as well, because both of the two shapes answer with the same methods and which one a caller holds is no part of
/// what is asked: a query which is declared by each of the two is a query which neither of them can be called with.
/// </summary>
public interface IMethodQuery
{
    /// <summary>
    /// Gets a method handler for the specified method name and parameter types, or null when no method of that signature
    /// is found.<para/>
    /// A method which a base type declares is a method of the type as well, so the base types are walked for it, the
    /// nearest one first, which is what the two ways of asking for a method tell apart: a plural query answers with what
    /// the type itself declares rather than with what it inherits. Methods of base types are looked for only when
    /// parameter types are given, and the first one which they match is the one which is answered with. A call which
    /// names no parameter type asks for the method of that name alone, which is the first one which the type itself
    /// declares, whatever its signature: a method which takes no parameter is asked for by the empty signature rather
    /// than by no types, which is what a lookup of a caller that holds the signature is for.
    /// </summary>
    /// <param name="methodName">The name of the method to retrieve.</param>
    /// <param name="parameterTypes">The parameter types of the method to retrieve.</param>
    /// <returns>An <see cref="IMethodHandler"/> for the specified method, or null if the method is not found.</returns>
    IMethodHandler? GetMethod(string methodName, params IType[] parameterTypes);

    /// <summary>
    /// Get the handlers of every method which the type declares, in the order in which it declares them, the overloads
    /// of one name included.<para/>
    /// The methods of a base type are left out, because they are not the ones which the type declares.
    /// </summary>
    /// <returns>The handlers of the methods.</returns>
    IMethodHandler[] GetMethods();

    /// <summary>
    /// Get the handlers of the methods which carry every flag which is named, in the order in which the type declares
    /// them. An abstract method is a virtual one as well, and the flags name it by the narrower of the two shapes, so a
    /// query of the virtual ones answers with the methods which are virtual alone. A query which names no flag answers
    /// with every method which the type declares.
    /// </summary>
    /// <param name="methodFlags">The flags which the methods carry.</param>
    /// <returns>The handlers of the methods which carry the flags.</returns>
    IMethodHandler[] GetMethods(MethodFlags methodFlags);
}

/// <summary>
/// Represents a container that holds methods for a class or struct.
/// </summary>
public interface IMethodContainer : IMethodQuery
{
    /// <summary>
    /// Start building a method through a chainable <see cref="MethodDecorator"/>.
    /// </summary>
    /// <param name="methodName">Name of the method.</param>
    /// <param name="methodFlags">Flags of the method.</param>
    /// <returns>Result for chains calling.</returns>
    MethodDecorator.IGenericParameterDecorator AddMethod(string methodName, MethodFlags methodFlags);
}

/// <summary>
/// Represents the queries which read the properties which a type declares.<para/>
/// The queries are declared apart from the container of the kind of member which they name, and the type handler reaches
/// them as well, because both of the two shapes answer with the same properties and which one a caller holds is no part
/// of what is asked: a query which is declared by each of the two is a query which neither of them can be called with.
/// </summary>
public interface IPropertyQuery
{
    /// <summary>
    /// Gets a property handler for the specified property name, or null when no property of that name is found.<para/>
    /// A property which a base type declares is a property of the type as well, so the lookup walks the base types, the
    /// nearest one first, and answers with the first property of that name which it finds: the two ways of asking for a
    /// property mean different things, because a plural query answers with what the type itself declares rather than with
    /// what it inherits.
    /// </summary>
    /// <param name="propertyName">The name of the property to retrieve.</param>
    /// <returns>An <see cref="IPropertyHandler"/> for the specified property, or null if the property is not found.</returns>
    IPropertyHandler? GetProperty(string propertyName);

    /// <summary>
    /// Get the handlers of the properties which carry every flag which is named, in the order in which the type declares
    /// them. The flags of a property are the ones of the accessor which it holds, which is the getter, or the setter when
    /// the property holds no getter. A query which names no flag answers with every property which the type declares.
    /// </summary>
    /// <param name="propertyFlags">The flags which the properties carry.</param>
    /// <returns>The handlers of the properties which carry the flags.</returns>
    IPropertyHandler[] GetProperties(PropertyFlags propertyFlags);

    /// <summary>
    /// Get the handlers of every property which the type declares, in the order in which it declares them.<para/>
    /// The properties of a base type are left out, because they are not the ones which the type declares.
    /// </summary>
    /// <returns>The handlers of the properties.</returns>
    IPropertyHandler[] GetProperties();
}

/// <summary>
/// Represents a container that holds properties for a class or struct.
/// </summary>
public interface IPropertyContainer : IPropertyQuery
{
    /// <summary>
    /// Start building a property through a chainable <see cref="PropertyDecorator"/>.
    /// </summary>
    /// <param name="propertyName">Name of the property.</param>
    /// <param name="propertyFlags">Flags of the property.</param>
    /// <returns>Result for chains calling.</returns>
    PropertyDecorator.IPropertyTypeDecorator AddProperty(string propertyName, PropertyFlags propertyFlags);
}

/// <summary>
/// Represents a handler for a class type, providing access to its base type, interfaces, fields, methods, and properties.
/// </summary>
public interface IClassHandler : ITypeHandler, INestedTypeContainer, IBaseTypeContainer, IInterfaceContainer, IFieldContainer, IMethodContainer, IPropertyContainer
{
    /// <summary>
    /// Flags of the class, which are the visibility and the modifiers which the definition declares. A class which was
    /// declared static is written as an abstract and sealed one, and it is read back as <see cref="ClassFlags.Static"/>.
    /// </summary>
    ClassFlags Flags { get; }
}

/// <summary>
/// Represents a handler for a struct type, providing access to its interfaces, fields, methods, and properties.
/// </summary>
public interface IStructHandler : ITypeHandler, INestedTypeContainer, IInterfaceContainer, IFieldContainer, IMethodContainer, IPropertyContainer
{
    /// <summary>
    /// Flags of the struct, which are the visibility which the definition declares and the two kinds which it is marked
    /// as by an attribute: the one which can only live on the stack, and the one whose fields cannot be assigned after
    /// it was created.
    /// </summary>
    StructFlags Flags { get; }
}

/// <summary>
/// Represents a handler for an enum type, providing access to its fields and methods.
/// </summary>
public interface IEnumHandler : ITypeHandler
{
    /// <summary>
    /// Flags of the enum, which are the visibility which the definition declares, an enum holding nothing else.
    /// </summary>
    EnumFlags Flags { get; }

    /// <summary>
    /// Gets the underlying type of the enum (e.g., int, byte, etc.).
    /// </summary>
    Type UnderlyingType { get; }

    /// <summary>
    /// Adds an enum value to the enum type with the specified name and value.
    /// </summary>
    /// <param name="enumName">The name of the enum value to add.</param>
    /// <param name="value">The value of the enum value to add.</param>
    void AddEnum(string enumName, object value);

    /// <summary>
    /// Gets an enum value from the enum type with the specified name and outputs its value. If the enum value is not found, returns null.
    /// </summary>
    /// <param name="enumName">The name of the enum value to retrieve.</param>
    /// <returns>The output parameter that will hold the value of the enum value if found.</returns>
    object? GetEnum(string enumName);
}

/// <summary>
/// Type handler extension methods.
/// </summary>
public static class TypeHandlerExtensions
{
    /// <param name="query">The query which reads the interfaces.</param>
    extension(IInterfaceQuery query)
    {
        /// <summary>
        /// Checks if the container contains the specified interface type.
        /// </summary>
        public bool ContainsInterface(Type interfaceType) => query.ContainsInterface(interfaceType.ToGneedleType());

        /// <summary>
        /// Checks if the container contains the specified interface type.
        /// </summary>
        public bool ContainsInterface<TInterface>() => query.ContainsInterface(typeof(TInterface));
    }

    /// <param name="container">The interface container.</param>
    extension(IInterfaceContainer container)
    {
        /// <summary>
        /// Append an interface to the type which is handled.
        /// </summary>
        /// <param name="interfaceType">The interface which is added, which has to be an interface.</param>
        /// <exception cref="WeavingException">Thrown when the type which is given is not an interface.</exception>
        public void AddInterface(Type interfaceType)
        {
            if (!interfaceType.IsInterface) throw new WeavingException(ErrorMessages.TYPE_IS_NOT_INTERFACE);
            container.AddInterface(interfaceType.ToGneedleType());
        }

        /// <summary>
        /// Append an interface to the type which is handled.
        /// </summary>
        /// <typeparam name="TInterface">The interface which is added.</typeparam>
        public void AddInterface<TInterface>() => container.AddInterface(typeof(TInterface));
    }
}