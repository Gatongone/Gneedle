namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing a property and its accessors, following the chainable pattern of
/// <see cref="ClassDecorator"/>. Create via <c>ITypeHandler.AddProperty(name)</c>.
/// </summary>
public class PropertyDecorator : PropertyDecorator.IPropertyTypeDecorator
{
    private readonly TypeHandler m_TypeHandler;
    private readonly string m_PropertyName;
    private IType m_PropertyType = typeof(object).ToGneedleType();
    private DefaultPropertyBody? m_GetterBody;
    private DefaultPropertyBody? m_SetterBody;
    private PropertyFlags? m_Flags;

    internal PropertyDecorator(TypeHandler typeHandler, string propertyName)
    {
        m_TypeHandler = typeHandler;
        m_PropertyName = propertyName;
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
    public ITypeDecorator WithFlags(PropertyFlags propertyFlags)
    {
        m_Flags = propertyFlags;
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

        // Apply property flags to getter/setter method attributes.
        if (m_Flags is { } flags)
        {
            var methodAttrs = flags.ToMethodAttributes();
            if (propertyDef.GetMethod != null)
            {
                propertyDef.GetMethod.Attributes = methodAttrs;
            }

            if (propertyDef.SetMethod != null)
            {
                propertyDef.SetMethod.Attributes = methodAttrs;
            }
        }

        return handler;
    }

    /// <summary>
    /// Decorator for create type definition to current module.
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
    /// Decorator for describing property accessors.
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

        /// <summary>
        /// Append property flags to the getter/setter methods.
        /// </summary>
        /// <param name="propertyFlags">Property flags.</param>
        /// <returns>Result for chains calling.</returns>
        ITypeDecorator WithFlags(PropertyFlags propertyFlags);
    }

    /// <summary>
    /// Decorator for describing property type.
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