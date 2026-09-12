namespace Gneedle.Inject;

/// <summary>
/// Assembly which reading and writing from memories.
/// </summary>
internal sealed class MemoryAssembly : Assembly
{
    /// <summary>
    /// Default assembly version.
    /// </summary>
    private const string DEFAULT_ASSEMBLY_VERSION = "1.0.0";

    /// <param name="assemblyName">
    /// A text string specifying the assembly's name.
    /// </param>
    /// <param name="publicKey"></param>
    /// <param name="pubicKeyToken"></param>
    /// <param name="hash"></param>
    /// <param name="culture">
    /// Information on the culture or language the assembly supports.
    /// This information should be used only to designate an assembly as a satellite assembly containing culture- or language-specific information.
    /// (An assembly with culture information is automatically assumed to be a satellite assembly.)
    /// </param>
    /// <param name="moduleKind">.Net module kind.</param>
    internal MemoryAssembly(string assemblyName, byte[]? publicKey = null, byte[]? pubicKeyToken = null, byte[]? hash = null, string? culture = null, ModuleKind moduleKind = ModuleKind.Dll)
        : this(assemblyName, new Version(DEFAULT_ASSEMBLY_VERSION), publicKey, pubicKeyToken, hash, culture, moduleKind) { }

    /// <param name="assemblyName">
    /// A text string specifying the assembly's name.
    /// </param>
    /// <param name="version">
    /// A major and minor version number, and a revision and build number.
    /// The common language runtime uses these numbers to enforce version policy.</param>
    /// <param name="hash"></param>
    /// <param name="culture">
    /// Information on the culture or language the assembly supports.
    /// This information should be used only to designate an assembly as a satellite assembly containing culture- or language-specific information.
    /// (An assembly with culture information is automatically assumed to be a satellite assembly.)
    /// </param>
    /// <param name="moduleKind">.Net module kind.</param>
    /// <param name="publicKey"></param>
    /// <param name="publicKeyToken"></param>
    internal MemoryAssembly(string assemblyName, Version version, byte[]? publicKey = null, byte[]? publicKeyToken = null, byte[]? hash = null, string? culture = null, ModuleKind moduleKind = ModuleKind.Dll)
        : base(AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition(assemblyName, version)
            {
                PublicKey      = publicKey,
                PublicKeyToken = publicKeyToken,
                Hash           = hash,
                Culture        = culture
            },
            assemblyName,
            GetModuleParameters(moduleKind)
        )) { }

    /// <summary>
    /// Get module parameters with System.Private.Core library provider.
    /// </summary>
    /// <param name="moduleKind">Module kind.</param>
    /// <returns>Module parameters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Enum value out of range.</exception>
    private static ModuleParameters GetModuleParameters(ModuleKind moduleKind)
    {
        if (moduleKind is not (ModuleKind.Dll or ModuleKind.Console or ModuleKind.Windows or ModuleKind.NetModule))
        {
            throw new ArgumentOutOfRangeException(nameof(moduleKind), moduleKind, null);
        }

        return new ModuleParameters
        {
            Architecture               = SystemInfo.Architecture,
            Kind                       = moduleKind,
            ReflectionImporterProvider = SPCLReflectionImporterProvider.Instance,
            // The resolver holds the assemblies which are read into the module, so that one which only exists in memory
            // is resolvable. It is given here because the resolver of a module cannot be replaced once it was created.
            AssemblyResolver = new CachedAssemblyResolver(new DefaultAssemblyResolver())
        };
    }

    /// <inheritdoc/>
    public override System.Reflection.Assembly Load()
    {
        using var cache = new MemoryStream();
        SaveTo(cache);
        // .Net5.0 supports loading same assemblies with a different version.
#if NET5_0_OR_GREATER
        // Writing leaves the position where the writer last patched the image, while LoadFromStream reads from the
        // current position and fails on anything but the beginning.
        cache.Position = 0;
        return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(cache);
#else
        // Assembly.Load reads the whole buffer, which does not depend on the position.
        return System.Reflection.Assembly.Load(cache.GetBuffer());
#endif
    }
}