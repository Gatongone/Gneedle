namespace Gneedle.Inject;

/// <summary>
/// Thrown when the instructions which a template compiles a symbol of it into are not the shape which the weaving
/// reads, so the member which the symbol names cannot be woven in.
/// </summary>
public class InvalidILException : Exception
{
    /// <summary>
    /// Create the exception which names no member, which is what a caller which does not know one throws.
    /// </summary>
    public InvalidILException() : base(ErrorMessages.INVALID_IL) { }

    /// <summary>
    /// Create the exception which names the member whose instructions were not read.
    /// </summary>
    /// <param name="message">What is reported, which is built from <see cref="ErrorMessages.INVALID_IL"/>.</param>
    public InvalidILException(string message) : base(message) { }
}