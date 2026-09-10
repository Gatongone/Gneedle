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
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class FromAssemblyAttribute : Attribute
{
    /// <summary>
    /// Full name of the attribute, which is used to find it in the metadata of a module.
    /// </summary>
    internal const string TYPE_NAME = $"{nameof(Gneedle)}.{nameof(Inject)}.{nameof(FromAssemblyAttribute)}";

    /// <summary>
    /// Name of the assembly which declares the real type.
    /// </summary>
    public readonly string Name;

    /// <param name="name">Assembly name</param>
    public FromAssemblyAttribute(string name) => Name = name;
}
