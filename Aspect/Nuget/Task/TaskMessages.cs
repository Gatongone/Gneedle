namespace Gneedle.Aspect;

/// <summary>
/// The messages which the tasks of this package report to the build which ran them.<para/>
/// A message names the assembly or the project which the task was run on, so that what a build reports says which of
/// the projects of a solution it is about. They are constants rather than written where they are logged, so that what a
/// caller reads is written in one place and in one voice.
/// </summary>
internal static class TaskMessages
{
    /// <summary>
    /// The task has begun on the assembly which a project built. The placeholder is the path of the assembly.
    /// </summary>
    internal const string WEAVING_ASSEMBLY = "Weaving the assembly which the project built. Assembly: {0}";

    /// <summary>
    /// The injectors which the assembly declares were applied to it, and it was written back. The placeholder is the
    /// path of the assembly.
    /// </summary>
    internal const string ASSEMBLY_WOVEN = "The injectors which the assembly declares were applied to it. Assembly: {0}";

    /// <summary>
    /// No injector changed the assembly, so the file was left as the build wrote it. The placeholder is the path of the
    /// assembly.
    /// </summary>
    internal const string ASSEMBLY_LEFT_AS_IT_WAS = "The assembly declares no injector which changed it, so it was left as it was. Assembly: {0}";

    /// <summary>
    /// The target which weaves was written into a project which refers to the weaver. The placeholder is the name of
    /// the project.
    /// </summary>
    internal const string TARGET_WRITTEN = "The target which weaves was written into the project. Project: {0}";

    /// <summary>
    /// The target which weaves was taken back out of a project which no longer refers to the weaver, or which turns the
    /// aspect off. The placeholder is the name of the project.
    /// </summary>
    internal const string TARGET_TAKEN_OUT = "The target which weaves was taken out of the project. Project: {0}";
}
