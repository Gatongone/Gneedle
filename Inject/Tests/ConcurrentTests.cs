using System.Threading.Tasks;

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
    private const int Count = 4;

    [Test]
    public void The_Assemblies_Woven_At_The_Same_Time_Are_Woven_As_The_One_Woven_Alone()
    {
        // The weaving of an assembly is a rewrite of it, so two runs of it over one image are the same image: what the
        // runs which stand beside each other answer is read against the run which stands alone, which is what tells a
        // cache which one of them lost an entry, or which one of them answered with the entry of another, from a run
        // which merely took the other order.
        var image = File.ReadAllBytes(typeof(ConcurrentTests).Assembly.Location);

        var alone = Weave(image);
        var together = new byte[Count][];

        Parallel.For(0, Count, index => together[index] = Weave(image));

        for (var index = 0; index < Count; index++)
        {
            Assert.That(together[index], Is.EqualTo(alone), $"the image of the weaving {index} is not the image of the one woven alone.");
        }
    }

    private static byte[] Weave(byte[] image)
    {
        var (_, woven) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image);

        return woven;
    }
}
