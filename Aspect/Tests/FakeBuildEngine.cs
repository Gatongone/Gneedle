using Microsoft.Build.Framework;

namespace Gneedle.Aspect.Test;

/// <summary>
/// The engine which an MSBuild task logs through and asks about the build it runs in.<para/>
/// A task is given one by the build, and the tests run the task themselves, so this stands in for the build: it holds
/// what was logged, which is what the task reports and what it fails on.
/// </summary>
public sealed class FakeBuildEngine : IBuildEngine
{
    /// <summary>
    /// The message of every message which the task logged.
    /// </summary>
    public List<string> Messages { get; } = [];

    /// <summary>
    /// The message of every error which the task logged.
    /// </summary>
    public List<string> Errors { get; } = [];

    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => string.Empty;

    public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;

    public void LogCustomEvent(CustomBuildEventArgs e) { }
    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);
    public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? string.Empty);
    public void LogWarningEvent(BuildWarningEventArgs e) { }
}
