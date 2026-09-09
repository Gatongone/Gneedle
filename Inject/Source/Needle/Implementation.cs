namespace Gneedle.Inject;

internal struct Implementation
{
    /// <summary>
    /// Base type of target class.
    /// </summary>
    public TypeReference? BaseType;

    /// <summary>
    /// Interface types of target class.
    /// </summary>
    public TypeReference[] Interfaces;
}