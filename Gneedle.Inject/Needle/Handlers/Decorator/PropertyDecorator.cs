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

    internal PropertyDecorator(TypeHandler typeHandler, string propertyName)
    {
        m_TypeHandler = typeHandler;
        m_PropertyName = propertyName;
    }

    public IAccessorDecorator WithType(IType propertyType)
    {
        m_PropertyType = propertyType;
        return this;
    }

    public IAccessorDecorator WithType(Type propertyType)
    {
        m_PropertyType = propertyType.ToGneedleType();
        return this;
    }

    public IAccessorDecorator WithGetter(DefaultPropertyBody body)
    {
        m_GetterBody = body;
        return this;
    }

    public IAccessorDecorator WithSetter(DefaultPropertyBody body)
    {
        m_SetterBody = body;
        return this;
    }

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

        return handler;
    }

    public interface ITypeDecorator
    {
        IPropertyHandler GetHandler();
    }

    public interface IAccessorDecorator : ITypeDecorator
    {
        IAccessorDecorator WithGetter(DefaultPropertyBody body);
        IAccessorDecorator WithSetter(DefaultPropertyBody body);
    }

    public interface IPropertyTypeDecorator : IAccessorDecorator
    {
        IAccessorDecorator WithType(IType propertyType);
        IAccessorDecorator WithType(Type propertyType);
    }
}