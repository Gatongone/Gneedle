using System.Collections.Concurrent;

namespace Gneedle.Inject;

/// <summary>
/// Resolver which finds the assemblies which were read into a module, the ones which lie in the directory of the image
/// which refers to them, and defers to another resolver for the rest.<para/>
/// The resolver of a module searches the file system alone, so an assembly which only exists in memory, or which was read
/// from a stream which is not backed by a file, would not be found by it. One which is loaded in the process is read back
/// here instead, from the file it was loaded from, or from the memory which the runtime mapped it to.
/// </summary>
/// <param name="fallback">Resolver which finds the assemblies which were not read into the module.</param>
/// <param name="searchDirectory">Directory which the assemblies an image refers to lie in, or null when the caller knows
/// of none.</param>
internal sealed class CachedAssemblyResolver(IAssemblyResolver fallback, string? searchDirectory = null) : IAssemblyResolver
{
    /// <summary>
    /// The extensions which the file of an assembly which lies beside an image is named by, which are the ones a managed
    /// image is written to.
    /// </summary>
    private static readonly string[] s_AssemblyExtensions = {".dll", ".exe"};

#if NETFRAMEWORK
    /// <summary>
    /// Cache of the non public <c>Assembly.GetRawBytes</c> method, which hands the bytes of the image over. The other
    /// runtimes read the image which the runtime mapped instead, which needs no such method.
    /// </summary>
    private static System.Reflection.MethodInfo? s_GetRawBytes;
#endif

    /// <summary>
    /// Assemblies which were read into the module, keyed by the full name of each.<para/>
    /// The assemblies of one project are woven at the same time as each other, and the requests of the runtime for an
    /// assembly arrive on threads which are not the one which weaves: the cache is read and written on all of them.
    /// </summary>
    internal ConcurrentDictionary<string, AssemblyDefinition> Assemblies { get; } = new();

