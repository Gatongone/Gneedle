namespace Gneedle.Inject;

/// <summary>
/// Whether a member is the one which a signature describes, and what the generic parameters of that member stand
/// for: a member which declares parameters of its own is one which no list of type names describes, so the
/// signature which names it is read as the positions those parameters stand at.
/// </summary>
internal static class SignatureMatching
{
    /// <summary>
    /// The kinds which the constraints of a type parameter name, which the runtime reads the instantiation of that
    /// parameter against.
    /// </summary>
    private const GenericParameterAttributes TheKindsWhichAConstraintNames
        = GenericParameterAttributes.ReferenceTypeConstraint | GenericParameterAttributes.NotNullableValueTypeConstraint;

    /// <param name="parameterDefs">Method parameter definitions.</param>
    extension(IList<ParameterDefinition> parameterDefs)
    {
        /// <summary>
        /// Check whether the parameters have same names with <c>targetTypes</c>.
        /// </summary>
        /// <param name="targetTypes"></param>
        internal bool SameWith(IReadOnlyList<TypeReference> targetTypes)
            => parameterDefs.Count == targetTypes.Count
                && !parameterDefs.Where((t, index) => !TypeName.HasSameName(t.ParameterType, targetTypes[index])).Any();

        /// <summary>
        /// Check whether the parameters have same names with <c>targetTypes</c>.
        /// </summary>
        /// <param name="targetTypes"></param>
        internal bool SameWith(IList<IType> targetTypes)
            => parameterDefs.Count == targetTypes.Count
                && !parameterDefs.Where((t, index) => !TypeName.HasSameName(t.ParameterType, targetTypes[index])).Any();

        /// <summary>
        /// Check whether the parameters have same names with <c>targetTypes</c>.
        /// </summary>
        /// <param name="targetTypes"></param>
        internal bool SameWith(IList<Type> targetTypes)
            => parameterDefs.Count == targetTypes.Count
                && !parameterDefs.Where((t, index) => !TypeName.HasSameName(t.ParameterType, targetTypes[index])).Any();
    }

    /// <param name="methodDef">Method definition.</param>
    extension(MethodDefinition methodDef)
    {
        /// <summary>
        /// Check whether the method is the one which the given signature describes, and read the arguments which the
        /// generic parameters it declares stand for out of that description.<para/>
        /// A method which declares a parameter of its own is one which no signature of named types describes, because
        /// the type of that parameter is the name of the method rather than the name of any type. The description is
        /// the signature which a caller hands the method, as the delegate of a template describes the member which it
        /// names, so such a parameter is bound to the type which stands where it stands rather than compared with it:
        /// the types of the arguments name the parameters which stand in the places of those arguments, and the type
        /// of the value which the method hands back names the ones which stand nowhere among them, which is the shape a
        /// member of the parameters of the caller has, just like <c>TOut Make&lt;TIn, TOut&gt;(TIn value)</c>.
        /// Every parameter of the method has to be described for the method to be named at all, because a call of a
        /// method which stands open is one which the runtime refuses to run.
        /// </summary>
        /// <param name="parameterTypes">The types of the arguments which the method is called with.</param>
        /// <param name="returnType">
        /// The type of the value which the method hands back, or null when the caller holds none - a caller which holds
        /// none describes the method with the type of nothing, just like the delegate of a member which hands nothing
        /// back. It names the parameters which stand in no position of the arguments, which is the only place left for
        /// one of them to be named at.
        /// </param>
        /// <param name="instance">
        /// The instantiation of the type which declares the method which the method is reached through, or null where it
        /// is reached through none: the signature of a member of a generic type is written where that type is declared,
        /// so the parameter which stands in it is the one the instantiation holds an argument for rather than a type
        /// which names an assembly. <c>int</c> is what the <c>T</c> of <c>GenericHelper&lt;int&gt;</c> is.
        /// </param>
        /// <param name="arguments">
        /// The types which the generic parameters of the method stand for, in the order they are declared, or null when
        /// the signature does not describe the method.
        /// </param>
        /// <returns>Whether the signature describes the method.</returns>
        internal bool SameWith(IReadOnlyList<TypeReference> parameterTypes, TypeReference? returnType, out IReadOnlyList<TypeReference>? arguments, TypeReference? instance = null)
        {
            var declaration = methodDef.DeclaringType;

            arguments = null;

            // A method which hands nothing back hands back no value, so a caller which describes one with the type of
            // nothing describes no value at all: a delegate which stands for a member that hands nothing back, just like
            // `Action<T>`, hands back `System.Void`, and a parameter of the member which that type named is one the call
            // would leave standing open - and the type of nothing is no argument of an instantiation at all, so the
            // assembly which named it is one the runtime refuses to load. The type of nothing describes nothing,
            // wherever the caller wrote it down.
            if (returnType is { MetadataType: MetadataType.Void })
            {
                returnType = null;
            }

            // A method which declares no parameter of its own names every type of its signature, so it is described by
            // the signature which holds those very names, and the call names the method itself.
            if (methodDef.GenericParameters.Count == 0)
            {
                return methodDef.DescribedBy(parameterTypes, instance);
            }

            if (methodDef.Parameters.Count != parameterTypes.Count)
            {
                return false;
            }

            var bound = new TypeReference?[methodDef.GenericParameters.Count];
            for (var index = 0; index < methodDef.Parameters.Count; index++)
            {
                if (!Bind(methodDef, methodDef.Parameters[index].ParameterType.WithTheArgumentsOf(declaration, instance), parameterTypes[index], bound))
                {
                    return false;
                }
            }

            // The type which the method hands back names a parameter of the method as well, because it is what tells one
            // instantiation of a method which declares parameters of its own from another: a parameter which no argument
            // of the call stands in the place of is one which the signature names by the value the method hands back,
            // where the caller wrote that value down, and the call of such a member is written with both arguments.
            if (returnType != null && !Bind(methodDef, methodDef.ReturnType.WithTheArgumentsOf(declaration, instance), returnType, bound))
            {
                return false;
            }

            // A parameter which no position of the signature binds is one which a call cannot name the argument of.
            var described = new TypeReference[bound.Length];
            for (var index = 0; index < bound.Length; index++)
            {
                if (bound[index] is not { } argument)
                {
                    return false;
                }

                described[index] = argument;
            }

            arguments = described;
            return true;
        }
    }

