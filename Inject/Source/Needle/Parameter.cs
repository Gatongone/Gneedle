namespace Gneedle.Inject;

/// <summary>
/// Parameter info for the method.
/// </summary>
/// <param name="name">Parameter name.</param>
/// <param name="type">Parameter type.</param>
public readonly struct Parameter(string name, IType type)
{
    /// <summary>
    /// Parameter type.
    /// </summary>
    public readonly IType Type = type;

    /// <summary>
    /// Parameter name.
    /// </summary>
    public readonly string Name = name;
}
