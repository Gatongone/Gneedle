#if NET5_0_OR_GREATER
using System.Runtime.Loader;
#endif

using Mono.Cecil;

namespace Gneedle.Inject.Test;

/// <summary>
/// Tests of <see cref="AssemblyLoader"/>, which loads the image of an assembly into the process which weaves it.
/// </summary>
[TestFixture]
public class AssemblyLoaderTests
{
#if NET5_0_OR_GREATER
    /// <summary>
    /// An image which names an assembly of the weaver is read where the weaver which runs lies, whichever context that
    /// is.<para/>
    /// The post processor of a Unity project loads the weaver into a context of its own, and Unity makes that context
    /// collectible. A type of the target which implements an interface of the weaver has to implement the very interface
    /// which the weaving holds, which is the one of that context, and an assembly of the weaver is not handed over to a
    /// context which the runtime made for the image: the reading of the types of the target fails whole where it is asked
    /// for, which leaves an assembly which declares an injector unwoven.
    /// </summary>
    [Test]
    public void LoadFromBytes_Of_An_Image_Which_Names_A_Weaver_Of_A_Context_Of_Its_Own_Is_Read()
    {
        // The weaver is copied under a name of its own before it is loaded, because the copy of this process is the one
        // the runtime answers a request with before any resolution of the weaver is asked: a name which nothing of this
        // process holds is what leaves the copy of the context as the only one which can answer, which is the state of
        // the process of a post processor of Unity.
        const string name = "Gneedle.Inject.PostProcessing";
        var directory = Path.Combine(Path.GetTempPath(), $"Gneedle.Context.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var context = new AssemblyLoadContext("PostProcessingAssemblyLoadContext", isCollectible: true);
        try
        {
            var weaverPath = Path.Combine(directory, $"{name}.dll");
            var targetPath = Path.Combine(directory, "Target.dll");

            using (var weaver = AssemblyDefinition.ReadAssembly(typeof(AssemblyLoader).Assembly.Location))
            {
                weaver.Name.Name = name;
                weaver.Write(weaverPath);
            }

            // The image which is woven is the one of these tests with the weaver of this process replaced by the copy
            // above, so that every type of it which implements an interface of the weaver implements the one of that copy.
            using (var target = AssemblyDefinition.ReadAssembly(typeof(AssemblyLoaderTests).Assembly.Location))
            {
                var reference = target.MainModule.AssemblyReferences
                                      .First(reference => reference.Name == typeof(AssemblyLoader).Assembly.GetName().Name);
                reference.Name = name;
                target.Write(targetPath);
            }

            // The copy is loaded from its bytes rather than from the file, because a runtime which loaded an assembly
            // from a file holds that file until the context which holds the assembly is unloaded - which a context only
            // asks for - and the directory of the test could not be taken back.
            using var weaverImage = File.OpenRead(weaverPath);
            var weaverOfTheContext = context.LoadFromStream(weaverImage);
            var loader = weaverOfTheContext.GetType(typeof(AssemblyLoader).FullName!)!;
            var image = File.ReadAllBytes(targetPath);

            var loaded = (System.Reflection.Assembly) loader.GetMethod(nameof(AssemblyLoader.LoadFromBytes))!.Invoke(null, [image])!;

            // Every type of the image is read, which is what the weaving does with the assembly it was handed.
            var types = loaded.GetTypes();
            var injector = weaverOfTheContext.GetType(typeof(ITypeInjector).FullName!)!;

            Assert.That(types.Where(injector.IsAssignableFrom), Is.Not.Empty,
                "A type of the image implements the interface of the weaver which runs, and it is read as one of them.");
        }
        finally
        {
            context.Unload();
            Directory.Delete(directory, recursive: true);
        }
    }
#endif

    /// <summary>
    /// Every load of an image is an assembly of its own, so that a weaving never reads what an earlier one left.
    /// </summary>
    [Test]
    public void LoadFromBytes_Of_The_Same_Image_Twice_Answers_An_Assembly_Of_Its_Own_Each_Time()
    {
        // The very same assembly is woven over and over while a project is being written, and each image of it is read
        // for what it holds: a load which answered the copy of an earlier one would weave what an earlier compilation
        // wrote, and an injector which was added or taken out of the sources would not be read again.
        var image = File.ReadAllBytes(typeof(AssemblyLoaderTests).Assembly.Location);

        var first = AssemblyLoader.LoadFromBytes(image);
        var second = AssemblyLoader.LoadFromBytes(image);

        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.GetName().Name, Is.EqualTo(first.GetName().Name));
    }
}