using Gneedle.Inject;

namespace Gneedle.Aspect;

public sealed class AssemblyInject : Microsoft.Build.Utilities.Task
{
    [Required] public string ProjectPath { get; private set; }
    [Required] public string TargetPath { get; private set; }

    /// <summary>
    /// Whether the attributes which the injectors are read from, and the reference to the weaver which they name, are
    /// kept in the assembly which is woven.<para/>
    /// They are removed by default, so that the assembly which was woven does not carry the weaver. A project which
    /// declares its attributes for another project to weave with keeps them, which it asks for with the property
    /// <c>KeepWeaver</c>. The value is read as the text of that property, so that a project which was never given
    /// one keeps nothing.
    /// </summary>
    public string? KeepWeaver { get; set; }

    public override bool Execute()
    {
        var project = ProjectRootElement.Open(ProjectPath);
        if (project == null) return false;

        if (project.VerifyAspectDisable()) return true;

        Log.LogMessageFromText($"Inject assembly: {TargetPath}", MessageImportance.High);
        if (InjectAssemblies(TargetPath, KeepsTheWeaver()))
        {
            Log.LogMessageFromText($"Inject assembly: {TargetPath} success.", MessageImportance.High);
        }
        else
        {
            Log.LogMessageFromText($"Inject assembly: {TargetPath} no changes.", MessageImportance.High);
        }

        // The project is only read, to tell whether the aspect is disabled, so it is not saved back. A member which an
        // injector named and the assembly does not hold is reported as an error, and the task fails with it rather than
        // reporting a build which carried on.
        return !Log.HasLoggedErrors;
    }

    /// <summary>
    /// Whether the project asked for the weaver to be kept in the assembly which is woven.
    /// </summary>
    private bool KeepsTheWeaver() => string.Equals(KeepWeaver, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Apply the injectors which the target assembly declares to it.
    /// </summary>
    /// <param name="assemblyPath">Path of the assembly which is woven.</param>
    /// <param name="keepsTheWeaver">Whether the attributes and the reference to the weaver are kept in it.</param>
    /// <returns>Whether the assembly was changed.</returns>
    private bool InjectAssemblies(string assemblyPath, bool keepsTheWeaver)
    {
        // The image is read into bytes and the assembly is loaded from those bytes, which keeps the file to the caller:
        // a reader which holds the file leaves the write which follows nowhere to go. The weaving itself, which any
        // driver of the library does the same way, is asked of the library.
        var image = File.ReadAllBytes(assemblyPath);
        var runtimeAssembly = AssemblyLoader.LoadFromBytes(image);

        var (changed, result) = Injections.Apply(runtimeAssembly, image, !keepsTheWeaver, message => Log.LogError(message));
        if (changed) File.WriteAllBytes(assemblyPath, result);

        return changed;
    }
}
