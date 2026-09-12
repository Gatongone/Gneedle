namespace Gneedle.Aspect;

/// <summary>
/// The names which the package and the project it weaves agree on: the two properties which a project sets to say how
/// it is woven, and the target which the package writes into a project in order to weave it.
/// </summary>
internal static class TaskConstants
{
    /// <summary>
    /// Name of the target which runs the task that weaves, and which the package writes into a project that refers to
    /// it. It is also what a project which no longer refers to the package is looked for by, so that the target is
    /// taken back out of it.
    /// </summary>
    public const string TARGET = "GneedleTarget";

    /// <summary>
    /// Name of the property which a project sets to <c>disable</c> to have the aspect left out of it, and which is read
    /// for nothing else: any other value leaves the aspect on.
    /// </summary>
    public const string TOGGLE = "Aspect";

    /// <summary>
    /// Name of the property which a project sets to <c>true</c> to keep the attributes of the injectors and the
    /// reference to the weaver in the assembly which is woven, which a project that declares its attributes for another
    /// project to weave with asks for.
    /// </summary>
    public const string KEEP   = "KeepWeaver";
}