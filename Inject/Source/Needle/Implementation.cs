namespace Gneedle.Inject;

/// <summary>
/// Implementation info for the target class.
/// </summary>
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