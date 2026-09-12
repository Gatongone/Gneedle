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
