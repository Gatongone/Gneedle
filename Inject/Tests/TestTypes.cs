namespace Gneedle.Inject.Test;

/// <summary>
/// The interface which a type under test is given, whether through the chain which describes a type being added or
/// through the container which a type already there is handled by. It is declared here rather than by either of them,
/// because both ask for it.
/// </summary>
public interface ITestInterface;

/// <summary>
/// The base type which a type under test is derived from.
/// </summary>
public class TestBaseClass;
