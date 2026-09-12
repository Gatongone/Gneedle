using Mono.Cecil;

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
/// Tests for <see cref="Injections"/>, which applies the injectors of an assembly to the image it was loaded from.
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
    /// The image of the assembly which holds these tests, which holds the fixtures as well.
    /// </summary>
    private static byte[] TestAssemblyImage() => File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location);

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
        built.Handler.AddClass("Host", "Gneedle.Test.Generated", ClassFlags.Public);
        using var written = new MemoryStream();
        built.SaveTo(written);
        var image = written.ToArray();

        var reported = new List<string>();
        var (changed, result) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image, reportError: reported.Add);

        Assert.That(changed, Is.False);
        Assert.That(reported, Is.Empty);
        Assert.That(result, Is.SameAs(image), "an image which was not changed is not the one which was given.");
    }
}
