using System.Reflection;

namespace Gneedle.Aspect.Test;

/// <summary>
/// Tests for <see cref="AddProjectsPostBuild"/>, which runs after the aspect weaver is built: it walks the solution
/// which the project is built with, and writes the target which weaves into every project of that solution which refers
/// to the weaver.<para/>
/// The solution and the projects are written into a directory of their own, so that what the task does to a project is
/// read back from a file which nothing else touches.
/// </summary>
[TestFixture]
public class AddProjectsPostBuildTests
{
    /// <summary>
    /// The type a solution gives an entry which is a folder rather than a project.
    /// </summary>
    private const string SolutionFolder = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";

    /// <summary>
    /// The type a solution gives an entry which is a C# project.
    /// </summary>
    private const string Project = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

    /// <summary>
    /// The directory which the solution and its projects are written into, which is a directory of its own per test.
    /// </summary>
    private string m_WorkDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        m_WorkDirectory = Path.Combine(Path.GetTempPath(), $"Gneedle.Aspect.Scan.{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_WorkDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_WorkDirectory)) Directory.Delete(m_WorkDirectory, recursive: true);
    }

    #region Fixture

    /// <summary>
    /// Write a project which refers to the weaver, optionally disabling the aspect, and return the path of it.
    /// </summary>
    private string WriteProject(string name, bool disabled = false)
    {
        var directory = Path.Combine(m_WorkDirectory, name);
        Directory.CreateDirectory(directory);

        var property = disabled ? "    <Aspect>disable</Aspect>\n" : string.Empty;
        var path = Path.Combine(directory, $"{name}.csproj");
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net5.0</TargetFramework>
            {property}  </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Gneedle.Aspect.csproj" />
              </ItemGroup>
            </Project>
            """);
        return path;
    }

    /// <summary>
    /// Write a solution which holds the projects named, and a solution folder beside them when it is asked for, and
    /// return the path of it.
    /// </summary>
    private string WriteSolution(bool withFolder, params string[] projectNames)
    {
        const string first = "11111111-1111-1111-1111-111111111111";
        const string second = "22222222-2222-2222-2222-222222222222";

        var entries = new System.Text.StringBuilder();
        var configurations = new System.Text.StringBuilder();
        foreach (var name in projectNames)
        {
            entries.AppendLine($"""Project("{Project}") = "{name}", "{name}\{name}.csproj", "{first}" """.TrimEnd() + "\nEndProject");
            configurations.AppendLine($"\t\t{{{first}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            configurations.AppendLine($"\t\t{{{first}}}.Debug|Any CPU.Build.0 = Debug|Any CPU");
        }

        // A solution folder is an entry of the solution which is not a project, and the path it carries is the folder
        // which it groups rather than a file which could be opened.
        if (withFolder)
        {
            entries.AppendLine($"""Project("{SolutionFolder}") = "Build", "Build", "{second}" """.TrimEnd() + "\nEndProject");
            configurations.AppendLine($"\t\t{{{second}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            Directory.CreateDirectory(Path.Combine(m_WorkDirectory, "Build"));
        }

        var path = Path.Combine(m_WorkDirectory, "Scan.sln");
        File.WriteAllText(path, $"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            {entries}Global
            {"\t"}GlobalSection(SolutionConfigurationPlatforms) = preSolution
            {"\t\t"}Debug|Any CPU = Debug|Any CPU
            {"\t"}EndGlobalSection
            {"\t"}GlobalSection(ProjectConfigurationPlatforms) = postSolution
            {configurations}{"\t"}EndGlobalSection
            EndGlobal
            """);
        return path;
    }

    /// <summary>
    /// Run the task over a solution, as the build runs it.
    /// </summary>
    private static (bool Result, FakeBuildEngine Engine) Scan(string solutionPath)
    {
        var engine = new FakeBuildEngine();
        var task = new AddProjectsPostBuild { BuildEngine = engine };

        // The parameters of a task are set by the build, which reaches them by reflection rather than through a setter:
        // the task declares them to be read by the build alone.
        typeof(AddProjectsPostBuild).GetProperty(nameof(AddProjectsPostBuild.ProjectName))!.SetValue(task, "Gneedle.Aspect");
        typeof(AddProjectsPostBuild).GetProperty(nameof(AddProjectsPostBuild.SolutionPath))!.SetValue(task, solutionPath);
        typeof(AddProjectsPostBuild).GetProperty(nameof(AddProjectsPostBuild.TargetPath))!.SetValue(task, @"C:\packages\Gneedle.Aspect\tools\netstandard2.1\Gneedle.Aspect.dll");
        return (task.Execute(), engine);
    }

    #endregion

    [Test]
    public void A_Project_Which_Refers_To_The_Weaver_Is_Given_The_Target()
    {
        var project = WriteProject("App");
        var solution = WriteSolution(withFolder: false, "App");

        var (result, engine) = Scan(solution);

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        var scan = File.ReadAllText(project);
        Assert.That(scan, Does.Contain("GneedleTarget"), "the target which weaves was not added.");
        Assert.That(scan, Does.Contain("AssemblyInject"), "the task which weaves was not added.");
    }

    [Test]
    public void A_Project_Which_Disables_The_Aspect_Is_Left_Alone()
    {
        var project = WriteProject("Disabled", disabled: true);
        var solution = WriteSolution(withFolder: false, "Disabled");

        var (result, engine) = Scan(solution);

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        Assert.That(File.ReadAllText(project), Does.Not.Contain("GneedleTarget"), "a project which disabled the aspect was added to.");
    }

    [Test]
    public void A_Solution_Which_Holds_A_Folder_Is_Scanned()
    {
        // The path of a solution folder is the folder it groups, which is not a project file: opening it as one failed
        // the whole build of a solution which holds a folder.
        var project = WriteProject("App");
        var solution = WriteSolution(withFolder: true, "App");

        var (result, engine) = Scan(solution);

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        Assert.That(engine.Errors, Is.Empty);
        Assert.That(File.ReadAllText(project), Does.Contain("GneedleTarget"));
    }

    [Test]
    public void A_Project_Which_Is_Woven_Into_Asks_For_The_Weaver_To_Be_Kept()
    {
        // The value of the property is passed on to the task where it is set, and is left empty where it is not, which
        // the task reads as the default of removing the weaver.
        var project = WriteProject("App");
        var solution = WriteSolution(withFolder: false, "App");

        Scan(solution);

        Assert.That(File.ReadAllText(project), Does.Contain("KeepWeaver=\"$(KeepWeaver)\""));
    }
}
