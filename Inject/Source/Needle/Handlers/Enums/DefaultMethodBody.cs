namespace Gneedle.Inject;

/// <summary>
/// Represents the default method body behavior.
/// </summary>
public enum DefaultMethodBody
{
    /// <summary>
    /// Call the method of the base type which has the same name and parameters, and return what it returns.
    /// </summary>
    CallFromBase      = 1,

    /// <summary>
    /// Throw a <see cref="NotSupportedException"/>.
    /// </summary>
    ThrowException    = 2,

    /// <summary>
    /// Return the default value of the return type, which is null for a reference type and the zeroed value of a value
    /// type.
    /// </summary>
    WithDefaultReturn = 3
}

/// <summary>
/// Represents the default property body behavior.
/// </summary>
public enum DefaultPropertyBody
{
    /// <summary>
    /// The accessor calls the accessor of the base type which has the same name and parameters.
    /// </summary>
    CallFromBase       = 1,

    /// <summary>
    /// The accessor throws a <see cref="NotSupportedException"/>.
    /// </summary>
    ThrowException     = 2,

    /// <summary>
    /// The accessor returns the default value of its return type.<para/>
    /// A setter returns void, so the body of one which is given this only returns, and the property keeps the value it
    /// had.
    /// </summary>
    WithDefaultReturn  = 3,

    /// <summary>
    /// The accessor reads or writes a field of the declaring type, which is created
    /// by the injector and named <c>&lt;{property_name}&gt;k__BackingField</c>.<para/>
    /// An indexer cannot be read or written this way.
    /// </summary>
    WithFieldOperation = 4
}