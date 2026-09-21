using System.Reflection;

namespace Gneedle.Inject.Test;

/// <summary>
/// The order in which the injectors of a member were applied, which the injectors below record themselves in.<para/>
/// What is written is a file rather than a field of this class, because the weaving reads the injectors of an assembly
/// which was loaded from the image of this one: the injectors run as members of that copy, and a field of theirs is a
/// field of the copy, which the test which is running in this process cannot read.
/// </summary>
public static class TheOrderOfTheInjectors
{
    /// <summary>
    /// The name of the variable of the process which holds the path of the file the order is written to, which is what
    /// tells the copy of the assembly where to write, and which no copy writes to where a test did not set it.
    /// </summary>
    public const string LOG_VARIABLE = "GneedleInjectorOrderLog";

    /// <summary>
    /// Write the name of an injector into the log, where the process was given one to write to.
    /// </summary>
    /// <param name="name">The name of the injector which was applied.</param>
    public static void Record(string name)
    {
        if (Environment.GetEnvironmentVariable(LOG_VARIABLE) is not { } path) return;

        File.AppendAllText(path, name + Environment.NewLine);
    }
}

/// <summary>
/// An injector which records that it was applied, under the name and at the priority which it was declared with.
/// </summary>
/// <param name="name">The name which the record of this injector is written under.</param>
/// <param name="priority">The order in which this injector is applied among the injectors of the member it stands on.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RecordsTheOrderAttribute(string name, int priority = 0) : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public int Priority { get; } = priority;

    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler) => TheOrderOfTheInjectors.Record(name);
}

/// <summary>
/// An injector of a priority of its own which records itself under the name of its type, which is what tells two
/// injectors of one priority apart.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AFirstOfItsPriorityAttribute : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler) => TheOrderOfTheInjectors.Record(nameof(AFirstOfItsPriorityAttribute));
}

/// <summary>
/// The same, of a name which sorts after the one above, and which is written where the member is declared before it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ZLastOfItsPriorityAttribute : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler) => TheOrderOfTheInjectors.Record(nameof(ZLastOfItsPriorityAttribute));
}

/// <summary>
/// The members which the injectors above are put on, which the tests weave and read the order of.
/// </summary>
public class OrderedFixture
{
    /// <summary>
    /// A member which two injectors of one type are put on, of which the one which was declared last declares the
    /// greater priority: what is read is that the greater priority is applied before the lesser, whichever order the
    /// attributes were written in.
    /// </summary>
    [RecordsTheOrder("lesser", -10)]
    [RecordsTheOrder("greater", 10)]
    public static int ByPriority() => 0;

    /// <summary>
    /// A member which two injectors of one priority are put on, of two types which sort one way and are written the
    /// other: what is read is that the names of the types are what the order is, the one which sorts first being
    /// applied first.
    /// </summary>
    [ZLastOfItsPriority]
    [AFirstOfItsPriority]
    public static int ByTheNameOfTheType() => 0;
}

/// <summary>
/// Tests for the order in which the injectors of a member are applied.<para/>
/// The specification of the language says that the attribute specifications of a member are equivalent in every order,
/// so the order which the runtime hands them back in is no order of the source at all: what a member's injectors are
/// applied in is the order of the priorities which they declare, and the name of the type of each where two declare
/// none. The tests are run one at a time because the injectors record themselves somewhere the process holds.
/// </summary>
[TestFixture]
[NonParallelizable]
public class InjectorOrderTests
{
    [Test]
    public void The_Injectors_Of_A_Member_Are_Applied_By_Their_Priority()
    {
        var applied = WeaveTheTests();
        Assert.Multiple(() =>
        {
            Assert.That(applied, Does.Contain("greater").And.Contain("lesser"),
                "the injectors of the member which declares two priorities were not applied at all.");
            // Array.IndexOf rather than the extension of the enumerable, which an array is not one of on every framework.
            Assert.That(Array.IndexOf(applied, "greater"), Is.LessThan(Array.IndexOf(applied, "lesser")),
                "the injector of the greater priority was not applied before the one of the lesser.");
        });
    }

    [Test]
    public void Two_Injectors_Of_One_Priority_Are_Applied_By_The_Name_Of_Their_Types()
    {
        var applied = WeaveTheTests();

        var first = Array.IndexOf(applied, nameof(AFirstOfItsPriorityAttribute));
        var last = Array.IndexOf(applied, nameof(ZLastOfItsPriorityAttribute));

        Assert.That(first, Is.GreaterThanOrEqualTo(0).And.LessThan(last),
            "the injector whose type sorts first was not applied before the one whose type sorts last.");
    }

    /// <summary>
    /// Weave the assembly which holds these tests, which holds the fixtures which the injectors are put on as well, and
    /// hand back the names of the injectors which were applied, in the order in which they were applied.
    /// </summary>
    /// <returns>The names of the injectors which were applied.</returns>
    private static string[] WeaveTheTests()
    {
        var log = Path.Combine(Path.GetTempPath(), $"Gneedle.Inject.Order.{Guid.NewGuid():N}.txt");
        Environment.SetEnvironmentVariable(TheOrderOfTheInjectors.LOG_VARIABLE, log);
        try
        {
            var image = File.ReadAllBytes(typeof(InjectorOrderTests).Assembly.Location);

            var (changed, _) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image);

            Assert.That(changed, Is.True, "the assembly of these tests was woven by none of its injectors.");
            return File.Exists(log) ? File.ReadAllLines(log) : [];
        }
        finally
        {
            Environment.SetEnvironmentVariable(TheOrderOfTheInjectors.LOG_VARIABLE, null);
            if (File.Exists(log)) File.Delete(log);
        }
    }
}