/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/13-01:50:55
 * Github: https://github.com/Gatongone
 */

namespace Gneedle.Inject;

/// <summary>
/// 
/// </summary>
public class MemoryAssembly : AssemblyWriter
{
    /// <summary>
    /// Default assembly version.
    /// </summary>
    private const string DEFAULT_ASSEMBLY_VERSION = "1.0.0";
    
    /// <summary>
    /// Assembly memories buffer.
    /// </summary>
    private byte[]? m_AssemblyCache;
    
    /// <summary>
    /// Default dll module parameters with System.Private.Core library provider.
    /// </summary>
    private static readonly ModuleParameters s_DefaultDllModuleParameters = new()
    {
        Architecture = SystemInfo.Architecture,
        Kind = ModuleKind.Dll,
        ReflectionImporterProvider = SPCLReflectionImporterProvider.Instance
    };
    
    /// <summary>
    /// Default console module parameters with System.Private.Core library provider.
    /// </summary>
    private static readonly ModuleParameters s_DefaultConsoleModuleParameters = new()
    {
        Architecture = SystemInfo.Architecture,
        Kind = ModuleKind.Console,
        ReflectionImporterProvider = SPCLReflectionImporterProvider.Instance
    };
    
    /// <summary>
    /// Default console module parameters with System.Private.Core library provider.
    /// </summary>
    private static readonly ModuleParameters s_DefaultNetModuleParameters = new()
    {
        Architecture = SystemInfo.Architecture,
        Kind = ModuleKind.NetModule,
        ReflectionImporterProvider = SPCLReflectionImporterProvider.Instance
    };
    
    /// <summary>
    /// Default console module parameters with System.Private.Core library provider.
    /// </summary>
    private static readonly ModuleParameters s_DefaultWindowsModuleParameters = new()
    {
        Architecture = SystemInfo.Architecture,
        Kind = ModuleKind.Windows,
        ReflectionImporterProvider = SPCLReflectionImporterProvider.Instance
    };
    
    /// <param name="assemblyName">Library name.</param>
    /// <param name="moduleKind">.Net module kind</param>
    public MemoryAssembly(string assemblyName, ModuleKind moduleKind = ModuleKind.Dll) : this(assemblyName, new Version(DEFAULT_ASSEMBLY_VERSION), moduleKind) { }
    
    /// <param name="assemblyName">Library name.</param>
    /// <param name="version">Library version.</param>
    /// <param name="moduleKind">.Net module kind.</param>
    public MemoryAssembly(string assemblyName, Version version, ModuleKind moduleKind = ModuleKind.Dll) : base(AssemblyDefinition.CreateAssembly(
        new AssemblyNameDefinition(assemblyName, version),
        assemblyName,
        GetModuleParameters(moduleKind)
    )) { }
    
    /// <summary>
    /// Get module parameters with System.Private.Core library provider.
    /// </summary>
    /// <param name="moduleKind">Module kind.</param>
    /// <returns>Module parameters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Enum value out of range.</exception>
    private static ModuleParameters GetModuleParameters(ModuleKind moduleKind) => moduleKind switch
    {
        ModuleKind.Dll       => s_DefaultDllModuleParameters,
        ModuleKind.Console   => s_DefaultConsoleModuleParameters,
        ModuleKind.Windows   => s_DefaultWindowsModuleParameters,
        ModuleKind.NetModule => s_DefaultNetModuleParameters,
        _                    => throw new ArgumentOutOfRangeException(nameof(moduleKind), moduleKind, null)
    };

    /// <summary>
    /// Save assembly to memories.
    /// </summary>
    protected override void OnDefaultSave() => SaveTo(out m_AssemblyCache);

    /// <summary>
    /// Convert to <see cref="Mono.Cecil.AssemblyDefinition"/> from memories.
    /// </summary>
    /// <returns></returns>
    public override System.Reflection.Assembly ToReflectionAssembly()
    {
        if (m_AssemblyCache == null || m_AssemblyCache.Length == 0)
        {
            throw new DirtyOperationException(ErrorResources.DIRTY_ASSEMBLY_OPERATION);
        }
        return System.Reflection.Assembly.Load(m_AssemblyCache);
    }
}