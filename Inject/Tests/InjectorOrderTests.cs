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
/// The chain which the order of an inherited injector is read along, whose names are of its own so that the record of
/// it stands alone in the log of the weaving: the name of an injector is what the other installments of the log are
/// told apart by as well, and a name which two chains share is a record which says nothing about either.
/// </summary>
public class ABaseWhichDeclaresTheInnerInjector
{
    /// <summary>The member which the injector of the base stands on, which is applied first.</summary>
    [RecordsTheOrder("inner")]
    public virtual int Compute() => 1;
}

/// <summary>The type which overrides that member and carries an injector of its own, which is applied after it.</summary>
public class AnOverrideWhichDeclaresTheOuterInjector : ABaseWhichDeclaresTheInnerInjector
{
    /// <summary>The member which the injector of the override stands on.</summary>
    [RecordsTheOrder("outer")]
    public override int Compute() => 2;
}

/// <summary>
/// An injector which declares that it is not read above the member it stands on, which is what a type of an attribute
/// says with `Inherited = false`.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class RecordsOnlyWhereItStandsAttribute : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler) => TheOrderOfTheInjectors.Record(nameof(RecordsOnlyWhereItStandsAttribute));
}

/// <summary>
/// An injector of a type, which records itself the way the injectors above do, under a name of its own.
/// </summary>
/// <param name="name">The name which the record of this injector is written under.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class RecordsTheTypeAttribute(string name) : Attribute, ITypeInjector
{
    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public void Inject(Type type, ITypeHandler handler) => TheOrderOfTheInjectors.Record(name);
}

/// <summary>
/// The types which the injectors of a member are read along: a base type which declares a virtual member carrying one,
/// and what the types below it may do with that member.
/// </summary>
public class ABaseWithAnInjectorOnAVirtualMember
{
    /// <summary>The member which the injector stands on.</summary>
    [RecordsTheOrder("inherited")]
    public virtual int Compute() => 1;
}

/// <summary>The type which overrides the member above and carries no injector of its own.</summary>
public class AnOverrideWhichCarriesNone : ABaseWithAnInjectorOnAVirtualMember
{
    /// <inheritdoc/>
    public override int Compute() => 2;
}

/// <summary>The same, of an override which carries an injector of its own.</summary>
public class AnOverrideWhichCarriesOne : ABaseWithAnInjectorOnAVirtualMember
{
    /// <summary>The member which the injector of the override stands on.</summary>
    [RecordsTheOrder("override")]
    public override int Compute() => 3;
}

/// <summary>The base of a member which hides the one above it rather than overriding it.</summary>
public class ABaseWithAnInjectorOnAMemberWhichIsHidden
{
    /// <summary>The member which the injector stands on, which is hidden below.</summary>
    [RecordsTheOrder("hidden")]
    public virtual int Compute() => 1;
}

/// <summary>The type which declares a member of that name which takes a slot of its own.</summary>
public class AHiddenMember : ABaseWithAnInjectorOnAMemberWhichIsHidden
{
    /// <summary>The member which hides the one above it, which supersedes it in a reading of the runtime.</summary>
    public new virtual int Compute() => 2;
}

/// <summary>The base of a member whose injector declares that it does not inherit.</summary>
public class ABaseWithAnInjectorWhichDoesNotInherit
{
    /// <summary>The member which the injector stands on.</summary>
    [RecordsOnlyWhereItStands]
    public virtual int Compute() => 1;
}

/// <summary>The type which overrides that member, and which is read as carrying nothing.</summary>
public class AnOverrideOfAnInjectorWhichDoesNotInherit : ABaseWithAnInjectorWhichDoesNotInherit
{
    /// <inheritdoc/>
    public override int Compute() => 2;
}

/// <summary>The interface whose member carries an injector, which a type implements.</summary>
public interface ICountedWithAnInjector
{
    /// <summary>The member which the injector stands on.</summary>
    [RecordsTheOrder("interface")]
    int Compute();
}

