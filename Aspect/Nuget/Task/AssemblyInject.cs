using Gneedle.Inject;

namespace Gneedle.Aspect;

/// <summary>
/// The build task which weaves the assembly a project built: it reads the assembly from the file it was written to,
/// applies the injectors which the assembly declares to it, and writes the result back over that file.<para/>
/// The task is run by the target which the package writes into a project, and so are its two properties read, so that
/// weaving is a part of the build that produced the assembly rather than a step of its own after it.
/// </summary>
public sealed class AssemblyInject : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// What the file which the woven image is written to before it is put in place of the assembly is named by, which
    /// is beside that assembly and is taken away again with the write.
    /// </summary>
    private const string WrittenSuffix = ".gneedle";

    /// <summary>
    /// The extensions which the symbols of an assembly lie beside it under, which are the formats which the weaver
    /// reads and writes.
    /// </summary>
    private static readonly string[] SymbolExtensions = {".pdb", ".mdb"};

    /// <summary>
    /// Path of the project which was built, which is read to tell whether that project turns the aspect off. The
    /// project is not written to.
    /// </summary>
    [Required] public string ProjectPath { get; private set; }

    /// <summary>
    /// Path of the assembly which the project built, which is the file which the weaving reads and writes back.
    /// </summary>
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

    /// <inheritdoc/>
    /// <remarks>
    /// The assembly is written back only when an injector changed it, so a project which declares none, and one which
    /// turns the aspect off, are answered with the assembly they built. A member which an injector names and the
    /// assembly does not hold is reported as an error, and the task fails on it rather than reporting a build which
    /// carried on with an assembly which was woven only in part. What the task cannot do at all, which is to read the
    /// project or to read or write the assembly, is reported rather than thrown at the build: a task which throws
    /// fails the build with a message of its own, which says nothing of the project or of the assembly it was run on.
    /// </remarks>
    public override bool Execute()
    {
        ProjectRootElement project;
        try
        {
            project = ProjectRootElement.Open(ProjectPath);
        }
        catch (Exception exception)
        {
            // Nothing can be woven without telling whether the project turns the aspect off, which is what the project
            // is read for.
            Log.LogError(string.Format(TaskMessages.PROJECT_NOT_READ, ProjectPath, exception.Message));
            return false;
        }

        if (project.VerifyAspectDisable()) return true;

        Log.LogMessageFromText(string.Format(TaskMessages.WEAVING_ASSEMBLY, TargetPath), MessageImportance.High);
        try
        {
            if (InjectAssemblies(TargetPath, KeepsTheWeaver()))
            {
                Log.LogMessageFromText(string.Format(TaskMessages.ASSEMBLY_WOVEN, TargetPath), MessageImportance.High);
            }
            else
            {
                Log.LogMessageFromText(string.Format(TaskMessages.ASSEMBLY_LEFT_AS_IT_WAS, TargetPath), MessageImportance.High);
            }
        }
        catch (Exception exception)
        {
            // The image is put in place of the file it was read from as one step, so an assembly which could not be
            // woven is the one which the build wrote rather than one which was written in part.
            Log.LogError(string.Format(TaskMessages.ASSEMBLY_NOT_WOVEN, TargetPath, exception.Message));
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
        // driver of the library does the same way, is asked of the library. The folder which the assembly lies in is
        // handed over with it, because that is where the build put the assemblies it refers to, and the process which
        // runs the task is a node of the build which knows nothing of the project which was built: an attribute, and the
        // template which it names, may both be of an assembly of another project which declares them for this one.
        var image = File.ReadAllBytes(assemblyPath);
        var runtimeAssembly = AssemblyLoader.LoadFromBytes(image);

        var (changed, result) = Injections.Apply(runtimeAssembly, image, !keepsTheWeaver, message => Log.LogError(message),
                                                 searchDirectory: Path.GetDirectoryName(assemblyPath));
        if (!changed) return false;

        Write(assemblyPath, result);
        RemoveSymbols(assemblyPath);
        return true;
    }

    /// <summary>
    /// Put the image which was woven in the place of the assembly which was read, as one step.<para/>
    /// The image is written beside the assembly and is then put in its place, so that a build which is stopped while
    /// the write is going on, or which cannot make it, finds the assembly it built rather than one which was written
    /// only in part.
    /// </summary>
    /// <param name="assemblyPath">Path of the assembly which is written over.</param>
    /// <param name="image">The bytes of the image which was woven.</param>
    private static void Write(string assemblyPath, byte[] image)
    {
        var written = assemblyPath + WrittenSuffix;
        // The write lies within the step which takes the image away as well, because an image which the write could not
        // make in full is one which is beside the assembly without being in its place, which is what nothing of it is
        // to be left as: a file which was written only in part is taken away by the same step which takes away one
        // which could not be put in place at all.
        try
        {
            File.WriteAllBytes(written, image);
            File.Replace(written, assemblyPath, destinationBackupFileName: null);
        }
        finally
        {
            // An image which was written and could not be put in place is taken away, so that nothing of it is left
            // beside the assembly which the build wrote.
            if (File.Exists(written)) File.Delete(written);
        }
    }

    /// <summary>
    /// Take away the symbols which lie beside an assembly whose image was just written over.<para/>
    /// The image which the weaver writes holds no debug directory, because it writes the image alone and the symbols
    /// which were compiled with it are not woven: a reader goes by that directory to find the symbols of an assembly,
    /// so the symbols which were compiled for the assembly which was replaced describe an image which is no longer
    /// there. They are taken away rather than left, because a reader which goes by the name of the file alone would be
    /// given them for an image which they do not describe.
    /// </summary>
    /// <param name="assemblyPath">Path of the assembly whose symbols are taken away.</param>
    private void RemoveSymbols(string assemblyPath)
    {
        foreach (var extension in SymbolExtensions)
        {
            var symbols = Path.ChangeExtension(assemblyPath, extension);
            if (!File.Exists(symbols)) continue;

            try
            {
                File.Delete(symbols);
            }
            catch (Exception exception)
            {
                // The assembly was woven and is in place, which is what the build asked for: symbols which stay beside
                // it are worth telling about and are not a failure of the weaving.
                Log.LogWarning(string.Format(TaskMessages.SYMBOLS_LEFT, symbols, exception.Message));
            }
        }
    }
}