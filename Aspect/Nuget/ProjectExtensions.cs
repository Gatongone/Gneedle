namespace Gneedle.Aspect;

/// <summary>
/// Extensions for reading and writing the project files which the tasks of this package go over.<para/>
/// An element is asked of the project by name, and is added to it when it does not hold one, so that the target and the
/// task which the package writes into a project are written once however often the build that reads them runs. Every
/// member which adds an element tells the caller that it did, because the project is written back to its file only when
/// something about it changed.
/// </summary>
internal static class ProjectExtensions
{
    /// <summary>
    /// Whether the project turns the aspect off, which it does by setting the property which
    /// <see cref="TaskConstants.TOGGLE"/> names to <c>disable</c>.
    /// </summary>
    /// <param name="project">The project which is read.</param>
    /// <returns>Whether the aspect is disabled for the project.</returns>
    public static bool VerifyAspectDisable(this ProjectRootElement project) => project.TryGetProperty(TaskConstants.TOGGLE, out var property) && property!.Value == "disable";

    /// <summary>
    /// The task which the target runs under <paramref name="name"/>, which is added to the target when it runs none.
    /// </summary>
    /// <param name="target">The target which is read.</param>
    /// <param name="name">Name of the task which is asked for.</param>
    /// <param name="isAdd">Whether a task was added to the target, which is false when one was found.</param>
    /// <returns>The task of that name which the target runs.</returns>
    public static ProjectTaskElement RequireTask(this ProjectTargetElement target, string name, out bool isAdd)
    {
        isAdd = false;
        var result = target.Tasks.FirstOrDefault(task => task.Name.Equals(name));
        if (result != null) return result;
        isAdd = true;
        return target.AddTask(name);
    }

    /// <summary>
    /// Whether the project refers to another project or to a package by <paramref name="name"/>.<para/>
    /// A project is referred to by the path of its file, so the name which a <c>ProjectReference</c> carries is compared
    /// as the name of a file, while a package is referred to by its name alone.
    /// </summary>
    /// <param name="root">The project which is read.</param>
    /// <param name="name">Name of the project or of the package which is asked for.</param>
    /// <returns>Whether the project refers to it.</returns>
    public static bool ContainsReference(this ProjectRootElement root, string name)
    {
        foreach (var element in root.ItemGroups.SelectMany(properties => properties.Items))
        {
            switch (element.ElementName)
            {
                case "ProjectReference" when Path.GetFileNameWithoutExtension(element.Include).Equals(name):
                case "PackageReference" when element.Include.Equals(name):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The property which the project sets under <paramref name="name"/>, which is the one the first of the groups of
    /// properties that sets it holds.
    /// </summary>
    /// <param name="root">The project which is read.</param>
    /// <param name="name">Name of the property which is asked for.</param>
    /// <param name="property">The property which was found, or null when the project sets none under that name.</param>
    /// <returns>Whether the project sets the property.</returns>
    public static bool TryGetProperty(this ProjectRootElement root, string name, out ProjectPropertyElement? property)
    {
        property = null;
        foreach (var element in root.PropertyGroups.SelectMany(properties => properties.Properties))
        {
            if (!element.ElementName.Equals(name)) continue;
            property = element;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The target which runs before or after the build, which is added to the project when it runs none there.<para/>
    /// Which event a target runs on is told by its <c>BeforeTargets</c> and its <c>AfterTargets</c> rather than by its
    /// name, which any name a project likes may carry, so the target which already runs on the event is answered rather
    /// than a second one which runs it a second time.
    /// </summary>
    /// <param name="root">The project which is read.</param>
    /// <param name="type">The event of the build which the target runs on.</param>
    /// <param name="name">Name of the target which is asked for, which is the name the event goes by when it is empty.</param>
    /// <param name="isAdd">Whether a target was added to the project, which is false when one was found.</param>
    /// <returns>The target which runs on the event.</returns>
    public static ProjectTargetElement RequireBuildEvent(this ProjectRootElement root, BuildEventType type, string name, out bool isAdd)
    {
        const string keyPreBuild = "PreBuild";
        const string keyPostBuild = "PostBuild";
        const string keyPreBuildEvent = "PreBuildEvent";
        const string keyPostBuildEvent = "PostBuildEvent";
        isAdd = false;

        if (string.IsNullOrEmpty(name))
        {
            name = type == BuildEventType.PreBuild ? keyPreBuild : keyPostBuild;
        }

        var eventKey = type == BuildEventType.PreBuild ? keyPreBuildEvent : keyPostBuildEvent;

        var postBuild = root.Targets
                            .Where(target => !target.ElementName.Equals(name))
                            .FirstOrDefault(target => (type != BuildEventType.PreBuild || target.BeforeTargets.Equals(keyPreBuildEvent))
                                && (type != BuildEventType.PostBuild || target.AfterTargets.Equals(keyPostBuildEvent)));

        if (postBuild != null) return postBuild;

        postBuild = root.AddTarget(name);
        if (type == BuildEventType.PreBuild)
            postBuild.BeforeTargets = eventKey;
        else
            postBuild.AfterTargets = eventKey;

        isAdd = true;
        return postBuild;
    }
}

/// <summary>
/// The event of a build which a target of a project runs on.
/// </summary>
public enum BuildEventType
{
    /// <summary>
    /// The target runs after the build.
    /// </summary>
    PostBuild,

    /// <summary>
    /// The target runs before the build.
    /// </summary>
    PreBuild
}
