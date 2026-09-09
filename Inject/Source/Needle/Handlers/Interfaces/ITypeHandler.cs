namespace Gneedle.Inject;

public interface IBaseTypeContainer
{
    IClassHandler BaseType { get; }
}

public interface IInterfaceContainer
{
    bool ContainsInterface(IType interfaceType);
}

public interface ITypeHandler : IAttributeContainer
{
    IAssemblyHandler AssemblyHandler { get; }
    string Name { get; set; }
    string Namespace { get; set; }
}

public interface IFieldContainer
{
    /// <summary>
    /// Start building a field through a chainable <see cref="FieldDecorator"/>.
    /// </summary>
    FieldDecorator AddField(string fieldName);

    /// <summary>
    /// Gets a field handler for the specified field name. If the field is not found, returns null.
    /// </summary>
    /// <param name="fieldName">The name of the field to retrieve.</param>
    /// <returns>An <see cref="IFieldHandler"/> for the specified field, or null if the field is not found.</returns>
    IFieldHandler? GetField(string fieldName);
}

public interface IMethodContainer
{
    /// <summary>
    /// Start building a method through a chainable <see cref="MethodDecorator"/>.
    /// </summary>
    MethodDecorator AddMethod(string methodName);

    /// <summary>
    /// Gets a method handler for the specified method name and parameter types. If the method is not found, returns null.
    /// </summary>
    /// <param name="methodName">The name of the method to retrieve.</param>
    /// <param name="parameterTypes">The parameter types of the method to retrieve.</param>
    /// <returns>An <see cref="IMethodHandler"/> for the specified method, or null if the method is not found.</returns>
    IMethodHandler? GetMethod(string methodName, params IType[] parameterTypes);
}

public interface IPropertyContainer
{
    PropertyDecorator AddProperty(string propertyName);

    /// <summary>
    /// Gets a property handler for the specified property name. If the property is not found, returns null.
    /// </summary>
    /// <param name="propertyName">The name of the property to retrieve.</param>
    /// <returns>An <see cref="IPropertyHandler"/> for the specified property, or null if the property is not found.</returns>
    IPropertyHandler? GetProperty(string propertyName);
}

public interface IClassHandler : ITypeHandler, IBaseTypeContainer, IInterfaceContainer, IFieldContainer, IMethodContainer, IPropertyContainer;

public interface IStructHandler : ITypeHandler, IInterfaceContainer, IFieldContainer, IMethodContainer, IPropertyContainer;

public interface IEnumHandler : ITypeHandler
{
    IFieldHandler AddEnum(string enumName, long value);
    IFieldHandler? GetEnum(string enumName);
}

public static class TypeHandlerExtensions
{
    extension(IInterfaceContainer container)
    {
        public bool ContainsInterface(Type interfaceType) => container.ContainsInterface(interfaceType.ToGneedleType());
        public bool ContainsInterface<TInterface>() => container.ContainsInterface(typeof(TInterface));
    }
}