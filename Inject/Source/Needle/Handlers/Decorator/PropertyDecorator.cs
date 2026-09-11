using System.Reflection;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;

namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing a property and its accessors, following the chainable pattern of
/// <see cref="ClassDecorator"/>. Create via <c>ITypeHandler.AddProperty(name, flags)</c>.
/// </summary>
public class PropertyDecorator : PropertyDecorator.IPropertyTypeDecorator
{
    private readonly TypeHandler m_TypeHandler;
    private readonly string m_PropertyName;
    private readonly PropertyFlags m_PropertyFlags;
    private IType m_PropertyType = typeof(object).ToGneedleType();
    private DefaultPropertyBody? m_GetterBody;
    private MethodInfo? m_GetterBodyMethod;
    private DefaultPropertyBody? m_SetterBody;
    private MethodInfo? m_SetterBodyMethod;

    /// <summary>
    /// Create a decorator which describes a property before it is appended to the module.
    /// </summary>
    /// <param name="typeHandler">Handler of the type which the property is appended to.</param>
    /// <param name="propertyName">Name of the property.</param>
    /// <param name="propertyFlags">Flags of the property.</param>
    internal PropertyDecorator(TypeHandler typeHandler, string propertyName, PropertyFlags propertyFlags)
    {
        m_TypeHandler   = typeHandler;
        m_PropertyName  = propertyName;
        m_PropertyFlags = propertyFlags;
    }

    /// <inheritdoc/>
    public IAccessorDecorator WithType(IType propertyType)
    {
        m_PropertyType = propertyType;
        return this;
    }

    /// <inheritdoc/>
    public IAccessorDecorator WithType(Type propertyType)
    {
        m_PropertyType = propertyType.ToGneedleType();
        return this;
    }

    /// <inheritdoc/>
    public IAccessorDecorator WithGetter(DefaultPropertyBody body)
    {
        // The two ways of describing a body replace each other, so that the one which was asked for last is the one which
        // is applied. They are held for one accessor alone, because a getter and a setter do not depend on each other.
        m_GetterBody       = body;
        m_GetterBodyMethod = null;
        return this;
    }

    /// <inheritdoc/>
    public IAccessorDecorator WithGetter(MethodInfo method)
    {
        m_GetterBodyMethod = method;
        m_GetterBody       = null;
        return this;
    }

    /// <inheritdoc/>
    public IAccessorDecorator WithSetter(DefaultPropertyBody body)
    {
        m_SetterBody       = body;
        m_SetterBodyMethod = null;
        return this;
    }

    /// <inheritdoc/>
    public IAccessorDecorator WithSetter(MethodInfo method)
    {
        m_SetterBodyMethod = method;
        m_SetterBody       = null;
        return this;
    }

    /// <inheritdoc/>
    public IPropertyHandler GetHandler()
    {
        var propertyType = m_TypeHandler.AssemblyHandler.ResolveParameterType(m_TypeHandler.Source, m_PropertyType);
        var propertyDef = new PropertyDefinition(m_PropertyName, PropertyAttributes.None, propertyType);
        m_TypeHandler.Source.Properties.Add(propertyDef);
        var handler = new PropertyHandler(propertyDef, m_TypeHandler);

        // The body which was described is the one which is applied, and the accessor is created by the call which applies
        // it, so a property whose accessor was described no body holds none of that accessor.
        if (m_GetterBodyMethod is { } getterBody)
        {
            handler.SetGetter(getterBody);
        }
        else if (m_GetterBody is { } defaultGetterBody)
        {
            handler.SetGetter(defaultGetterBody);
        }

        if (m_SetterBodyMethod is { } setterBody)
        {
            handler.SetSetter(setterBody);
        }
        else if (m_SetterBody is { } defaultSetterBody)
        {
            handler.SetSetter(defaultSetterBody);
        }

        // Apply property flags to getter/setter method attributes. The flags always keep the special name which an
        // accessor needs, see PropertyFlags.ToMethodAttributes().
        var methodAttrs = m_PropertyFlags.ToMethodAttributes();
        if (propertyDef.GetMethod != null)
        {
            propertyDef.GetMethod.Attributes = methodAttrs;
        }

        if (propertyDef.SetMethod != null)
        {
            propertyDef.SetMethod.Attributes = methodAttrs;
        }

        return handler;
    }

    /// <summary>
    /// Decorator which completes the property. It is the end of the chain.
    /// </summary>
    public interface ITypeDecorator
    {
        /// <summary>
        /// Build property definition to module.
        /// </summary>
        /// <returns>Handler for property.</returns>
        IPropertyHandler GetHandler();
    }

    /// <summary>
    /// Decorator for describing property accessors. A getter and a setter do not depend on each other, so either
    /// could be described first and either could be left out.
    /// </summary>
    public interface IAccessorDecorator : ITypeDecorator
    {
        /// <summary>
        /// Append getter to the property, with the default body behavior.
        /// </summary>
        /// <param name="body">The default body of the getter.</param>
        /// <returns>Result for chains calling.</returns>
        IAccessorDecorator WithGetter(DefaultPropertyBody body);

        /// <summary>
        /// Set the body of the getter from the method which holds the IL to copy.
        /// </summary>
        /// <param name="method">The method which holds the body of the getter.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="ArgumentException">Thrown when the parameters or the return type of the method do not match the getter.</exception>
        IAccessorDecorator WithGetter(MethodInfo method);

        /// <summary>
        /// Append setter to the property, with the default body behavior.
        /// </summary>
        /// <param name="body">The default body of the setter.</param>
        /// <returns>Result for chains calling.</returns>
        IAccessorDecorator WithSetter(DefaultPropertyBody body);

        /// <summary>
        /// Set the body of the setter from the method which holds the IL to copy.<para/>
        /// The method returns void and takes the value of the property as its last parameter, and the index of the
        /// property before it when the property is an indexer.
        /// </summary>
        /// <param name="method">The method which holds the body of the setter.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="ArgumentException">Thrown when the parameters of the method do not match the setter.</exception>
        IAccessorDecorator WithSetter(MethodInfo method);
    }

    /// <summary>
    /// Decorator for describing property type. It is the entry of the chain, which follows the order in which the
    /// parts of a property depend on each other: the type, then the accessors. The flags are given to
    /// <c>ITypeHandler.AddProperty(name, flags)</c>.
    /// </summary>
    public interface IPropertyTypeDecorator : IAccessorDecorator
    {
        /// <summary>
        /// Append property type from <see cref="IType"/>.
        /// </summary>
        /// <param name="propertyType">Property type of type.</param>
        /// <returns>Result for chains calling.</returns>
        IAccessorDecorator WithType(IType propertyType);

        /// <summary>
        /// Append property type from <see cref="System.Type"/>.
        /// </summary>
        /// <param name="propertyType">Property type of type.</param>
        /// <returns>Result for chains calling.</returns>
        IAccessorDecorator WithType(Type propertyType);
    }
}