    extension(MethodDefinition methodDef)
    {
        /// <summary>
        /// Check whether the parameters of the method, read through the instance which it was reached through, are the
        /// types which the given signature names.<para/>
        /// A method which declares no parameter of its own names every type of its signature, and the signature of a
        /// member of a generic type is written where that type is declared: the parameter which stands in it is the one
        /// the instantiation of the type holds an argument for, so <c>int</c> is what the <c>T</c> of the parameter of
        /// <c>Echo</c> on <c>GenericHelper&lt;int&gt;</c> is.
        /// </summary>
        /// <param name="parameterTypes">The types of the arguments which the method is called with.</param>
        /// <param name="instance">The instantiation of the type which declares the method, or null where it was reached through none.</param>
        /// <returns>Whether the parameters describe the method.</returns>
        internal bool DescribedBy(IReadOnlyList<TypeReference> parameterTypes, TypeReference? instance)
            => methodDef.Parameters.Count == parameterTypes.Count
                && !methodDef.Parameters.Where((parameter, index) =>
                       !TypeName.HasSameName(parameter.ParameterType.WithTheArgumentsOf(methodDef.DeclaringType, instance),
                                             parameterTypes[index])).Any();
    }

    /// <summary>
    /// Bind the parameters which <paramref name="methodDef"/> declares in <paramref name="described"/> to the types
    /// which stand at the same positions of <paramref name="describing"/>, and tell whether the two describe the same
    /// type.
    /// </summary>
    /// <param name="methodDef">The method whose parameters are bound.</param>
    /// <param name="described">The type which holds the parameters, which is the one the method declares.</param>
    /// <param name="describing">The type which describes it, which is the one the caller wrote.</param>
    /// <param name="bound">The arguments which the parameters stand for so far, by position.</param>
    /// <returns>Whether <paramref name="describing"/> describes <paramref name="described"/>.</returns>
    private static bool Bind(MethodDefinition methodDef, TypeReference described, TypeReference describing, TypeReference?[] bound)
    {
        // A parameter of the method itself stands for whatever the signature holds in its place, and a parameter which
        // two positions bind is one which only the same type describes both times: a position which is reached again
        // tells whether it stands for the type which stands there, and a parameter which stands unbound is one which
        // this position names.
        if (described is GenericParameter parameter && ReferenceEquals(parameter.Owner, methodDef))
        {
            // A parameter which the type it is instantiated with does not fit is one the runtime refuses to call: the
            // instantiation which the signature names is the one which is written into the woven assembly, so a
            // signature which names a type the constraints of the parameter reject describes no member rather than one
            // which cannot be called.
            if (!Fits(parameter, describing))
            {
                return false;
            }

            if (bound[parameter.Position] is { } boundArgument)
            {
                return TypeName.HasSameName(boundArgument, describing);
            }

            bound[parameter.Position] = describing;
            return true;
        }

        // A generic instance is described by an instance of the same type, and the arguments of it describe the
        // parameters which stand in the arguments of the definition: List<int> is what List<T> is described by.
        if (described is GenericInstanceType describedInstance && describing is GenericInstanceType describingInstance)
        {
            if (!TypeName.HasSameName(describedInstance.ElementType, describingInstance.ElementType)
                || describedInstance.GenericArguments.Count != describingInstance.GenericArguments.Count)
            {
                return false;
            }

            for (var index = 0; index < describedInstance.GenericArguments.Count; index++)
            {
                if (!Bind(methodDef, describedInstance.GenericArguments[index], describingInstance.GenericArguments[index], bound))
                {
                    return false;
                }
            }

            return true;
        }

        // A type which wraps another is described by one of the same kind, and the element is what describes the
        // parameter nested in it: int[] is what T[] is described by, and int[,] is not, because the rank belongs to
        // the array rather than to the element which stands in it.
        if (described is TypeSpecification describedSpecification && describing is TypeSpecification describingSpecification
            && describedSpecification.GetType() == describingSpecification.GetType())
        {
            // The two elements are one another's description whatever the rank of the arrays which hold them, so the
            // rank is read here: a signature which describes an element of an array of one rank describes the element
            // of the member which stands in an array of any rank as well, where the signature is a type of its own.
            if (describedSpecification is ArrayType describedArray && describingSpecification is ArrayType describingArray
                && describedArray.Rank != describingArray.Rank)
            {
                return false;
            }

            return Bind(methodDef, describedSpecification.ElementType, describingSpecification.ElementType, bound);
        }

        return TypeName.HasSameName(described, describing);
    }

