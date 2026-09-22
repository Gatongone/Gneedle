using System.Reflection;
using System.Runtime.CompilerServices;

namespace Gneedle.Inject;

/// <summary>
/// The lock which the tables of a module of Mono.Cecil are read and written under, and the writing of them.<para/>
/// What a handler caches is a dictionary of what it has already imported, and a lookup which finds an entry writes
/// nothing; the lookup which misses one is the one which imports, and an import appends a reference and the types which
/// it names to the tables of the module. Those tables are collections of Cecil which no thread may write while another
/// reads them, so the cache being one the reads and the writes of which belong to more than one thread - which is what
/// the handlers say of it - is not enough on its own: the writes of the module are held together by this lock, and a
/// cache which answered every lookup would say nothing about them.<para/>
/// Every write of those two tables - the references of a module and the types it declares - is made under the lock of
/// the module it writes. The imports among them are made through this class, so that the lock is named where the rule is
/// rather than at each of the places which import, and the writes which are not an import - the reference of the
/// standard which the importer of Cecil appends from inside an import, the references which an assembly is appended and
/// dropped by, and the types which a weave declares and takes away - take the same lock where they stand.
/// </summary>
/// <remarks>
/// The lock is of one module rather than of the whole library, so that the weavings of two assemblies, which are two
/// modules, still run beside each other. It is found through the module rather than held beside it, because the import
/// of a type is reached from the importer of Cecil as well, which holds the module alone.<para/>
/// The lock is taken again by an import which another import reached, which a lock of the runtime allows: what is held
/// is the module, and the same thread may hold it as often as it reaches another write of the same module.
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
    internal static object Of(ModuleDefinition module) => s_Locks.GetValue(module, static _ => new object());

    /// <summary>
    /// The reference which <paramref name="type"/> names, imported into <paramref name="module"/> under the lock of it.
    /// </summary>
    /// <param name="module">The module which the reference is imported into.</param>
    /// <param name="type">The type which is imported.</param>
    /// <returns>The reference of the type, which belongs to that module.</returns>
    internal static TypeReference Import(ModuleDefinition module, TypeReference type)
    {
        lock (Of(module)) return module.ImportReference(type);
    }

    /// <summary>
    /// The same, of a type of the runtime rather than of a reference of another module.
    /// </summary>
    /// <param name="module">The module which the reference is imported into.</param>
    /// <param name="type">The type which is imported.</param>
    /// <returns>The reference of the type, which belongs to that module.</returns>
    internal static TypeReference Import(ModuleDefinition module, Type type)
    {
        lock (Of(module)) return module.ImportReference(type);
    }

    /// <summary>
    /// The same, of a method.
    /// </summary>
    /// <param name="module">The module which the reference is imported into.</param>
    /// <param name="method">The method which is imported.</param>
    /// <returns>The reference of the method, which belongs to that module.</returns>
    internal static MethodReference Import(ModuleDefinition module, MethodReference method)
    {
        lock (Of(module)) return module.ImportReference(method);
    }

    /// <summary>
    /// The same, of a method of the runtime rather than of a reference of another module.
    /// </summary>
    /// <param name="module">The module which the reference is imported into.</param>
    /// <param name="method">The method which is imported.</param>
    /// <returns>The reference of the method, which belongs to that module.</returns>
    internal static MethodReference Import(ModuleDefinition module, MethodBase method)
    {
        lock (Of(module)) return module.ImportReference(method);
    }

    /// <summary>
    /// The same, of a field.
    /// </summary>
    /// <param name="module">The module which the reference is imported into.</param>
    /// <param name="field">The field which is imported.</param>
    /// <returns>The reference of the field, which belongs to that module.</returns>
    internal static FieldReference Import(ModuleDefinition module, FieldReference field)
    {
        lock (Of(module)) return module.ImportReference(field);
    }

    /// <summary>
    /// Append a type which the module declares to the types of that module, under the lock of it.
    /// </summary>
    /// <param name="module">The module which the type is appended to.</param>
    /// <param name="type">The type which is appended.</param>
    internal static void Declare(ModuleDefinition module, TypeDefinition type)
    {
        lock (Of(module)) module.Types.Add(type);
    }

    /// <summary>
    /// Append a type which the module declares to the type which declares it, under the lock of that module.<para/>
    /// A type which is declared by another one is a type of the module like any other, so the lock which holds the
    /// writes of the module's type table is the one which holds this one as well.
    /// </summary>
    /// <param name="module">The module which declares both types.</param>
    /// <param name="declaring">The type which the appended type is declared by.</param>
    /// <param name="type">The type which is appended.</param>
    internal static void DeclareNested(ModuleDefinition module, TypeDefinition declaring, TypeDefinition type)
    {
        lock (Of(module)) declaring.NestedTypes.Add(type);
    }

    /// <summary>
    /// Take a type which the module declares back off the type which declares it, under the lock of that module,
    /// which is the write which undoes <see cref="DeclareNested"/>.
    /// </summary>
    /// <param name="module">The module which declares both types.</param>
    /// <param name="declaring">The type which the removed type is declared by.</param>
    /// <param name="type">The type which is removed.</param>
    internal static void UndeclareNested(ModuleDefinition module, TypeDefinition declaring, TypeDefinition type)
    {
        lock (Of(module)) declaring.NestedTypes.Remove(type);
    }
}