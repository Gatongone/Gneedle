namespace Gneedle.Inject;

/// <summary>
/// Handler for an enum type definition, providing access to the underlying Cecil type.
/// </summary>
internal class EnumHandler(AssemblyHandler assemblyHandler, TypeDefinition source, TypeReference underlyingType) : TypeHandler(assemblyHandler, source), IEnumHandler
{
    private const FieldAttributes ENUM_MEMBER_ATTRS = FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault;

    /// <inheritdoc/>
    public Type UnderlyingType { get; } = Type.GetType(underlyingType.FullName)!;

    /// <inheritdoc/>
    public void AddEnum(string enumName, object value)
    {
        var field = new FieldDefinition(enumName, ENUM_MEMBER_ATTRS, Source.Fields.First(f => f.Name == "value__").FieldType)
        {
            Constant = value
        };
        VerifyUnderlyingType(value);
        Source.Fields.Add(field);
    }

    /// <inheritdoc/>
    public object? GetEnum(string enumName)
    {
        var field = Source.Fields.FirstOrDefault(f => f.Name == enumName && f is {IsStatic: true, IsLiteral: true});
        return field?.Constant;
    }

    /// <summary>
    /// Verify that the provided value is of a valid underlying type for an enum.
    /// </summary>
    /// <param name="value">The value to verify.</param>
    /// <exception cref="ArgumentException">Thrown when the value is not of a valid underlying type.</exception>
    private static void VerifyUnderlyingType(object value)
    {
        if (value is not (sbyte or byte or short or ushort or int or uint or long or ulong))
        {
            throw new ArgumentException($"Invalid enum underlying type: {value.GetType().Name}. Must be one of: sbyte, byte, short, ushort, int, uint, long, ulong.");
        }
    }
}