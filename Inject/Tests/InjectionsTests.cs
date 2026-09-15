using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Assembly = Gneedle.Inject.Assembly;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace Gneedle.Inject.Test;

/// <summary>
/// An attribute which marks the type it is put on as obsolete, so that the injection is visible in the image which was
/// woven.
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public sealed class MarkTypeAttribute : Attribute, ITypeInjector
{
    /// <inheritdoc/>
    public void Inject(Type type, ITypeHandler handler) => handler.AddAttribute(typeof(ObsoleteAttribute).ToGneedleType(), "marked");
}

/// <summary>
/// The type which carries the marker above, whose injection is read out of the image which was woven.
/// </summary>
[MarkType]
public class MarkedFixture;

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
    public void Inject(Type type, IClassHandler handler) => handler.AddAttribute(typeof(ObsoleteAttribute).ToGneedleType(), "class");
}

/// <inheritdoc cref="MarkClassAttribute"/>
[AttributeUsage(AttributeTargets.All)]
public sealed class MarkStructAttribute : Attribute, IStructInjector
{
    /// <inheritdoc/>
    public void Inject(Type type, IStructHandler handler) => handler.AddAttribute(typeof(ObsoleteAttribute).ToGneedleType(), "struct");
}

/// <inheritdoc cref="MarkClassAttribute"/>
[AttributeUsage(AttributeTargets.All)]
public sealed class MarkEnumAttribute : Attribute, IEnumInjector
{
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
/// Tests for <see cref="Injections"/>, which applies the injectors which an assembly declares to the image it was loaded
/// from, and for the taking of the weaver back out which that runs.
/// </summary>
[TestFixture]
public class InjectionsTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private const string MarkedType = "Gneedle.Inject.Test.MarkedFixture";

    /// <summary>
    /// The name of the attribute which the injector of <see cref="MarkedFixture"/> is read from.<para/>
    /// The name is written out rather than taken from the type, because naming the type in this assembly is what keeps
    /// the type in the assembly which is woven: a type which the code names cannot be removed without taking the name
    /// with it, so an assertion which named it would hold it in place and then fail on its own doing.
    /// </summary>
    private const string MarkerAttributeName = "Gneedle.Inject.Test.MarkTypeAttribute";

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

    [Test]
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
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
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
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        host.AddMethod("Ping", typeof(void).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);

        Assert.That(((AssemblyHandler) assembly.Handler).RemoveTheWeaver(), Is.False);
        Assert.That(assembly.Source.MainModule.Types.Any(type => type.Name == "Host"), Is.True);
    }

    #endregion
}