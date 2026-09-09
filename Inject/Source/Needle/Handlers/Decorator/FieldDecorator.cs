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
    private FieldFlags m_FieldFlags = FieldFlags.Public;

    internal FieldDecorator(TypeHandler typeHandler, string fieldName)
    {
        m_TypeHandler = typeHandler;
        m_FieldName   = fieldName;
    }

    /// <inheritdoc/>
    public IModifierDecorator WithType(IType fieldType)
    {
        m_FieldType = fieldType;
        return this;
    }

    /// <inheritdoc/>
    public IModifierDecorator WithType(Type fieldType)
    {
        m_FieldType = fieldType.ToGneedleType();
        return this;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithFlags(FieldFlags fieldFlags)
    {
        m_FieldFlags = fieldFlags;
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
    /// Decorator for create type definition to current module.
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
    /// Decorator for describing field modifier.
    /// </summary>
    public interface IModifierDecorator : ITypeDecorator
    {
        /// <summary>
        /// Set field flags.
        /// </summary>
        /// <param name="fieldFlags">Field flags.</param>
        /// <returns>Result for chains calling.</returns>
        ITypeDecorator WithFlags(FieldFlags fieldFlags);
    }

    /// <summary>
    /// Decorator for describing field type.
    /// </summary>
    public interface IFieldTypeDecorator : IModifierDecorator
    {
        /// <summary>
        /// Append field type from <see cref="IType"/>.
        /// </summary>
        /// <param name="fieldType">Field type of type.</param>
        /// <returns>Result for chains calling.</returns>
        IModifierDecorator WithType(IType fieldType);

        /// <summary>
        /// Append field type from <see cref="System.Type"/>.
        /// </summary>
        /// <param name="fieldType">Field type of type.</param>
        /// <returns>Result for chains calling.</returns>
        IModifierDecorator WithType(Type fieldType);
    }
}