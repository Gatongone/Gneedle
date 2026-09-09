namespace Gneedle.Aspect;

/// <summary>
/// Add <see cref="AssemblyInject"/> task to PostBuildEvent when the project use ProjectReference item to refer Gneedle.Aspect project with solution.
/// </summary>
public class AddProjectsPostBuild : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Gneedle.Aspect name.
    /// </summary>
    public string ProjectName { get; private set; }

    /// <summary>
    /// Path of the solution that manages Gneedle.Aspect.csproj.
    /// </summary>
    public string SolutionPath { get; private set; }

    /// <summary>
    /// Gneedle.Aspect.dll path.
    /// </summary>
    public string TargetPath { get; private set; }

    public override bool Execute()
    {
        var solution = SolutionFile.Parse(SolutionPath);
        var projects = solution.ProjectsInOrder
                               .Where(p => !ProjectName.Equals(p.ProjectName))
                               .Select(p => ProjectRootElement.Open(p.AbsolutePath));
        foreach (var targetProject in projects)
        {
            var targetProjectName = Path.GetFileNameWithoutExtension(targetProject.FullPath);
            ProcessProject(targetProject, targetProjectName);
            targetProject.Save();
        }

        return true;
    }

    private void ProcessProject(ProjectRootElement project, string projectName)
    {
        if (!VerifyProject(project))
        {
            if (CleanElements(project))
            {
                Log.LogMessageFromText($"Remove {TaskConstants.TARGET} target from {projectName}.csproj......", MessageImportance.High);
            }
            return;
        }

        if (AddElements(project))
        {
            Log.LogMessageFromText($"Add {TaskConstants.TARGET} target to {projectName}.csproj......", MessageImportance.High);
        }
    }

    private bool VerifyProject(ProjectRootElement project) => project.ContainsReference(ProjectName) && !project.VerifyAspectDisable();

    private bool AddElements(ProjectRootElement project)
    {
        var postBuild = project.RequireBuildEvent(BuildEventType.PostBuild, TaskConstants.TARGET, out var isTargetAdd);
        if (!project.UsingTasks.Any(task => task.TaskName.Equals(typeof(AssemblyInject).FullName)))
        {
            isTargetAdd = true;
            project.AddUsingTask($"{ProjectName}.{nameof(AssemblyInject)}", TargetPath, string.Empty);
        }

        var injectTask = postBuild.RequireTask(nameof(AssemblyInject), out var isTaskAdd);
        injectTask.SetParameter(nameof(AssemblyInject.TargetPath), "$(TargetPath)");
        injectTask.SetParameter(nameof(AssemblyInject.ProjectPath), "$(ProjectPath)");
        return isTargetAdd || isTaskAdd;
    }

    private bool CleanElements(ProjectRootElement project)
    {
        var removed = false;
        var target = project.Targets.FirstOrDefault(target => target.Name.Equals(TaskConstants.TARGET));
        if (target != null)
        {
            removed = true;
            project.RemoveChild(target);
        }

        var task = project.UsingTasks.FirstOrDefault(task => task.TaskName.Equals(typeof(AssemblyInject).FullName));
        if (task != null)
        {
            removed = true;
            project.RemoveChild(task);
        }

        return removed;
    }
}