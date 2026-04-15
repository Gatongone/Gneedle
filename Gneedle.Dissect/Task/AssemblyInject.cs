namespace Gneedle.Dissect;

public sealed class AssemblyInject : Microsoft.Build.Utilities.Task
{
    [Required] public string ProjectPath { get; private set; }
    [Required] public string TargetPath { get; private set; }

    public override bool Execute()
    {
        var project = ProjectRootElement.Open(ProjectPath);
        if (project == null) return false;

        if (project.VerifyDissectDisable()) return true;

        Log.LogMessageFromText($"Inject assembly: {TargetPath}", MessageImportance.High);
        InjectAssemblies();
        project.Save();
        return true;
    }

    private void InjectAssemblies() { }
}