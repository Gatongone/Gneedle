namespace Gneedle.Inject;

/// <summary>
/// Resolver which finds the assemblies which were read into a module, and defers to another resolver for the rest.<para/>
/// The resolver of a module searches the file system alone, so an assembly which only exists in memory, or which was read
/// from a stream which is not backed by a file, would not be found by it. One which is loaded in the process is read back
/// here instead, from the file it was loaded from, or from the memory which the runtime mapped it to.
/// </summary>
/// <param name="fallback">Resolver which finds the assemblies which were not read into the module.</param>
internal sealed class CachedAssemblyResolver(IAssemblyResolver fallback) : IAssemblyResolver
{
#if NETFRAMEWORK
    /// <summary>
    /// Cache of the non public <c>Assembly.GetRawBytes</c> method, which hands the bytes of the image over. The other
    /// runtimes read the image which the runtime mapped instead, which needs no such method.
    /// </summary>
    private static System.Reflection.MethodInfo? s_GetRawBytes;
#endif

    /// <summary>
    /// Assemblies which were read into the module, keyed by the full name of each.
    /// </summary>
    internal Dictionary<string, AssemblyDefinition> Assemblies { get; } = new();

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
        using var memoryStream = new MemoryStream(rawBytes!);
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
    /// is read first there because it covers both.
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

            var eLfanew = System.Runtime.InteropServices.Marshal.ReadInt32(basePtr, 0x3C);
            var peHeader = eLfanew + 4; // skip the "PE\0\0" signature
            int numberOfSections = System.Runtime.InteropServices.Marshal.ReadInt16(basePtr, peHeader + 2);
            int sizeOfOptionalHeader = System.Runtime.InteropServices.Marshal.ReadInt16(basePtr, peHeader + 16);
            var sectionTable = peHeader + 20 + sizeOfOptionalHeader;

            // The on-disk size is the end of the last section's raw data.
            var fileSize = 0;
            for (var i = 0; i < numberOfSections; i++)
            {
                var section = sectionTable + i * 40;
                var sizeOfRawData = System.Runtime.InteropServices.Marshal.ReadInt32(basePtr, section + 16);
                var pointerToRawData = System.Runtime.InteropServices.Marshal.ReadInt32(basePtr, section + 20);
                fileSize = Math.Max(fileSize, pointerToRawData + sizeOfRawData);
            }

            if (fileSize <= 0)
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
}