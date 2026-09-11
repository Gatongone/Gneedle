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
        foreach (var project in solution.ProjectsInOrder)
        {
            if (ProjectName.Equals(project.ProjectName)) continue;

            // A solution folder stands in the solution beside the projects which it groups, and the path it holds is
            // that folder, which is not a project file and cannot be read as one. A project which the solution names
            // and the disk does not hold cannot be described either, and reading it is what would report it.
            if (!File.Exists(project.AbsolutePath)) continue;

            var targetProject = ProjectRootElement.Open(project.AbsolutePath);
            var targetProjectName = Path.GetFileNameWithoutExtension(targetProject.FullPath);
            if (TryProcessProject(targetProject, targetProjectName))
            {
                targetProject.Save();
            }
        }

        return true;
    }

    private bool TryProcessProject(ProjectRootElement project, string projectName)
    {
        var dirty = false;
        if (!VerifyProject(project))
        {
            if (CleanElements(project))
            {
                Log.LogMessageFromText($"Remove {TaskConstants.TARGET} target from {projectName}.csproj......", MessageImportance.High);
                dirty = true;
            }
            return dirty;
        }

        if (AddElements(project))
        {
            Log.LogMessageFromText($"Add {TaskConstants.TARGET} target to {projectName}.csproj......", MessageImportance.High);
            dirty = true;
        }
        return dirty;
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

        // The target which is written here runs in a project which does not read the props of the package, so the
        // property is passed on where it is set and is left empty where it is not, which the task reads as its default.
        injectTask.SetParameter(nameof(AssemblyInject.KeepWeaver), $"$({TaskConstants.KEEP})");
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