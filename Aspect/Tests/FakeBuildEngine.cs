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

    /// <inheritdoc/>
    public bool ContinueOnError => false;

    /// <inheritdoc/>
    public int LineNumberOfTaskNode => 0;

    /// <inheritdoc/>
    public int ColumnNumberOfTaskNode => 0;

    /// <inheritdoc/>
    public string ProjectFileOfTaskNode => string.Empty;

    /// <summary>
    /// A build of another project is not run and is answered as one which finished, because the tasks of this package
    /// do not ask for one.
    /// </summary>
    /// <inheritdoc/>
    public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;

    /// <summary>
    /// A custom event is not recorded, because nothing which the tasks of this package report is one.
    /// </summary>
    public void LogCustomEvent(CustomBuildEventArgs e) { }

    /// <summary>
    /// Record the message of an error, which is what the tests read to tell what a task refused.
    /// </summary>
    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);

    /// <summary>
    /// Record the message of a message, which is what the tests read to tell what a task did.
    /// </summary>
    public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? string.Empty);

    /// <summary>
    /// A warning is not recorded, because nothing which the tasks of this package report is one.
    /// </summary>
    public void LogWarningEvent(BuildWarningEventArgs e) { }
}
