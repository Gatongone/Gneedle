namespace Gneedle.Inject;

/// <summary>
/// Thrown when the library is asked for something which the runtime it runs on does not offer, which no argument of the
/// caller could make right.
/// </summary>
public class NotSupportException(string message) : Exception(message);