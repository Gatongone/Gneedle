namespace Gneedle.Aspect;

/// <summary>
/// The build task which runs once the weaver itself was built: it walks the solution the weaver is built with, and
/// writes the target which runs <see cref="AssemblyInject"/> into every project of that solution which refers to the
/// weaver.<para/>
/// A project which refers to the weaver by a project reference is built from a checkout of it rather than from the
/// package, so it does not read what the package writes into a build, and this is what gives it the weaving instead.
/// The task runs both ways: a project which refers to the weaver and no longer disables the aspect is written to, and
/// one which stopped referring to it, or which disables the aspect, is taken back out of again, so that what the task
/// writes is a function of the solution and not a matter of how often it ran.
/// </summary>
public class AddProjectsPostBuild : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Name of the weaver, which is the name of the project and of the package which a project of the solution is
    /// looked for a reference to by.
    /// </summary>
    public string ProjectName { get; private set; }

    /// <summary>
    /// Path of the solution which the weaver is built with, whose projects are the ones which the target is written
    /// into.
    /// </summary>
    public string SolutionPath { get; private set; }

    /// <summary>
    /// Path of the assembly of the weaver, which is what the task which the written target runs is looked for in.
    /// </summary>
    public string TargetPath { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// A project which cannot be read is passed over rather than reported, because the solution stands beside the
    /// projects which it groups: what it names and the disk no longer holds is not a failure of the weaving.
    /// </remarks>
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

    /// <summary>
    /// Write what the target of a project should be, which is the target when the project is one which the weaving is
    /// written into and nothing when it is not, and report whether the project named it before.
    /// </summary>
    /// <param name="project">The project which is read and written.</param>
    /// <param name="projectName">Name of the project, which is what the change is reported by.</param>
    /// <returns>Whether the project was changed, and so is to be written back to its file.</returns>
    private bool TryProcessProject(ProjectRootElement project, string projectName)
    {
        var dirty = false;
        if (!VerifyProject(project))
        {
            if (CleanElements(project))
            {
                Log.LogMessageFromText(string.Format(TaskMessages.TARGET_TAKEN_OUT, projectName), MessageImportance.High);
                dirty = true;
            }
            return dirty;
        }

        if (AddElements(project))
        {
            Log.LogMessageFromText(string.Format(TaskMessages.TARGET_WRITTEN, projectName), MessageImportance.High);
            dirty = true;
        }
        return dirty;
    }

    /// <summary>
    /// Whether the project is one which the weaving is written into, which is one which refers to the weaver and does
    /// not turn the aspect off.
    /// </summary>
    /// <param name="project">The project which is read.</param>
    /// <returns>Whether the target belongs in the project.</returns>
    private bool VerifyProject(ProjectRootElement project) => project.ContainsReference(ProjectName) && !project.VerifyAspectDisable();

    /// <summary>
    /// Write the target which weaves, and the task which the target runs, into the project.
    /// </summary>
    /// <param name="project">The project which is written.</param>
    /// <returns>Whether either of them was added, which is false when the project held both.</returns>
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

    /// <summary>
    /// Take the target which weaves, and the task which the target runs, back out of a project which the weaving no
    /// longer belongs in.
    /// </summary>
    /// <param name="project">The project which is written.</param>
    /// <returns>Whether either of them was there, which is false when the project held neither.</returns>
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