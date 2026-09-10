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
    private DefaultPropertyBody? m_SetterBody;

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
        m_GetterBody = body;
        return this;
    }

    /// <inheritdoc/>
    public IAccessorDecorator WithSetter(DefaultPropertyBody body)
    {
        m_SetterBody = body;
        return this;
    }

    /// <inheritdoc/>
    public IPropertyHandler GetHandler()
    {
        var propertyType = m_TypeHandler.AssemblyHandler.ResolveParameterType(m_TypeHandler.Source, m_PropertyType);
        var propertyDef = new PropertyDefinition(m_PropertyName, PropertyAttributes.None, propertyType);
        m_TypeHandler.Source.Properties.Add(propertyDef);
        var handler = new PropertyHandler(propertyDef, m_TypeHandler);

        if (m_GetterBody is { } getterBody)
        {
            handler.SetGetter(getterBody);
        }

        if (m_SetterBody is { } setterBody)
        {
            handler.SetSetter(setterBody);
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
        /// Append getter to the property.
        /// </summary>
        /// <param name="body">The default body of the getter.</param>
        /// <returns>Result for chains calling.</returns>
        IAccessorDecorator WithGetter(DefaultPropertyBody body);

        /// <summary>
        /// Append setter to the property.
        /// </summary>
        /// <param name="body">The default body of the setter.</param>
        /// <returns>Result for chains calling.</returns>
        IAccessorDecorator WithSetter(DefaultPropertyBody body);
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
