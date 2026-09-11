using System.Reflection;
using Gneedle.Inject;
using Mono.Cecil;
using Assembly = Gneedle.Inject.Assembly;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Aspect.Test;

/// <summary>
/// Tests for <see cref="AssemblyInject"/>, the task which applies the injectors of an assembly to it.<para/>
/// The injectors are read from the assembly by reflection, so the members which are injected have to be the members of
/// a compiled assembly which carries them. Most of the tests take the assembly which holds them, copied to a file of
/// its own, because the fixtures are compiled into it; each works on its own copy, since the task writes the assembly
/// back to the file it read. The tests which need an assembly of a shape the compiler does not produce build one.
/// </summary>
[TestFixture]
public class AssemblyInjectTests
{
    private const string TargetType = "Gneedle.Aspect.Test.Fixtures.Target";

    /// <summary>
    /// The name of the attribute which the injectors of the fixtures are read from.<para/>
    /// The name is written out rather than taken from the type, because naming the type in this assembly is what keeps
    /// the type in the assembly which is woven: a type which the code names cannot be removed without taking the name
    /// with it, so an assertion which named it would hold it in place and then fail on its own doing.
    /// </summary>
    private const string ThrowBodyAttributeName = "Gneedle.Aspect.Test.ThrowBodyAttribute";

    private string m_WorkDirectory = null!;
    private string m_InjectionLog = null!;

    [SetUp]
    public void SetUp()
    {
        m_WorkDirectory = Path.Combine(Path.GetTempPath(), $"Gneedle.Aspect.Test.{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_WorkDirectory);
        m_InjectionLog = Path.Combine(m_WorkDirectory, "injected.txt");
        Environment.SetEnvironmentVariable(RecordingAttribute.LogVariable, m_InjectionLog);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(RecordingAttribute.LogVariable, null);
        if (Directory.Exists(m_WorkDirectory)) Directory.Delete(m_WorkDirectory, recursive: true);
    }

    /// <summary>
    /// The members which the injectors of the last run recorded, in the order they were recorded.
    /// </summary>
    private string[] Recorded() => File.Exists(m_InjectionLog) ? File.ReadAllLines(m_InjectionLog) : [];

    #region Fixture

    /// <summary>
    /// Copy the assembly which holds these tests, which holds the fixtures as well, and return the path of the copy.
    /// </summary>
    private string CopyOfTheTestAssembly()
    {
        var source = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var target = Path.Combine(m_WorkDirectory, Path.GetFileName(source));
        File.Copy(source, target, overwrite: true);
        return target;
    }

    /// <summary>
    /// Build an assembly which holds what <paramref name="build"/> describes, write it to the work directory, and
    /// return the path it was written to.
    /// </summary>
    private string BuiltAssembly(string name, Action<IAssemblyHandler> build)
    {
        var assembly = Assembly.Create(name);
        build(assembly.Handler);

        var path = Path.Combine(m_WorkDirectory, $"{name}.dll");
        assembly.SaveTo(path);
        return path;
    }

    /// <summary>
    /// Write a project file for the task to read, which disables the injection when <paramref name="disabled"/>.
    /// </summary>
    private string Project(bool disabled = false)
    {
        var path = Path.Combine(m_WorkDirectory, $"{Guid.NewGuid():N}.csproj");
        File.WriteAllText(path, disabled
            ? "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Gneedle>disable</Gneedle></PropertyGroup></Project>"
            : "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        return path;
    }

    /// <summary>
    /// Run the task on an assembly, as the build runs it.
    /// </summary>
    private static (bool Result, FakeBuildEngine Engine) Inject(string assemblyPath, string projectPath)
    {
        var engine = new FakeBuildEngine();
        var task = new AssemblyInject { BuildEngine = engine };

        // The parameters of a task are set by the build, which reaches them by reflection rather than through a setter:
        // the task declares them to be read by the build alone.
        typeof(AssemblyInject).GetProperty(nameof(AssemblyInject.ProjectPath))!.SetValue(task, projectPath);
        typeof(AssemblyInject).GetProperty(nameof(AssemblyInject.TargetPath))!.SetValue(task, assemblyPath);
        return (task.Execute(), engine);
    }

    /// <summary>
    /// Whether the body of a method of the assembly throws, which is what the injectors below write into a member.
    /// </summary>
    private static bool Throws(string assemblyPath, string typeName, string methodName)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var method = assembly.MainModule.GetType(typeName)!.Methods.Single(method => method.Name == methodName);
        return method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj);
    }

