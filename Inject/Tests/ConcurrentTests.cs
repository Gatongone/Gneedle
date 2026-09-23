namespace Gneedle.Inject.Test;

/// <summary>
/// What the library answers when several assemblies are woven at the same time, which is what a project of assemblies
/// does: the caches of a handler, of a resolver and of the loader are read and written by every weaving of it, so the
/// weaving of one assembly is not the only thread which stands at any of them.
/// </summary>
[TestFixture]
public class ConcurrentTests
{
    /// <summary>
    /// The number of weavings which are asked of at the same time, which is more than the threads of a test host so that
    /// the weavings stand at the caches beside each other rather than one after another.
    /// </summary>
    private const int COUNT = 4;

    [Test]
    public void The_Assemblies_Woven_At_The_Same_Time_Are_Woven_As_The_One_Woven_Alone()
    {
        // The weaving of an assembly is a rewrite of it, so two runs of it over one image are the same image: what the
        // runs which stand beside each other answer is read against the run which stands alone, which is what tells a
        // cache which one of them lost an entry, or which one of them answered with the entry of another, from a run
        // which merely took the other order.
        var image = File.ReadAllBytes(typeof(ConcurrentTests).Assembly.Location);

        var alone = Weave(image);
        var together = new byte[COUNT][];

        Parallel.For(0, COUNT, index => together[index] = Weave(image));

        for (var index = 0; index < COUNT; index++)
        {
            Assert.That(together[index], Is.EqualTo(alone), $"the image of the weaving {index} is not the image of the one woven alone.");
        }
    }

    [Test]
    public void A_Type_Which_Several_Threads_Import_At_Once_Is_Imported_Once()
    {
        // A lookup which misses the cache of a handler is the one which imports, and an import appends to the tables of
        // the module the handler holds - which are not collections more than one thread can stand at. The threads below
        // stand at one handler, as the requests of the runtime for an assembly do while a weaving runs, and each of them
        // asks for the same type: what one of them imports is what the rest are given.
        const string assemblyName = "ConcurrentImports";
        var (handler, _, module) = TestFixtures.NewHost(assemblyName);

        var asked = new Type[COUNT * 8];
        for (var index = 0; index < asked.Length; index++) asked[index] = typeof(List<int>);

        var answered = new CecilType[asked.Length];
        Parallel.For(0, asked.Length, index => answered[index] = handler.GetCecilType(asked[index]));
        Assert.Multiple(() =>
        {
            Assert.That(answered.Distinct().Count(), Is.EqualTo(1),
                "the threads which asked for one type were answered with more than one, so each of them imported it.");
            Assert.That(module.AssemblyReferences.Select(reference => reference.Name).Distinct().Count(),
                Is.EqualTo(module.AssemblyReferences.Count),
                "a reference was appended more than once, so the module names an assembly twice.");
        });
    }

    [Test]
    public void The_Members_Which_Several_Threads_Add_To_One_Type_Are_All_Added()
    {
        // The members a type declares are not among the two tables ModuleLock holds - the references of the module and
        // the types it declares - because a member is named within the type which declares it rather than by the module,
        // so a decorator adds one to a type without the lock, as the weaving always has. Whether that is enough is what
        // this reads: a collection of Cecil is not one which two threads may write at the same time, and a member which
        // one of them adds is one the other may write over.
        const string assemblyName = "ConcurrentMembers";
        var (_, host, _) = TestFixtures.NewHost(assemblyName);

        const int added = COUNT * 8;
        Parallel.For(0, added, index => host.AddMethod("Member" + index, typeof(int).ToGneedleType(), [], [], MethodFlags.Public));

        Assert.That(host.Source.Methods.Count(method => method.Name.StartsWith("Member", StringComparison.Ordinal)),
            Is.EqualTo(added), "a member which a thread added is one the type does not declare.");
    }

    private static byte[] Weave(byte[] image)
    {
        var (_, woven) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image);

        return woven;
    }
}