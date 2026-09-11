namespace Gneedle.Inject;

/// <summary>
/// Parameter info for the method.
/// </summary>
/// <param name="name">Parameter name.</param>
/// <param name="type">Parameter type.</param>
public readonly struct Parameter(string name, IType type)
{
    /// <summary>
    /// Create the info of a parameter which holds no name.<para/>
    /// The name reaches the metadata of the produced method alone, where it is what a named argument names, what a
    /// debugger shows and what a stack trace prints. It takes no part in weaving, because the parameter of a template is
    /// matched to the parameter of the same position rather than to the one of the same name.
    /// </summary>
    /// <param name="type">Parameter type.</param>
    public Parameter(IType type) : this(string.Empty, type) { }

    /// <summary>
    /// Parameter type.
    /// </summary>
    public readonly IType Type = type;

    /// <summary>
    /// Parameter name.
    /// </summary>
    public readonly string Name = name;
}