    #endregion

    #region Every member is reached

    [Test]
    public void Every_Member_Which_An_Injector_Names_Is_Injected()
    {
        var assembly = CopyOfTheTestAssembly();

        var (result, engine) = Inject(assembly, Project());

        Assert.That(result, Is.True, string.Join(Environment.NewLine, engine.Errors));
        foreach (var name in new[] {"Public", "Internal", "Private", "Protected", "PrivateStatic", "PrivateWithResult"})
        {
            Assert.That(Throws(assembly, TargetType, name), Is.True, $"'{name}' was not injected into.");
        }
    }

    [Test]
    public void A_Field_Which_Is_Not_Public_Is_Injected()
    {
        var assembly = CopyOfTheTestAssembly();

        Inject(assembly, Project());

        using var read = AssemblyDefinition.ReadAssembly(assembly);
        var type = read.MainModule.GetType(TargetType)!;
        foreach (var name in new[] {"m_Marked", "m_MarkedInternal"})
        {
            var field = type.Fields.Single(field => field.Name == name);
            Assert.That(field.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(ObsoleteAttribute).FullName), Is.True, name);
        }
    }

    [Test]
    public void A_Property_Which_An_Injector_Names_Is_Injected()
    {
        var assembly = CopyOfTheTestAssembly();

        Inject(assembly, Project());

        using var read = AssemblyDefinition.ReadAssembly(assembly);
        var getter = read.MainModule.GetType(TargetType)!.Properties.Single(property => property.Name == "MarkedGetter").GetMethod!;
        Assert.That(getter.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj), Is.True);
    }

    [Test]
    public void A_Member_Which_No_Injector_Names_Is_Left_Alone()
    {
        var assembly = CopyOfTheTestAssembly();

        Inject(assembly, Project());

        using var read = AssemblyDefinition.ReadAssembly(assembly);
        var type = read.MainModule.GetType(TargetType)!;
        var untouched = type.Methods.Single(method => method.Name == "Untouched");
        Assert.That(untouched.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj), Is.False);
        Assert.That(type.Fields.Single(field => field.Name == "m_Unmarked").CustomAttributes, Is.Empty);
        Assert.That(type.Properties.Single(property => property.Name == "Plain").GetMethod!.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj), Is.False);
    }

    [Test]
    public void A_Member_Which_A_Base_Type_Declares_Is_Injected_Once()
    {
        // The members of a base type are walked with the base type, which the assembly declares as well. Walking the
        // inherited members as well as the declared ones would inject the member of the base type once per derived type.
        var assembly = CopyOfTheTestAssembly();

        Inject(assembly, Project());

        Assert.That(Recorded(), Is.EqualTo(new[] {"Base.Inherited"}), "the member of the base type was injected into more or less than once.");
    }

    #endregion

    #region What the task skips

    [Test]
    public void An_Assembly_Whose_Aspect_Is_Disabled_Is_Not_Injected()
    {
        var assembly = CopyOfTheTestAssembly();
        var before = File.ReadAllBytes(assembly);

        var (result, engine) = Inject(assembly, Project(disabled: true));

        Assert.That(result, Is.True);
        Assert.That(engine.Errors, Is.Empty);
        Assert.That(Recorded(), Is.Empty);
        Assert.That(File.ReadAllBytes(assembly), Is.EqualTo(before), "the assembly was written although the injection was disabled");
    }

    [Test]
    public void An_Assembly_Which_Holds_No_Injector_Is_Not_Written()
    {
        // The task writes the assembly back only when an injector changed it, so an assembly which no injector names is
        // left as it was, and it says so.
        var assembly = BuiltAssembly("NoInjectorAssembly", handler =>
            handler.AddClass(nameof(PlainFixture), "Gneedle.Aspect.Test.Built", ClassFlags.Public)
                   .GetHandler()
                   .AddMethod("Ping", MethodFlags.Public | MethodFlags.Static)
                   .GetHandler());
        var before = File.ReadAllBytes(assembly);

        var (result, engine) = Inject(assembly, Project());

        Assert.That(result, Is.True);
        Assert.That(engine.Messages.Any(message => message.Contains("no changes")), Is.True, string.Join(Environment.NewLine, engine.Messages));
        Assert.That(File.ReadAllBytes(assembly), Is.EqualTo(before), "the assembly was written although no injector changed it");
    }

    #endregion

    #region What the task leaves behind

    [Test]
    public void An_Injector_Attribute_Which_Nothing_Else_Names_Is_Removed_With_Its_Uses()
    {
        var assembly = CopyOfTheTestAssembly();

        Inject(assembly, Project());

        using var read = AssemblyDefinition.ReadAssembly(assembly);

        // The attribute is what the injector was read from, and it names the weaver, so it goes with the mark which it
        // left on the member: nothing of the assembly names the type, and the type is not there.
        Assert.That(read.MainModule.Types.Any(type => type.FullName == ThrowBodyAttributeName), Is.False);
        var method = read.MainModule.GetType(TargetType)!.Methods.Single(method => method.Name == "Public");
        Assert.That(method.CustomAttributes, Is.Empty);
    }

    [Test]
    public void An_Injector_Attribute_Which_The_Assembly_Still_Names_Keeps_Its_Place()
    {
        // A type which the assembly names itself, by a typeof or a signature, cannot be taken away without taking the
        // names of it as well. It stays, and gives up what makes it an injector, which is what names the weaver.
        var assembly = CopyOfTheTestAssembly();

        Inject(assembly, Project());

        using var read = AssemblyDefinition.ReadAssembly(assembly);
        var type = read.MainModule.GetType(typeof(ClassOnlyAttribute).FullName!)!;

        Assert.That(type, Is.Not.Null);
        Assert.That(type.Interfaces.Any(implementation => implementation.InterfaceType.FullName == typeof(IClassInjector).FullName), Is.False);
        Assert.That(type.Methods.Any(method => method.Name == nameof(IClassInjector.Inject)), Is.False);
    }

    [Test]
    public void The_Reference_To_The_Weaver_Is_Kept_While_The_Assembly_Names_It()
    {
        // The assembly which these tests build with is one which weaves through the weaver, so it names the weaver for
        // its own sake beside the attributes which it declares, and the reference to it is needed.
        var assembly = CopyOfTheTestAssembly();

        Inject(assembly, Project());

        using var read = AssemblyDefinition.ReadAssembly(assembly);
        Assert.That(read.MainModule.AssemblyReferences.Any(reference => reference.Name == "Gneedle.Inject"), Is.True);
    }

    #endregion

    #region What the task refuses

    [Test]
    public void A_Class_Injector_On_A_Type_Which_Is_Not_A_Class_Fails()
    {
        // An injector which names the kind of type it applies to does not apply to a type of another kind, and the
        // injection it stands for does not happen. It is reported rather than passed over, and the assembly is not
        // written, because nothing was injected into it.
        var assembly = BuiltAssembly("RefusedInjectorAssembly", handler =>
            handler.AddStruct("NotAClass", "Gneedle.Aspect.Test.Built", StructFlags.Public)
                   .GetHandler()
                   .AddAttribute(typeof(ClassOnlyAttribute).ToGneedleType()));
        var before = File.ReadAllBytes(assembly);

        var (result, engine) = Inject(assembly, Project());

        Assert.That(result, Is.False);
        Assert.That(engine.Errors.Single(), Does.Contain("is not a class"));
        Assert.That(File.ReadAllBytes(assembly), Is.EqualTo(before), "the assembly was written although the injector was refused");
    }

    #endregion
}

/// <summary>
/// A type which the tests build an assembly out of, so that an assembly which holds nothing but what a test describes
/// is one the task can be run on.
/// </summary>
public class PlainFixture;
