namespace Gneedle.Aspect;

/// <summary>
/// The messages which the tasks of this package report to the build which ran them.<para/>
/// A message names the assembly or the project which the task was run on, so that what a build reports says which of
/// the projects of a solution it is about. A message which reports a failure names what went wrong as well, which is
/// what the framework said about it, so that a build which fails says more than that the task which was to weave could
/// not. They are constants rather than written where they are logged, so that what a caller reads is written in one
/// place and in one voice.
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

    /// <summary>
    /// The solution which the scan was to walk could not be read, so no project of it was walked. The first placeholder
    /// is the path of the solution, and the second is what went wrong.
    /// </summary>
    internal const string SOLUTION_NOT_READ = "The solution could not be read, so none of the projects it groups was woven. Solution: {0}. {1}";

    /// <summary>
    /// The project which the scan was to write could not be read or written, and the projects after it were walked all
    /// the same. The first placeholder is the path of the project, and the second is what went wrong.
    /// </summary>
    internal const string PROJECT_NOT_WOVEN = "The project could not be given the target which weaves, and the projects which follow it were walked all the same. Project: {0}. {1}";

    /// <summary>
    /// The project which the task was to weave against could not be read, so whether it turns the aspect off could not
    /// be told. The first placeholder is the path of the project, and the second is what went wrong.
    /// </summary>
    internal const string PROJECT_NOT_READ = "The project could not be read, so whether it turns the aspect off could not be told. Project: {0}. {1}";

    /// <summary>
    /// The assembly which the project built could not be woven, and the file was left as the build wrote it. The first
    /// placeholder is the path of the assembly, and the second is what went wrong.
    /// </summary>
    internal const string ASSEMBLY_NOT_WOVEN = "The assembly could not be woven and was left as the build wrote it. Assembly: {0}. {1}";

    /// <summary>
    /// The symbols which lie beside the assembly which was replaced could not be taken away, so they are left beside an
    /// image which they do not describe. The first placeholder is the path of the symbols, and the second is what went
    /// wrong.
    /// </summary>
    internal const string SYMBOLS_LEFT = "The symbols of the assembly which was replaced could not be taken away and are left beside it. Symbols: {0}. {1}";
}