    /// <inheritdoc/>
    public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());

    /// <inheritdoc/>
    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        if (Assemblies.TryGetValue(name.FullName, out var assembly)) return assembly;

        // The version of a reference may differ from the version of the assembly which was read, and the full name holds
        // the version, so the simple name is the one which ties them together as well.
        var byName = Assemblies.Values.FirstOrDefault(assembly => assembly.Name.Name == name.Name);
        if (byName != null) return byName;

        // The directory of the image is read before the resolver of the process is asked, because that resolver knows
        // nothing of the folders of the project which was built: an assembly which a build copied beside the image is the
        // one the image refers to, whether the process holds one of that name or not.
        var beside = ReadBeside(name);
        if (beside != null) return Assemblies[name.FullName] = beside;

        try
        {
            return fallback.Resolve(name, parameters);
        }
        catch (AssemblyResolutionException)
        {
            // The assembly may be loaded in the process without a file which the resolver of the module could search: it
            // may have been loaded from bytes, or from a file in a directory which no search path of that resolver holds.
        }

        var definition = ReadLoadedAssembly(name);

        // The assembly is kept, so that it is resolved without reading its image again.
        Assemblies[name.FullName] = definition ?? throw new AssemblyResolutionException(name);
        return definition;
    }

    /// <inheritdoc/>
    public void Dispose() => fallback.Dispose();

    /// <summary>
    /// Read the assembly of <paramref name="name"/> which lies in the directory of the image which refers to it.
    /// </summary>
    /// <remarks>
    /// The file is read into bytes and the assembly is read out of those bytes rather than out of the file, so that the
    /// file system is left alone by a resolution which a build holds on to for as long as it runs: a build which wove an
    /// assembly holds every assembly which was read for it until the node which ran it ends, and a file which is held
    /// that long is a file which the build after it cannot write over.
    /// </remarks>
    /// <param name="name">Name of the assembly.</param>
    /// <returns>The assembly definition, or null when no file of that name is there, or it holds another assembly.</returns>
    private AssemblyDefinition? ReadBeside(AssemblyNameReference name)
    {
        if (string.IsNullOrEmpty(searchDirectory)) return null;

        foreach (var extension in s_AssemblyExtensions)
        {
            var path = Path.Combine(searchDirectory, name.Name + extension);
            if (!File.Exists(path)) continue;

            var definition = ReadFromBytes(File.ReadAllBytes(path));

            // A file of that name which holds another assembly is not the one which was named, and an image which is
            // answered with it would be answered with types of an assembly it does not refer to.
            if (definition.Name.Name == name.Name) return definition;
            definition.Dispose();
        }

        return null;
    }

    /// <summary>
    /// Read the assembly of <paramref name="name"/> which is loaded in the process from the bytes of its image.
    /// </summary>
    /// <remarks>
    /// The bytes are read from the file which the assembly was loaded from where it has one, and from the memory which
    /// the runtime mapped it to otherwise, which is the case for an assembly which was loaded from bytes.
    /// </remarks>
    /// <param name="name">Name of the assembly.</param>
    /// <returns>The assembly definition, or null when no assembly of that name is loaded, or its image can't be read.</returns>
    private AssemblyDefinition? ReadLoadedAssembly(AssemblyNameReference name)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == name.Name);
        if (assembly == null || !TryGetAssemblyRawBytes(assembly, out var rawBytes)) return null;

        // The bytes are read whenever it returns true, which the out parameter of a nullable type cannot tell.
        return ReadFromBytes(rawBytes!);
    }

    /// <summary>
    /// Read an assembly out of the bytes of its image.
    /// </summary>
    /// <remarks>
    /// The stream of the image is handed over to the assembly rather than closed with the read: the module is read
    /// deferred, so the body of a method is read out of that stream as it is asked for, and a stream which was closed
    /// with the read is a body which can no longer be read. A template which an injector names and which another
    /// assembly declares is read that way, which is why the two belong together.
    /// </remarks>
    /// <param name="bytes">The bytes of the image.</param>
    /// <returns>The assembly definition.</returns>
    private AssemblyDefinition ReadFromBytes(byte[] bytes)
    {
        // The stream holds the memory of the image alone, so nothing but the module which reads it keeps it, and no
        // handle of the file system is left open by a read which a build holds on to for as long as it runs.
        var memoryStream = new MemoryStream(bytes);
        return AssemblyDefinition.ReadAssembly(memoryStream, new ReaderParameters
        {
            InMemory         = true,
            ReadWrite        = false,
            ReadingMode      = ReadingMode.Deferred,
            AssemblyResolver = this
        });
    }

    /// <summary>
    /// Get the bytes of the image of <paramref name="assembly"/>, which no file necessarily backs.
    /// </summary>
    /// <param name="assembly">The assembly which need to get raw bytes.</param>
    /// <param name="rawBytes">Bytes that is a COFF-based image containing the assembly.</param>
    /// <returns>True when there is any way to get raw bytes.</returns>
    private static bool TryGetAssemblyRawBytes(System.Reflection.Assembly assembly, out byte[]? rawBytes)
    {
        // An assembly which AssemblyLoader loaded carries the bytes of its image, which is the only way to read them on a
        // runtime other than Windows. Those bytes are preferred over the file, because the file may have been replaced
        // since the assembly was loaded from it.
        if (AssemblyLoader.TryGetSource(assembly, out var source))
        {
            rawBytes = source;
            return true;
        }

        // File-backed assembly: the complete image is on disk and read directly.
        try
        {
            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                if (File.Exists(location))
                {
                    rawBytes = File.ReadAllBytes(location);
                    return true;
                }

                // Location is set but the file is gone. Such an assembly is mapped in virtual
                // layout, which the mapped-image path below cannot read back safely, so give up.
                rawBytes = null;
                return false;
            }
        }
        catch (NotSupportedException)
        {
            // Dynamic assembly: no file backing, fall through (rejected by IsDynamic below).
        }

        // An assembly which no file backs, just like one which was loaded from bytes,
        // is read from the memory which the runtime mapped it to.
        return TryReadMappedImage(assembly, out rawBytes);
    }

    /// <summary>
    /// Get the bytes of the image of <paramref name="assembly"/> from the memory which the runtime mapped it to.
    /// </summary>
    /// <remarks>
    /// An assembly which the runtime mapped from bytes lies in the memory in the layout of the file, so measuring the
    /// end of the last section through the section table of its header tells how much of that memory is the file. An
    /// assembly which the runtime mapped from a file of the file system lies in the virtual layout instead, which cannot
    /// be read back this way, so only the former is covered. .NET Framework hands the bytes over through a method, which
    /// is read first there because it covers both.<para/>
    /// The length which the section table names is a value of the image itself, and a malformed image names one which
    /// stands past the memory it was mapped into: what is measured is held to the size of that memory, which the system
    /// is asked for, so that the read of an image which names more than it holds is refused rather than made, since the
    /// fault of a read which leaves the memory which is mapped cannot be caught.
    /// </remarks>
    /// <param name="assembly">The assembly which need to get raw bytes.</param>
    /// <param name="rawBytes">Bytes that is a COFF-based image containing the assembly, or null.</param>
    /// <returns>True when the bytes of the image could be read.</returns>
    private static bool TryReadMappedImage(System.Reflection.Assembly assembly, out byte[]? rawBytes)
    {
#if NETFRAMEWORK
        // .NET Framework holds the bytes of the image itself, and hands them over through a non public method. The
        // parameter types are named because the name alone matches the overloads of the base types as well, which makes
        // the lookup ambiguous.
        s_GetRawBytes ??= assembly.GetType().GetMethod("GetRawBytes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (s_GetRawBytes?.Invoke(assembly, null) is byte[] {Length: > 0} bytes)
        {
            rawBytes = bytes;
            return true;
        }

        rawBytes = null;
        return false;
#else
        rawBytes = null;

        // Both of these are rejected before any pointer is touched, because reading one which is not an image faults the
        // process in a way which cannot be caught (an AccessViolation), so a bad pointer must never be read to tell.
        //   - A dynamic assembly has no backing image at all, and GetHINSTANCE hands over a non-zero but invalid pointer.
        //   - The memory of a module is handed over by the Windows layout of an image alone. What another runtime hands
        //     over there is not defined, so it is not read.
        if (assembly.IsDynamic || !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            var basePtr = System.Runtime.InteropServices.Marshal.GetHINSTANCE(assembly.ManifestModule);
            if (basePtr == IntPtr.Zero ||  basePtr == (IntPtr) (-1) || System.Runtime.InteropServices.Marshal.ReadInt16(basePtr, 0) != 0x5A4D) // "MZ"
            {
                return false;
            }

            // How much of the memory at the pointer is mapped is the bound which the length measured below is held to,
            // and the fields which name that length are read out of an array rather than out of the pointer: the header
            // of the image is read into this memory first, and measured there. What stands past the window which is read
            // in - a section table of an image which is not one - is refused rather than read, because a read which
            // stands past the memory which is mapped faults the process in a way which cannot be caught.
            var mapped = MappedSizeOf(basePtr);
            var headerLength = (int) Math.Min(mapped, HeaderWindow);
            if (headerLength < MinimumHeader)
            {
                return false;
            }

            var header = new byte[headerLength];
            System.Runtime.InteropServices.Marshal.Copy(basePtr, header, 0, headerLength);

            // The image takes the length which its own section table names, which a malformed image can name past the
            // memory it was mapped into: the copy below is the read which that would fault on.
            if (!TryMeasureImage(header, mapped, out var fileSize))
            {
                return false;
            }

            rawBytes = new byte[fileSize];
            System.Runtime.InteropServices.Marshal.Copy(basePtr, rawBytes, 0, fileSize);
            return true;
        }
        catch (Exception)
        {
            rawBytes = null;
            return false;
        }
#endif
    }

#if !NETFRAMEWORK
    /// <summary>
    /// The most bytes of the beginning of an image which are read into this memory to be measured, which holds the
    /// headers of every image and the section table of every one a compiler writes.
    /// </summary>
    private const int HeaderWindow = 0x4000;

    /// <summary>
    /// The fewest bytes of an image which can be measured: its DOS header, the signature of its PE header, its file
    /// header, and the smallest optional header which a portable image declares.
    /// </summary>
    private const int MinimumHeader = 0x40 + SignatureSize + FileHeaderSize + OptionalHeader32;

    /// <summary>
    /// The sizes of the parts of the header of an image which name where its sections are: the signature which tells a
    /// portable image from the VxD driver which shares the header, the file header whose fields hold the number of
    /// sections and the size of the optional header, the optional header of a 32-bit image and of a 64-bit one - which
    /// are the two sizes a portable image declares - and one entry of the section table.
    /// </summary>
    private const int SignatureSize = 4;
    private const int FileHeaderSize = 20;
    private const int OptionalHeader32 = 224;
    private const int OptionalHeader64 = 240;
    private const int SectionHeaderSize = 40;

    /// <summary>
    /// The most sections an image can declare, which is the number the loader of Windows accepts.
    /// </summary>
    private const int MaxSections = 96;

    /// <summary>
    /// How much of the memory at <paramref name="basePtr"/> is mapped, or zero when it cannot be asked about.
    /// </summary>
    /// <remarks>
    /// The view of an image lies in one region, so the size of the region which the base of an image stands in is the
    /// size of the whole of it, and it is a length which was measured by the runtime rather than read out of the image
    /// itself - which is what makes it a bound a malformed image cannot name.
    /// </remarks>
    /// <param name="basePtr">Base of the memory which the image was mapped into.</param>
    /// <returns>The number of bytes of it which are mapped.</returns>
    private static long MappedSizeOf(IntPtr basePtr)
    {
        var information = new MEMORY_BASIC_INFORMATION();
        var size = (IntPtr) System.Runtime.InteropServices.Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        return VirtualQuery(basePtr, ref information, size) == IntPtr.Zero ? 0 : information.RegionSize.ToInt64();
    }

    /// <summary>
    /// The length which the image whose beginning is <paramref name="header"/> takes on disk, which is where the raw
    /// data of its last section ends.<para/>
    /// The window which is handed in is all of the image which was read out of the memory it was mapped into, and every
    /// offset and length below is held to it: an image whose section table stands past it is refused rather than read
    /// further. What the length itself stands in is held by the caller, which is the one which knows how much memory
    /// the image was mapped into.
    /// </summary>
    /// <param name="header">The beginning of the image, which holds the headers naming its sections.</param>
    /// <param name="mapped">Number of bytes of the memory which the image was mapped into, which is the bound every
    /// section of it is held to.</param>
    /// <param name="fileSize">The length of the image on disk.</param>
    /// <returns>Whether the image could be measured, which is false when it is not a portable image at all, or when a
    /// section of it names raw data which the memory it was mapped into does not hold.</returns>
    internal static bool TryMeasureImage(byte[] header, long mapped, out int fileSize)
    {
        fileSize = 0;
        if (header.Length < MinimumHeader)
        {
            return false;
        }

        // The offset of the PE header, which is the field the DOS header of every image ends with.
        var eLfanew = BitConverter.ToInt32(header, 0x3C);
        if (!Fits(eLfanew, SignatureSize, header.Length) || BitConverter.ToInt32(header, eLfanew) != 0x00004550) // "PE\0\0"
        {
            return false;
        }

        var fileHeader = eLfanew + SignatureSize;
        if (!Fits(fileHeader, FileHeaderSize, header.Length))
        {
            return false;
        }

        int numberOfSections = BitConverter.ToUInt16(header, fileHeader + 2);
        int sizeOfOptionalHeader = BitConverter.ToUInt16(header, fileHeader + 16);
        if (numberOfSections == 0 || numberOfSections > MaxSections)
        {
            return false;
        }

        // The optional header stands between the file header and the section table, so a size of it which no portable
        // image declares is one which names a section table which is not where the table of this image is.
        if (sizeOfOptionalHeader != OptionalHeader32 && sizeOfOptionalHeader != OptionalHeader64)
        {
            return false;
        }

        var sectionTable = fileHeader + FileHeaderSize + sizeOfOptionalHeader;
        if (!Fits(sectionTable, (long) numberOfSections * SectionHeaderSize, header.Length))
        {
            return false;
        }

        // The raw data of a section stands past the window which was read in, so what is measured here is where each of
        // them ends rather than whether it was read: a section which names more than the memory the image was mapped
        // into holds is refused, because the copy of it is what would fault the process on it.
        var measured = 0L;
        for (var index = 0; index < numberOfSections; index++)
        {
            var section = sectionTable + index * SectionHeaderSize;
            int sizeOfRawData = BitConverter.ToInt32(header, section + 16);
            int pointerToRawData = BitConverter.ToInt32(header, section + 20);
            if (sizeOfRawData < 0 || pointerToRawData < 0)
            {
                return false;
            }

            var end = (long) pointerToRawData + sizeOfRawData;
            if (end > mapped)
            {
                return false;
            }

            measured = Math.Max(measured, end);
        }

        if (measured <= 0 || measured > int.MaxValue)
        {
            return false;
        }

        fileSize = (int) measured;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="length"/> bytes which stand at <paramref name="offset"/> lie within
    /// <paramref name="within"/> bytes.
    /// </summary>
    /// <param name="offset">Offset of the thing which is asked about.</param>
    /// <param name="length">Length of it.</param>
    /// <param name="within">Number of bytes it has to stand within.</param>
    /// <returns>Whether it stands within them, which a negative offset or length does not.</returns>
    private static bool Fits(long offset, long length, long within)
        => offset >= 0 && length >= 0 && offset <= within - length;

    /// <summary>
    /// The description which the system keeps of the memory at an address, of which the size of the region is read.
    /// </summary>
    private struct MEMORY_BASIC_INFORMATION
    {
        /// <summary>Base of the region.</summary>
        public IntPtr BaseAddress;

        /// <summary>Base of the memory which was reserved for it.</summary>
        public IntPtr AllocationBase;

        /// <summary>The protection which the whole of it was reserved with.</summary>
        public uint AllocationProtect;

        /// <summary>Number of bytes of the region which share the state and the protection.</summary>
        public IntPtr RegionSize;

        /// <summary>Whether it is committed, reserved or free.</summary>
        public uint State;

        /// <summary>The protection of the pages of it.</summary>
        public uint Protect;

        /// <summary>Whether it is private, mapped or an image.</summary>
        public uint Type;
    }

    /// <summary>
    /// Ask the system about the memory at an address, which is what says how much of it is mapped.
    /// </summary>
    /// <param name="address">Address which is asked about.</param>
    /// <param name="information">What was answered, which holds the size of the region.</param>
    /// <param name="length">Size of the answer which was handed over.</param>
    /// <returns>Number of bytes which were written to the answer, or zero when nothing was.</returns>
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualQuery(IntPtr address, ref MEMORY_BASIC_INFORMATION information, IntPtr length);
#endif
}