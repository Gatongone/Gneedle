using System.Reflection;
using System.Xml.Linq;

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
    /// Write a project of the declaration given into a directory of its own, and return the path of it.<para/>
    /// The declaration is what the project element of an SDK project wraps, so that a test writes the groups it is
    /// about and nothing else.
    /// </summary>
    /// <param name="name">Name of the project, which is the name of its directory and of its file.</param>
    /// <param name="declares">What the project declares.</param>
    private string WriteProjectDeclaring(string name, string declares)
    {
        var directory = Path.Combine(m_WorkDirectory, name);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{name}.csproj");
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk">
            {declares}</Project>
            """);
        return path;
    }

    /// <summary>
    /// The target which a project declares under <paramref name="name"/>, or null when it declares none.
    /// </summary>
    /// <param name="project">The project which was read back from its file.</param>
    /// <param name="name">Name of the target which is asked for.</param>
    private static XElement? Target(XDocument project, string name)
        => project.Descendants().FirstOrDefault(element => element.Name.LocalName == "Target" && (string?) element.Attribute("Name") == name);

    /// <summary>
    /// Whether a target runs the task which <paramref name="name"/> names, which is a child element of it rather than
    /// anything its text holds.
    /// </summary>
    /// <param name="target">The target which is read.</param>
    /// <param name="name">Name of the task which is asked for.</param>
    private static bool Runs(XElement target, string name) => target.Elements().Any(element => element.Name.LocalName == name);

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

    #region Reading a project the way the build reads it

    [Test]
    public void A_Target_Which_The_Project_Runs_On_The_Event_Is_Left_Alone()
    {
        // The target which the package writes is one of its own name, which is the name it is taken back out of a
        // project by: a target which the project itself runs after the build is left where it is, and the target which
        // weaves is added beside it rather than a task of the package being written into it.
        var project = WriteProjectDeclaring("App", """
            <PropertyGroup>
              <TargetFramework>net5.0</TargetFramework>
            </PropertyGroup>
            <ItemGroup>
              <ProjectReference Include="..\Gneedle.Aspect.csproj" />
            </ItemGroup>
            <Target Name="CopyTheOutput" AfterTargets="PostBuildEvent">
              <Message Text="copied" Importance="high" />
            </Target>

            """);
        var solution = WriteSolution(withFolder: false, "App");

        var (result, engine) = Scan(solution);

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        var written = XDocument.Load(project);
        var woven = Target(written, "GneedleTarget");
        Assert.That(woven, Is.Not.Null, "the target which weaves was not added to a project which runs a target of its own on the event.");
        Assert.That(Runs(woven!, "AssemblyInject"), Is.True, "the target which weaves runs no task which weaves.");
        Assert.That(Runs(Target(written, "CopyTheOutput")!, "AssemblyInject"), Is.False,
                    "the task which weaves was written into a target which the project runs.");
    }

    [Test]
    public void A_Property_Which_A_Later_Group_Sets_Is_The_One_Which_Is_Read()
    {
        // A project is read from its first line to its last, and a property holds the value of the last group which
        // sets it: a project which turns the aspect off and on again is woven.
        var project = WriteProjectDeclaring("App", """
            <PropertyGroup>
              <TargetFramework>net5.0</TargetFramework>
              <Aspect>disable</Aspect>
            </PropertyGroup>
            <ItemGroup>
              <ProjectReference Include="..\Gneedle.Aspect.csproj" />
            </ItemGroup>
            <PropertyGroup>
              <Aspect>enable</Aspect>
            </PropertyGroup>

            """);
        var solution = WriteSolution(withFolder: false, "App");

        var (result, engine) = Scan(solution);

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        Assert.That(File.ReadAllText(project), Does.Contain("GneedleTarget"),
                    "a project which set the property back was left out of the weaving.");
    }

    [Test]
    public void A_Property_Which_A_Later_Group_Sets_Back_Is_The_One_Which_Is_Read()
    {
        // The same the other way round, which is the order a project that was woven once and turns the aspect off
        // again is written in.
        var project = WriteProjectDeclaring("App", """
            <PropertyGroup>
              <TargetFramework>net5.0</TargetFramework>
              <Aspect>enable</Aspect>
            </PropertyGroup>
            <ItemGroup>
              <ProjectReference Include="..\Gneedle.Aspect.csproj" />
            </ItemGroup>
            <PropertyGroup>
              <Aspect>disable</Aspect>
            </PropertyGroup>

            """);
        var solution = WriteSolution(withFolder: false, "App");

        var (result, engine) = Scan(solution);

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        Assert.That(File.ReadAllText(project), Does.Not.Contain("GneedleTarget"),
                    "a project which turned the aspect off last was woven.");
    }

    [Test]
    public void A_Property_Which_Is_Spelled_In_Another_Case_Is_Read()
    {
        // The build reads the name of a property without regard to case, so the property which the package reads is
        // set by a project which spells it another way.
        var project = WriteProjectDeclaring("App", """
            <PropertyGroup>
              <TargetFramework>net5.0</TargetFramework>
              <aspect>disable</aspect>
            </PropertyGroup>
            <ItemGroup>
              <ProjectReference Include="..\Gneedle.Aspect.csproj" />
            </ItemGroup>

            """);
        var solution = WriteSolution(withFolder: false, "App");

        var (result, engine) = Scan(solution);

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        Assert.That(File.ReadAllText(project), Does.Not.Contain("GneedleTarget"),
                    "a project which turned the aspect off was woven.");
    }

    [Test]
    public void A_Project_Reference_Which_Is_Spelled_In_Another_Case_Is_Read()
    {
        // A project reference names a file, and a file is the one its path names whatever the case of the letters in
        // it, which is how the build reads the reference.
        var project = WriteProjectDeclaring("App", """
            <PropertyGroup>
              <TargetFramework>net5.0</TargetFramework>
            </PropertyGroup>
            <ItemGroup>
              <ProjectReference Include="..\gneedle.aspect.csproj" />
            </ItemGroup>

            """);
        var solution = WriteSolution(withFolder: false, "App");

        var (result, engine) = Scan(solution);

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        Assert.That(File.ReadAllText(project), Does.Contain("GneedleTarget"),
                    "a project which refers to the weaver was not woven.");
    }

    [Test]
    public void A_Package_Reference_Which_Is_Spelled_In_Another_Case_Is_Read()
    {
        // A package is named by its id, which the registry it was published to holds without regard to case.
        var project = WriteProjectDeclaring("App", """
            <PropertyGroup>
              <TargetFramework>net5.0</TargetFramework>
            </PropertyGroup>
            <ItemGroup>
              <PackageReference Include="gneedle.aspect" />
            </ItemGroup>

            """);
        var solution = WriteSolution(withFolder: false, "App");

        var (result, engine) = Scan(solution);

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        Assert.That(File.ReadAllText(project), Does.Contain("GneedleTarget"),
                    "a project which refers to the weaver was not woven.");
    }

    [Test]
    public void An_Item_Which_Is_Spelled_In_Another_Case_Is_Read()
    {
        // The name of an item is read by the build without regard to case as well, so a project which writes the
        // reference of the weaver another way is a project which refers to it.
        var project = WriteProjectDeclaring("App", """
            <PropertyGroup>
              <TargetFramework>net5.0</TargetFramework>
            </PropertyGroup>
            <ItemGroup>
              <projectreference Include="..\Gneedle.Aspect.csproj" />
            </ItemGroup>

            """);
        var solution = WriteSolution(withFolder: false, "App");

        var (result, engine) = Scan(solution);

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        Assert.That(File.ReadAllText(project), Does.Contain("GneedleTarget"),
                    "a project which refers to the weaver was not woven.");
    }

    #endregion
}