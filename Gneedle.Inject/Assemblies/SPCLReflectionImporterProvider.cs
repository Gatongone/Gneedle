/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/13-02:05:00
 * Github: https://github.com/Gatongone
 */

namespace Gneedle.Inject;

/// <summary>
/// Provider with System.Private.CoreLib importer.
/// </summary>
internal class SPCLReflectionImporterProvider : IReflectionImporterProvider
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
    private class SPCLReflectionImporter : DefaultReflectionImporter
    {
        /// <summary>
        /// SPCL Assembly name.
        /// </summary>
        private const string SYSTEM_PRIVATE_CORE_LIB = "System.Private.CoreLib";
        
        /// <summary>
        /// Default name reference with netstandard2.0.
        /// </summary>
        private readonly AssemblyNameReference m_AssemblyNameRef = AssemblyNameReference.Parse("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
        
        /// <inheritdoc cref="Mono.Cecil.DefaultReflectionImporter(ModuleDefinition)"/>
        public SPCLReflectionImporter(ModuleDefinition module) : base(module)
        {
            if (!module.AssemblyReferences.Contains(m_AssemblyNameRef))
                module.AssemblyReferences.Add(m_AssemblyNameRef);
        }
        
        /// <inheritdoc cref="Mono.Cecil.DefaultReflectionImporter.ImportReference(System.Reflection.AssemblyName)"/>
        public override AssemblyNameReference ImportReference(System.Reflection.AssemblyName reference) 
            => reference.Name == SYSTEM_PRIVATE_CORE_LIB ? m_AssemblyNameRef : base.ImportReference(reference);
    }
}