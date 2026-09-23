namespace Gneedle.Inject;

/// <summary>
/// Thrown when the instructions which a template compiles a symbol of it into are not the shape which the weaving
/// reads, so the member which the symbol names cannot be woven in.
/// </summary>
public class InvalidILException : WeavingException
{
    /// <summary>
    /// Create the exception which names the member whose instructions were not read.<para/>
    /// The message is built from <see cref="ErrorMessages.INVALID_IL"/>, which names the member in a placeholder, so an
    /// exception which is made without one is refused here rather than raised with a placeholder written into it: what
    /// a reader of it would be shown is <c>Member: {0}.</c>, which names nothing.
    /// </summary>
    /// <param name="message">What is reported, which is built from <see cref="ErrorMessages.INVALID_IL"/>.</param>
    public InvalidILException(string message) : base(message) { }
}