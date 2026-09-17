namespace Gneedle.Inject;

/// <summary>
/// The interfaces which an attribute implements to be one of the injectors of an assembly, and the reading of a type for
/// them.<para/>
/// An attribute is one where it implements one of the interfaces, whichever way it reaches them: an attribute which
/// derives from another one, an interface which extends one of the interfaces, and a type which implements an interface
/// that extends one are all injectors, and a weaving which passed them over would leave the members they were put on
/// without the injection they asked for.
/// </summary>
/// <remarks>
/// The reading follows the base types and the interfaces of the types which the module declares, and stops where the
/// module ends, rather than resolving whatever a type refers to. What it is read for is the types which declare an
/// injector, which are the ones the weaving removes, or strips where the assembly still names them: an injector which
/// another assembly declares is not a type of this one, and the weaving leaves it where it is. The attribute which such
/// an injector was read from is a part of the assembly which carries it, which the weaving takes off the member which has
/// it by the name it read, so that nothing of an injector is left behind either way.
/// </remarks>
internal static class InjectorInterfaces
{
    /// <summary>
    /// The interface which an attribute implements to be asked to inject into the assembly, named as the metadata names
    /// it.
    /// </summary>
    internal static readonly string[] AssemblyInjectorNames = [typeof(IAssemblyInjector).FullName!];

    /// <summary>
    /// The interfaces which an attribute implements to be asked to inject into a type, named as the metadata names
    /// them.<para/>
    /// Every one of them is looked for, which the kinds are told apart by afterwards: looking for the first alone leaves
    /// the attributes of the other three on a type, where they are passed over without a word because nothing asked for
    /// them.
    /// </summary>
    internal static readonly string[] TypeInjectorNames = [typeof(ITypeInjector).FullName!, typeof(IClassInjector).FullName!, typeof(IStructInjector).FullName!, typeof(IEnumInjector).FullName!];

    /// <summary>
    /// The interface which an attribute implements to be asked to inject into a method, named as the metadata names it.
    /// </summary>
    internal static readonly string[] MethodInjectorNames = [typeof(IMethodInjector).FullName!];

    /// <summary>
    /// The interface which an attribute implements to be asked to inject into a field, named as the metadata names it.
    /// </summary>
    internal static readonly string[] FieldInjectorNames = [typeof(IFieldInjector).FullName!];

    /// <summary>
    /// The interface which an attribute implements to be asked to inject into a property, named as the metadata names
    /// it.
    /// </summary>
    internal static readonly string[] PropertyInjectorNames = [typeof(IPropertyInjector).FullName!];

    /// <summary>
    /// Every interface an injector implements, whichever member it injects into, which is what the types an injector is
    /// read from are read for and what the types of them are taken out by.
    /// </summary>
    internal static readonly string[] AllNames =
    [
        .. AssemblyInjectorNames, .. TypeInjectorNames, .. MethodInjectorNames, .. FieldInjectorNames, .. PropertyInjectorNames
    ];

    /// <summary>
    /// Whether a type is one of the attributes which an injector of the kinds which are named is read from: a type
    /// which implements one of the interfaces itself, or through a type which it derives from, or through an interface
    /// which extends one.
    /// </summary>
    /// <param name="type">The type which is asked about.</param>
    /// <param name="injectorInterfaces">Full names of the interfaces which an injector of the kinds is read from.</param>
    /// <returns>Whether the type is one which an injector of one of the kinds is read from.</returns>
    internal static bool IsAnInjector(TypeDefinition type, string[] injectorInterfaces)
    {
        var module = type.Module;
        // A type which refers to itself, which the metadata of a hand-written assembly can hold, would be walked
        // forever, and the answer for one which was walked already is the one which was written down for it.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current != null && visited.Add(current.FullName); current = Declared(module, current.BaseType))
        {
            if (current.Interfaces.Any(implementation => ImplementsAnInjector(module, implementation.InterfaceType, injectorInterfaces, visited))) return true;
        }

        return false;
    }

    /// <summary>
    /// The types which a module declares, the nested ones included.
    /// </summary>
    /// <param name="module">The module which is read.</param>
    /// <returns>The types of the module.</returns>
    internal static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var type in module.Types)
        {
            foreach (var declared in AllTypes(type)) yield return declared;
        }

        yield break;

        // The type and the types which it declares, the nested ones included, depth first.
        static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
        {
            yield return type;
            foreach (var nested in type.NestedTypes)
            {
                foreach (var declared in AllTypes(nested)) yield return declared;
            }
        }
    }

    /// <summary>
    /// Whether an interface is one which an injector implements, itself or through an interface which the module
    /// declares.
    /// </summary>
    /// <param name="module">The module which the type is read out of, or null when the type belongs to no module.</param>
    /// <param name="reference">The interface which is asked about.</param>
    /// <param name="injectorInterfaces">Full names of the interfaces which an injector of the kinds is read from.</param>
    /// <param name="visited">The types which were walked already, which a type that refers to itself would be walked
    /// forever without.</param>
    /// <returns>Whether the interface is one which an injector implements.</returns>
    private static bool ImplementsAnInjector(ModuleDefinition? module, TypeReference reference, string[] injectorInterfaces, HashSet<string> visited)
    {
        if (Array.IndexOf(injectorInterfaces, reference.FullName) >= 0) return true;
        if (!visited.Add(reference.FullName)) return false;

        return Declared(module, reference) is { } declared
               && declared.Interfaces.Any(implementation => ImplementsAnInjector(module, implementation.InterfaceType, injectorInterfaces, visited));
    }

    /// <summary>
    /// The type which a reference names, where the module which is read declares it.
    /// </summary>
    /// <remarks>
    /// A type of another assembly is not read, which is what keeps the reading to the types the module declares: a
    /// reference which names one is answered as a type which the module does not hold rather than looked up.
    /// </remarks>
    /// <param name="module">The module which is read, or null when there is none.</param>
    /// <param name="reference">The reference which is looked up, or null when there is none.</param>
    /// <returns>The type which the reference names, or null when the module does not declare it.</returns>
    private static TypeDefinition? Declared(ModuleDefinition? module, TypeReference? reference)
    {
        if (module == null || reference == null) return null;

        // The element type of a reference is what a type is declared by, so a base type which is written as an instance
        // of a generic one is looked up as the generic type itself.
        var element = reference.GetElementType();
        return ReferenceEquals(element.Scope, module) ? module.GetType(element.FullName) : null;
    }
}