    /// <summary>
    /// Whether the type which a parameter of a method is instantiated with fits the constraints which the parameter
    /// itself declares.<para/>
    /// A parameter which declares that its type is a reference, or that it is a value which is not nullable, is one
    /// which the runtime accepts the types of that kind alone for: the instantiation which the signature names is the
    /// one which the woven assembly calls, so a type of the other kind is one the member is not called with, and a
    /// signature which names it describes no member rather than one which cannot run.<para/>
    /// A constraint which names a type is satisfied by the types which are made of that type, which the walk of the base
    /// types and the interfaces of the argument tells, and everything that walk does not tell is left to the runtime.
    /// </summary>
    /// <param name="parameter">The parameter which the method declares.</param>
    /// <param name="argument">The type which the parameter is instantiated with.</param>
    /// <returns>Whether the argument is one which the parameter accepts.</returns>
    private static bool Fits(GenericParameter parameter, TypeReference argument)
        => FitsTheKindsOf(parameter, argument) && SatisfiesTheConstraintsOf(parameter, argument);

    /// <summary>
    /// Whether the type which a parameter of a method is instantiated with is of a kind which the parameter itself
    /// declares, which is the kind the constraints of it name rather than the types which they name.
    /// </summary>
    /// <param name="parameter">The parameter which the method declares.</param>
    /// <param name="argument">The type which the parameter is instantiated with.</param>
    /// <returns>Whether the argument is of a kind which the parameter accepts.</returns>
    private static bool FitsTheKindsOf(GenericParameter parameter, TypeReference argument)
    {
        var kind = parameter.Attributes & TheKindsWhichAConstraintNames;
        if (kind == 0) return true;

        // Both kinds at once is a constraint which no type fits, and the runtime accepts no instantiation of it.
        if (kind == TheKindsWhichAConstraintNames) return false;

        // A type which this read does not tell the kind of is left to the runtime, because the refusal here is a member
        // which no signature describes: an instantiation which the runtime refuses is one which is not called, and one
        // which it accepts is one which this read would otherwise have refused.
        if (IsAValueType(argument) is not { } isValueType) return true;

        // A value is no reference, and a reference is no value: a value which may be absent is one the constraint that
        // names the value types refuses as well, which the kind of the type alone does not tell it, because it is a
        // value type: the type which pairs a value with the absence of it is read here.
        return kind == GenericParameterAttributes.ReferenceTypeConstraint
            ? !isValueType
            : isValueType && !IsANullableValueType(argument);
    }

