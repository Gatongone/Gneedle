namespace Gneedle.Inject;

partial class AssemblyHandler
{
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
    /// An injector which another assembly declares is one which this assembly does not hold a type of, while the
    /// attribute which it was read from is a part of this assembly and is taken off the member which carries it: those
    /// are named here rather than looked for among the types of the module.<para/>
    /// The reference is dropped only once nothing names the weaver, because an assembly which calls the weaver from its
    /// own code needs the reference, and an image which names a type of an assembly it does not refer to cannot be read.
    /// </remarks>
    /// <param name="attributesRead">Full names of the attribute types which the injectors were read from, as the
    /// metadata names them, or null when none was read.</param>
    /// <returns>Whether the assembly was changed.</returns>
    internal bool RemoveTheWeaver(IEnumerable<string>? attributesRead = null)
    {
        var module = Assembly.Source.MainModule;
        var attributes = FindInjectorAttributes(module);
        var read = attributesRead as IReadOnlyCollection<string> ?? attributesRead?.ToArray() ?? [];
        var changed = false;

        if (attributes.Count > 0 || read.Count > 0)
        {
            changed |= RemoveUses(module, attributes, read);

            // A type which declares an injector is named by the types which declare one as well, and which of them is
            // read first is the order of the metadata rather than anything about them: a type which derives from an
            // injector names it by its base type, and an attribute which implements one names it by its interface. The
            // ones which nothing names are taken out, which may leave the ones they named unnamed in their turn, and the
            // passes are repeated until they settle. What is still named by then is named by something which stays, and
            // that is the part which is kept and stripped.
            var pending = new List<TypeDefinition>(attributes.Values);
            for (var removed = true; removed;)
            {
                removed = false;
                for (var index = pending.Count - 1; index >= 0; index--)
                {
                    if (NamesTheType(module, pending[index])) continue;

                    Remove(module, pending[index]);
                    pending.RemoveAt(index);
                    removed = true;
                }
            }

            foreach (var type in pending) StripInjector(module, type);

            // A type which the module declares is one which is removed or stripped above, which is a change of the
            // assembly however the attributes were taken off it.
            changed |= attributes.Count > 0;
        }

        // The reading which tells whether the assembly still names the weaver and the writing which takes the reference
        // out are one step, which is what the lock of the module holds together, as it does for the reference which is
        // appended.
        lock (ModuleLock.Of(module))
        {
            var weaver = module.AssemblyReferences.FirstOrDefault(reference => reference.Name == typeof(IAssemblyInjector).Assembly.GetName().Name);
            if (weaver != null && !NamesTheAssembly(module, weaver))
            {
                module.AssemblyReferences.Remove(weaver);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// The types of the module which declare an injector, by full name.<para/>
    /// A type which implements one of the interfaces of the weaver is one, whichever way it reaches them: an injector
    /// which derives from another one and an interface which extends one of the interfaces are read as injectors of the
    /// kind they reach, and the types are asked of the whole of themselves rather than of what they declare directly, so
    /// that a type which is declared after the one which derives from it is found all the same.
    /// </summary>
    /// <param name="module">The module which is read.</param>
    private static Dictionary<string, TypeDefinition> FindInjectorAttributes(ModuleDefinition module)
    {
        var attributes = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);

        foreach (var type in InjectorInterfaces.AllTypes(module))
        {
            if (InjectorInterfaces.IsAnInjector(type, InjectorInterfaces.AllNames)) attributes[type.FullName] = type;
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
    /// <param name="read">Full names of the attribute types which the injectors were read from, which are the ones which
    /// were applied. An injector which another assembly declares is one which only this names.</param>
    /// <returns>Whether an attribute was removed.</returns>
    private static bool RemoveUses(ModuleDefinition module, IReadOnlyDictionary<string, TypeDefinition> attributes, IReadOnlyCollection<string> read)
    {
        var removed = false;
        Remove(module.Assembly.CustomAttributes);
        Remove(module.CustomAttributes);

        foreach (var type in InjectorInterfaces.AllTypes(module))
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

        return removed;

        // Take the attributes which the injectors were read from off a member, which are the ones which the member
        // carries. They are removed from the end, so that the ones which are left keep the order they were written in.
        void Remove(Mono.Collections.Generic.Collection<CustomAttribute> uses)
        {
            for (var index = uses.Count - 1; index >= 0; index--)
            {
                var name = uses[index].AttributeType.FullName;
                if (!attributes.ContainsKey(name) && !read.Contains(name)) continue;

                uses.RemoveAt(index);
                removed = true;
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
        // What the type is taken out of is a collection of the module, which is written under the lock of it.
        lock (ModuleLock.Of(module))
        {
            if (type.DeclaringType != null) type.DeclaringType.NestedTypes.Remove(type);
            else module.Types.Remove(type);
        }
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
            if (Array.IndexOf(InjectorInterfaces.AllNames, type.Interfaces[index].InterfaceType.FullName) >= 0)
            {
                ModuleLock.UndeclareMember(type.Module, type, type.Interfaces[index]);
            }
        }

        for (var index = type.Methods.Count - 1; index >= 0; index--)
        {
            var method = type.Methods[index];
            if (method.Name != INJECT_METHOD || method.Parameters.Count != 2) continue;
            if (NamesTheMethod(module, method)) continue;

            ModuleLock.UndeclareMember(type.Module, type, method);
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

        foreach (var type in InjectorInterfaces.AllTypes(module))
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

        // Whether a reference is the one which is looked for, or is made of it: a reference is asked about together
        // with the references it is made of, so that a type which is named by the declaring type or the arguments of
        // another one is found as well.
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
                GenericInstanceType instance                 => instance.GenericArguments.Any(Matches) || Matches(instance.ElementType),
                TypeSpecification specification              => Matches(specification.ElementType),
                MethodReference method                       => Matches(method.DeclaringType) || Matches(method.ReturnType) || method.Parameters.Any(parameter => Matches(parameter.ParameterType)),
                FieldReference field                         => Matches(field.DeclaringType) || Matches(field.FieldType),
                TypeReference {DeclaringType: not null} type => Matches(type.DeclaringType),
                _                                            => false
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
}