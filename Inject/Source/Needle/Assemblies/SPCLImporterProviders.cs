namespace Gneedle.Inject;

/// <summary>
/// The corlib of the runtime which the weaving runs on, whose types are imported through the standard of the assembly
/// which is woven rather than through that corlib.
/// </summary>
/// <remarks>
/// The runtime of whoever drives the weaving is not the framework of the assembly which is read: a build which runs on
/// .NET names System.Private.CoreLib for every type of the framework it writes, while the assembly which it writes is one
/// of the framework of the project, which is mscorlib or the standard. A loader which is handed both of them reads the
/// assembly as one which cannot be resolved - the editor of Unity refuses the whole of an assembly with "Unable to
/// resolve reference 'System.Private.CoreLib'" - so a type which was imported from the corlib of the runtime leaves the
/// assembly which holds it one which is not loaded at all.<para/>
/// The name of the corlib is not one which a produced assembly may hold, so every import of one is answered with the
/// standard instead, which every framework of a project resolves. Both the import of a runtime type and the import of a
/// reference of the metadata name a scope, so both of them are given an importer of their own.
/// </remarks>
internal static class SPCL
{
    /// <summary>
    /// The name of the corlib of the runtime which the weaving runs on.
    /// </summary>
    public const string CORLIB = "System.Private.CoreLib";

    /// <summary>
    /// The name of the standard which the types of that corlib are imported through.
    /// </summary>
    public const string NETSTANDARD = "netstandard";

    /// <summary>
    /// The reference which a module which names no standard at all is given, which is the one of the standard 2.0.
    /// </summary>
    private static readonly AssemblyNameReference s_Netstandard = AssemblyNameReference.Parse("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");

    /// <summary>
    /// The reference which the corlib of the runtime is imported through by <paramref name="module"/>, which is added to
    /// the module when it names none.
    /// </summary>
    /// <remarks>
    /// A module which names the standard already, which one compiled against the framework of a Unity player does, is
    /// imported through the reference which it holds: the second reference of the same name would be written beside the
    /// first one, and the assembly which holds both of them names an assembly twice, which nothing tells how to resolve.
    /// The version is not compared, because the reference which is held is the one the runtime of that framework
    /// resolves either way.
    /// </remarks>
    /// <param name="module">The module which the corlib of the runtime is imported into.</param>
    /// <returns>The reference of the standard.</returns>
    public static AssemblyNameReference NetstandardOf(ModuleDefinition module)
    {
        // This is reached from the importer of Cecil, in the middle of the import of a type, and what it writes is the
        // tables of the module that import writes: it holds the lock of that module, which the import which reached it
        // holds as well.
        lock (ModuleLock.Of(module))
        {
            var reference = module.AssemblyReferences.FirstOrDefault(candidate => candidate.Name == NETSTANDARD) ?? s_Netstandard;
            module.AssemblyReferences.TryAdd(reference);

            return reference;
        }
    }
}

/// <summary>
/// Provider with System.Private.CoreLib importer, which answers an import of a type of the runtime with the standard.
/// </summary>
internal sealed class SPCLReflectionImporterProvider : IReflectionImporterProvider
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static SPCLReflectionImporterProvider Instance => new();

    /// <inheritdoc cref="Mono.Cecil.IReflectionImporter"/>
    public IReflectionImporter GetReflectionImporter(ModuleDefinition module) => new SPCLReflectionImporter(module);

    /// <summary>
    /// System.Private.CoreLib importer with netstandard2.0.
    /// </summary>
    internal sealed class SPCLReflectionImporter : DefaultReflectionImporter
    {
        /// <summary>
        /// The reference which the corlib of the runtime is imported through.
        /// </summary>
        private readonly AssemblyNameReference m_Netstandard;

        /// <inheritdoc cref="Mono.Cecil.DefaultReflectionImporter(ModuleDefinition)"/>
        public SPCLReflectionImporter(ModuleDefinition module) : base(module) => m_Netstandard = SPCL.NetstandardOf(module);

        /// <inheritdoc cref="Mono.Cecil.DefaultReflectionImporter.ImportReference(System.Reflection.AssemblyName)"/>
        public override AssemblyNameReference ImportReference(System.Reflection.AssemblyName reference) => reference.Name == SPCL.CORLIB
            ? m_Netstandard
            : base.ImportReference(reference);
    }
}

/// <summary>
/// Provider with System.Private.CoreLib importer of the metadata, which answers an import of a reference of the runtime
/// with the standard.<para/>
/// A member which the weaving reads out of the runtime - the constructor of an attribute which it applies, a member which
/// a template names - is written into the assembly as a reference of the assembly which declares it, which is the corlib
/// of the runtime. The reference is imported here rather than by the importer of the runtime types, because a type which
/// was resolved out of another assembly names its members through that assembly rather than through the runtime.
/// </summary>
internal sealed class SPCLMetadataImporterProvider : IMetadataImporterProvider
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static SPCLMetadataImporterProvider Instance => new();

    /// <inheritdoc cref="Mono.Cecil.IMetadataImporter"/>
    public IMetadataImporter GetMetadataImporter(ModuleDefinition module) => new SPCLMetadataImporter(module);

    /// <summary>
    /// System.Private.CoreLib metadata importer with netstandard2.0.
    /// </summary>
    internal sealed class SPCLMetadataImporter : DefaultMetadataImporter
    {
        /// <summary>
        /// The reference which the corlib of the runtime is imported through.
        /// </summary>
        private readonly AssemblyNameReference m_Netstandard;

        /// <inheritdoc cref="Mono.Cecil.DefaultMetadataImporter(ModuleDefinition)"/>
        public SPCLMetadataImporter(ModuleDefinition module) : base(module) => m_Netstandard = SPCL.NetstandardOf(module);

        /// <inheritdoc cref="Mono.Cecil.DefaultMetadataImporter.ImportReference(AssemblyNameReference)"/>
        public override AssemblyNameReference ImportReference(AssemblyNameReference reference) => reference.Name == SPCL.CORLIB
            ? m_Netstandard
            : base.ImportReference(reference);
    }
}
