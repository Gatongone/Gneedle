namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for a struct of the metadata which is built.
/// </summary>
/// <param name="assemblyHandler">Handler of the assembly which declares the struct.</param>
/// <param name="source">The struct definition which is handled.</param>
internal class StructHandler(AssemblyHandler assemblyHandler, TypeDefinition source) : TypeHandler(assemblyHandler, source), IStructHandler
{
    /// <inheritdoc/>
    public ClassDecorator AddNestedClass(string typeName, ClassFlags flags = ClassFlags.Public)
        => AssemblyHandler.AddNestedClass(Source, typeName, flags);

    /// <inheritdoc/>
    public StructDecorator AddNestedStruct(string typeName, StructFlags flags = StructFlags.Public)
        => AssemblyHandler.AddNestedStruct(Source, typeName, flags);

    /// <inheritdoc/>
    public EnumDecorator AddNestedEnum(string typeName, EnumFlags flags = EnumFlags.Public)
        => AssemblyHandler.AddNestedEnum(Source, typeName, flags);

    /// <inheritdoc/>
    public StructFlags Flags => Source.ToStructFlags();
}
