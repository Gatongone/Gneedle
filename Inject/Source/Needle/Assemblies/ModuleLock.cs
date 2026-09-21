using System.Runtime.CompilerServices;

namespace Gneedle.Inject;

/// <summary>
/// The lock which the tables of a module of Mono.Cecil are read and written under.<para/>
/// What a handler caches is a dictionary of what it has already imported, and a lookup which finds an entry writes
/// nothing; the lookup which misses one is the one which imports, and an import appends a reference and the types which
/// it names to the tables of the module. Those tables are collections of Cecil which no thread may write while another
/// reads them, so the cache being one the reads and the writes of which belong to more than one thread - which is what
/// the handlers say of it - is not enough on its own: the writes of the module are held together by this lock, and a
/// cache which answered every lookup would say nothing about them.
/// </summary>
/// <remarks>
/// The lock is of one module rather than of the whole library, so that the weavings of two assemblies, which are two
/// modules, still run beside each other. It is found through the module rather than held beside it, because the import
/// of a type is reached from the importer of Cecil as well, which holds the module alone.
/// </remarks>
internal static class ModuleLock
{
    /// <summary>
    /// The lock of each module, held weakly, so that a module which is unloaded takes the lock of it along.
    /// </summary>
    private static readonly ConditionalWeakTable<ModuleDefinition, object> s_Locks = new();

    /// <summary>
    /// The lock of <paramref name="module"/>, which is the one every writer of that module holds.
    /// </summary>
    /// <param name="module">The module whose tables are written.</param>
    /// <returns>The lock of it.</returns>
    internal static object Of(ModuleDefinition module) => s_Locks.GetValue(module, _ => new object());
}