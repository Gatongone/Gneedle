namespace Gneedle.Inject;

/// <summary>
/// Handler for an enum type definition, providing access to the underlying Cecil type.
/// </summary>
internal class EnumHandler(AssemblyHandler assemblyHandler, TypeDefinition source) : TypeHandler(assemblyHandler, source), IEnumHandler
{
    /// <inheritdoc/>
    public IFieldHandler AddEnum(string enumName, long value)
    {
        var field = new FieldDefinition(enumName, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, Source.Fields.First(f => f.Name == "value__").FieldType);
        field.Constant = value;
        Source.Fields.Add(field);
        return new FieldHandler(field, this);
    }

    /// <inheritdoc/>
    public IFieldHandler? GetEnum(string enumName)
    {
        var field = Source.Fields.FirstOrDefault(f => f.Name == enumName && f.IsStatic && f.IsLiteral);
        return field == null ? null : new FieldHandler(field, this);
    }
}