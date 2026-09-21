using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Assembly = Gneedle.Inject.Assembly;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Gneedle.Inject.Test;

using static Gneedle.Inject.Test.TestFixtures;

/// <summary>
/// An attribute which marks the type it is put on as obsolete, so that the injection is visible in the image which was
/// woven.
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class MarkTypeAttribute : Attribute, ITypeInjector
{
    /// <inheritdoc/>
    public int Priority => 0;
    /// <inheritdoc/>
    public void Inject(Type type, ITypeHandler handler) => handler.AddAttribute(typeof(ObsoleteAttribute).ToGneedleType(), "marked");
}

/// <summary>
/// An attribute which marks the type it is put on, which is the attribute above: what makes it one of the attributes
/// which an injector is read from is the interface which the attribute it derives from implements rather than one which
/// it implements itself.
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public sealed class DerivedMarkAttribute : MarkTypeAttribute;

/// <summary>
/// The type which carries the marker above, whose injection is read out of the image which was woven.
/// </summary>
[MarkType]
public class MarkedFixture;

/// <summary>
/// The type which carries the derived marker above, whose injection is read out of the image which was woven.
/// </summary>
[DerivedMark]
public class DerivedMarkedFixture;

/// <summary>
/// An attribute which throws while the injector of the type it is put on runs, so that a run which wove the types around
/// it is told of one which it did not.<para/>
/// It throws for the type which the environment names alone, because the injectors of this assembly are run by every test
/// which weaves it: a fixture which threw on every run would fail the tests which hold nothing to do with it. The name is
/// handed over in an environment variable because the assembly which is woven is a copy of this one, so a field which the
/// test wrote is a field of a copy which the injector cannot read.
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public sealed class ThrowTypeAttribute : Attribute, ITypeInjector
{
    /// <inheritdoc/>
    public int Priority => 0;
    /// <summary>
    /// The environment variable which names the type the injector throws for.
    /// </summary>
    public const string TypeVariable = "GneedleThrowType";

    /// <inheritdoc/>
    public void Inject(Type type, ITypeHandler handler)
    {
        if (Environment.GetEnvironmentVariable(TypeVariable) != type.FullName) return;
        throw new InvalidOperationException($"The injector of '{type.FullName}' cannot weave it.");
    }
}

/// <summary>
/// The type which carries the throwing injector above.
/// </summary>
[ThrowType]
public class ThrowingFixture;

/// <summary>
/// The three attributes below mark the type they are put on as obsolete, each of them naming the kind of type it
/// applies to, so that the kind of the handler which an injector of a type is asked of is read out of what the injector
/// wrote rather than out of the handler itself.
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public sealed class MarkClassAttribute : Attribute, IClassInjector
{
    /// <inheritdoc/>
    public int Priority => 0;
    /// <inheritdoc/>
    public void Inject(Type type, IClassHandler handler) => handler.AddAttribute(typeof(ObsoleteAttribute).ToGneedleType(), "class");
}

/// <inheritdoc cref="MarkClassAttribute"/>
[AttributeUsage(AttributeTargets.All)]
public sealed class MarkStructAttribute : Attribute, IStructInjector
{
    /// <inheritdoc/>
    public int Priority => 0;
    /// <inheritdoc/>
    public void Inject(Type type, IStructHandler handler) => handler.AddAttribute(typeof(ObsoleteAttribute).ToGneedleType(), "struct");
}

/// <inheritdoc cref="MarkClassAttribute"/>
[AttributeUsage(AttributeTargets.All)]
public sealed class MarkEnumAttribute : Attribute, IEnumInjector
{
    /// <inheritdoc/>
    public int Priority => 0;
    /// <inheritdoc/>
    public void Inject(Type type, IEnumHandler handler) => handler.AddAttribute(typeof(ObsoleteAttribute).ToGneedleType(), "enum");
}

/// <summary>
/// The class which carries the marker of its kind above.
/// </summary>
[MarkClass]
public class MarkedClassFixture;

/// <summary>
/// The struct which carries the marker of its kind above.
/// </summary>
[MarkStruct]
public struct MarkedStructFixture;

/// <summary>
/// The enum which carries the marker of its kind above.
/// </summary>
[MarkEnum]
public enum MarkedEnumFixture
{
    /// <summary>
    /// A member, because an enum which holds none declares nothing at all.
    /// </summary>
    None
}

/// <summary>
/// The bodies which the injector below gives the members it is put on, which are what the two overloads of
/// <see cref="OverloadedFixture"/> are told apart by once the assembly is woven.
/// </summary>
public static class OverloadBodies
{
    /// <summary>
    /// The body of the member which takes no parameter.
    /// </summary>
    public static int None() => 1;

    /// <summary>
    /// The body of the member which takes one.
    /// </summary>
    public static int One(int value) => 2;
}

/// <summary>
/// An attribute which gives the member it is put on the body which its parameters name.<para/>
/// The body is chosen by the member which the injector is given rather than by the handler which it is written
/// through, so a weaving which reached another member than the one the injector was put on writes the body of the
/// member which carries it into the one which it reached: the two are told apart by a run of the assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RunBodyAttribute : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public int Priority => 0;
    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler)
        => handler.SetBody(typeof(OverloadBodies).GetMethod(method.GetParameters().Length == 0 ? nameof(OverloadBodies.None) : nameof(OverloadBodies.One))!);
}

/// <summary>
/// A type which declares two members of one name, which each carry the injector above.<para/>
/// The member which takes a parameter is declared first, because the member which the name alone finds is the first one
/// which the metadata declares: the case this stands for is the one where the two are not the same member.
/// </summary>
public class OverloadedFixture
{
    [RunBody]
    public int Run(int value) => -2;

    [RunBody]
    public int Run() => -1;
}

/// <summary>
/// An interface which an attribute implements to be one which runs the body of the member it is put on, which reaches
/// the weaver through the interface of the kind rather than declaring it itself.
/// </summary>
public interface IRunBodyInjector : IMethodInjector;

