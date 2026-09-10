using Mono.Cecil;

namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing a field, following the chainable pattern of
/// <see cref="ClassDecorator"/>. Create via <c>ITypeHandler.AddField(name, flags)</c>.
/// </summary>
public class FieldDecorator : FieldDecorator.IFieldTypeDecorator
{
    private readonly TypeHandler m_TypeHandler;
    private readonly string m_FieldName;
    private readonly FieldFlags m_FieldFlags;
    private IType m_FieldType = typeof(object).ToGneedleType();

    internal FieldDecorator(TypeHandler typeHandler, string fieldName, FieldFlags fieldFlags)
    {
        m_TypeHandler = typeHandler;
        m_FieldName   = fieldName;
        m_FieldFlags  = fieldFlags;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithType(IType fieldType)
    {
        m_FieldType = fieldType;
        return this;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithType(Type fieldType)
    {
        m_FieldType = fieldType.ToGneedleType();
        return this;
    }

    /// <inheritdoc/>
    public IFieldHandler GetHandler()
    {
        var fieldType = m_TypeHandler.AssemblyHandler.ResolveParameterType(m_TypeHandler.Source, m_FieldType);
        var attrs = m_FieldFlags.ToFieldAttributes();
        var fieldDef = new FieldDefinition(m_FieldName, attrs, fieldType);
        m_TypeHandler.Source.Fields.Add(fieldDef);
        return new FieldHandler(fieldDef, m_TypeHandler);
    }

    /// <summary>
    /// Decorator which completes the field. It is the end of the chain.
    /// </summary>
    public interface ITypeDecorator
    {
        /// <summary>
        /// Build field definition to module.
        /// </summary>
        /// <returns>Handler for field.</returns>
        IFieldHandler GetHandler();
    }

    /// <summary>
    /// Decorator for describing field type. It is the entry of the chain. The flags are given to
    /// <c>ITypeHandler.AddField(name, flags)</c>.
    /// </summary>
    public interface IFieldTypeDecorator : ITypeDecorator
    {
        /// <summary>
        /// Append field type from <see cref="IType"/>.
        /// </summary>
        /// <param name="fieldType">Field type of type.</param>
        /// <returns>Result for chains calling.</returns>
        ITypeDecorator WithType(IType fieldType);

        /// <summary>
        /// Append field type from <see cref="System.Type"/>.
        /// </summary>
        /// <param name="fieldType">Field type of type.</param>
        /// <returns>Result for chains calling.</returns>
        ITypeDecorator WithType(Type fieldType);
    }
}