/// <summary>The type which implements the member above, which is not a type the attribute is read on.</summary>
public class ATypeWhichImplementsIt : ICountedWithAnInjector
{
    /// <inheritdoc/>
    public int Compute() => 1;
}

/// <summary>The type which carries an injector of a type, and the type which derives from it.</summary>
[RecordsTheType("type")]
public class ATypeWithAnInjector { }

/// <summary>The type which derives from the one above, which carries nothing of its own.</summary>
public class ADerivedType : ATypeWithAnInjector { }

/// <summary>
/// Tests for which injectors of a member are applied, and in which order.<para/>
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

    [Test]
    public void An_Injector_Of_A_Virtual_Member_Is_Read_On_The_Member_Which_Overrides_It()
    {
        // The attributes of a member are read on the members which override it wherever the type of the attribute
        // declares that it inherits, and the type which declares none is one which inherits: what is read here is that
        // the injector of the base was applied twice, which is once for the member it stands on and once for the
        // override, and that the injector which the override carries itself was applied as well.
        var applied = WeaveTheTests();

        Assert.Multiple(() =>
        {
            Assert.That(applied.Count(name => name == "inherited"), Is.EqualTo(3),
                "the injector of the virtual member of the base type was not read on every member which overrides it.");
            Assert.That(applied.Count(name => name == "override"), Is.EqualTo(1),
                "the injector which the override carries itself was not applied to it.");
        });
    }

    [Test]
    public void An_Injector_Of_A_Base_Member_Is_Applied_Before_The_One_The_Override_Carries()
    {
        // What an around body is applied to is the body which the member holds at that moment, so the injector which is
        // applied last is the one which stands outermost: the more specific advice is the one which sees the call first,
        // and it is written after the one it stands on.
        var applied = WeaveTheTests();

        // The two are the injectors of one member, so what is read is that they stand next to each other in the log and
        // that the one of the base stands first.
        var inner = Array.IndexOf(applied, "inner");
        var outer = Array.IndexOf(applied, "outer");
        Assert.Multiple(() =>
        {
            Assert.That(inner, Is.GreaterThanOrEqualTo(0), "the injector of the base member was not applied at all.");
            Assert.That(outer, Is.EqualTo(inner + 1),
                "the injector of the base member and the one which the override carries were not applied one after the other.");
            Assert.That(inner, Is.LessThan(outer),
                "the injector of the base member was not applied before the one which the override carries.");
        });
    }

    [Test]
    public void An_Injector_Is_Not_Read_On_A_Member_Which_Hides_The_One_It_Stands_On()
    {
        // A member which hides the one above it declares a member of its own rather than overriding that one, and the
        // chain which an attribute is read along is the chain of the overrides.
        var applied = WeaveTheTests();

        Assert.That(applied.Count(name => name == "hidden"), Is.EqualTo(1),
            "the injector of the member of the base type was read on the member which hides it.");
    }

    [Test]
    public void An_Injector_Which_Declares_That_It_Does_Not_Inherit_Is_Not_Read_Above_The_Member_It_Stands_On()
    {
        var applied = WeaveTheTests();

        Assert.That(applied.Count(name => name == nameof(RecordsOnlyWhereItStandsAttribute)), Is.EqualTo(1),
            "the injector which declares that it does not inherit was read on the member which overrides the one it stands on.");
    }

    [Test]
    public void An_Injector_Is_Not_Read_On_A_Member_Which_Implements_The_Member_It_Stands_On()
    {
        // An implementation of an interface member is not an override of it, and the runtime reads no attribute of an
        // interface member on the member which implements it either.
        var applied = WeaveTheTests();

        Assert.That(applied, Has.None.EqualTo("interface"),
            "the injector of the interface member was read on the member which implements it.");
    }

    [Test]
    public void An_Injector_Of_A_Type_Is_Read_On_The_Types_Which_Derive_From_It()
    {
        var applied = WeaveTheTests();

        Assert.That(applied.Count(name => name == "type"), Is.EqualTo(2),
            "the injector of the type was not read on the type which derives from it.");
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