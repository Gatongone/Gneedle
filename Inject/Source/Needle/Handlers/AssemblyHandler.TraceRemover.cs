namespace Gneedle.Inject;

partial class AssemblyHandler
{
    /// <summary>
    /// The interfaces which a type implements to be one of the attributes which an injector is read from.
    /// </summary>
    private static readonly string[] s_InjectorInterfaces =
    [
        typeof(IAssemblyInjector).FullName!, typeof(ITypeInjector).FullName!, typeof(IClassInjector).FullName!,
        typeof(IStructInjector).FullName!,   typeof(IEnumInjector).FullName!, typeof(IMethodInjector).FullName!,
        typeof(IFieldInjector).FullName!,    typeof(IPropertyInjector).FullName!
    ];

    /// <summary>
    /// The name of the method which every injector interface declares, which the type which implements one writes.
    /// </summary>
    private const string INJECT_METHOD = nameof(IMethodInjector.Inject);

    /// <summary>
    /// Remove the weaver from the assembly which it wove.<para/>
    /// An injector is read from an attribute which the project declares, and that attribute implements an interface of
    /// the weaver and reaches for the weaver as it runs, so the assembly names the weaver for as long as the attribute
    /// is there. The attributes are taken off the members which carry them, the types which declare them give up what
    /// makes them an injector, and the reference to the weaver is dropped once nothing of the assembly names it any
    /// more, so that the assembly which was woven stands alone.
    /// </summary>
    /// <remarks>
    /// A type which declares an injector is removed whole only where nothing of the assembly names it any more, because
    /// a type which a <c>typeof</c>, a call or a field still names cannot be taken away without taking those with it,
    /// and an image which names what it does not hold cannot be read. One which is still named keeps its place and gives
    /// up the interfaces it implements with the methods which implement them, which are the ones which reach for the
    /// weaver.<para/>
    /// The reference is dropped only once nothing names the weaver, because an assembly which calls the weaver from its
    /// own code needs the reference, and an image which names a type of an assembly it does not refer to cannot be read.
    /// </remarks>
    /// <returns>Whether the assembly was changed.</returns>
    internal bool RemoveTheWeaver()
    {
        var module = Assembly.Source.MainModule;
        var attributes = FindInjectorAttributes(module);
        var changed = false;

        if (attributes.Count > 0)
        {
            RemoveUses(module, attributes);
            foreach (var type in attributes.Values)
            {
                if (NamesTheType(module, type)) StripInjector(module, type);
                else Remove(module, type);
            }

            changed = true;
        }

        var weaver = module.AssemblyReferences.FirstOrDefault(reference => reference.Name == typeof(IAssemblyInjector).Assembly.GetName().Name);
        if (weaver != null && !NamesTheAssembly(module, weaver))
        {
            module.AssemblyReferences.Remove(weaver);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// The types of the module which declare an injector, by full name.
    /// </summary>
    /// <param name="module">The module which is read.</param>
    private static Dictionary<string, TypeDefinition> FindInjectorAttributes(ModuleDefinition module)
    {
        var attributes = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);

        foreach (var type in AllTypes(module))
        {
            if (type.Interfaces.Any(implementation => Array.IndexOf(s_InjectorInterfaces, implementation.InterfaceType.FullName) >= 0))
            {
                attributes[type.FullName] = type;
            }
        }

        // A type which inherits from a type which declares an injector is one as well, which is settled afterwards
        // because the base type may be declared after the type which inherits from it. The walk is repeated until it
        // settles, so that a chain of any length is followed.
        for (var found = true; found;)
        {
            found = false;
            foreach (var type in AllTypes(module))
            {
                if (!attributes.ContainsKey(type.FullName) && type.BaseType != null && attributes.ContainsKey(type.BaseType.FullName))
                {
                    attributes[type.FullName] = type;
                    found                   = true;
                }
            }
        }

        return attributes;
    }

    /// <summary>
    /// Remove the attributes which name an injector from every member of the module which carries one.<para/>
    /// The attribute is what the injector was read from, and it has done its work by the time this runs. An attribute
    /// which names a type which is not there any more cannot be left behind either, because the image would then name
    /// what it does not hold.
    /// </summary>
    /// <param name="module">The module which is read.</param>
    /// <param name="attributes">The types which declare an injector, by full name.</param>
    private static void RemoveUses(ModuleDefinition module, IReadOnlyDictionary<string, TypeDefinition> attributes)
    {
        Remove(module.Assembly.CustomAttributes);
        Remove(module.CustomAttributes);

        foreach (var type in AllTypes(module))
        {
            Remove(type.CustomAttributes);
            foreach (var genericParameter in type.GenericParameters) Remove(genericParameter.CustomAttributes);

            foreach (var field in type.Fields) Remove(field.CustomAttributes);
            foreach (var property in type.Properties) Remove(property.CustomAttributes);
            foreach (var @event in type.Events) Remove(@event.CustomAttributes);

            foreach (var method in type.Methods)
            {
                Remove(method.CustomAttributes);
                Remove(method.MethodReturnType.CustomAttributes);
                foreach (var parameter in method.Parameters) Remove(parameter.CustomAttributes);
                foreach (var genericParameter in method.GenericParameters) Remove(genericParameter.CustomAttributes);
            }
        }

        return;

        // The attributes are removed from the end, so that the ones which are left keep the order they were written in.
        void Remove(Mono.Collections.Generic.Collection<CustomAttribute> uses)
        {
            for (var index = uses.Count - 1; index >= 0; index--)
            {
                if (attributes.ContainsKey(uses[index].AttributeType.FullName)) uses.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// Remove a type from whatever holds it, which is the module for a type of the module and the type which declares
    /// it for a nested one.
    /// </summary>
    /// <param name="module">The module which holds the type.</param>
    /// <param name="type">The type which is removed.</param>
    private static void Remove(ModuleDefinition module, TypeDefinition type)
    {
        if (type.DeclaringType != null) type.DeclaringType.NestedTypes.Remove(type);
        else module.Types.Remove(type);
    }

    /// <summary>
    /// Take the input of the weaver away from a type which the assembly still names, so that the type keeps its place
    /// while it stops naming the weaver.
    /// </summary>
    /// <remarks>
    /// The interfaces go with the method which each of them declares, which is the one which reaches for the weaver. A
    /// method which the assembly calls is left where it is called, which leaves the reference to the weaver in place
    /// rather than an image which calls what it does not hold.
    /// </remarks>
    /// <param name="module">The module which is read.</param>
    /// <param name="type">The type which declares an injector and is kept.</param>
    private static void StripInjector(ModuleDefinition module, TypeDefinition type)
    {
        for (var index = type.Interfaces.Count - 1; index >= 0; index--)
        {
            if (Array.IndexOf(s_InjectorInterfaces, type.Interfaces[index].InterfaceType.FullName) >= 0) type.Interfaces.RemoveAt(index);
        }

        for (var index = type.Methods.Count - 1; index >= 0; index--)
        {
            var method = type.Methods[index];
            if (method.Name != INJECT_METHOD || method.Parameters.Count != 2) continue;
            if (NamesTheMethod(module, method)) continue;

            type.Methods.RemoveAt(index);
        }
    }

    /// <summary>
    /// Whether anything of the module names <paramref name="type"/> apart from the type itself.
    /// </summary>
    /// <param name="module">The module which is read.</param>
    /// <param name="type">The type which was read.</param>
    private static bool NamesTheType(ModuleDefinition module, TypeDefinition type)
        => AnyReference(module, type, reference => reference is TypeReference named && named.FullName == type.FullName);

    /// <summary>
    /// Whether anything of the module calls <paramref name="method"/> apart from the type which declares it.
    /// </summary>
    /// <param name="module">The module which is read.</param>
    /// <param name="method">The method which was read.</param>
    private static bool NamesTheMethod(ModuleDefinition module, MethodDefinition method)
        => AnyReference(module, method.DeclaringType, reference => reference is MethodReference named
                                                                && named.Name == method.Name
                                                                && named.DeclaringType.FullName == method.DeclaringType.FullName);

    /// <summary>
    /// Whether anything which the module holds names a type of <paramref name="assembly"/>, which is what an image
    /// needs the reference for.
    /// </summary>
    /// <param name="module">The module which is read.</param>
    /// <param name="assembly">The reference which the module may not need any more.</param>
    private static bool NamesTheAssembly(ModuleDefinition module, AssemblyNameReference assembly)
        => AnyReference(module, null, reference => reference is TypeReference named
                                                && named.Scope is AssemblyNameReference scope
                                                && scope.Name == assembly.Name);

    /// <summary>
    /// Whether any reference which the metadata of the module holds matches, apart from the ones which
    /// <paramref name="skipped"/> holds itself, which are the ones which are removed with it.<para/>
    /// The types which the module was read with are not the ones it holds, so the question is asked of the metadata
    /// which is there rather than of the tables of the image it was read from.
    /// </summary>
    /// <param name="module">The module which is read.</param>
    /// <param name="skipped">The type whose own references are not counted, or null when all of them are.</param>
    /// <param name="matches">Whether a reference is the one which is looked for.</param>
    private static bool AnyReference(ModuleDefinition module, TypeDefinition? skipped, Func<MemberReference, bool> matches)
    {
        var visited = new HashSet<MemberReference>();

        foreach (var type in AllTypes(module))
        {
            if (IsWithin(type, skipped)) continue;

            if (Matches(type.BaseType)) return true;
            if (type.Interfaces.Any(implementation => Matches(implementation.InterfaceType))) return true;
            if (type.CustomAttributes.Any(attribute => Matches(attribute.AttributeType))) return true;
            if (type.GenericParameters.Any(Matches)) return true;

            foreach (var field in type.Fields)
            {
                if (Matches(field.FieldType)) return true;
            }

            foreach (var property in type.Properties)
            {
                if (Matches(property.PropertyType)) return true;
            }

            foreach (var method in type.Methods)
            {
                if (Matches(method.ReturnType)) return true;
                if (method.Parameters.Any(parameter => Matches(parameter.ParameterType))) return true;
                if (method.GenericParameters.Any(Matches)) return true;
                if (!method.HasBody) continue;

                if (method.Body.Variables.Any(variable => Matches(variable.VariableType))) return true;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (Matches(instruction.Operand as MemberReference)) return true;
                }
            }
        }

        return false;

        // A reference is asked about together with the references it is made of, so that a type which is named by the
        // declaring type or the arguments of another one is found as well.
        bool Matches(MemberReference? reference)
        {
            // A reference which was walked already settles the question once, and the visited ones stop a reference
            // which holds itself from walking forever, which the generic parameter of a type can be.
            if (reference == null || !visited.Add(reference)) return false;
            if (matches(reference)) return true;

            return reference switch
            {
                // A generic instance is a specification which holds its arguments beside the type it is made of, so it
                // is asked about before the specifications, which hold the element type alone.
                GenericInstanceType instance    => instance.GenericArguments.Any(Matches) || Matches(instance.ElementType),
                TypeSpecification specification => Matches(specification.ElementType),
                MethodReference method          => Matches(method.DeclaringType) || Matches(method.ReturnType)
                                                                                || method.Parameters.Any(parameter => Matches(parameter.ParameterType)),
                FieldReference field            => Matches(field.DeclaringType) || Matches(field.FieldType),
                TypeReference { DeclaringType: not null } type => Matches(type.DeclaringType),
                _                               => false
            };
        }
    }

    /// <summary>
    /// Whether the type is <paramref name="outer"/> itself or one which it declares.
    /// </summary>
    /// <param name="type">The type which is read.</param>
    /// <param name="outer">The type which declares it, or null when none does.</param>
    private static bool IsWithin(TypeDefinition type, TypeDefinition? outer)
    {
        for (var current = type; current != null; current = current.DeclaringType)
        {
            if (current == outer) return true;
        }

        return false;
    }

    /// <summary>
    /// The types which the module declares, the nested ones included.
    /// </summary>
    /// <param name="module">The module which is read.</param>
    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var type in module.Types)
        {
            foreach (var declared in AllTypes(type)) yield return declared;
        }

        yield break;

        static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
        {
            yield return type;
            foreach (var nested in type.NestedTypes)
            {
                foreach (var declared in AllTypes(nested)) yield return declared;
            }
        }
    }
}
