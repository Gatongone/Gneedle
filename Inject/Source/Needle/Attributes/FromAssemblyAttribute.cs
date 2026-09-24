namespace Gneedle.Inject;

/// <summary>
/// Marks a type which stands for the type of the same full name which another assembly declares.<para/>
/// A template could not reference a type of the assembly which is being weaved, because the assembly which holds the
/// template is compiled before that assembly is. The type is declared as a stub of the real one instead, and the stub
/// is replaced by the real type while the template is injected.
/// </summary>
/// <example>
/// <code>
/// [FromAssembly("Gneedle.Test.Generated")]
/// public class Widget { public static int Size; }
/// </code>
/// Every reference to <c>Widget</c> is turned into a reference to the <c>Widget</c> which the assembly named
/// <c>Gneedle.Test.Generated</c> declares.
/// </example>
/// <remarks>
/// A stub of a given name is what the real type of the name is found by, so two types of one name in two namespaces
/// are two stubs of the same name in the two namespaces of them, which is what a template writes where it names either.
/// A stub which is not written in the namespace of the type it stands for is one which names that type itself:
///
/// <code>
/// [FromAssembly("Gneedle.Test.Generated", "Gneedle.Test.Generated.Widget")]
/// public class AnyName { public static int Size; }
/// </code>
///
/// The name which such a stub is declared by is then no part of what it stands for, so every stub of an assembly may
/// stand in one namespace of its own. What is named is the type of the name, which the number of generic parameters of
/// the stub is added to - a stub which declares one stands for <c>Widget`1</c>, since a type which declares any is
/// named by that number wherever it is named. A type which is nested and whose declaring type declares parameters of
/// its own is not named this way: what such a type is named by holds the parameters of every type which declares it,
/// which the stub declares one of.
/// </remarks>
/// <param name="name">Assembly name</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class FromAssemblyAttribute(string name) : Attribute
{
    /// <summary>
    /// Full name of the attribute, which is used to find it in the metadata of a module.
    /// </summary>
    internal const string TYPE_NAME = $"{nameof(Gneedle)}.{nameof(Inject)}.{nameof(FromAssemblyAttribute)}";

    /// <summary>
    /// Name of the assembly which declares the real type.
    /// </summary>
    public readonly string Name = name;

    /// <summary>
    /// The same, of a stub which names the type it stands for rather than carrying its name itself.
    /// </summary>
    /// <param name="name">Name of the assembly which declares the real type.</param>
    /// <param name="typeFullName">Full name of the real type, which the number of generic parameters which the stub
    /// declares is added to.</param>
    public FromAssemblyAttribute(string name, string typeFullName) : this(name) => TypeFullName = typeFullName;

    /// <summary>
    /// Full name of the real type where the stub names it, or null where the stub is of its name.
    /// </summary>
    public string? TypeFullName { get; }
}