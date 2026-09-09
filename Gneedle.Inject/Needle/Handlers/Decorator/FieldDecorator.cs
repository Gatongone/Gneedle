using Mono.Cecil;

namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing a field, following the chainable pattern of
/// <see cref="ClassDecorator"/>. Create via <c>ITypeHandler.AddField(name)</c>.
/// </summary>
public class FieldDecorator : FieldDecorator.IFieldTypeDecorator
{
    private readonly TypeHandler m_TypeHandler;
    private readonly string m_FieldName;
    private IType m_FieldType = typeof(object).ToGneedleType();
    private bool m_IsStatic;
    private FieldAttributes m_Attributes = FieldAttributes.Public;

    internal FieldDecorator(TypeHandler typeHandler, string fieldName)
    {
        m_TypeHandler = typeHandler;
        m_FieldName   = fieldName;
    }

    public IModifierDecorator WithType(IType fieldType)
    {
        m_FieldType = fieldType;
        return this;
    }

    public IModifierDecorator WithType(Type fieldType)
    {
        m_FieldType = fieldType.ToGneedleType();
        return this;
    }

    public ITypeDecorator AsStatic()
    {
        m_IsStatic = true;
        return this;
    }

    public ITypeDecorator WithAttributes(FieldAttributes attributes)
    {
        m_Attributes = attributes;
        return this;
    }

    public IFieldHandler GetHandler()
    {
        var fieldType = m_TypeHandler.AssemblyHandler.ResolveParameterType(m_TypeHandler.Source, m_FieldType);
        var attrs = m_Attributes | (m_IsStatic ? FieldAttributes.Static : 0);
        var fieldDef = new FieldDefinition(m_FieldName, attrs, fieldType);
        m_TypeHandler.Source.Fields.Add(fieldDef);
        return new FieldHandler(fieldDef, m_TypeHandler);
    }

    public interface ITypeDecorator
    {
        IFieldHandler GetHandler();
    }

    public interface IModifierDecorator : ITypeDecorator
    {
        ITypeDecorator AsStatic();
        ITypeDecorator WithAttributes(FieldAttributes attributes);
    }

    public interface IFieldTypeDecorator : IModifierDecorator
    {
        IModifierDecorator WithType(IType fieldType);
        IModifierDecorator WithType(Type fieldType);
    }
}