namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing an enum, following the chainable pattern of
/// <see cref="ClassDecorator"/>. Create via <c>AssemblyHandler.AddEnum()</c>.
/// </summary>
public class EnumDecorator : EnumDecorator.IEnumTypeDecorator
{
    /// <summary>
    /// Handler of the assembly which the enum belongs to, which the members of the enum are added through.
    /// </summary>
    private readonly AssemblyHandler m_AssemblyHandler;

    /// <summary>
    /// The enum which the chain describes, which is the definition the members are appended to.
    /// </summary>
    private readonly TypeDefinition m_TypeDefinition;

    /// <summary>
    /// Type of the values of the enum.
    /// </summary>
    private TypeReference m_UnderlyingType;

    /// <summary>
    /// Whether the enum is given <see cref="FlagsAttribute"/>, which says that its values are bit flags which combine.
    /// </summary>
    private bool m_WithFlagsAttribute;

    /// <summary>
    /// Create a decorator which describes an enum before it is appended to the module.
    /// </summary>
    /// <param name="assemblyHandler">Handler of the assembly which the enum is appended to.</param>
    /// <param name="typeDefinition">The enum definition which is described.</param>
    /// <param name="underlyingType">Type of the values of the enum.</param>
    internal EnumDecorator(AssemblyHandler assemblyHandler, TypeDefinition typeDefinition, TypeReference underlyingType)
    {
        m_AssemblyHandler = assemblyHandler;
        m_TypeDefinition  = typeDefinition;
        m_UnderlyingType  = underlyingType;
    }

    /// <inheritdoc/>
    public IEnumTypeDecorator WithUnderlyingType(IType underlyingType)
    {
        m_UnderlyingType = underlyingType switch
        {
            NongenericType nongeneric => m_AssemblyHandler.GetCecilType(nongeneric.Type).Reference,
            _ => m_UnderlyingType
        };
        return this;
    }

    /// <inheritdoc/>
    public IEnumTypeDecorator WithUnderlyingType(Type underlyingType)
    {
        m_UnderlyingType = m_AssemblyHandler.GetCecilType(underlyingType).Reference;
        return this;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithFlagsAttribute()
    {
        m_WithFlagsAttribute = true;
        return this;
    }

    /// <inheritdoc/>
    public IEnumHandler GetHandler()
    {
        // Set base type to System.Enum. Its reference is owned by the target module already, so it is appended as it is.
        m_TypeDefinition.BaseType = m_AssemblyHandler.GetCecilType(typeof(Enum)).Reference;

        // Add the special value__ instance field of the underlying type.
        var valueField = new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, m_UnderlyingType);
        m_TypeDefinition.Fields.Add(valueField);

        // Add the [Flags] attribute if requested.
        if (m_WithFlagsAttribute)
        {
            var flagsDef = m_AssemblyHandler.GetCecilType(typeof(FlagsAttribute)).Definition;
            var attribute = flagsDef.CreateCustomAttribute(m_AssemblyHandler.Assembly.Source.MainModule);
            m_TypeDefinition.CustomAttributes.Add(attribute);
        }

        // Add type to module.
        m_AssemblyHandler.Assembly.Source.MainModule.Types.Add(m_TypeDefinition);

        return new EnumHandler(m_AssemblyHandler, m_TypeDefinition, m_UnderlyingType);
    }

    /// <summary>
    /// Decorator for create type definition to current module.
    /// </summary>
    public interface ITypeDecorator
    {
        /// <summary>
        /// Build enum definition to module.
        /// </summary>
        /// <returns>Handler for enum.</returns>
        IEnumHandler GetHandler();
    }

    /// <summary>
    /// Decorator for describing enum modifiers.
    /// </summary>
    public interface IEnumTypeDecorator : ITypeDecorator
    {
        /// <summary>
        /// Append <see cref="FlagsAttribute"/> to the enum.
        /// </summary>
        /// <returns>Result for chains calling.</returns>
        ITypeDecorator WithFlagsAttribute();

        /// <summary>
        /// Set the underlying type of the enum from <see cref="IType"/>.
        /// </summary>
        /// <param name="underlyingType">Underlying type.</param>
        /// <returns>Result for chains calling.</returns>
        IEnumTypeDecorator WithUnderlyingType(IType underlyingType);

        /// <summary>
        /// Set the underlying type of the enum from <see cref="System.Type"/>.
        /// </summary>
        /// <param name="underlyingType">Underlying type.</param>
        /// <returns>Result for chains calling.</returns>
        IEnumTypeDecorator WithUnderlyingType(Type underlyingType);
    }
}