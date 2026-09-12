namespace Gneedle.Inject;

/// <summary>
/// Provider with System.Private.CoreLib importer.
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
        /// SPCL Assembly name.
        /// </summary>
        private const string SYSTEM_PRIVATE_CORE_LIB = "System.Private.CoreLib";

        /// <summary>
        /// Name of the standard which the corlib is imported through.
        /// </summary>
        private const string NETSTANDARD = "netstandard";

        /// <summary>
        /// Default name reference with netstandard2.0.
        /// </summary>
        private static readonly AssemblyNameReference s_AssemblyNameRef = AssemblyNameReference.Parse("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");

        /// <summary>
        /// The reference which the corlib of the weaving runtime is imported through.
        /// </summary>
        private readonly AssemblyNameReference m_Netstandard;

        /// <inheritdoc cref="Mono.Cecil.DefaultReflectionImporter(ModuleDefinition)"/>
        public SPCLReflectionImporter(ModuleDefinition module) : base(module)
        {
            // A module which names the standard already, which one compiled against the framework of a Unity player does,
            // is imported through the reference which it holds: the second reference of the same name would be written
            // beside the first one, and the assembly which holds both of them names an assembly twice, which nothing
            // tells how to resolve. The version is not compared, because the reference which is held is the one the
            // runtime of that framework resolves either way.
            m_Netstandard = module.AssemblyReferences.FirstOrDefault(reference => reference.Name == NETSTANDARD) ?? s_AssemblyNameRef;
            module.AssemblyReferences.TryAdd(m_Netstandard);
        }

        /// <inheritdoc cref="Mono.Cecil.DefaultReflectionImporter.ImportReference(System.Reflection.AssemblyName)"/>
        public override AssemblyNameReference ImportReference(System.Reflection.AssemblyName reference) => reference.Name == SYSTEM_PRIVATE_CORE_LIB
            ? m_Netstandard
            : base.ImportReference(reference);
    }
}