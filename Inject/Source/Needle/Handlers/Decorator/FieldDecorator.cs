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
    private IType m_FieldType = typeof(object).ToIType();

    /// <summary>
    /// The handler of the field which the chain built, or null while the field is still being described: the chain
    /// answers with it from the point where it ends, which is what makes a chain which is asked for the handler of the
    /// field twice build one field rather than two of the same name.
    /// </summary>
    private IFieldHandler? m_Handler;

    /// <summary>
    /// Create a decorator which describes a field before it is appended to the module.
    /// </summary>
    /// <param name="typeHandler">Handler of the type which the field is appended to.</param>
    /// <param name="fieldName">Name of the field.</param>
    /// <param name="fieldFlags">Flags of the field.</param>
    internal FieldDecorator(TypeHandler typeHandler, string fieldName, FieldFlags fieldFlags)
    {
        m_TypeHandler = typeHandler;
        m_FieldName = fieldName;
        m_FieldFlags = fieldFlags;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithType(IType fieldType)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_FieldName);
        m_FieldType = fieldType;
        return this;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithType(Type fieldType)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_FieldName);
        m_FieldType = fieldType.ToIType();
        return this;
    }

    /// <inheritdoc/>
    public IFieldHandler GetHandler()
    {
        // The field is appended to the type once, and the chain answers with the handler of it from then on: a chain
        // which is asked for the handler of the field twice builds one field rather than two of the same name.
        if (m_Handler is { } built) return built;

        var fieldType = m_TypeHandler.AssemblyHandler.ResolveParameterType(m_TypeHandler.Source, m_FieldType);
        var attrs = m_FieldFlags.ToFieldAttributes();
        var fieldDef = new FieldDefinition(m_FieldName, attrs, fieldType);
        ModuleLock.DeclareMember(m_TypeHandler.Source.Module, m_TypeHandler.Source, fieldDef);

        m_Handler = new FieldHandler(fieldDef, m_TypeHandler);
        return m_Handler;
    }

    /// <summary>
    /// Decorator which completes the field. It is the end of the chain, which asks for the handler of the field which
    /// the chain built and for nothing else.
    /// </summary>
    public interface ITypeDecorator
    {
        /// <summary>
        /// Build the field into the module and answer with the handler of the field which was built.<para/>
        /// The field is built once: the chain answers with the handler of it from then on, and a part which is described
        /// after that point is refused.
        /// </summary>
        /// <returns>Handler for field.</returns>
        IFieldHandler GetHandler();
    }

    /// <summary>
    /// Decorator for describing field type. It is the entry of the chain, which holds a level for each part of a field
    /// in the order the parts depend on each other: the type, and then nothing but the end of the chain.<para/>
    /// The chain describes the field until the field is built, which is where it ends: a part which is described after
    /// that is refused, because what the chain holds is read where the field is built and nothing reads it afterwards.
    /// The flags are given to <c>ITypeHandler.AddField(name, flags)</c>.
    /// </summary>
    public interface IFieldTypeDecorator : ITypeDecorator
    {
        /// <summary>
        /// Append field type from <see cref="IType"/>.
        /// </summary>
        /// <param name="fieldType">Field type of type.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the field was already built.</exception>
        ITypeDecorator WithType(IType fieldType);

        /// <summary>
        /// Append field type from <see cref="System.Type"/>.
        /// </summary>
        /// <param name="fieldType">Field type of type.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the field was already built.</exception>
        ITypeDecorator WithType(Type fieldType);
    }
}