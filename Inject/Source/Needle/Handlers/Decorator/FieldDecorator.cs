using Mono.Cecil;

namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing a field, following the chainable pattern of
/// <see cref="ClassDecorator"/>. Create via <c>ITypeHandler.AddField(name, flags)</c>.
/// </summary>
public class FieldDecorator : FieldDecorator.IFieldTypeDecorator
{
    /// <summary>
    /// Handler of the type which the field is appended to when the chain ends.
    /// </summary>
    private readonly TypeHandler m_TypeHandler;

    /// <summary>
    /// Name of the field which the chain describes.
    /// </summary>
    private readonly string m_FieldName;

    /// <summary>
    /// Flags of the field which the chain describes.
    /// </summary>
    private readonly FieldFlags m_FieldFlags;

    /// <summary>
    /// Type of the value which the field holds, which is <see cref="object"/> until another is asked for.
    /// </summary>
    private IType m_FieldType = typeof(object).ToGneedleType();

    /// <summary>
    /// Create a decorator which describes a field before it is appended to the module.
    /// </summary>
    /// <param name="typeHandler">Handler of the type which the field is appended to.</param>
    /// <param name="fieldName">Name of the field.</param>
    /// <param name="fieldFlags">Flags of the field.</param>
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
