namespace Gneedle.Aspect;

internal static class ProjectExtensions
{
    public static bool VerifyAspectDisable(this ProjectRootElement project) => project.TryGetProperty(TaskConstants.TOGGLE, out var property) && property!.Value == "disable";

    public static ProjectTaskElement RequireTask(this ProjectTargetElement target, string name, out bool isAdd)
    {
        isAdd = false;
        var result = target.Tasks.FirstOrDefault(task => task.Name.Equals(name));
        if (result != null) return result;
        isAdd = true;
        return target.AddTask(name);
    }

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

public enum BuildEventType
{
    PostBuild,
    PreBuild
}