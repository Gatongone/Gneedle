using System.Diagnostics;
using System.Reflection;

namespace Gneedle.Inject.Test;

/// <summary>
/// The build which the templates of the suite were read out of.<para/>
/// The tests read the templates back as the IL which the compiler wrote for them, and which IL that is depends on
/// whether the build of this assembly was optimized: the shapes which the assertions name are the ones a build without
/// the optimizer writes, and the shapes which a release build of a consumer holds are the ones the optimizer writes. The
/// second of those is the leg which <c>-p:Optimize=true</c> asks for, which names itself in the metadata of the
/// assembly, and the first is the build without it.
/// </summary>
[TestFixture]
public class TemplateLegTests
{
    /// <summary>
    /// The leg which the assembly names is the leg which the assembly was built as, which is what makes a run of the
    /// suite a run of the shapes it names: a build which does not answer the flag, or a build which was left from the
    /// other leg, would run every test of one shape against the other and report success for a suite which read nothing
    /// of what it says it read.
    /// </summary>
    [Test]
    public void The_Assembly_Is_The_Leg_Which_It_Names()
    {
        var assembly = typeof(TemplateLegTests).Assembly;
        var namesAnOptimizedLeg = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                                          .Any(attribute => attribute.Key == "OptimizedTemplates");

        var debuggable = assembly.GetCustomAttribute<DebuggableAttribute>();
        Assert.That(debuggable, Is.Not.Null, "the assembly holds no attribute which says how it was built, so the shapes which it holds cannot be told apart.");

        var optimized = !debuggable!.IsJITOptimizerDisabled;
        Assert.That(optimized, Is.EqualTo(namesAnOptimizedLeg),
            $"the assembly is built {(optimized ? "with" : "without")} the optimizer, and names {(namesAnOptimizedLeg ? "an optimized leg" : "the leg without it")} of itself.");
    }
}