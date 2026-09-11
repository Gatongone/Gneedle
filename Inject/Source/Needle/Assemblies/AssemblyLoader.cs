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
    /// Load the assembly of <paramref name="rawBytes"/> and remember those bytes.
    /// </summary>
    /// <param name="rawBytes">Bytes that is a COFF-based image containing a managed assembly.</param>
    /// <returns>The loaded assembly.</returns>
    public static System.Reflection.Assembly LoadFromBytes(byte[] rawBytes)
    {
        var assembly = System.Reflection.Assembly.Load(rawBytes);
        Remember(assembly, rawBytes);
        return assembly;
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