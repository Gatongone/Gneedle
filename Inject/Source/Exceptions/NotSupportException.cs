namespace Gneedle.Inject;

/// <summary>
/// Exception with any not supported case.
/// </summary>
public class NotSupportException(string message) : Exception(message);