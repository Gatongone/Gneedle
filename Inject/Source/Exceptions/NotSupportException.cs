namespace Gneedle.Inject;

/// <summary>
/// Thrown when the library is asked for something which the runtime it runs on does not offer, which no argument of the
/// caller could make right.<para/>
/// It is a refusal like any other: what is reported of it is its message, which says what the runtime does not offer,
/// rather than the kind and the frame which a fault the weaver did not expect is reported with.
/// </summary>
public class NotSupportException(string message) : WeavingException(message);