    /// <summary>
    /// Whether the type which a parameter of a method is instantiated with satisfies the types which the constraints of
    /// that parameter name.<para/>
    /// The constraint of a parameter is satisfied by a type which is the type it names, and by one which derives from
    /// that type or which implements it, so the walk is of everything the type is made of: the type itself, the base
    /// type which each type of the walk is declared with, and the interfaces which each of them declares.<para/>
    /// Everything which the walk does not tell - a type which stands for another, the assembly of a type which is not
    /// there to be read - is left to the runtime rather than refused, because the refusal here is a member which no
    /// signature describes and the runtime is what refuses an instantiation which it does not accept.
    /// </summary>
    /// <param name="parameter">The parameter which the method declares.</param>
    /// <param name="argument">The type which the parameter is instantiated with.</param>
    /// <returns>Whether the argument satisfies every constraint which names a type.</returns>
    private static bool SatisfiesTheConstraintsOf(GenericParameter parameter, TypeReference argument)
    {
        foreach (var constraint in parameter.Constraints)
        {
            if (!SatisfiesTheConstraint(constraint.ConstraintType, argument)) return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the type which a constraint names is one of the types which the argument is made of.
    /// </summary>
    /// <param name="constraint">The type which the constraint of the parameter names.</param>
    /// <param name="argument">The type which the parameter is instantiated with.</param>
    /// <returns>Whether the constraint is satisfied, or true where this read does not tell.</returns>
    private static bool SatisfiesTheConstraint(TypeReference constraint, TypeReference argument)
        => IsMadeOf(argument, constraint) != false;

    /// <summary>
    /// Whether the types which a type is made of hold the type which is wanted, which is what tells that the type is one
    /// which the wanted type accepts.<para/>
    /// Everything which this read does not tell - a type which stands for another, the assembly of a type which is not
    /// there to be read - is answered with null rather than with a refusal, because the refusal here is a member which no
    /// signature describes and the runtime is what refuses an instantiation which it does not accept.
    /// </summary>
    /// <param name="argument">The type which the parameter is instantiated with.</param>
    /// <param name="wanted">The type which the constraint names.</param>
    /// <returns>True where the types hold the wanted type, false where they are read and hold it not, and null where this read does not tell.</returns>
    private static bool? IsMadeOf(TypeReference argument, TypeReference wanted)
    {
        // A type which stands for another type anywhere in it is one whose types this read does not tell, which is so of
        // the types the wanted one is written with as well.
        if (HoldsAParameter(wanted)) return null;

        var (made, told) = TypesWhichTheTypeIsMadeOf(argument);

        foreach (var type in made)
        {
            switch (NamesTheTypeOfTheConstraint(type, wanted))
            {
                case true: return true;
                case null: told = false; break;
            }
        }

        return told ? false : null;
    }

    /// <summary>
    /// Whether the type is the one which a constraint names, which is the name of the type itself and - where the
    /// constraint names an instance of a generic type - the arguments of that instance read with the variance which the
    /// declaration of them names: a type is accepted for a parameter which the declaration marks covariant where it is
    /// accepted for the argument which the constraint names, and for one which it marks contravariant where that
    /// argument is accepted for it, while an argument of a parameter which is marked neither way is read by its name.
    /// </summary>
    /// <param name="type">The type which the walk holds.</param>
    /// <param name="wanted">The type which the constraint names.</param>
    /// <returns>True where the type is the one which the constraint names, false where it is read and is not, and null where this read does not tell.</returns>
    private static bool? NamesTheTypeOfTheConstraint(TypeReference type, TypeReference wanted)
    {
        if (TypeName.HasSameName(type, wanted)) return true;

        // An array is accepted for an array which holds the values of the same number of dimensions where the element of
        // it is converted to the element of that one by a reference conversion, which is the covariance of the arrays,
        // and where the two elements are the ones which the runtime reads an array of either for.
        if (wanted is ArrayType wantedArray && type is ArrayType givenArray)
        {
            if (givenArray.Rank != wantedArray.Rank) return false;

            return IsConvertedOrCompatible(givenArray.ElementType, wantedArray.ElementType);
        }

        // The collections and the sequences which name the element are given to an array of one dimension for every
        // type which the element is converted to by a reference conversion, so the argument which one of them names is
        // read as the element of the array rather than as a type which the array is made of: an array of strings is a
        // collection of the values of the framework, and an array of arrays of strings is a collection of arrays of
        // them, although neither is made of those types.
        if (type is ArrayType vector && vector.Rank == 1 && wanted is GenericInstanceType named
            && s_TheCollectionsWhichNameTheElement.Any(declaration => TypeName.HasSameName(named.ElementType, declaration)))
        {
            return IsConvertedOrCompatible(vector.ElementType, named.GenericArguments[0]);
        }

        // A constraint which names no instance of a generic type names the type itself, which the name of the type
        // tells, and a type which is not an instance of one is named by no argument at all.
        if (wanted is not GenericInstanceType constraint) return false;
        if (type is not GenericInstanceType instance) return false;

        if (!TypeName.HasSameName(instance.ElementType, constraint.ElementType)
            || instance.GenericArguments.Count != constraint.GenericArguments.Count)
        {
            return false;
        }

        var declaration = DefinitionOf(constraint);
        if (declaration == null) return null;

        for (var index = 0; index < instance.GenericArguments.Count; index++)
        {
            var variance = declaration.GenericParameters[index].Attributes;
            var given = instance.GenericArguments[index];
            var asked = constraint.GenericArguments[index];

            bool? accepted;
            if ((variance & GenericParameterAttributes.Covariant) != 0)
            {
                accepted = IsConvertedByReference(given, asked);
            }
            else if ((variance & GenericParameterAttributes.Contravariant) != 0)
            {
                accepted = IsConvertedByReference(asked, given);
            }
            else
            {
                // An argument which the declaration marks neither way is read by its name alone, and one which stands
                // for another type is named by nothing.
                accepted = HoldsAParameter(given) || HoldsAParameter(asked)
                    ? null
                    : TypeName.HasSameName(given, asked);
            }

            if (accepted != true) return accepted;
        }

        return true;
    }

    /// <summary>
    /// Whether the runtime reads an array of either of the two types as an array of the other where the two stand as the
    /// elements of arrays, which is a relation which no reference conversion tells: the values of the integer family of
    /// one width are read as one another whatever the sign of each of them is, and an enumeration is read as the type
    /// which stands under it.
    /// </summary>
    /// <param name="left">The type which an array holds.</param>
    /// <param name="right">The type which another array holds.</param>
    /// <returns>Whether the two arrays are related by the elements which they hold.</returns>
    private static bool AreCompatibleAsElementsOfAnArray(TypeReference left, TypeReference right)
        => WidthOfAnElement(left) is { } width && width == WidthOfAnElement(right);

    /// <summary>
    /// The width which the runtime reads the values of a type as where it relates two arrays by the elements which they
    /// hold, or null where the type is one which it relates to no other: the width of an enumeration is the width of the
    /// type which stands under it.
    /// </summary>
    /// <param name="type">The type which an array holds.</param>
    /// <returns>The width of the values of the type, or null where no other type of that width is related to it.</returns>
    private static string? WidthOfAnElement(TypeReference type)
        => ElementTypeWhichTheValuesAreReadAs(type)?.MetadataType switch
        {
            MetadataType.SByte or MetadataType.Byte => "1",
            MetadataType.Int16 or MetadataType.UInt16 => "2",
            MetadataType.Int32 or MetadataType.UInt32 => "4",
            MetadataType.Int64 or MetadataType.UInt64 => "8",
            // The native int and the native unsigned int are related to one another alone, whatever the width the
            // runtime holds them at: neither is related to the value of the width which that runtime has.
            MetadataType.IntPtr or MetadataType.UIntPtr => "n",
            _ => null
        };

    /// <summary>
    /// The type which the values of a type are read as where the runtime relates two arrays by the elements which they
    /// hold, which is the type under an enumeration and the type itself for every other type.
    /// </summary>
    /// <param name="type">The type which an array holds.</param>
    /// <returns>The type which the values are read as, or null where the assembly of the type is not there to be read.</returns>
    private static TypeReference? ElementTypeWhichTheValuesAreReadAs(TypeReference type)
    {
        // The definition which an array and the other wrappers resolve to is the definition of the element which they
        // hold, and none of them is an enumeration or any other of the types which have a width: the type itself is
        // what is read of them.
        if (type is TypeSpecification and not GenericInstanceType) return type;

        var definition = DefinitionOf(type);

        if (definition is not { IsEnum: true }) return definition is null ? null : type;

        return definition.Fields.FirstOrDefault(field => field.Name == "value__")?.FieldType;
    }

    /// <summary>
    /// Whether the runtime reads a value of one type as a value of the other where the two stand as the elements of
    /// arrays: a reference conversion is one which the variance of a parameter is read with as well, so it is read
    /// first, and the widths of the two are what the elements of arrays are related by.
    /// </summary>
    /// <param name="type">The type which is converted.</param>
    /// <param name="wanted">The type which it is converted to.</param>
    /// <returns>Whether the two are read as one another, or null where this read does not tell.</returns>
    private static bool? IsConvertedOrCompatible(TypeReference type, TypeReference wanted)
    {
        var converted = IsConvertedByReference(type, wanted);

        return converted == true || AreCompatibleAsElementsOfAnArray(type, wanted) ? true : converted;
    }

    /// <summary>
    /// Whether a type is converted to another one by a reference conversion, which is the conversion which the variance
    /// of a parameter of a declaration is read with: a value which is boxed is no reference of the type which the
    /// argument of an instance names, so a value type is accepted for the very type it is alone, while a reference is
    /// accepted for every type which the types it is made of hold.
    /// </summary>
    /// <param name="type">The type which is converted.</param>
    /// <param name="wanted">The type which it is converted to.</param>
    /// <returns>Whether the conversion is one of references, or null where this read does not tell.</returns>
    private static bool? IsConvertedByReference(TypeReference type, TypeReference wanted)
    {
        if (TypeName.HasSameName(type, wanted)) return true;

        return IsAValueType(type) is { } isAValue
            ? isAValue ? false : IsMadeOf(type, wanted)
            : null;
    }

    /// <summary>
    /// The types which a type is made of: the type itself, the base type which each type of the walk is declared with,
    /// and the interfaces which each of them declares, with the arguments of the instantiation which a type of the walk
    /// was reached through put in the place of the parameters of the declaration which holds it.<para/>
    /// The metadata of an array declares it no base type and no interface, so an array is made of the types which the
    /// runtime gives it; a type which stands for another and a wrapper which names no declaration of its own are told of
    /// nothing at all, which is what the second of the answers tells.
    /// </summary>
    /// <param name="type">The type which is read.</param>
    /// <returns>The types which the type is made of, and whether every one of them was read.</returns>
    private static (IReadOnlyList<TypeReference> Made, bool Told) TypesWhichTheTypeIsMadeOf(TypeReference type)
    {
        var told = true;
        var made = new List<TypeReference>();
        var walked = new HashSet<string>();
        var pending = new Stack<TypeReference>();

        if (type is ArrayType array)
        {
            foreach (var given in TypesWhichAnArrayIsGiven(array)) pending.Push(given);
        }
        else
        {
            pending.Push(type);
        }

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (HoldsAParameter(current) || IsAWrappedType(current))
            {
                told = false;
                continue;
            }

            if (!walked.Add(current.FullName)) continue;
            made.Add(current);

            // The definition which an array resolves to is the definition of the element which it holds, so an array is
            // not read through it: what an array is made of is the types which the runtime gives it, which the walk
            // holds already, and the array itself, which the walk of the constraint reads the covariance of the arrays
            // with.
            if (current is ArrayType) continue;

            var definition = DefinitionOf(current);
            if (definition == null)
            {
                told = false;
                continue;
            }

            foreach (var implemented in definition.Interfaces)
            {
                pending.Push(implemented.InterfaceType.WithTheArgumentsOf(definition, current));
            }

            if (definition.BaseType is { } baseType)
            {
                pending.Push(baseType.WithTheArgumentsOf(definition, current));
            }
            else if (definition.IsInterface)
            {
                // An interface is declared with no base type, and every interface is a reference of the type of every
                // value, so the walk of one reaches that type as the walk of a class reaches it through its base types.
                pending.Push(definition.Module.TypeSystem.Object);
            }
        }

        return (made, told);
    }

    /// <summary>
    /// Whether the type is one of the wrappers which this read does not walk through: a pointer, an address, a pinned
    /// and a sentinel type, and the two which carry a modifier beside the element.<para/>
    /// An instance of a generic type is a wrapper as well and it is not one of these, because the element of it is the
    /// declaration whose base types and interfaces the arguments of the instance stand in, and an array is a wrapper
    /// which is read as the types which the runtime gives it rather than by this walk.
    /// </summary>
    /// <param name="type">The type which is read.</param>
    /// <returns>Whether the type is a wrapper which holds no declaration of its own.</returns>
    private static bool IsAWrappedType(TypeReference type)
        => type is not GenericInstanceType and not ArrayType and TypeSpecification;

    /// <summary>
    /// The types which an array is made of, which the runtime gives to it rather than the metadata which declares it:
    /// the array itself, the type of every array of the framework and the collections and the sequences which name no
    /// element at all, and - where the array is the one which the runtime calls a vector, which is one dimension and no
    /// lower bound, because the generic ones are given to that shape alone - those same types naming the element which
    /// the array holds.
    /// </summary>
    /// <param name="array">The array which is read.</param>
    /// <returns>The types which the array is given, itself among them.</returns>
    private static IReadOnlyList<TypeReference> TypesWhichAnArrayIsGiven(ArrayType array)
    {
        var module = array.Module;
        var given = new List<TypeReference> { array };

        foreach (var declaration in s_TheTypesWhichEveryArrayIsGiven)
        {
            given.Add(module.ImportReference(declaration));
        }

        // The collections and the sequences of the runtime which name the element are given to a vector, and they are
        // read where the array meets one of them rather than here: they name every type which the element of the array
        // is converted to, which no walk of the types it is made of tells, because the conversion of the element is a
        // conversion of the argument rather than of a type which the element is made of.
        return given;
    }

    /// <summary>
    /// The types which the runtime gives to every array whatever the values are which it holds: the type of every array
    /// of the framework, the collections and the sequences which name no element, and the two which tell two arrays
    /// apart by the values which they hold.
    /// </summary>
    private static readonly Type[] s_TheTypesWhichEveryArrayIsGiven =
    [
        typeof(Array),
        typeof(System.Collections.IList),
        typeof(System.Collections.ICollection),
        typeof(System.Collections.IEnumerable),
        typeof(System.Collections.IStructuralComparable),
        typeof(System.Collections.IStructuralEquatable),
        typeof(ICloneable)
    ];

    /// <summary>
    /// The collections and the sequences which the runtime gives to an array of one dimension alone, each of which names
    /// the element which the array holds.
    /// </summary>
    private static readonly Type[] s_TheCollectionsWhichNameTheElement =
    [
        typeof(System.Collections.Generic.IList<>),
        typeof(System.Collections.Generic.ICollection<>),
        typeof(System.Collections.Generic.IEnumerable<>),
        typeof(System.Collections.Generic.IReadOnlyList<>),
        typeof(System.Collections.Generic.IReadOnlyCollection<>)
    ];

    /// <summary>
    /// Whether the type stands for another type anywhere among the types which it is written with.
    /// </summary>
    /// <param name="type">The type which is read.</param>
    /// <returns>Whether a parameter of a declaration stands in the type at any depth.</returns>
    private static bool HoldsAParameter(TypeReference type)
        => type switch
        {
            GenericParameter => true,
            GenericInstanceType genericInstance => genericInstance.GenericArguments.Any(HoldsAParameter),
            TypeSpecification specification => HoldsAParameter(specification.ElementType),
            _ => false
        };

    /// <summary>
    /// The definition of the type which the assembly of it is read for, or null where that assembly is not there.
    /// </summary>
    /// <param name="type">The type which is read.</param>
    /// <returns>The definition of the type, or null where the assembly of it was not read.</returns>
    private static TypeDefinition? DefinitionOf(TypeReference type)
    {
        try
        {
            return type.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the type is the type which pairs a value with the absence of it, which is the one type of the values
    /// which the constraint that names the value types does not accept: the runtime refuses an instantiation of such a
    /// parameter with it, while it accepts one of the values which may not be absent.
    /// </summary>
    /// <param name="type">The type which is read.</param>
    /// <returns>Whether the type is a value which may be absent, or false where this read does not tell.</returns>
    private static bool IsANullableValueType(TypeReference type)
    {
        // A parameter of a member or of a type stands for the type which the constraints of it name, and none of them
        // tells whether that type is this one, so the runtime is what refuses such an instantiation.
        if (type is GenericParameter) return false;

        try
        {
            return type.Resolve()?.FullName == typeof(Nullable<>).FullName;
        }
        catch (AssemblyResolutionException)
        {
            // The assembly of the type is not there to be read, so the type is left to the runtime as it is where the
            // kind of it is read.
            return false;
        }
    }

    /// <summary>
    /// Whether the values of a type are the values of the type itself rather than references to values of it, which the
    /// metadata of the type tells without reading the assembly of it where the type is one of the types the runtime
    /// names itself.
    /// </summary>
    /// <param name="type">The type which is read.</param>
    /// <returns>Whether the type is a value type, or null where the kind of the type is one this read does not tell.</returns>
    private static bool? IsAValueType(TypeReference type)
    {
        // A parameter of a member or of a type stands for the type which the constraints of it name, and for no kind at
        // all where they name none.
        if (type is GenericParameter parameter)
        {
            var kind = parameter.Attributes & TheKindsWhichAConstraintNames;
            if (kind == GenericParameterAttributes.ReferenceTypeConstraint) return false;
            if (kind == GenericParameterAttributes.NotNullableValueTypeConstraint) return true;
            return null;
        }

        // An array is a reference to its elements whatever the type of those elements is, which the metadata of the type
        // names as an array rather than as one of the types the runtime holds the values of.
        if (type is ArrayType) return false;

        switch (type.MetadataType)
        {
            case MetadataType.Boolean
              or MetadataType.Char
              or MetadataType.SByte
              or MetadataType.Byte
              or MetadataType.Int16
              or MetadataType.UInt16
              or MetadataType.Int32
              or MetadataType.UInt32
              or MetadataType.Int64
              or MetadataType.UInt64
              or MetadataType.Single
              or MetadataType.Double
              or MetadataType.IntPtr
              or MetadataType.UIntPtr:
                return true;

            // A string and the type of every value which the stack carries as a reference are references, which is
            // what `object` stands for.
            case MetadataType.String
              or MetadataType.Object:
                return false;
        }

        // Every other type of the metadata is one which an assembly of its own declares, so the definition of it is what
        // tells its kind: it is read where the assembly of it can be read, and a type whose assembly is not there at all
        // - one which was woven against a reference without the assembly beside it - is one this read does not tell.
        try
        {
            return type.Resolve()?.IsValueType;
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    /// <param name="typeReference">The type reference which the parameters of a declaration stand in, and which the arguments of an instantiation replace.</param>
    extension(TypeReference typeReference)
    {
        /// <summary>
        /// The type which a reference of a declaration stands for where that declaration is instantiated: every generic
        /// parameter of the declaration which stands in the reference is replaced by the argument of the instantiation
        /// at the same position.
        /// </summary>
        /// <remarks>
        /// A base type is written where the type which declares it stands, so the base of a generic declaration names
        /// the parameters of that declaration rather than the arguments of the instance which a body reaches: the base
        /// of <c>Middle&lt;T&gt;</c> is <c>Base&lt;T&gt;</c> where the base of <c>Middle&lt;int&gt;</c> is <c>Base&lt;int&gt;</c>,
        /// and only the arguments of the instantiation tell the two apart.
        /// </remarks>
        /// <param name="declaration">The type which declares the reference, whose parameters the arguments replace.</param>
        /// <param name="instance">The instantiation which the declaration was reached through, which holds the arguments.</param>
        /// <returns>The reference with the arguments of the instantiation in place of the parameters of the declaration. A parameter whose owner is not the declaration stands where it did.</returns>
        internal TypeReference WithTheArgumentsOf(TypeDefinition declaration, TypeReference? instance)
        {
            var parameters = declaration.GenericParameters;

            // A declaration which names no parameter passes nothing down, and neither does an instance which holds no
            // argument for each of them: the reference stands as it is.
            if (parameters.Count == 0 || instance is not GenericInstanceType instantiation
                || instantiation.GenericArguments.Count != parameters.Count)
            {
                return typeReference;
            }

            return Replace(typeReference);

            TypeReference Replace(TypeReference type)
            {
                switch (type)
                {
                    // A parameter of another type, such as one which the base of a nested type names of the type it is
                    // nested in, is left standing: the caller refuses an instantiation which still holds one.
                    case GenericParameter parameter
                        when parameter.Owner is TypeReference owner && owner.FullName == declaration.FullName:
                        return instantiation.GenericArguments[parameter.Position];

                    // An argument is a type of its own, which may hold a parameter as well, just like Base<List<T>>.
                    case GenericInstanceType genericInstance:
                        var replacement = new GenericInstanceType(genericInstance.ElementType);
                        foreach (var argument in genericInstance.GenericArguments)
                        {
                            replacement.GenericArguments.Add(Replace(argument));
                        }

                        return replacement;

                    // And so is the element of an array, just like Base<T[]>.
                    case ArrayType arrayType:
                        return new ArrayType(Replace(arrayType.ElementType), arrayType.Rank);

                    // A parameter stands in a wrapper as well, just like Base<ref T> or Base<T*>: the wrapper names the
                    // declaration where it is written around a parameter, so one which is left standing stands for no
                    // signature of named types and the member it describes is refused rather than found.
                    case ByReferenceType byReference:
                        return new ByReferenceType(Replace(byReference.ElementType));

                    case PointerType pointer:
                        return new PointerType(Replace(pointer.ElementType));

                    case PinnedType pinned:
                        return new PinnedType(Replace(pinned.ElementType));

                    case SentinelType sentinel:
                        return new SentinelType(Replace(sentinel.ElementType));

                    case OptionalModifierType optionalModifier:
                        return new OptionalModifierType(optionalModifier.ModifierType, Replace(optionalModifier.ElementType));

                    case RequiredModifierType requiredModifier:
                        return new RequiredModifierType(requiredModifier.ModifierType, Replace(requiredModifier.ElementType));

                    default:
                        return type;
                }
            }
        }

    }
}
