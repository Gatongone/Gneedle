namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for an assembly, which resolves the types which it declares, adds new ones to it, and carries
/// the attributes of the assembly itself.<para/>
/// The attributes which are read and written here are the ones of the assembly, which an injector of the assembly
/// itself is read from and which are put on the module rather than on a member of it.
/// </summary>
public interface IAssemblyHandler : IAttributeContainer
{
    /// <summary>
    /// Append a reference to <paramref name="targetAssembly"/> to the assembly which is handled.
    /// </summary>
    /// <param name="targetAssembly">The assembly which need to be referenced.</param>
    /// <exception cref="ArgumentException">Thrown when the target assembly references the handled one, which cannot be represented in metadata.</exception>
    void AddReference(Assembly targetAssembly);

    /// <summary>
    /// Get the handler of the type of the given full name, or null when no type of that name is declared.
    /// </summary>
    /// <param name="typeFullName">Full name of the type, which names a nested one as <c>Namespace.Outer/Namespace.Inner</c>.</param>
    /// <returns>The handler of the type, or null.</returns>
    ITypeHandler? GetType(string typeFullName);

    /// <summary>
    /// Get the handler of the type which the given runtime type stands for.
    /// </summary>
    /// <param name="type">The type which need to be handled.</param>
    /// <returns>The handler of the type.</returns>
    ITypeHandler GetType(Type type);

    /// <summary>
    /// Get the handlers of every type which the assembly declares, the nested types included.
    /// </summary>
    /// <returns>The handlers of the types.</returns>
    ITypeHandler[] GetTypes();

    /// <summary>
    /// Start building a class through a chainable <see cref="ClassDecorator"/>.
    /// </summary>
    /// <param name="typeName">Name of the class.</param>
    /// <param name="typeNamespace">Namespace of the class.</param>
    /// <param name="flags">Flags of the class.</param>
    /// <returns>Result for chains calling.</returns>
    ClassDecorator AddClass(string typeName, string typeNamespace , ClassFlags flags = ClassFlags.Public);

    /// <summary>
    /// Start building a struct through a chainable <see cref="StructDecorator"/>.
    /// </summary>
    /// <param name="className">Name of the struct.</param>
    /// <param name="typeNamespace">Namespace of the struct.</param>
    /// <param name="flags">Flags of the struct.</param>
    /// <returns>Result for chains calling.</returns>
    StructDecorator AddStruct(string className, string typeNamespace, StructFlags flags = StructFlags.Public);

    /// <summary>
    /// Start building an enum through a chainable <see cref="EnumDecorator"/>.
    /// </summary>
    /// <param name="enumName">Name of the enum.</param>
    /// <param name="typeNamespace">Namespace of the enum.</param>
    /// <param name="flags">Flags of the enum.</param>
    /// <returns>Result for chains calling.</returns>
    EnumDecorator AddEnum(string enumName, string typeNamespace, EnumFlags flags = EnumFlags.Public);
}