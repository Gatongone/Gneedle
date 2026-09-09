namespace Gneedle.Inject;

/// <summary>
/// Represents the default method body behavior.
/// </summary>
public enum DefaultMethodBody
{
    CallFromBase      = 1,
    ThrowException    = 2,
    WithDefaultReturn = 3
}

/// <summary>
/// Represents the default property body behavior.
/// </summary>
public enum DefaultPropertyBody
{
    CallFromBase       = 1,
    ThrowException     = 2,
    WithDefaultReturn  = 3,
    WithFieldOperation = 4
}