namespace Gneedle.Aspect;

/// <summary>
/// Extensions for reading and writing the project files which the tasks of this package go over.<para/>
/// An element is asked of the project by name, and is added to it when it does not hold one, so that the target and the
/// task which the package writes into a project are written once however often the build that reads them runs. Every
/// member which writes an element tells the caller that it did, because the project is written back to its file only
/// when something about it changed.<para/>
/// What is read is read the way the build of that project reads it, because the two have to agree on what the project
/// says: the names of properties and of items, and the name which a package is referred to by, are read without regard
/// to the case of the letters in them, and a property holds the value which the last group that sets it gives.
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
        var result = target.Tasks.FirstOrDefault(task => task.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (result != null) return result;
        isAdd = true;
        return target.AddTask(name);
    }

    /// <summary>
    /// Whether the project refers to another project or to a package by <paramref name="name"/>.<para/>
    /// A project is referred to by the path of its file, so the name which a <c>ProjectReference</c> carries is compared
    /// as the name of a file, while a package is referred to by its name alone. Both are read the way the build reads
    /// them: the name of an item is the one it is written under whatever the case of the letters in it, and neither the
    /// name of a file nor the id of a package tells two of them apart by case alone.
    /// </summary>
    /// <param name="root">The project which is read.</param>
    /// <param name="name">Name of the project or of the package which is asked for.</param>
    /// <returns>Whether the project refers to it.</returns>
    public static bool ContainsReference(this ProjectRootElement root, string name)
    {
        foreach (var element in root.ItemGroups.SelectMany(group => group.Items))
        {
            if (IsItem(element, "ProjectReference") && Path.GetFileNameWithoutExtension(element.Include).Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            if (IsItem(element, "PackageReference") && element.Include.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// The property which the project sets under <paramref name="name"/>, which is the one the last of the groups of
    /// properties that sets it holds.<para/>
    /// The name of a property is read the way the build reads it, without regard to the case of the letters in it, and
    /// the value the build holds is the one the last group which sets it gives. A group of properties which the build
    /// reads under a condition is read here whatever the condition says, because the project is what is read rather
    /// than a build of it.
    /// </summary>
    /// <param name="root">The project which is read.</param>
    /// <param name="name">Name of the property which is asked for.</param>
    /// <param name="property">The property which was found, or null when the project sets none under that name.</param>
    /// <returns>Whether the project sets the property.</returns>
    public static bool TryGetProperty(this ProjectRootElement root, string name, out ProjectPropertyElement? property)
    {
        property = null;
        foreach (var element in root.PropertyGroups.SelectMany(group => group.Properties))
        {
            if (element.ElementName.Equals(name, StringComparison.OrdinalIgnoreCase)) property = element;
        }

        return property != null;
    }

    /// <summary>
    /// The target which runs after the build, which is added to the project when it holds none of the name given.<para/>
    /// The target is found by the name which this package writes it under rather than by the event it runs on: a target
    /// which a project declares under another name is one which the package did not write, and which it cannot take
    /// back out again as a whole, so the target of the package is added beside it rather than a task of the package
    /// being written into it.
    /// </summary>
    /// <param name="root">The project which is read.</param>
    /// <param name="name">Name of the target which is asked for.</param>
    /// <param name="isChanged">Whether the project was changed, and so is to be written back to its file.</param>
    /// <returns>The target which runs after the build.</returns>
    public static ProjectTargetElement RequirePostBuildTarget(this ProjectRootElement root, string name, out bool isChanged)
    {
        const string postBuildEvent = "PostBuildEvent";

        var target = root.Targets.FirstOrDefault(target => target.Name.Equals(name, StringComparison.Ordinal));
        isChanged = target == null;
        target ??= root.AddTarget(name);

        // A target of this name which runs on another event, or on none, is one which was written elsewhere and left:
        // the event is set on it rather than a second target of the same name being added, which is what a project
        // cannot hold.
        if (!RunsOn(target.AfterTargets, postBuildEvent))
        {
            target.AfterTargets = postBuildEvent;
            isChanged = true;
        }

        return target;
    }

    /// <summary>
    /// Whether a target runs on the event named, which the attribute of the target holds as the list of the targets it
    /// runs after, separated by semicolons.
    /// </summary>
    /// <param name="targets">What the attribute of the target holds, or null when it holds none.</param>
    /// <param name="name">Name of the event which is asked for.</param>
    private static bool RunsOn(string? targets, string name)
        => targets != null && targets.Split(';').Any(target => target.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether an element is the item which <paramref name="name"/> names, which the build reads without regard to the
    /// case of the letters in it.
    /// </summary>
    /// <param name="element">The element which is read.</param>
    /// <param name="name">Name of the item which is asked for.</param>
    private static bool IsItem(ProjectItemElement element, string name) => element.ElementName.Equals(name, StringComparison.OrdinalIgnoreCase);
}