/// <summary>
/// An attribute which runs a body of the member it is put on, which is one of the attributes which an injector is read
/// from through the interface above rather than through one of the weaver.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RunBodyThroughAnInterfaceAttribute : Attribute, IRunBodyInjector
{
    /// <inheritdoc/>
    public int Priority => 0;
    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler) => handler.SetBody(typeof(OverloadBodies).GetMethod(nameof(OverloadBodies.None))!);
}

/// <summary>
/// The type whose member carries the injector above.
/// </summary>
public class ThroughAnInterfaceFixture
{
    /// <summary>
    /// The member which the injector above is put on.
    /// </summary>
    [RunBodyThroughAnInterface]
    public int Run() => -1;
}

/// <summary>
/// The variable of the process which the injectors below run for the type it names alone.<para/>
/// Both of them are read by every test which weaves this assembly, and a fixture which was woven by them on every run
/// would report on every run: the variable is what keeps the members which carry them from being woven by the tests
/// which have nothing to do with them.
/// </summary>
public static class InjectorOrder
{
    /// <summary>
    /// The environment variable which names the type the injectors below run for.
    /// </summary>
    public const string TypeVariable = "GneedleInjectorOrderType";

    /// <summary>
    /// Whether the injectors below run for the type which declares the member they were put on.
    /// </summary>
    public static bool RunsFor(MethodBase member) => Environment.GetEnvironmentVariable(TypeVariable) == member.DeclaringType!.FullName;
}

/// <summary>
/// The bodies which the injectors below write into the members they are put on.
/// </summary>
public static class InjectorOrderBodies
{
    /// <summary>
    /// Proceed into the body which the member held, which is what a member woven around holds.
    /// </summary>
    public static void ProceedOnly() => Proceed.Invoke();

    /// <summary>
    /// A body which calls nothing, so that a member which was woven around is told from one which was not by whether
    /// the generated method is called anywhere in it.
    /// </summary>
    public static void Quiet() { }
}

/// <summary>
/// An attribute which weaves the member it is put on around the body which the member holds.<para/>
/// The order in which it is applied among the injectors of a member is the order of the priorities which they declare:
/// the attribute specifications of a member are equivalent in every order, so which of two injectors takes over the
/// body of the member and which proceeds into what the other wrote is told by the priority and by nothing else.
/// </summary>
/// <param name="priority">The order in which this injector is applied among the injectors of the member it stands on.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class AroundBodyInjectorAttribute(int priority = 0) : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public int Priority { get; } = priority;

    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler)
    {
        if (!InjectorOrder.RunsFor(method)) return;
        handler.AroundBody(typeof(InjectorOrderBodies).GetMethod(nameof(InjectorOrderBodies.ProceedOnly))!);
    }
}

/// <summary>
/// An attribute which replaces the body of the member it is put on.<para/>
/// The order in which it is applied is the order of the priorities, as the injector above says.
/// </summary>
/// <param name="priority">The order in which this injector is applied among the injectors of the member it stands on.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ReplaceBodyInjectorAttribute(int priority = 0) : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public int Priority { get; } = priority;

    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler)
    {
        if (!InjectorOrder.RunsFor(method)) return;
        handler.SetBody(typeof(InjectorOrderBodies).GetMethod(nameof(InjectorOrderBodies.Quiet))!);
    }
}

/// <summary>
/// A type whose member carries two injectors which each weave around it.
/// </summary>
public class TwoAroundFixture
{
    /// <summary>
    /// The member which both injectors above are put on.
    /// </summary>
    [AroundBodyInjector]
    [AroundBodyInjector]
    public static void Run() { }
}

/// <summary>
/// A type whose member carries an injector which replaces the body and one which weaves around it.
/// </summary>
public class ReplaceThenAroundFixture
{
    /// <summary>
    /// The member which the two injectors above are put on.
    /// </summary>
    [ReplaceBodyInjector(priority: 10)]
    [AroundBodyInjector(priority: 0)]
    public static void Run() { }
}

/// <summary>
/// A type whose member carries the same two injectors as the type above, of the other order: which of them is applied
/// first is the priority, so what tells the two types apart is the pair of priorities rather than where the attributes
/// stand, which no test could tell apart at all.
/// </summary>
public class AroundThenReplaceFixture
{
    /// <summary>
    /// The member which the two injectors above are put on.
    /// </summary>
    [AroundBodyInjector(priority: 10)]
    [ReplaceBodyInjector(priority: 0)]
    public static void Run() { }
}

/// <summary>
/// Tests for <see cref="Injections"/>, which applies the injectors which an assembly declares to the image it was loaded
/// from, and for the taking of the weaver back out which that runs.
/// </summary>
[TestFixture]
public class InjectionsTests
{

    private const string MarkedType = "Gneedle.Inject.Test.MarkedFixture";

    /// <summary>
    /// The name of the attribute which the injector of <see cref="MarkedFixture"/> is read from.<para/>
    /// The name is written out rather than taken from the type, because naming the type in this assembly is what keeps
    /// the type in the assembly which is woven: a type which the code names cannot be removed without taking the name
    /// with it, so an assertion which named it would hold it in place and then fail on its own doing.
    /// </summary>
    private const string MarkerAttributeName = "Gneedle.Inject.Test.MarkTypeAttribute";

    /// <summary>
    /// The name of the attribute which derives from the one above, which is written out for the same reason as the name
    /// above.
    /// </summary>
    private const string DerivedMarkerAttributeName = "Gneedle.Inject.Test.DerivedMarkAttribute";

    /// <summary>
    /// The name of the type which carries the derived marker above, which is written out for the same reason as the
    /// names above.
    /// </summary>
    private const string DerivedMarkedType = "Gneedle.Inject.Test.DerivedMarkedFixture";

