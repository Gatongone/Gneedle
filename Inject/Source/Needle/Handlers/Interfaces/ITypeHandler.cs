namespace Gneedle.Inject;

/// <summary>
/// Represents a container that holds a base type for a class or struct.
/// </summary>
public interface IBaseTypeContainer
{
    /// <summary>
    /// Gets the base type handler for the class or struct. If the type has no base type, returns null.
    /// </summary>
    IClassHandler BaseType { get; }
}

/// <summary>
/// Represents a container that holds interfaces for a class or struct.
/// </summary>
public interface IInterfaceContainer
{
    /// <summary>
    /// Checks if the container contains the specified interface type.
    /// </summary>
    /// <param name="interfaceType">The interface type to check for.</param>
    /// <returns>True if the container contains the specified interface type; otherwise, false.</returns>
    bool ContainsInterface(IType interfaceType);
}

/// <summary>
/// Represents a handler for a type, providing access to its assembly, name, namespace, and attributes.
/// </summary>
public interface ITypeHandler : IAttributeContainer
{
    IAssemblyHandler AssemblyHandler { get; }
    string Name { get; set; }
    string Namespace { get; set; }
}

/// <summary>
/// Represents a container that holds fields for a class or struct.
/// </summary>
public interface IFieldContainer
{
    /// <summary>
    /// Start building a field through a chainable <see cref="FieldDecorator"/>.
    /// </summary>
    /// <param name="fieldName">Name of the field.</param>
    /// <param name="fieldFlags">Flags of the field.</param>
    /// <returns>Result for chains calling.</returns>
    FieldDecorator.IFieldTypeDecorator AddField(string fieldName, FieldFlags fieldFlags);

    /// <summary>
    /// Gets a field handler for the specified field name. If the field is not found, returns null.
    /// </summary>
    /// <param name="fieldName">The name of the field to retrieve.</param>
    /// <returns>An <see cref="IFieldHandler"/> for the specified field, or null if the field is not found.</returns>
    IFieldHandler? GetField(string fieldName);
}

/// <summary>
/// Represents a container that holds methods for a class or struct.
/// </summary>
public interface IMethodContainer
{
    /// <summary>
    /// Start building a method through a chainable <see cref="MethodDecorator"/>.
    /// </summary>
    /// <param name="methodName">Name of the method.</param>
    /// <param name="methodFlags">Flags of the method.</param>
    /// <returns>Result for chains calling.</returns>
    MethodDecorator.IGenericParameterDecorator AddMethod(string methodName, MethodFlags methodFlags);

    /// <summary>
    /// Gets a method handler for the specified method name and parameter types. If the method is not found, returns null.
    /// </summary>
    /// <param name="methodName">The name of the method to retrieve.</param>
    /// <param name="parameterTypes">The parameter types of the method to retrieve.</param>
    /// <returns>An <see cref="IMethodHandler"/> for the specified method, or null if the method is not found.</returns>
    IMethodHandler? GetMethod(string methodName, params IType[] parameterTypes);
}

/// <summary>
/// Represents a container that holds properties for a class or struct.
/// </summary>
public interface IPropertyContainer
{
    /// <summary>
    /// Start building a property through a chainable <see cref="PropertyDecorator"/>.
    /// </summary>
    /// <param name="propertyName">Name of the property.</param>
    /// <param name="propertyFlags">Flags of the property.</param>
    /// <returns>Result for chains calling.</returns>
    PropertyDecorator.IPropertyTypeDecorator AddProperty(string propertyName, PropertyFlags propertyFlags);

    /// <summary>
    /// Gets a property handler for the specified property name. If the property is not found, returns null.
    /// </summary>
    /// <param name="propertyName">The name of the property to retrieve.</param>
    /// <returns>An <see cref="IPropertyHandler"/> for the specified property, or null if the property is not found.</returns>
    IPropertyHandler? GetProperty(string propertyName);
}

/// <summary>
/// Represents a handler for a class type, providing access to its base type, interfaces, fields, methods, and properties.
/// </summary>
public interface IClassHandler : ITypeHandler, IBaseTypeContainer, IInterfaceContainer, IFieldContainer, IMethodContainer, IPropertyContainer;

/// <summary>
/// Represents a handler for a struct type, providing access to its interfaces, fields, methods, and properties.
/// </summary>
public interface IStructHandler : ITypeHandler, IInterfaceContainer, IFieldContainer, IMethodContainer, IPropertyContainer;

/// <summary>
/// Represents a handler for an enum type, providing access to its fields and methods.
/// </summary>
public interface IEnumHandler : ITypeHandler
{
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
    /// <param name="container">The interface container.</param>
    extension(IInterfaceContainer container)
    {
        /// <summary>
        /// Checks if the container contains the specified interface type.
        /// </summary>
        public bool ContainsInterface(Type interfaceType) => container.ContainsInterface(interfaceType.ToGneedleType());

        /// <summary>
        /// Checks if the container contains the specified interface type.
        /// </summary>
        public bool ContainsInterface<TInterface>() => container.ContainsInterface(typeof(TInterface));
    }
}