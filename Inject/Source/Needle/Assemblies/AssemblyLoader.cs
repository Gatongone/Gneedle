using System.Runtime.CompilerServices;

namespace Gneedle.Inject;

/// <summary>
/// Loads the assemblies of an image which no file of the file system holds, and remembers the bytes each was loaded
/// from.<para/>
/// Nothing hands the image of an assembly over on .NET 5 and later, so an assembly which was loaded from bytes could
/// not be told apart from one which was not, and a type of it could not be resolved. The bytes are remembered here
/// instead, which is what <see cref="FromAssemblyAttribute"/> needs to resolve a type of such an assembly.
/// </summary>
/// <remarks>
/// Only the assemblies which are loaded or remembered here are remembered: the load event of the application domain
/// carries no bytes, so one which the runtime loaded by another way cannot be remembered after the fact. An assembly
/// which was loaded that way is still read where its image lies in the memory, which only Windows can do.
/// </remarks>
public static class AssemblyLoader
{
    /// <summary>
    /// The bytes which an assembly was loaded from, keyed by that assembly. The key is weak, so that an assembly which
    /// is unloaded takes the bytes of its image with it.
    /// </summary>
    private static readonly ConditionalWeakTable<System.Reflection.Assembly, byte[]> s_Images = new();

    /// <summary>
    /// Guard of <see cref="s_Images"/>, which every use of that table holds.<para/>
    /// A table which holds weak keys is thread safe on its own, but the removal and the addition which replace an entry
    /// are not a single operation, and a lookup which ran between them would find nothing. That would cost more than a
    /// repeated read: the image in the memory is not read on every runtime, so the bytes of the table are the only way
    /// to read an assembly which was loaded from bytes there.
    /// </summary>
    private static readonly object s_ImagesGuard = new();

    /// <summary>
    /// The assemblies of the weaver, which a target which was compiled against it refers to.
    /// </summary>
    private static readonly Dictionary<string, System.Reflection.Assembly> s_WeaverAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        [typeof(AssemblyLoader).Assembly.GetName().Name!] = typeof(AssemblyLoader).Assembly,
        [typeof(TypeReference).Assembly.GetName().Name!]  = typeof(TypeReference).Assembly
    };

    /// <summary>
    /// Whether the assemblies of the weaver are answered to an assembly which asks for them, which is done once.
    /// </summary>
    private static int s_WeaverAssembliesRegistered;

    /// <summary>
    /// Load the assembly of <paramref name="rawBytes"/> and remember those bytes.
    /// </summary>
    /// <param name="rawBytes">Bytes that is a COFF-based image containing a managed assembly.</param>
    /// <returns>The loaded assembly.</returns>
    public static System.Reflection.Assembly LoadFromBytes(byte[] rawBytes)
    {
        RegisterWeaverAssemblies();

        var assembly = System.Reflection.Assembly.Load(rawBytes);
        Remember(assembly, rawBytes);
        return assembly;
    }

    /// <summary>
    /// Answer the requests of an assembly which was loaded from bytes for the assemblies of the weaver.
    /// </summary>
    /// <remarks>
    /// The runtime resolves an assembly which was loaded from bytes from the ones which are already loaded and from the
    /// ones which lie beside the process, while a build keeps the weaver in the folder of the package which holds it,
    /// which is neither of the two. Waiting for the request to fail is what leaves the answer here rather than in the
    /// resolution which the runtime would do by itself.<para/>
    /// The assemblies are answered with the ones which are loaded rather than read again from the folder they lie in,
    /// because a type of the target which implements an interface of the weaver has to implement the very interface
    /// which the weaver holds: a second copy of the assembly would hold a second interface, and a type of it would
    /// implement that one.
    /// </remarks>
    private static void RegisterWeaverAssemblies()
    {
        if (Interlocked.Exchange(ref s_WeaverAssembliesRegistered, 1) != 0) return;
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) => FindWeaverAssembly(args.Name);
    }

    /// <summary>
    /// The assembly of the weaver which <paramref name="name"/> names, or null when it names none of them.
    /// </summary>
    /// <param name="name">The name of the assembly which was asked for.</param>
    private static System.Reflection.Assembly? FindWeaverAssembly(string name)
    {
        // The version is not compared, because the target and the weaver are read from the one state of the project and
        // an assembly which the target was built against is the one which the weaver holds.
        var simpleName = new System.Reflection.AssemblyName(name).Name;
        return simpleName != null && s_WeaverAssemblies.TryGetValue(simpleName, out var assembly) ? assembly : null;
    }

    /// <summary>
    /// Remember the bytes which <paramref name="assembly"/> was loaded from.
    /// </summary>
    /// <remarks>
    /// An assembly which the runtime loaded by another way than <see cref="LoadFromBytes"/> could not be remembered by
    /// the loader itself, so it is remembered through this.
    /// </remarks>
    /// <param name="assembly">The assembly which was loaded from the bytes.</param>
    /// <param name="rawBytes">Bytes that is a COFF-based image containing the assembly.</param>
    /// <exception cref="ArgumentNullException">Thrown when the assembly or the bytes are null.</exception>
    public static void Remember(System.Reflection.Assembly assembly, byte[] rawBytes)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        if (rawBytes == null) throw new ArgumentNullException(nameof(rawBytes));

        // The entry is removed before the addition because Add throws on a key which is there, and AddOrUpdate, which
        // would not, is a part of .NET Standard 2.1 alone, which .NET Framework does not hold.
        lock (s_ImagesGuard)
        {
            s_Images.Remove(assembly);
            s_Images.Add(assembly, rawBytes);
        }
    }

    /// <summary>
    /// Try to get the bytes which <paramref name="assembly"/> was loaded from.
    /// </summary>
    /// <param name="assembly">The assembly which need to get raw bytes.</param>
    /// <param name="rawBytes">The bytes which the assembly was loaded from.</param>
    /// <returns>Whether the bytes of the assembly are remembered.</returns>
    internal static bool TryGetSource(System.Reflection.Assembly assembly, out byte[] rawBytes)
    {
        // The guard is held here as well, so that an entry which is being replaced is never read as a missing one.
        lock (s_ImagesGuard)
        {
            return s_Images.TryGetValue(assembly, out rawBytes!);
        }
    }
}