    /// <summary>
    /// The names of the interface which reaches the weaver and of the type whose member carries the attribute which
    /// implements it, which are written out for the same reason as the names above.
    /// </summary>
    private const string InterfaceInjectorName = "Gneedle.Inject.Test.IRunBodyInjector";

    /// <inheritdoc cref="InterfaceInjectorName"/>
    private const string ThroughAnInterfaceType = "Gneedle.Inject.Test.ThroughAnInterfaceFixture";

    /// <summary>
    /// The name of the type whose injector throws, which is written out for the same reason as the name above.
    /// </summary>
    private const string ThrowingType = "Gneedle.Inject.Test.ThrowingFixture";

    /// <summary>
    /// The name of the type which declares the two members of one name, which is written out for the same reason as the
    /// names above.
    /// </summary>
    private const string OverloadedType = "Gneedle.Inject.Test.OverloadedFixture";

    /// <summary>
    /// The names of the types which carry the markers of their kind, which are written out for the same reason as the
    /// names above.
    /// </summary>
    private const string MarkedClassType = "Gneedle.Inject.Test.MarkedClassFixture";

    /// <inheritdoc cref="MarkedClassType"/>
    private const string MarkedStructType = "Gneedle.Inject.Test.MarkedStructFixture";

    /// <inheritdoc cref="MarkedClassType"/>
    private const string MarkedEnumType = "Gneedle.Inject.Test.MarkedEnumFixture";

