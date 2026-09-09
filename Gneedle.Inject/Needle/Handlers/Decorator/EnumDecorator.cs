namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing an enum, following the chainable pattern of
/// <see cref="ClassDecorator"/>. Create via <c>AssemblyHandler.AddEnum()</c>.
/// </summary>
public class EnumDecorator : EnumDecorator.IEnumTypeDecorator
{
    private readonly AssemblyHandler m_AssemblyHandler;
    private readonly TypeDefinition m_TypeDefinition;
    private TypeReference m_UnderlyingType;
    private bool m_WithFlagsAttribute;

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
        // Set base type to System.Enum.
        var enumBaseType = m_AssemblyHandler.GetCecilType(typeof(Enum)).Reference;
        m_TypeDefinition.BaseType = m_AssemblyHandler.Assembly.Source.MainModule.ImportReference(enumBaseType);

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

        return new EnumHandler(m_AssemblyHandler, m_TypeDefinition);
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