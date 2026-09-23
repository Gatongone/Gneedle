namespace Gneedle.Inject;

/// <summary>
/// Thrown where the weaver refused a weave, and what it refused is what it was given rather than a fault of its own:
/// a member which the assembly does not hold, a template which cannot be read, a shape which the weaving does not
/// write.<para/>
/// What the kind is for is telling a refusal from an exception which nothing expected. What is reported of a refusal is
/// its message alone, which names what was refused and what was wrong with it; of anything else the kind of the
/// exception and the frame it stands at are written with it as well, because a shape the weaver did not expect is the
/// fault a reader has the least to find it by.<para/>
/// A type of the framework which the weaving happens to raise is not a refusal, and neither is one of these read as
/// one: the <c>ArgumentException</c> of a member which a call does not describe, and the <c>NullReferenceException</c>
/// of a resolver which cannot read an assembly, are faults which the reader is given whole. What derives from this is
/// the refusals the weaver raises itself, which is why the class is one rather than the framework type it stands for:
/// an <see cref="ArgumentException"/> is raised by as much of the framework as by the weaver, so what the exception is
/// is what tells the two apart.
/// </summary>
public class WeavingException : ArgumentException
{
    /// <summary>
    /// Create the exception which names what was refused.
    /// </summary>
    /// <param name="message">What is reported, which names what was refused.</param>
    public WeavingException(string message) : base(message) { }

    /// <summary>
    /// Create the exception of an argument which the caller named and which the weaver cannot use, such as a buffer
    /// which is too small to hold what it was being given to hold.
    /// </summary>
    /// <param name="message">What is reported, which names what was refused.</param>
    /// <param name="parameterName">Name of the argument of the caller which was refused.</param>
    public WeavingException(string message, string parameterName) : base(message, parameterName) { }
}