    /// <summary>
    /// The image of the assembly which holds these tests, which holds the fixtures as well.
    /// </summary>
    private static byte[] TestAssemblyImage() => File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location);

    #region Applying the injectors of an assembly

    [Test]
    public void Apply_Runs_The_Injector_Which_A_Type_Declares_And_Takes_The_Weaver_Out()
    {
        var image = TestAssemblyImage();
        var assembly = AssemblyLoader.LoadFromBytes(image);

        var (changed, result) = Injections.Apply(assembly, image);

        Assert.That(changed, Is.True);
        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result));
        var type = read.MainModule.GetType(MarkedType)!;

        // The injector ran, and what it wrote is on the type which the attribute named.
        Assert.That(type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(ObsoleteAttribute).FullName), Is.True,
                    "the injector of the type did not run.");

        // The attribute has done its work by now, so it is gone from the member and from the assembly which declares it.
        Assert.That(type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == MarkerAttributeName), Is.False,
                    "the mark was left on the member.");

        // An assembly which names itself cannot be read back, so the assembly which was produced names no assembly of its
        // own name, the one it is written to included.
        Assert.That(read.MainModule.AssemblyReferences.Any(reference => reference.Name == read.Name.Name), Is.False,
                    "the woven assembly refers to itself.");
        Assert.That(read.MainModule.Types.Any(candidate => candidate.FullName == MarkerAttributeName), Is.False,
                    "the attribute which the injector was read from was left in the assembly.");
    }

    [Test]
    public void Apply_Does_Not_Name_The_Corlib_Of_The_Runtime_In_The_Image_It_Produces()
    {
        // The weaving runs on the runtime of whoever drives it, whose corlib is not the one the assembly being woven was
        // compiled against: a build which runs on .NET writes the attributes it applies out of the corlib of .NET, while
        // the assembly which is woven names the corlib of its own framework. A loader which is handed both of them reads
        // the assembly as one which cannot be resolved - the editor of Unity answers "Unable to resolve reference
        // 'System.Private.CoreLib'" and refuses the whole of it - so an assembly which was woven is one which is not
        // loaded at all, and the injectors of a project never run there.
        var image = TestAssemblyImage();
        var assembly = AssemblyLoader.LoadFromBytes(image);

        var (changed, result) = Injections.Apply(assembly, image);

        Assert.That(changed, Is.True);
        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result));
        Assert.That(read.MainModule.AssemblyReferences.Any(reference => reference.Name == "System.Private.CoreLib"), Is.False,
                    "the image names the corlib of the runtime which wove it.");
    }

    [Test]
    public void Apply_Runs_The_Injector_Which_Is_Read_From_An_Attribute_Of_A_Type_That_Is_One()
    {
        // An attribute is one of the attributes which an injector is read from where the interface of the weaver is
        // implemented by a type which the attribute derives from, which is settled by walking the base types of the
        // module. Both of the two are gone from the image afterwards: the interface reaches for the weaver, and the
        // attribute which derives from the type that declares it names that type.
        var image = TestAssemblyImage();
        var assembly = AssemblyLoader.LoadFromBytes(image);

        var (changed, result) = Injections.Apply(assembly, image);

        Assert.That(changed, Is.True);
        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result));
        AssertMarked(read, DerivedMarkedType, "type");

        Assert.That(read.MainModule.Types.Any(candidate => candidate.FullName == DerivedMarkerAttributeName), Is.False,
                    "the attribute which the injector was read from was left in the assembly.");
        Assert.That(read.MainModule.Types.Any(candidate => candidate.FullName == MarkerAttributeName), Is.False,
                    "the attribute which the derived one derives from was left in the assembly.");
    }

    [Test]
    public void Apply_Runs_The_Injector_Which_Is_Read_From_An_Attribute_Of_An_Interface_That_Is_One()
    {
        // The same, one interface further out: the interface which the attribute implements is not one of the weaver,
        // and it is the interface of the weaver which that one implements.
        var image = TestAssemblyImage();
        var assembly = AssemblyLoader.LoadFromBytes(image);

        var (changed, result) = Injections.Apply(assembly, image);

        Assert.That(changed, Is.True);
        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result));
        var run = read.MainModule.GetType(ThroughAnInterfaceType)!.Methods.Single(method => method.Name == "Run");

        Assert.That(run.Body.Instructions.Select(instruction => instruction.OpCode), Is.EqualTo(new[] {OpCodes.Ldc_I4_1, OpCodes.Ret}),
                    "the injector which an interface of the weaver was implemented for did not run.");
        Assert.That(read.MainModule.Types.Any(candidate => candidate.FullName == InterfaceInjectorName), Is.False,
                    "the interface which reaches the weaver was left in the assembly.");
    }

    [Test]
    public void Apply_Reads_An_Injector_Of_Another_Assembly_And_Takes_Its_Attribute_Out()
    {
        // A project which declares the attributes for another project to weave with is the case of an injector which is
        // read from an attribute of one assembly and put on a member of another one. The injector is applied, and the
        // attribute is taken off the member which carries it: the type of it is declared by the other assembly, which
        // keeps it, so the attribute is the trace of a weaving which crossed the boundary of the assemblies.
        var built = Assembly.Create("HostOfAnInjectorOfAnotherAssembly");
        var host = AddAHost(built, "Host");
        var run = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        run.AddAttribute(typeof(RunBodyAttribute).ToGneedleType());

        using var written = new MemoryStream();
        built.SaveTo(written);
        var image = written.ToArray();

        var (changed, result) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image);

        Assert.That(changed, Is.True, "the injector which another assembly declares was not applied.");
        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result));
        var woven = read.MainModule.GetType($"{Ns}.Host")!.Methods.Single(method => method.Name == "Run");

        // The body which the injector names is declared by that assembly as well, so a member which was woven is one
        // whose weaving read the injector and the body of its template both across the boundary of the assemblies.
        Assert.That(woven.Body.Instructions.Select(instruction => instruction.OpCode), Is.EqualTo(new[] {OpCodes.Ldc_I4_1, OpCodes.Ret}),
                    "the body which the injector of the other assembly names was not woven.");
        Assert.That(woven.CustomAttributes.Any(attribute => attribute.AttributeType.Name == nameof(RunBodyAttribute)), Is.False,
                    "the attribute which the injector was read from was left on the member.");
    }

    [Test]
    public void IsAnInjector_Does_Not_Follow_A_Type_Which_The_Module_Does_Not_Declare()
    {
        // The reading follows the base types and the interfaces of the types which the module declares, and it stops
        // where the module ends: the weaving takes the traces of the injectors back out of the assembly it wove, and it
        // can only take out what the module declares. A type which was read through one of another assembly would be an
        // injector which is applied while its trace is left behind, which is an assembly that still names the weaver by
        // an attribute which nobody reads any more.
        var assembly = Assembly.Create("ForeignInjectorAssembly");
        var module = assembly.Source.MainModule;
        var host = AddAHost(assembly);

        // The names are those of a real injector of this assembly, and the references name them as types of another one:
        // what is reached for is a name which the module does not hold, and the assembly which holds it is not asked.
        var foreign = new AssemblyNameReference("Gneedle.Inject.Test", new Version(1, 0));
        host.Source.BaseType = new TypeReference("Gneedle.Inject.Test", nameof(DerivedMarkAttribute), module, foreign);
        host.Source.Interfaces.Add(new InterfaceImplementation(new TypeReference("Gneedle.Inject.Test", nameof(IRunBodyInjector), module, foreign)));

        Assert.That(InjectorInterfaces.IsAnInjector(host.Source, InjectorInterfaces.AllNames), Is.False,
                    "a base type or an interface of another assembly was followed.");

        // The same name is read once the module declares it, which is what tells the answer above apart from a reading
        // which follows no base type at all.
        var declared = new TypeDefinition("Gneedle.Inject.Test", "ForeignInjector", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        declared.Interfaces.Add(new InterfaceImplementation(module.ImportReference(typeof(IMethodInjector))));
        module.Types.Add(declared);
        host.Source.BaseType = new TypeReference("Gneedle.Inject.Test", declared.Name, module, module);

        Assert.That(InjectorInterfaces.IsAnInjector(host.Source, InjectorInterfaces.AllNames), Is.True,
                    "the base type of a type which the module declares was not followed.");
    }

    [Test]
    public void Apply_Keeps_The_Weaver_When_It_Is_Asked_For()
    {
        var image = TestAssemblyImage();
        var assembly = AssemblyLoader.LoadFromBytes(image);

        var (_, result) = Injections.Apply(assembly, image, removesTheWeaver: false);

        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result));
        var type = read.MainModule.GetType(MarkedType)!;
        Assert.That(type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == MarkerAttributeName), Is.True,
                    "the mark was taken off although the weaver was asked for.");
    }

    [Test]
    public void Apply_Changes_Nothing_When_The_Assembly_Holds_No_Injector()
    {
        var built = Assembly.Create("UninjectedAssembly");
        built.Handler.AddClass("Host", Ns, ClassFlags.Public);
        using var written = new MemoryStream();
        built.SaveTo(written);
        var image = written.ToArray();

        var reported = new List<string>();
        var (changed, result) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image, reportError: reported.Add);

        Assert.That(changed, Is.False);
        Assert.That(reported, Is.Empty);
        Assert.That(result, Is.SameAs(image), "an image which was not changed is not the one which was given.");
    }

    // The variable of the process is read by the injector of every type which is woven while it is set, so this test
    // runs on its own rather than beside the others, which would be woven by the injector of this test.
    [Test]
    [NonParallelizable]
    public void Apply_Reports_The_Kind_Of_The_Fault_And_The_Frame_It_Stands_At()
    {
        // What is reported of a type which could not be woven is what a reader has to find the fault by. A shape which
        // the weaver did not expect is the fault which is hardest to find, so the kind of the exception and the frame it
        // stands at are written rather than its message alone, which an exception of the runtime says nothing with.
        var image = TestAssemblyImage();
        var assembly = AssemblyLoader.LoadFromBytes(image);
        var reported = new List<string>();
        Environment.SetEnvironmentVariable(ThrowTypeAttribute.TypeVariable, ThrowingType);

        try
        {
            Injections.Apply(assembly, image, reportError: reported.Add);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ThrowTypeAttribute.TypeVariable, null);
        }

        var report = reported.Single(message => message.Contains(ThrowingType));
        Assert.That(report, Does.Contain(nameof(InvalidOperationException)), $"the report does not name the kind of the fault: {report}");
        Assert.That(report, Does.Contain($"{nameof(ThrowTypeAttribute)}.{nameof(ThrowTypeAttribute.Inject)}"),
                    $"the report does not name the frame which the fault stands at: {report}");
    }

    // The variable of the process is read by the injector of every type which is woven while it is set, so this test
    // runs on its own rather than beside the others, which would be woven by the injector of this test.
    [Test]
    [NonParallelizable]
    public void Apply_Which_Reported_A_Type_Answers_With_The_Image_It_Was_Given()
    {
        // One type of the assembly is woven and another one is not, which the run reports. What the assembly holds by
        // then is the part of the weaving which got through, which no caller can tell from a whole one: the image which
        // was given is answered instead, so that a build which reports the error and carries on writes the assembly it
        // built rather than half of a woven one.
        var image = TestAssemblyImage();
        var assembly = AssemblyLoader.LoadFromBytes(image);
        var reported = new List<string>();
        Environment.SetEnvironmentVariable(ThrowTypeAttribute.TypeVariable, ThrowingType);

        try
        {
            var (changed, result) = Injections.Apply(assembly, image, reportError: reported.Add);

            Assert.That(reported.Any(message => message.Contains(ThrowingType)), Is.True, string.Join(Environment.NewLine, reported));
            Assert.That(changed, Is.False, "a run which reported a type answered with the image it wove.");
            Assert.That(result, Is.SameAs(image), "the image of a run which reported a type is not the one which was given.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ThrowTypeAttribute.TypeVariable, null);
        }

        // The same assembly is woven by a run which reports nothing, which is what tells the answer above apart from an
        // assembly which had nothing to be woven.
        Assert.That(Injections.Apply(assembly, image).Changed, Is.True, "the assembly of this test is woven by nothing.");
    }

    #region Two injectors on one member

    /// <summary>
    /// The names of the types whose members carry two injectors, which are written out rather than taken from the types
    /// for the reason which the names above are written out for.
    /// </summary>
    private const string TwoAroundType = "Gneedle.Inject.Test.TwoAroundFixture";

    /// <inheritdoc cref="TwoAroundType"/>
    private const string ReplaceThenAroundType = "Gneedle.Inject.Test.ReplaceThenAroundFixture";

    /// <inheritdoc cref="TwoAroundType"/>
    private const string AroundThenReplaceType = "Gneedle.Inject.Test.AroundThenReplaceFixture";

    /// <summary>
    /// Weave the assembly of these tests with the injectors of the two turned on for the one type which is named, and
    /// answer with what the run reported and with the image it answered with.
    /// </summary>
    private static (bool Changed, byte[] Result, byte[] Given, List<string> Reported) WeaveWithTheInjectorsOf(string typeName)
    {
        var image = TestAssemblyImage();
        var assembly = AssemblyLoader.LoadFromBytes(image);
        var reported = new List<string>();
        Environment.SetEnvironmentVariable(InjectorOrder.TypeVariable, typeName);

        try
        {
            var (changed, result) = Injections.Apply(assembly, image, reportError: reported.Add);
            return (changed, result, image, reported);
        }
        finally
        {
            Environment.SetEnvironmentVariable(InjectorOrder.TypeVariable, null);
        }
    }

    /// <summary>
    /// Whether the member calls the method which the weaving around it generated, which is what a member that kept the
    /// body it held is told from one which gave it up.
    /// </summary>
    private static bool CallsTheProceedMethod(byte[] image, string typeName)
    {
        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(image));
        var run = read.MainModule.GetType(typeName)!.Methods.Single(method => method.Name == "Run");
        return run.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference && reference.Name == "<Run>k__Proceed");
    }

    /// <summary>
    /// Whether the type declares the method which the weaving around the member above generated.
    /// </summary>
    private static bool DeclaresTheProceedMethod(byte[] image, string typeName)
    {
        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(image));
        return read.MainModule.GetType(typeName)!.Methods.Any(method => method.Name == "<Run>k__Proceed");
    }

    // The variable of the process is read by the injectors of every type which is woven while it is set, so these tests
    // run on their own rather than beside the others.
    [Test]
    [NonParallelizable]
    public void Apply_Of_Two_Injectors_Which_Weave_Around_One_Member_Reports_The_Second_And_Answers_With_The_Image_It_Was_Given()
    {
        // A member is woven around once: the second weave around it has nowhere to put the body which the first one
        // took over, so it is refused rather than woven over the top of it.
        var (changed, result, given, reported) = WeaveWithTheInjectorsOf(TwoAroundType);

        Assert.That(reported, Is.Not.Empty, "the second weave around the member was not reported.");
        Assert.That(reported.Single(), Does.Contain("already woven around"), string.Join(Environment.NewLine, reported));

        // The refusal leaves the member of that type woven in part, which is what a build reports and discards: no image
        // at all is handed back of the run.
        Assert.That(changed, Is.False, "a run which reported a member answered with the image it wove.");
        Assert.That(result, Is.SameAs(given), "the image of a run which reported is not the one which was given.");
    }

    [Test]
    [NonParallelizable]
    public void Apply_Of_A_Body_Which_Is_Replaced_And_Then_Woven_Around_Wraps_The_Replacement()
    {
        // The replacement writes the body the member holds, and the weave around it takes that body over: what the
        // template proceeds into is the body which the injector before it wrote.
        var (changed, result, _, reported) = WeaveWithTheInjectorsOf(ReplaceThenAroundType);

        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));
        Assert.That(changed, Is.True, "the member was woven by nothing.");
        Assert.That(DeclaresTheProceedMethod(result, ReplaceThenAroundType), Is.True, "no body was taken over to proceed into.");
        Assert.That(CallsTheProceedMethod(result, ReplaceThenAroundType), Is.True, "the member does not proceed into the body it was given.");
    }

    [Test]
    [NonParallelizable]
    public void Apply_Of_A_Body_Which_Is_Woven_Around_And_Then_Replaced_Leaves_The_Generated_Method_Uncalled()
    {
        // The replacement writes the body of the member and says nothing of the body it held, so the weave around the
        // member is gone by the time the replacement is written. What is left of it is the method which holds the body
        // which was taken over, which nothing calls by then.
        var (changed, result, _, reported) = WeaveWithTheInjectorsOf(AroundThenReplaceType);

        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));
        Assert.That(changed, Is.True, "the member was woven by nothing.");
        Assert.That(CallsTheProceedMethod(result, AroundThenReplaceType), Is.False, "the member proceeds into a body which the replacement gave up.");

        // The method which the weave around generated is declared by the type and is added to it before the replacement
        // runs, so the replacement leaves it behind: nothing calls it by then, and nothing takes it back out.
        Assert.That(DeclaresTheProceedMethod(result, AroundThenReplaceType), Is.True,
                    "the method which the weave around generated was not left in the type.");
    }

    #endregion

    [Test]
    public void Apply_Weaves_A_Member_Through_The_Signature_Which_Its_Injector_Was_Put_On()
    {
        // The two members of one name stand for the case which a name does not settle: each injector was put on one of
        // the overloads, and the member which it is applied to is the one whose parameters are the parameters of the
        // member which carries it. The member which takes no parameter carries none for the weaving to read, so it is
        // the one which a lookup by the name alone answers with the member beside it.
        var image = TestAssemblyImage();
        var assembly = AssemblyLoader.LoadFromBytes(image);

        var (changed, result) = Injections.Apply(assembly, image);

        Assert.That(changed, Is.True);
        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result));
        var type = read.MainModule.GetType(OverloadedType)!;

        var none = type.Methods.Single(method => method.Name == "Run" && method.Parameters.Count == 0);
        Assert.That(none.Body.Instructions.Select(instruction => instruction.OpCode), Is.EqualTo(new[] {OpCodes.Ldc_I4_1, OpCodes.Ret}),
                    "the member which takes no parameter was woven into other than the body which its injector names.");

        var one = type.Methods.Single(method => method.Name == "Run" && method.Parameters.Count == 1);
        Assert.That(one.Body.Instructions.Select(instruction => instruction.OpCode), Is.EqualTo(new[] {OpCodes.Ldc_I4_2, OpCodes.Ret}),
                    "the member which takes a parameter was woven into other than the body which its injector names.");
    }

    [Test]
    public void GetType_Of_A_Type_Answers_With_The_Handler_Of_The_Kind_Of_It()
    {
        // The kind of a type is told by the handler of it, and that is what the injector which names a kind is asked
        // of: a handler which every type is answered with alike holds no kind, which refuses an injector that names one
        // for a type of every kind there is.
        using var stream = new MemoryStream(TestAssemblyImage());
        using var target = Assembly.Read(stream);
        var handler = (AssemblyHandler) target.Handler;

        Assert.That(handler.GetType(typeof(MarkedClassFixture)), Is.InstanceOf<IClassHandler>(), "a class is not answered with the handler of a class.");
        Assert.That(handler.GetType(typeof(MarkedStructFixture)), Is.InstanceOf<IStructHandler>(), "a struct is not answered with the handler of a struct.");
        Assert.That(handler.GetType(typeof(MarkedEnumFixture)), Is.InstanceOf<IEnumHandler>(), "an enum is not answered with the handler of an enum.");

        // An interface is of none of those kinds, so the handler of it names none either: an injector which names a
        // kind of type is refused for it rather than applied to it as the kind it is nearest to.
        Assert.That(handler.GetType(typeof(IDisposable)), Is.Not.InstanceOf<IClassHandler>(), "an interface is answered with the handler of a class.");
        Assert.That(handler.GetType(typeof(IDisposable)), Is.Not.InstanceOf<IStructHandler>(), "an interface is answered with the handler of a struct.");
    }

    [Test]
    public void Apply_Runs_The_Injector_Which_Names_The_Kind_Of_The_Type_It_Is_Put_On()
    {
        var image = TestAssemblyImage();
        var assembly = AssemblyLoader.LoadFromBytes(image);

        var (changed, result) = Injections.Apply(assembly, image);

        Assert.That(changed, Is.True, "the assembly of this test is woven by nothing.");
        using var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result));
        AssertMarked(read, MarkedClassType, "class");
        AssertMarked(read, MarkedStructType, "struct");
        AssertMarked(read, MarkedEnumType, "enum");
    }

    /// <summary>
    /// Assert that the marker of the kind of <paramref name="typeName"/> was written onto it by the injector which was
    /// put on it.
    /// </summary>
    /// <param name="read">The image which was woven.</param>
    /// <param name="typeName">Full name of the type which carries the marker of its kind.</param>
    /// <param name="kind">The kind which the injector of the type names, which what is reported names.</param>
    private static void AssertMarked(AssemblyDefinition read, string typeName, string kind)
    {
        var type = read.MainModule.GetType(typeName)!;
        Assert.That(type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(ObsoleteAttribute).FullName), Is.True,
                    $"the injector which names a {kind} did not run for the {kind} it was put on.");
    }

    #endregion

    #region The template of the assembly which is woven

    [Test]
    public void SetBody_Of_A_Template_Of_The_Assembly_Which_Is_Woven_Leaves_No_Name_Of_It()
    {
        // A template which the assembly being woven declares is read out of the module rather than imported from the
        // runtime, as a type of that assembly is: the import of a member names the assembly which declares it, and the
        // name of the assembly which is being written is a reference of an assembly to itself, which no loader reads
        // back. The image of this assembly is what is woven here, because that case is one which no assembly which is
        // built stands for.
        var image = TestAssemblyImage();
        using var stream = new MemoryStream(image);
        using var target = Assembly.Read(stream);
        var host = (TypeHandler) ((AssemblyHandler) target.Handler).GetType(typeof(OverloadedFixture));
        var run = (MethodHandler) host.GetMethodBySignature(nameof(OverloadedFixture.Run), [])!;

        run.SetBody(typeof(OverloadBodies).GetMethod(nameof(OverloadBodies.None))!);

        // The template was read, which is what tells an assembly which was not woven at all from one which was woven
        // without leaving the name of itself behind.
        Assert.That(run.Source.Body.Instructions.Select(instruction => instruction.OpCode), Is.EqualTo(new[] {OpCodes.Ldc_I4_1, OpCodes.Ret}),
                    "the template of the assembly itself was not woven.");
        Assert.That(target.Source.MainModule.AssemblyReferences.Any(reference => reference.Name == target.Source.Name.Name), Is.False,
                    "the woven assembly refers to itself.");
    }

    [Test]
    public void A_Template_Of_The_Assembly_Which_Is_Woven_Is_Woven_Into_Every_Member_It_Is_Given_To()
    {
        // The table of a switch belongs to the instruction of the template, which every weave of that template adds to
        // the body it writes: a template which the assembly being woven declares is the same definition on every weave,
        // so a table which is carried onto that instruction is the table which the next weave reads, and the entries of
        // it name the instructions of the body which was written first.
        var image = TestAssemblyImage();
        using var stream = new MemoryStream(image);
        using var target = Assembly.Read(stream);
        var handler = (AssemblyHandler) target.Handler;
        var first = SwitchHost(handler, "SwitchHostFirst");
        var second = SwitchHost(handler, "SwitchHostSecond");
        var template = typeof(PointerTests.ThisMemberTemplates).GetMethod(nameof(PointerTests.ThisMemberTemplates.ReadAFieldPerCase))!;

        var firstMethod = first.AddMethod("Read", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        firstMethod.SetBody(template);
        var firstBody = ((MethodHandler) firstMethod).Source.Body;

        var secondMethod = second.AddMethod("Read", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        Assert.DoesNotThrow(() => secondMethod.SetBody(template),
                            "the second weave of a template of the assembly itself was refused rather than carried.");
        var secondBody = ((MethodHandler) secondMethod).Source.Body;

        AssertTableOf(firstBody);
        AssertTableOf(secondBody);
    }

    /// <summary>
    /// Assert that every entry of the table of the switch which the body holds names an instruction of that body.
    /// </summary>
    /// <param name="body">The body which was woven.</param>
    private static void AssertTableOf(Mono.Cecil.Cil.MethodBody body)
    {
        var table = body.Instructions.SelectMany(instruction => instruction.Operand as Instruction[] ?? []).ToArray();

        Assert.That(table, Is.Not.Empty, "the switch table of the template was not written.");
        Assert.That(table.All(body.Instructions.Contains), Is.True,
                    "an entry of the switch table named an instruction which the body does not hold.");
    }

    /// <summary>
    /// Create a class of a host assembly which declares the two fields the switch template names.
    /// </summary>
    /// <param name="handler">The handler of the assembly to add the class to.</param>
    /// <param name="name">The name of the class, which a test runs one of its own so that the two weaves are told apart.</param>
    private static TypeHandler SwitchHost(AssemblyHandler handler, string name)
    {
        var host = (TypeHandler) handler.AddClass(name, Ns, ClassFlags.Public).GetHandler();
        host.Source.Fields.Add(new FieldDefinition("Value", FieldAttributes.Public, host.Source.Module.TypeSystem.Int32));
        host.Source.Fields.Add(new FieldDefinition("Other", FieldAttributes.Public, host.Source.Module.TypeSystem.Int32));
        return host;
    }

    [Test]
    public void A_Template_Of_Another_Assembly_Is_Read_Out_Of_The_Assembly_Which_Declares_It()
    {
        // A template which another assembly declares is read through the resolution of that assembly, which is the case
        // of a project which declares the attributes for another project to weave with. The image of an assembly is read
        // deferred, so the body of a method is read out of the stream of that image as it is asked for: the stream is
        // handed over to the module rather than closed with the read, which is what this reads the body through.
        var resolver = new CachedAssemblyResolver(new ResolverWhichFindsNothing());
        var name = typeof(OverloadBodies).Assembly.GetName();

        var assembly = resolver.Resolve(new AssemblyNameReference(name.Name!, name.Version!));
        var template = assembly.MainModule.GetType(typeof(OverloadBodies).FullName!)!
                                .Methods.Single(method => method.Name == nameof(OverloadBodies.None));

        Assert.That(template.Body.Instructions.Select(instruction => instruction.OpCode), Does.Contain(OpCodes.Ldc_I4_1),
                    "the body of a method of an assembly which is loaded in the process was not read.");
    }

    [Test]
    public void The_Assemblies_Which_An_Image_Refers_To_Are_Read_From_The_Directory_Which_It_Lies_In()
    {
        // A build weaves the assembly which it has just produced, and the process which runs it knows nothing of the
        // folders of that project: the assemblies which the image refers to are the ones which the build copied beside
        // it, and that folder is what is handed to the weaving as the place to read them from. Without it a project
        // which declares its attributes for another project to weave with is woven by nothing, because the attribute
        // which says what to weave with, and the body which that attribute names, are both of the other assembly.
        var directory = Path.Combine(Path.GetTempPath(), $"Gneedle.Inject.Test.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var beside = Assembly.Create("BesideOfTheImage");
            ((AssemblyHandler) beside.Handler).AddClass("Marker", Ns, ClassFlags.Public).GetHandler();
            using (var file = File.Create(Path.Combine(directory, "BesideOfTheImage.dll"))) beside.SaveTo(file);

            var name = new AssemblyNameReference("BesideOfTheImage", new Version(1, 0));

            // The resolver which finds nothing stands for the process of a build, which holds neither a folder of the
            // project which was built nor an assembly of it: what is read here is read from the folder which is given.
            Assert.Throws<AssemblyResolutionException>(() => new CachedAssemblyResolver(new ResolverWhichFindsNothing()).Resolve(name),
                                                       "an assembly which lies nowhere was resolved.");

            // The image names the assembly of the other project, which is what the project that carries the attributes
            // of another one is: the weaving reads them through that name.
            var woven = Assembly.Create("WovenImage");
            woven.Source.MainModule.AssemblyReferences.Add(new AssemblyNameReference(name.Name, name.Version));
            using var stream = new MemoryStream();
            woven.SaveTo(stream);
            stream.Position = 0;

            using var assembly = Assembly.Read(stream, AssemblySymbol.None, directory);
            var resolved = assembly.Source.MainModule.AssemblyResolver.Resolve(name);

            Assert.That(resolved.MainModule.GetType($"{Ns}.Marker"), Is.Not.Null,
                        "the assembly which the image refers to was not read from the directory which the image lies in.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A resolver which finds nothing, which is what the file system answers with for an assembly which none of the
    /// search directories of a resolver holds. It stands for the resolver of a module which was read from bytes, which
    /// holds no directory to look in, so that what is read here is the assembly which the process loaded.
    /// </summary>
    private sealed class ResolverWhichFindsNothing : IAssemblyResolver
    {
        /// <inheritdoc/>
        public AssemblyDefinition Resolve(AssemblyNameReference name) => throw new AssemblyResolutionException(name);

        /// <inheritdoc/>
        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) => throw new AssemblyResolutionException(name);

        /// <inheritdoc/>
        public void Dispose() { }
    }

    #endregion

    #region Taking the weaver back out

    /// <summary>
    /// Add a class which declares an injector to <paramref name="assembly"/>, which is what a project does when it
    /// writes an attribute to inject with.
    /// </summary>
    private static TypeHandler AddInjector(Assembly assembly)
    {
        var handler = (AssemblyHandler) assembly.Handler;
        var injector = (TypeHandler) handler.AddClass("Injector", Ns, ClassFlags.Public)
                                          .WithInterface(typeof(IMethodInjector).ToGneedleType())
                                          .GetHandler();

        var module = assembly.Source.MainModule;
        var method = new MethodDefinition("Inject", MethodAttributes.Public, module.TypeSystem.Void) { DeclaringType = injector.Source };
        method.Parameters.Add(new ParameterDefinition("method", ParameterAttributes.None, module.ImportReference(typeof(MethodInfo))));
        method.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.None, module.ImportReference(typeof(IMethodHandler))));
        method.Body.GetILProcessor().Emit(OpCodes.Ret);
        injector.Source.Methods.Add(method);

        return injector;
    }

    private static bool NamesTheWeaver(Assembly assembly)
        => assembly.Source.MainModule.AssemblyReferences.Any(reference => reference.Name == "Gneedle.Inject");

    /// <summary>
    /// Write the assembly to a stream, which an image whose metadata names what it does not hold cannot be.
    /// </summary>
    private static void VerifyWritable(Assembly assembly)
    {
        using var stream = new MemoryStream();
        assembly.SaveTo(stream);
    }

    [Test]
    public void RemoveTheWeaver_Removes_A_Type_Which_Nothing_Names_And_Drops_The_Reference()
    {
        var assembly = Assembly.Create("TraceOnlyInjectorAssembly");
        AddInjector(assembly);

        // The interface and the parameter of the method are what name the weaver, and the class is what holds them.
        Assert.That(NamesTheWeaver(assembly), Is.True);

        Assert.That(((AssemblyHandler) assembly.Handler).RemoveTheWeaver(), Is.True);

        Assert.That(assembly.Source.MainModule.Types.Any(type => type.Name == "Injector"), Is.False);
        Assert.That(NamesTheWeaver(assembly), Is.False);
        VerifyWritable(assembly);
    }

    [Test]
    public void RemoveTheWeaver_Keeps_A_Type_Which_The_Assembly_Names_And_Strips_It()
    {
        var assembly = Assembly.Create("TracedFieldAssembly");
        var injector = AddInjector(assembly);

        // A field which holds the attribute is what the assembly names it by, which is what a type cannot be removed
        // out from under without leaving the field naming what is not there.
        var host = AddAHost(assembly);
        host.Source.Fields.Add(new FieldDefinition("Injector", FieldAttributes.Public, injector.Source));

        ((AssemblyHandler) assembly.Handler).RemoveTheWeaver();

        var kept = assembly.Source.MainModule.GetType($"{Ns}.Injector");
        Assert.That(kept, Is.Not.Null, "the type which the field names was removed.");
        Assert.That(kept!.Interfaces.Any(implementation => implementation.InterfaceType.FullName == typeof(IMethodInjector).FullName), Is.False);
        Assert.That(kept.Methods.Any(method => method.Name == "Inject"), Is.False);

        // The interface and the method which reached for the weaver went with the strip, so the class which the field
        // holds names the weaver no longer, and the reference goes with them: the field keeps its type and the assembly
        // keeps no weaver.
        Assert.That(NamesTheWeaver(assembly), Is.False);
        VerifyWritable(assembly);
    }

    [Test]
    public void RemoveTheWeaver_Of_An_Assembly_Which_Declares_No_Injector_Changes_Nothing()
    {
        var assembly = Assembly.Create("UntracedAssembly");
        var host = AddAHost(assembly);
        host.AddMethod("Ping", typeof(void).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);

        Assert.That(((AssemblyHandler) assembly.Handler).RemoveTheWeaver(), Is.False);
        Assert.That(assembly.Source.MainModule.Types.Any(type => type.Name == "Host"), Is.True);
    }

    #endregion
}