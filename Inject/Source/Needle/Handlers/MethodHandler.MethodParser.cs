namespace Gneedle.Inject;

partial class MethodHandler
{
    /// <summary>
    /// Parse the call which proceeds into the body that was taken over with the arguments which the template itself was
    /// given, which is the symbol that names no signature: the member which is woven keeps the signature of the
    /// template, so the arguments of the call are the parameters of the template, in the order they are declared in.
    /// </summary>
    /// <param name="callIndex">Index of the instruction of the call to <see cref="Proceed.Invoke{TResult}"/>.</param>
    /// <param name="call">The reference of that call, which names the type the body hands back where it hands one back.</param>
    /// <param name="filter">The final instruction's container.</param>
    /// <param name="targetDef">The template method which the instructions are copied from.</param>
    /// <exception cref="ArgumentException">Thrown when the template proceeds without a body being woven around, or when the type which the call hands back is not the one which the member hands back.</exception>
    private void ParseProceedInvoke(int callIndex, MethodReference call, InstructionFilter filter, MethodDefinition targetDef)
    {
        // The body which was taken over is what the call stands for, and a template which proceeds without one being
        // taken over is a mistake of its own rather than a member which could not be found.
        if (m_ProceedMethod is not { } proceed)
        {
            throw new ArgumentException(string.Format(ErrorMessages.PROCEED_WITHOUT_AROUND_BODY, nameof(Proceed) + "." + nameof(Proceed.Invoke)));
        }

        // The type of the value which the call hands back is written at the call and the type which the member hands
        // back is written at the member, so the two are compared rather than left to the runtime to find disagreeing.
        var handedBack = call is GenericInstanceMethod genericCall ? genericCall.GenericArguments[0] : Source.Module.TypeSystem.Void;
        if (!TypeName.HasSameName(handedBack.ParseGenericTokens(Source, Source.Module), Source.ReturnType))
        {
            throw new ArgumentException(string.Format(ErrorMessages.PROCEED_INVOKE_RETURN_TYPE_MISMATCH, handedBack.FullName, Source.FullName));
        }

        // The receiver of the member comes first where the generated method belongs to an instance, and it is the
        // receiver of the member which is written rather than the one of the template: the call is an instruction of the
        // body which is woven, where the slot zero is the member. The arguments follow it in the order which the
        // template declares its parameters in, which is the order of the parameters of the member.
        if (!proceed.IsStatic)
        {
            filter.Insert(callIndex, Instruction.Create(OpCodes.Ldarg_0));
        }

        for (var position = 0; position < Source.Parameters.Count; position++)
        {
            filter.Insert(callIndex, CreateLdarg(position + (targetDef.IsStatic ? 0 : 1), targetDef));
        }

        // The call of the generated method takes the place of the call which proceeds.
        filter.Replace(callIndex, Instruction.Create(OpCodes.Call, GetCallableReference(proceed)));
    }

    /// <summary>
    /// Parse the symbol to actual method operation.
    /// </summary>
    /// <example>
    /// When the symbol is method invoke, the ILCode of field setting must look like following:
    /// <code>
    /// IL_0005: ldstr "{method_name}"
    /// IL_000a: call [Gneedle.Inject]Gneedle.Inject.This::Method&lt;{delegate_type}&gt;(string)
    /// IL_000f: ldloc.0 // 1st parameter
    /// IL_0010: ldloc.1 // 2nd parameter
    /// IL_0011: callvirt instance !2 class [System.Runtime]{delegate_type}::Invoke(!0, !1)
    /// IL_0016: stloc.2 // result stocking
    /// </code>
    /// Then we must make it look like the following:
    /// <code>
    /// IL_0005: ldarg.0 // this, only when the method is not static
    /// IL_0006: ldloc.0 // 1st parameter
    /// IL_0007: ldloc.1 // 2nd parameter
    /// IL_0008: call instance {return_type} {declaring_type}::{method_name}({1st_param_type}, {2nd_param_type})
    /// IL_000d: stloc.2 // result stocking
    /// </code>
    /// </example>
    /// <param name="memberName">Name of the member.</param>
    /// <param name="memberSymbol">Member flags about the member kind and its property.</param>
    /// <param name="callIndex">Index of the instruction of the call which the symbol stands for.</param>
    /// <param name="nameIndex">Index of the instruction of `ldstr {member_name}`, which is the one ahead of the call, or
    /// null for the symbol which carries no name: that one names its member from the member being woven rather than from
    /// a name, so the call is the whole of the symbol.</param>
    /// <param name="filter">The final instruction's container.</param>
    /// <param name="targetDef">The template method which the instructions are copied from.</param>
    /// <exception cref="InvalidILException">Thrown when the instructions around the name of the member are not the call which it stands for.</exception>
    /// <exception cref="ArgumentException">Thrown when the method is invalid.</exception>
    private void ParseMethod(string memberName, MemberSymbols memberSymbol, int callIndex, int? nameIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        if ((filter.Target[callIndex].Operand as GenericInstanceMethod)?.GenericArguments.FirstOrDefault() is not { } delegateRef)
        {
            throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, memberName));
        }

        if (delegateRef is not TypeDefinition delegateDef)
        {
            delegateDef = DeclaringTypeHandler.AssemblyHandler.GetCecilType(delegateRef).Definition;
        }

        // Generic instance arguments used to resolve open generic parameters in the Invoke signature.
        var genericArguments = (delegateRef as GenericInstanceType)?.GenericArguments;

        // Get method parameter from delegation. When the delegate is a generic instance (e.g. Func<int,int,int>),
        // the Invoke signature uses open generic parameters (T1,T2), so we map them to the actual arguments.
        // A parameter of a generic method is named by a Gneedle.Inject.M_[0-20] token rather than by a real generic
        // parameter, because a delegate cannot declare one, so the token is parsed to the generic parameter of the
        // source method here. Without it, the parameter of the delegate would be a type of the Gneedle.Inject assembly
        // which no method of the declaring type could ever match.
        var parameters = delegateDef.Methods
                                    .First(method => method.Name.Equals("Invoke"))
                                    .Parameters
                                    .Select(p => ResolveDelegateParameterType(p.ParameterType, genericArguments).ParseGenericTokens(Source, Source.Module))
                                    .ToArray();

        // Detect Instance.Method with new Instance(param) syntax: need to skip the array init sequence.
        // The instance which the template reached the method through, which is the receiver of the call where the method
        // is not static: the sequence which builds the instance is dropped, so the value it holds has to stand where the
        // member takes a receiver.
        Instruction? receiverIns = null;
        InstanceValue? instance = null;
        var instanceName = -1;
        if (memberSymbol.HasFlag(MemberSymbols.Instance) && nameIndex is { } name && TryGetInstanceValue(filter, name, targetDef, out var value))
        {
            instance     = value;
            instanceName = name;
            receiverIns  = value.Load;
        }

        // Detect Static.Method with Static.From("typename"): need to skip ldstr+call Static::From.
        var skipStaticFromCount = 0;
        if (memberSymbol.HasFlag(MemberSymbols.Static) && nameIndex is { } staticName && staticName >= 2)
        {
            var callFromIns = filter.Target[staticName - 1];
            if (callFromIns.OpCode == OpCodes.Call && callFromIns.Operand is MethodReference {Name: "From", DeclaringType: var declType}
                && declType.FullName == Static.TYPE_NAME)
            {
                var ldstrIns = filter.Target[staticName - 2];
                if (ldstrIns.OpCode == OpCodes.Ldstr)
                {
                    skipStaticFromCount = 2; // ldstr + call Static::From
                }
            }
        }

        // The index which is given is the one of the name, which is what the members reached through an instance of
        // Instance or Static are read against: a symbol which carries no name is not one of those, so the call stands in
        // its place, where it is read by nothing.
        var methodDef = GetMethod(memberSymbol, memberName, nameIndex ?? callIndex, filter, parameters, targetDef, out var namedInstance);
        if (methodDef == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, memberName));
        }

        // Skip the array init sequence if this is Instance.Method with new Instance(param), and the Static.From sequence if
        // this is Static.Method. The two are dropped whichever way the method is reached afterwards, which is the whole
        // of the sequence: a symbol which is handed back as a delegate reaches its member no less than one which invokes
        // it, and the sequence which built the instance of `Instance` is balanced by nothing but the name which ends it.
        // The value which the instance was built around goes with it where the method takes no receiver, or where the load
        // of it is written where the name stands instead: a value which is read for nothing is not left on the stack.
        if (instance is { } heldValue)
        {
            for (var i = heldValue.First - 4; i < heldValue.First; i++)
            {
                filter.Skip(i);
            }

            for (var i = heldValue.Last + 1; i < instanceName; i++)
            {
                filter.Skip(i);
            }

            if (heldValue.Load != null || methodDef.IsStatic)
            {
                for (var i = heldValue.First; i <= heldValue.Last; i++)
                {
                    filter.Skip(i);
                }
            }
        }

        if (skipStaticFromCount > 0 && nameIndex is { } fromName)
        {
            for (var i = fromName - skipStaticFromCount; i < fromName; i++)
            {
                filter.Skip(i);
            }
        }

        // A delegate which the template holds in a local is invoked by that local rather than where the symbol stands, so
        // what the symbol stands for there is the store of the delegate: the store and the call are dropped together, and
        // every read of the local which invokes the delegate is written as the call of the member instead. A read of the
        // local stands where it stands and is written as the receiver of the member, which is the load of the argument
        // the instance was named by: a value which the template computed is one value in one place, so the local holds the
        // delegate where that is so, as it does for a local which stands for more than the invocation of it.
        var instanceIsComputed = instance is {Load: null};
        var held = HeldLocal(filter.Target, callIndex);

        // The store which the local is written by holds the value which the symbol left only where every path of the body
        // goes through the symbol: the value which another path leaves there is the one the local holds just as well, and
        // the invocation of the local is then made on the member which that path names rather than on the one which the
        // symbol names. The delegate which each path built is what the local holds there, and neither symbol stands for
        // the invocation, so what each of them writes is the delegate of its own member.
        if (held is { } store && !TheSymbolIsOnEveryPathTo(filter.Target, callIndex, store.Store, targetDef))
        {
            held = null;
        }

        var heldInvocations = held is { } stored && !instanceIsComputed
            ? InvocationsOfTheHeldDelegate(filter.Target, stored.Local, delegateRef, targetDef)
            : null;

        if (held is { } heldStore && heldInvocations != null)
        {
            if (nameIndex is { } heldName)
            {
                filter.Skip(heldName);
            }

            filter.Skip(callIndex);
            filter.Skip(heldStore.Store);

            foreach (var (read, invocation) in heldInvocations)
            {
                // The read of the local is the receiver of the invocation, and it is written as the receiver of the
                // member, which a member of no instance takes none of.
                if (methodDef.IsStatic)
                {
                    filter.Skip(read);
                }
                else
                {
                    filter.Replace(read, CreateReceiver(receiverIns, targetDef));
                }

                filter.Replace(invocation, Instruction.Create(methodDef.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, GetCallableReference(methodDef, namedInstance)));
            }
        }
        // If there is `Invoke` method of the  target delegate is in following instructions, then replace it to the actual method calling.
        else if (held == null && TryGetNextInvoke(filter.Target, callIndex, delegateRef, targetDef, out var callvirtIndex))
        {
            // The name of a symbol is dropped, and the receiver of a member of an instance is loaded in its place, which
            // is the instruction ahead of the call. A symbol which carries no name has no such instruction, so the load
            // is inserted ahead of the call instead. The call itself is dropped either way, and what the delegate was
            // invoked through becomes the call of the member.
            if (nameIndex is { } loadedName)
            {
                filter.Skip(loadedName);
            }

            // The receiver of the member stands ahead of the call which the delegate is invoked through, which is where
            // the value which the template named it stands: only the load of an argument has to be written there.
            if (!methodDef.IsStatic && !instanceIsComputed)
            {
                filter.Insert(callIndex, CreateReceiver(receiverIns, targetDef));
            }

            // Skip `call [Gneedle.Inject]Gneedle.Inject.This::Method<class {delegate_type}>({parameter_types})`
            filter.Skip(callIndex);
            // callvirt instance class {delegate_type}::Invoke({parameter_types}) -> callvirt/call instance class {declaring_type}::{method_name}({parameter_types})
            filter.Replace(callvirtIndex, Instruction.Create(methodDef.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, GetCallableReference(methodDef, namedInstance)));
        }
        // Or create delegate by method pointer and call it right now.
        else
        {
            var importedMethod = GetCallableReference(methodDef, namedInstance);

            // The delegate which is built is the one which the template named rather than the definition which that one
            // is an instantiation of: the constructor of the definition takes the arguments of the open type, and a body
            // which named the definition of a generic delegate could not be loaded at all.
            var delegateType = Source.Module.ImportReference(delegateRef);
            var constructor = delegateDef.GetConstructors().First();
            var delegateCtor = new MethodReference(constructor.Name, Source.Module.TypeSystem.Void, delegateType)
            {
                HasThis           = constructor.HasThis,
                ExplicitThis      = constructor.ExplicitThis,
                CallingConvention = constructor.CallingConvention,
            };
            foreach (var parameter in constructor.Parameters)
            {
                delegateCtor.Parameters.Add(new ParameterDefinition(Source.Module.ImportReference(parameter.ParameterType)));
            }

            // The name of the symbol is dropped, and the receiver of a member of an instance is loaded in its place: a
            // symbol which carries no name has no such instruction, so the load is inserted ahead of the call instead.
            // The call then becomes the pointer of the member, and the delegate is built from it.
            if (nameIndex is { } loadedName)
            {
                filter.Skip(loadedName);
            }

            if (!methodDef.IsStatic)
            {
                // The target of the delegate is the value which the member is reached through, which stands where the
                // template named it or computed it: only the load of an argument has to be written where the pointer of
                // the member is taken.
                if (!instanceIsComputed)
                {
                    filter.Insert(callIndex, CreateReceiver(receiverIns, targetDef));
                }
            }
            else
            {
                // The constructor of a delegate is handed the member to call and the instance it is called on, which a
                // member which is static has none of: the target is the null which the constructor takes the instance
                // in the place of, as a compiler writes it for a member which the delegate names without an instance.
                filter.Insert(callIndex, Instruction.Create(OpCodes.Ldnull));
            }

            // call [Gneedle.Inject]Gneedle.Inject.This::Method<class {delegate_type}>({parameter_types}) -> ldftn {return_type} {declaring_type}::{method_name}({parameter_types})
            filter.Replace(callIndex, Instruction.Create(OpCodes.Ldftn, importedMethod));
            // insert `newobj System.Void {delegate_type}::.ctor({parameter_types})`
            filter.Insert(callIndex + 1, Instruction.Create(OpCodes.Newobj, delegateCtor));
        }
    }

    /// <summary>
    /// Get the reference which the instruction of the body has to hold to call <paramref name="methodDef"/>.
    /// </summary>
    /// <remarks>
    /// A generic method is called through a method specification rather than through the definition itself, because the
    /// call instruction has to name the generic arguments of the call. The template named them by the
    /// <c>Gneedle.Inject.M_[0-20]</c> tokens which were resolved to the generic parameters of the method being woven,
    /// which are the arguments here: the token of the template which names a parameter of the member resolved to the
    /// parameter of the method being woven which the member was looked up by, and the member is looked up by names. The
    /// generic method which the template proceeds through is a generated one which declares a parameter of every
    /// position, so the instantiation is the one a compiler emits for a call to a method of the generic parameters of
    /// its own caller. A token which stands for a parameter of the type which the method being woven is a member of is
    /// resolved to that parameter, which the body names as well as the ones it declares itself.
    /// </remarks>
    /// <param name="methodDef">The method which the body calls.</param>
    /// <param name="namedInstance">The type of the instance which the template reached the member through, or null where the template reached none.</param>
    /// <returns>The reference which the call instruction holds.</returns>
    /// <exception cref="ArgumentException">Thrown when a parameter of the member stands for no parameter which the body being woven names, because the call of it cannot name the argument for that parameter.</exception>
    private MethodReference GetCallableReference(MethodDefinition methodDef, TypeReference? namedInstance = null)
    {
        var importedMethod = GetMethodReference(methodDef, namedInstance);

        // A method which declares no parameter of its own is called through the reference itself, and the parameters of
        // the method being woven have no place in a call of it.
        if (methodDef.GenericParameters.Count == 0) return importedMethod;

        var genericInstance = new GenericInstanceMethod(importedMethod);
        for (var position = 0; position < methodDef.GenericParameters.Count; position++)
        {
            // A parameter of the member which its signature names is declared with the parameter itself, and the types
            // of the parameters of the delegate are what the member was looked up by, which are compared by name: the
            // token of the delegate which stands for such a parameter of the member resolved to the parameter of the
            // member being woven which bears its name, wherever it stands among them. A token stands for a parameter of
            // the type which the body is a member of just as well, so a parameter of the member which no parameter of
            // the body bears the name of is one which the type may name: both are parameters which the body names.
            var parameter = methodDef.GenericParameters[position];
            var named = Source.GenericParameters.FirstOrDefault(own => own.Name.Equals(parameter.Name))
                        ?? Source.DeclaringType.GenericParameters.FirstOrDefault(own => own.Name.Equals(parameter.Name));

            // A parameter of the member which the signature of the member does not name is one which no token ties to
            // anything, and the parameter of the member being woven which stands at its position is the argument for it,
            // as it was before the parameters of the member were read by name at all. A parameter of the member which
            // neither stands for a parameter of the body nor is one the body holds at that position is one whose call
            // would leave it open, which the runtime refuses to run rather than being a call of the member.
            if (named == null && position >= Source.GenericParameters.Count)
            {
                throw new ArgumentException(string.Format(ErrorMessages.INVALID_GENERIC_MEMBER_CALL, methodDef.FullName, Source.FullName));
            }

            genericInstance.GenericArguments.Add(named ?? Source.GenericParameters[position]);
        }

        return genericInstance;
    }

    /// <summary>
    /// Get the reference which the module holds to call <paramref name="methodDef"/>, which names the instantiation of
    /// the type which declares it rather than the definition of that type when that type declares parameters, and which
    /// declares the parameters of the member itself where it declares any.<para/>
    /// A member which such a type declares belongs to the definition, and the call of a method of a type which stands
    /// open is one which the runtime refuses to run: the declaring type is written as the instantiation which the body
    /// being woven stands in for that reason, which is the shape a compiler emits for a call to a member of the
    /// parameters of its caller. A type whose parameters the body cannot name is left as the imported definition, which
    /// is what the call held before.
    /// </summary>
    /// <param name="methodDef">The method which the body calls.</param>
    /// <param name="namedInstance">The type of the instance which the template reached the member through, or null where the template reached none.</param>
    /// <returns>The reference which the call instruction holds.</returns>
    private MethodReference GetMethodReference(MethodDefinition methodDef, TypeReference? namedInstance = null)
    {
        if (methodDef.DeclaringType is not {HasGenericParameters: true} declaringType) return Source.Module.ImportReference(methodDef);

        if (InstantiationOf(declaringType, namedInstance) is not { } declaringInstance) return Source.Module.ImportReference(methodDef);

        var reference = new MethodReference(methodDef.Name, methodDef.ReturnType, declaringInstance)
        {
            HasThis           = methodDef.HasThis,
            ExplicitThis      = methodDef.ExplicitThis,
            CallingConvention = methodDef.CallingConvention,
        };

        foreach (var parameter in methodDef.Parameters)
        {
            reference.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
        }

        // The member may declare parameters of its own as well, which belong to the definition as the parameters of the
        // type do, and the reference stands for the member rather than for that definition: a call of a member which is
        // instanced is one which names a member that declares a parameter for each of the arguments of it, so a call of
        // a reference which declares none of them is one the runtime refuses to read. The signature names the parameters
        // by position, so the ones which were copied above stand for the ones declared here.
        foreach (var genericParameter in methodDef.GenericParameters)
        {
            reference.GenericParameters.Add(new GenericParameter(genericParameter.Name, reference));
        }

        return reference;
    }

    /// <summary>
    /// The type of the instance which a symbol of a template reaches a member through: the type which the template
    /// named where it named one, the type being woven for <see cref="MemberSymbols.This"/> and
    /// <see cref="MemberSymbols.Base"/>, which stand for a member of the instance which the body is a member of, and
    /// null where the template named none.<para/>
    /// The member which one of those two reaches belongs to the woven type or to a base type of it, and the
    /// instantiation which the body can name for the declaration which holds it is read from the chain of base types
    /// which the woven type is declared by: the walk of that chain starts at the woven type wherever in the chain the
    /// member stands.
    /// </summary>
    /// <param name="memberSymbol">The symbol which the template reached the member through.</param>
    /// <param name="namedInstance">The type which the template named the instance through, where it named one.</param>
    /// <returns>The type of the instance which the member is reached through, or null where the template named none.</returns>
    private TypeReference? InstanceNamedBy(MemberSymbols memberSymbol, TypeReference? namedInstance = null)
    {
        if (namedInstance is not null) return namedInstance;

        return memberSymbol.HasFlag(MemberSymbols.This) || memberSymbol.HasFlag(MemberSymbols.Base) ? Source.DeclaringType : null;
    }

    /// <summary>
    /// The instantiation of <paramref name="declaringType"/> which the body being woven names, which is the type itself
    /// or the base type which it hands its own parameters down to, or null when the body can name no instantiation of
    /// it.<para/>
    /// The body is a member of the type which is woven, so the parameters which it can name are the ones that type
    /// declares: the argument of the instantiation is the parameter of the body at the same position, which is what a
    /// type which passes its parameters on to its base hands over.
    /// </summary>
    /// <param name="declaringType">The type which declares the member which is called.</param>
    /// <param name="namedInstance">The type of the instance which the template reached the member through, or null where the template reached none.</param>
    /// <returns>The instantiation of the declaring type, or null when the body names none of it.</returns>
    private TypeReference? InstantiationOf(TypeReference declaringType, TypeReference? namedInstance)
    {
        // The type which the template named the instance through is written where the member belongs to that type
        // itself: the value which the instance holds is one of that type rather than of a base type of it, and the
        // parameters of it, which the body being woven cannot name, are the ones which the template declared.
        if (namedInstance is GenericInstanceType named && named.ElementType.FullName == declaringType.FullName)
        {
            return named.ParseGenericTokens(Source, Source.Module);
        }

        var woven = Source.DeclaringType;

        if (woven.FullName == declaringType.FullName)
        {
            return woven.MakeGenericInstanceType(woven.GenericParameters.Select(static parameter => (TypeReference) parameter).ToArray());
        }

        // The base type of the woven type is written out where the type is declared, so the arguments of it stand in
        // the body already: a base which hands a parameter of its own down names the parameter of the body with it.
        if (woven.BaseType is GenericInstanceType baseInstance && baseInstance.ElementType.FullName == declaringType.FullName)
        {
            return baseInstance;
        }

        // The type which the template named the instance through derives from the declaring type: the member belongs to
        // the definition of a base as well, and the instantiation which a base of that type names is the one the body
        // can write where the definition of it cannot be.
        return namedInstance is null ? null : InstantiationOfABase(declaringType, namedInstance);
    }

    /// <summary>
    /// The instantiation of the type which declares a member, which a base of the type the template named the instance
    /// through stands for.<para/>
    /// The base of a type is written where that type is declared, so a base which names a parameter of the declaration
    /// it stands in names the argument which the instantiation of that declaration holds, whichever base of whichever
    /// base of the named type it is: the arguments are handed down the chain, and the parameters of the woven type
    /// itself are handed down as they stand, which the body names. A base which is left with a parameter of another
    /// declaration names nothing which the body can write, so the definition of it is what the body held before the
    /// base was walked.
    /// </summary>
    /// <param name="declaringType">The type which declares the member which is reached.</param>
    /// <param name="namedInstance">The type of the instance which the template reached the member through.</param>
    /// <returns>The instantiation of the declaring type, or null when no base of the named type names one.</returns>
    private TypeReference? InstantiationOfABase(TypeReference declaringType, TypeReference namedInstance)
    {
        // A type of an assembly which the weaver cannot reach has no base to read, and the walk ends there as it does
        // where the chain ends: the reference is the one the body held before the base was walked.
        try
        {
            // The base of a type is written where that type is declared, so the parameters which it names are the ones
            // of the declaration it stands in. The walk carries the instantiation which each type was reached through,
            // and hands each declaration the arguments of it, so that a base which is written with a parameter is read
            // as the argument which the instance behind the walk holds.
            for (var instance = namedInstance; instance.Resolve() is {BaseType: { } baseType} declaration;)
            {
                var reached = baseType.WithTheArgumentsOf(declaration, instance);
                if (reached is GenericInstanceType instantiation
                    && instantiation.ElementType.FullName == declaringType.FullName
                    && HoldsOnlyParametersWhichTheBodyNames(instantiation))
                {
                    return instantiation.ParseGenericTokens(Source, Source.Module);
                }

                instance = reached;
            }
        }
        catch (AssemblyResolutionException) { }

        return null;
    }

    /// <summary>
    /// Whether every generic parameter which stands in <paramref name="type"/> is one which the body being woven names
    /// itself: the parameters of the woven type and of the member which is being woven are the ones which the body
    /// holds, which an instantiation of a base of that type is written with, and a parameter of any other declaration
    /// stands for an argument which the body can write nowhere.
    /// </summary>
    /// <param name="type">The type which the walk of a chain of base types reached.</param>
    /// <returns>Whether the body can write the type with every parameter which stands in it.</returns>
    private bool HoldsOnlyParametersWhichTheBodyNames(TypeReference type) => type switch
    {
        GenericParameter parameter => parameter.Owner == Source.DeclaringType || parameter.Owner == Source,
        // An argument is a type of its own, which may hold a parameter as well, just like Base<List<T>>, and the
        // element of an array is one, just like Base<T[]>: both are read through the type they stand for.
        GenericInstanceType instance    => HoldsOnlyParametersWhichTheBodyNames(instance.ElementType) && instance.GenericArguments.All(HoldsOnlyParametersWhichTheBodyNames),
        TypeSpecification specification => HoldsOnlyParametersWhichTheBodyNames(specification.ElementType),
        _                               => type.DeclaringType is null || HoldsOnlyParametersWhichTheBodyNames(type.DeclaringType)
    };

    /// <summary>
    /// The method which a symbol of a template stands for: a member of the type being woven, a member of its base type,
    /// a member of the instance the template names, a member of the type it names statically, or the method which holds
    /// the body that was taken over.<para/>
    /// The instructions and the position of the symbol among them are read as well, because the instance which a member
    /// reached through <see cref="MemberSymbols.Instance"/> or through <see cref="MemberSymbols.Static"/> belongs to is
    /// written in the instruction before the name, and the type of it is what the member is looked up on.
    /// </summary>
    /// <param name="memberSymbol">The symbol which the template reached the member through.</param>
    /// <param name="methodName">Name of the member.</param>
    /// <param name="currentIndex">Index of the instruction which loads the name of the member.</param>
    /// <param name="filter">The filter which the body is written through.</param>
    /// <param name="parameters">The types of the arguments which the member is called with, which the member that is found has to be described by.</param>
    /// <param name="targetDef">The template which the instructions are read out of.</param>
    /// <param name="namedInstance">
    /// The type of the instance which the member is reached through, which is the type being woven for the symbols
    /// which stand for a member of the body's own instance, or null where the template named none.
    /// </param>
    /// <returns>The method which the symbol stands for, or null when the symbol is not one which names a method.</returns>
    /// <exception cref="ArgumentException">Thrown when the member cannot be resolved, or when the template proceeds without a body being woven around.</exception>
    private MethodDefinition? GetMethod(MemberSymbols memberSymbol, string methodName, int currentIndex, InstructionFilter filter, IReadOnlyList<TypeReference> parameters, MethodDefinition targetDef, out TypeReference? namedInstance)
    {
        namedInstance = InstanceNamedBy(memberSymbol);

        if (memberSymbol.HasFlag(MemberSymbols.Base))
        {
            return DeclaringTypeHandler.GetMethodInBase(methodName, parameters);
        }

        if (memberSymbol.HasFlag(MemberSymbols.This))
        {
            return DeclaringTypeHandler.GetMethodInThisOrABaseType(methodName, parameters);
        }

        if (memberSymbol.HasFlag(MemberSymbols.Proceed))
        {
            // The template proceeds without a body being woven around, which is a mistake of its own rather than a
            // method which could not be found.
            if (m_ProceedMethod is not { } proceed)
            {
                throw new ArgumentException(string.Format(ErrorMessages.PROCEED_WITHOUT_AROUND_BODY, methodName));
            }

            // The method which was generated by the weave is the one which is named, rather than a method of the
            // declaring type which is looked up by the name: the body which was moved belongs to this member, and a
            // member of the declaring type or of a base type of it which happens to carry that name is a different one.
            // The signature of the call is compared with the signature of this method, so a call which names another
            // one is refused rather than written.
            return proceed.Parameters.SameWith(parameters.ToArray()) ? proceed : null;
        }

        if (memberSymbol.HasFlag(MemberSymbols.Instance))
        {
            // The type of the instance which the member is looked up on: the name is preceded by the construction of the
            // instance of `Instance` which holds the value, which is the only way the receiver of the member can be
            // placed ahead of the name of it. A value whose type the walk cannot tell names no type to look the member
            // up on, which is refused rather than looked up on the member being woven, which holds a member of that name
            // by coincidence at most.
            TypeReference? argType = null;
            if (TryGetInstanceValue(filter, currentIndex, targetDef, out var instance))
            {
                argType = instance.Load is { } load ? GetArgType(load, targetDef) : GetValueType(filter, instance.Last, targetDef);
            }

            if (argType == null)
            {
                throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, methodName));
            }

            // A token as the instance type is just another spelling of the generic parameter of the injected method or of
            // its declaring type, so it is resolved exactly like a real generic parameter: the method is looked up on the
            // constraints of the generic parameter. The generic parameter itself has no definition to look the method up on
            // (Cecil's GenericParameter.Resolve() returns null), and a call on an unconstrained one would be invalid IL.
            var instanceType = argType.TryGetParsedGenericParameter(Source, out var genericParameter) ? genericParameter! : argType;

            if (instanceType is GenericParameter parameter)
            {
                return GetMethodFromConstraint(parameter, methodName, parameters);
            }

            // The type which the template named the instance through is written out where the member is called where
            // the member belongs to that type itself: the value which the instance holds is one of the type as the
            // template declared it, which is an instantiation of the type which the lookup below answers with.
            namedInstance = instanceType;

            // The instance type may stand for the type of another assembly, in which case the method is looked up on the real one.
            return DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(instanceType.ResolveDefinition(Source.Module), (string) filter.Target[currentIndex].Operand, parameters);
        }

        if (memberSymbol.HasFlag(MemberSymbols.Static))
        {
            // Static.From("FullTypeName").Method<D>("methodName") pattern:
            // [currentIndex-2]: ldstr "FullTypeName"
            // [currentIndex-1]: call Static::From
            // [currentIndex]:   ldstr "methodName"
            if (currentIndex < 2) throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, methodName));
            var typeNameIns = filter.Target[currentIndex - 2];
            if (typeNameIns.OpCode != OpCodes.Ldstr || typeNameIns.Operand is not string fullTypeName)
            {
                throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, methodName));
            }

            // Resolve the type using GetCecilType (checks cache + Type.GetType reflection + current assembly).
            var staticType = DeclaringTypeHandler.AssemblyHandler.GetCecilType(fullTypeName).Definition;
            return DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(staticType, (string) filter.Target[currentIndex].Operand, parameters);
        }

        return null;
    }

    /// <summary>
    /// The method which a template reaches through a generic parameter, which is looked up on each of the constraints
    /// of the parameter in turn: the parameter itself holds no definition to look a method up on, and a call on one
    /// which is unconstrained would be invalid IL.
    /// </summary>
    /// <param name="target">The generic parameter which the member is reached through.</param>
    /// <param name="methodName">Name of the member.</param>
    /// <param name="parameters">The types of the arguments which the member is called with.</param>
    /// <returns>The method which the constraints of the parameter describe.</returns>
    /// <exception cref="ArgumentException">Thrown when no constraint holds a method of that name and signature.</exception>
    private MethodDefinition GetMethodFromConstraint(GenericParameter target, string methodName, IReadOnlyList<TypeReference> parameters)
    {
        MethodDefinition? methodDef = null;
        foreach (var curType in target.Constraints.Select(item => DeclaringTypeHandler.AssemblyHandler.GetCecilType(item.ConstraintType).Definition))
        {
            methodDef = DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(curType, methodName, parameters, false);
            if (methodDef != null!)
            {
                return methodDef;
            }
        }

        return methodDef ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, methodName));
    }

    /// <summary>
    /// Find the instruction which invokes the delegate that a method symbol was parsed into, which is the first
    /// instruction after the symbol that calls a member of that delegate's type with the arguments the stack holds, the
    /// value which the symbol left being the one which the call is made on.<para/>
    /// The stack is walked from the beginning of the body rather than from the symbol, because the values which the call
    /// is handed are pushed before it and by instructions of their own, so what the call reads can only be told by
    /// carrying the stack along from where it is empty.
    /// </summary>
    /// <param name="bodyInstructions">The instructions of the body which is parsed.</param>
    /// <param name="callIndex">Index of the instruction which loads the delegate.</param>
    /// <param name="delegateType">Type of the delegate which the symbol was parsed into.</param>
    /// <param name="targetDef">The template which the instructions belong to.</param>
    /// <param name="index">Index of the instruction which invokes the delegate.</param>
    /// <returns>Whether the instruction was found, which is false when the body invokes no such delegate after the symbol.</returns>
    private bool TryGetNextInvoke(IReadOnlyList<Instruction> bodyInstructions, int callIndex, TypeReference delegateType, MethodDefinition targetDef, out int index)
    {
        // This stack is used to ensure that the method parameters are of the same type as the method signature before they're all pushed to the stack.
        var paramStack = new ParameterStack();
        // This stack is used to cache the types of 'stloc' operand during scanning the method body.
        var localStack = new TypeReference[bodyInstructions.Count];

        // TODO: Maybe we could cache all scanning results that wouldn't simulate parameter balance every time.
        // Scanning method body.
        for (var i = 0; i < bodyInstructions.Count; i++)
        {
            var ins = bodyInstructions[i];
            if (ins.OpCode == OpCodes.Nop) continue;

            // Only in the case could it be matched that is index greater than callIndex.
            if (i > callIndex && MatchTargetInvoke(ins, i))
            {
                index = i;
                return true;
            }

            // Keep the stack balanced until we match the target delegate invoke.
            // The placeholder `call This::Method<TDelegate>(string)` at callIndex is balanced like
            // any other static call: it pops the string argument and pushes the returned delegate.
            BalanceStack(ins, paramStack, localStack, targetDef);
        }

        index = -1;
        return false;

        // Whether an instruction is the one which invokes the delegate that is looked for, which is a call of the
        // `Invoke` of that delegate's type with the arguments the stack holds the types of. The delegate Invoke expects
        // the stack to hold [delegate-receiver, arg1, ...argN], and the receiver of it is required to be the value which
        // the read or the call of the placeholder left, which is what tells the invocation of one delegate from the
        // invocation of another whose arguments happen to be of the same types.
        bool MatchTargetInvoke(Instruction ins, int index)
            => (ins.OpCode == OpCodes.Callvirt || ins.OpCode == OpCodes.Call)   // It's not double that the instruction must be 'call' type.
                && ins.Operand is MethodReference {Name: "Invoke"} callMethod   // We only check the 'invoke' method from Delegate.
                && TypeName.HasSameName(delegateType, callMethod.DeclaringType) // Make sure declaring types are the same.
                && TheSymbolLeftTheReceiver(callMethod, index)                  // Make sure the invocation is made on the delegate.
                // The invocation is made on the value which the symbol left only where every path of the body goes
                // through the symbol: the arms of a branch which each name a member of the same delegate type both
                // leave a value for it, and the delegate which the arm which ran built is the one it is made on.
                && TheSymbolIsOnEveryPathTo(bodyInstructions, callIndex, index, targetDef)
                && TopOfStackMatches(callMethod);                               // Make sure the top-of-stack types match the invoke parameters.

        // Whether the arguments of the invocation are exactly the values which stand above the one which the symbol left,
        // which is what that value being the receiver of the invocation means: the arguments alone do not tell one
        // invocation of a delegate from another, so a template which invokes a delegate on the value of another
        // invocation had the inner instruction answered for both of them, and the invocation of the inner delegate was
        // written where the outer one stood.<para/>
        // The instructions between the symbol and the invocation are walked as the graph which they are rather than in a
        // row, because a value which the call is handed may be computed along a branch: the paths which a branch leaves
        // for meet again before the invocation, and every one of them reaches an instruction with the same number of
        // values above the one the symbol left, which is the number the invocation is handed. A path which leaves the
        // region says nothing about the invocation, so it is refused rather than followed, and a path which ends inside
        // the region is one which reaches no invocation at all.
        bool TheSymbolLeftTheReceiver(MethodReference callMethod, int invocation)
        {
            if (invocation <= callIndex) return false;

            // The instructions of the region, which the targets of the branches are read through: a branch which names
            // an instruction of another place is one which leaves the region.
            var region = new Dictionary<Instruction, int>(invocation - callIndex);
            for (var i = callIndex + 1; i <= invocation; i++) region[bodyInstructions[i]] = i;

            // The number of values which stand above the one the symbol left when an instruction is reached, which is
            // held for every instruction of the region: two paths which reach one instruction with different numbers
            // are two paths which the count at the invocation cannot be told from, and the instruction alone does not
            // tell which of them was taken.
            var aboveAt = new int?[invocation - callIndex];

            // The placeholder hands the name it was given back as the delegate, so the value which the symbol left
            // stands above nothing.
            aboveAt[0] = 0;
            var pending = new Stack<(int Index, int Above)>();
            pending.Push((callIndex + 1, 0));
            var reached = false;

            while (pending.Count > 0)
            {
                var (index, above) = pending.Pop();
                var ins = bodyInstructions[index];

                // The invocation is where the walk arrives, and the number of values which the paths carried to it is
                // the number of arguments which the delegate is handed. An invocation which no path reaches is one
                // which the symbol stands for no more than any other call of the delegate's type.
                if (index == invocation)
                {
                    if (above != callMethod.Parameters.Count) return false;
                    reached = true;
                    continue;
                }

                // An instruction which takes more values than the ones which stand above it took that value, and one
                // which the walk cannot count is one whose result cannot be told: either way the invocation is made on
                // something other than the value which the symbol left.
                var effect = ins.OpCode == OpCodes.Nop ? (Taken: 0, Left: 0)
                           : BranchEffect(ins) ?? StackEffect(ins);
                if (effect is not { } counted) return false;
                if (counted.Taken > above) return false;
                var left = above + counted.Left - counted.Taken;

                foreach (var successor in SuccessorsOf(ins))
                {
                    if (!region.TryGetValue(successor, out var successorIndex)) return false;
                    if (aboveAt[successorIndex - callIndex - 1] is { } seen)
                    {
                        if (seen != left) return false;
                        continue;
                    }

                    aboveAt[successorIndex - callIndex - 1] = left;
                    pending.Push((successorIndex, left));
                }
            }

            return reached;
        }

        // The values which a branch takes off the stack, which the walk counts before it hands the branch to the
        // instructions it leaves for: the effect of every other instruction is read off the instruction itself, and a
        // branch is counted from the case which it stands for rather than from the member it names.
        (int Taken, int Left)? BranchEffect(Instruction ins)
        {
            switch (ins.OpCode.Code)
            {
                case Code.Br or Code.Br_S or Code.Leave or Code.Leave_S:
                    return (0, 0);

                case Code.Brtrue or Code.Brtrue_S or Code.Brfalse or Code.Brfalse_S or Code.Switch:
                    return (1, 0);

                case Code.Beq or Code.Beq_S or Code.Bne_Un or Code.Bne_Un_S
                    or Code.Bge or Code.Bge_S or Code.Bge_Un or Code.Bge_Un_S
                    or Code.Bgt or Code.Bgt_S or Code.Bgt_Un or Code.Bgt_Un_S
                    or Code.Ble or Code.Ble_S or Code.Ble_Un or Code.Ble_Un_S
                    or Code.Blt or Code.Blt_S or Code.Blt_Un or Code.Blt_Un_S:
                    return (2, 0);

                default:
                    return null;
            }
        }

        // Whether the top of the stack holds the arguments which a call of a member is made with, which are the values
        // which were pushed last.
        bool TopOfStackMatches(MethodReference callMethod)
        {
            var invokeParameters = callMethod.Parameters;
            var count = invokeParameters.Count;
            if (paramStack.Types.Count < count) return false;
            // When the delegate is a generic instance, Invoke's parameters are open generic
            // parameters (!0,!1); resolve them to the actual generic arguments before comparing.
            var invokeGenericArguments = (callMethod.DeclaringType as GenericInstanceType)?.GenericArguments;
            var offset = paramStack.Types.Count - count;
            for (var i = 0; i < count; i++)
            {
                // The token of a generic method is parsed here as well, so that the parameter of the delegate is compared
                // as the generic parameter of the method which it stands for rather than as a type of Gneedle.Inject.
                var expected = ResolveDelegateParameterType(invokeParameters[i].ParameterType, invokeGenericArguments).ParseGenericTokens(Source, Source.Module);
                if (!StackTypeMatches(expected, paramStack.Types[offset + i], paramStack.Ins[offset + i])) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Whether every path which the body takes to an instruction passes through the one at <paramref name="callIndex"/>,
    /// which is what the value which that instruction reads being the one which the symbol left means.<para/>
    /// A path which reaches the instruction without going through the symbol is a path along which the value came from
    /// somewhere else, which is what the arms of a branch leave where each of them names a member of the same delegate
    /// type: the instruction after the join reads the value which the arm which ran left, which is the delegate of the
    /// member of that arm, so neither symbol stands for it and each of them writes the delegate of its own member. The
    /// walk starts where the runtime hands the control to the body, which is the instruction it begins with and the
    /// beginning of each of its handlers, and it reads no instruction past one which ends the path it stands on.
    /// </summary>
    /// <param name="bodyInstructions">The instructions of the body which is parsed.</param>
    /// <param name="callIndex">Index of the instruction which the symbol stands for.</param>
    /// <param name="target">Index of the instruction which reads the value.</param>
    /// <param name="targetDef">The template which the instructions belong to.</param>
    /// <returns>Whether the symbol stands on every path which reaches the instruction.</returns>
    private static bool TheSymbolIsOnEveryPathTo(IReadOnlyList<Instruction> bodyInstructions, int callIndex, int target, MethodDefinition targetDef)
    {
        var at = new Dictionary<Instruction, int>(bodyInstructions.Count);
        for (var i = 0; i < bodyInstructions.Count; i++) at[bodyInstructions[i]] = i;

        var visited = new bool[bodyInstructions.Count];
        var pending = new Stack<int>();

        // The body is entered where it begins, and a handler of it is entered where it begins as well, because the
        // runtime is what hands the control to both of them. An entry which is the instruction of the symbol itself is
        // left out rather than pushed: a path which begins at the symbol is one which passes through it, so walking
        // from there would ask whether the symbol stands on the paths which its own instruction leaves for, which is
        // not the question the walk is asked.
        void Enter(Instruction? entry)
        {
            if (entry != null && at.TryGetValue(entry, out var index) && index != callIndex) pending.Push(index);
        }

        if (bodyInstructions.Count > 0) Enter(bodyInstructions[0]);
        foreach (var handler in targetDef.Body.ExceptionHandlers)
        {
            Enter(handler.TryStart);
            Enter(handler.HandlerStart);
            Enter(handler.FilterStart);
        }

        while (pending.Count > 0)
        {
            var index = pending.Pop();
            if (visited[index]) continue;
            visited[index] = true;

            // The value which stands there was left by a path which the symbol stands on nowhere, so it is not the
            // value which the symbol left.
            if (index == target) return false;

            foreach (var successor in SuccessorsOf(bodyInstructions[index]))
            {
                if (at.TryGetValue(successor, out var next) && next != callIndex && !visited[next]) pending.Push(next);
            }
        }

        return true;
    }

    /// <summary>
    /// The instructions which an instruction hands a walk to: a branch leaves for the ones it names, and a branch which
    /// is taken in one of two cases leaves for the instruction after it as well. The operand is what tells the two
    /// apart, because an instruction which is not a branch carries an instruction as its operand nowhere.<para/>
    /// An instruction which ends the path it stands on hands the control to no instruction of the body at all: what
    /// stands after it is reached by nothing which runs, so a walk which read it would read a path the body never takes.
    /// </summary>
    /// <param name="ins">The instruction which is read.</param>
    /// <returns>The instructions which it hands the control to.</returns>
    private static IEnumerable<Instruction> SuccessorsOf(Instruction ins)
    {
        if (ins.Operand is Instruction target)
        {
            yield return target;
            if (ins.OpCode.FlowControl == FlowControl.Cond_Branch && ins.Next != null) yield return ins.Next;
            yield break;
        }

        if (ins.Operand is Instruction[] table)
        {
            foreach (var entry in table) yield return entry;
            if (ins.Next != null) yield return ins.Next;
            yield break;
        }

        if (ins.OpCode.Code is Code.Ret or Code.Throw or Code.Rethrow or Code.Jmp or Code.Endfinally or Code.Endfilter) yield break;

        if (ins.Next != null) yield return ins.Next;
    }

    /// <summary>
    /// The local which the value of a symbol is stored into, and the instruction which stores it, which is what a template
    /// which holds the delegate or the handle of the symbol writes where it would otherwise use it.
    /// </summary>
    /// <param name="bodyInstructions">The instructions of the body which is parsed.</param>
    /// <param name="callIndex">Index of the instruction of the call which the symbol stands for.</param>
    /// <returns>Index of the store and of the local which it writes, or null when the value is not stored into one.</returns>
    private static (int Store, int Local)? HeldLocal(IList<Instruction> bodyInstructions, int callIndex)
    {
        for (var i = callIndex + 1; i < bodyInstructions.Count; i++)
        {
            var ins = bodyInstructions[i];
            if (ins.OpCode == OpCodes.Nop) continue;

            return ins.TryGetStlocIndex(out var local) ? (i, local) : null;
        }

        return null;
    }

    /// <summary>
    /// Where the delegate which a local holds is invoked, which is every read of the local together with the instruction
    /// which invokes the delegate that the read is the receiver of.<para/>
    /// What the weaving writes for the symbol stands where each read stood and the delegate is never built, so a local
    /// which is read or written for anything else as well is one whose delegate stands for more than the invocation, and
    /// nothing is answered for it and the delegate is built into the local instead.
    /// </summary>
    /// <param name="bodyInstructions">The instructions of the body which is parsed.</param>
    /// <param name="local">Index of the local which holds the delegate.</param>
    /// <param name="delegateType">Type of the delegate which the symbol was parsed into.</param>
    /// <param name="targetDef">The template which the instructions belong to.</param>
    /// <returns>Index of every read of the local and of the invocation which it is the receiver of, or null when the
    /// local stands for more than the invocation of its delegate.</returns>
    private List<(int Read, int Invocation)>? InvocationsOfTheHeldDelegate(IReadOnlyList<Instruction> bodyInstructions, int local, TypeReference delegateType, MethodDefinition targetDef)
    {
        var invocations = new List<(int Read, int Invocation)>();
        var reads = 0;
        var stores = 0;

        for (var i = 0; i < bodyInstructions.Count; i++)
        {
            var ins = bodyInstructions[i];
            if (ins.TryGetStlocIndex(out var written) && written == local) stores++;

            if (!ins.TryGetLdlocIndex(out var read) || read != local) continue;

            reads++;
            if (!TryGetNextInvoke(bodyInstructions, i, delegateType, targetDef, out var invocation)) continue;

            invocations.Add((i, invocation));
        }

        return stores == 1 && invocations.Count == reads ? invocations : null;
    }

    /// <summary>
    /// Compare an expected parameter type against a type inferred from the evaluation stack.
    /// Integer-family types (bool/char/[s]byte/[u]short/int) are all loaded via <c>ldc.i4.*</c>
    /// and therefore indistinguishable on the stack, so they are treated as compatible.
    /// </summary>
    /// <param name="expected">The type which the call expects the value to be.</param>
    /// <param name="actual">The type of the value which the evaluation stack holds.</param>
    /// <param name="pushedBy">The instruction which pushed the value.</param>
    private bool StackTypeMatches(TypeReference expected, TypeReference actual, Instruction pushedBy)
        => TypeName.HasSameName(expected, actual)
            || (IsI4Compatible(expected) && IsI4Compatible(actual))
            // An enumeration is carried as the value under it, which is what a template which computes one out of an
            // integer leaves on the stack: there is no instruction which names the enumeration.
            || (IsI4Compatible(actual) && HasAnI4UnderlyingType(expected))
            // A value which was boxed is carried as the type it was boxed from, and it is the value which a call of a
            // parameter of `object` is made with rather than a reference of a type which names `object`.
            || (pushedBy.OpCode.Code == Code.Box && expected.MetadataType == MetadataType.Object);

    /// <summary>
    /// Whether the values of a type are carried by the stack as 4-byte integers, which is what an enumeration of an
    /// integer under it is, while a structure of the same width is carried as a value of its own type.
    /// </summary>
    /// <param name="type">The type which is read.</param>
    /// <returns>Whether the type is an enumeration which the stack carries as a 4-byte integer.</returns>
    private bool HasAnI4UnderlyingType(TypeReference type)
    {
        try
        {
            return type.ResolveDefinition(Source.Module) is {IsEnum: true} definition
                && definition.Fields.FirstOrDefault(field => field.Name == "value__") is { } value
                && IsI4Compatible(value.FieldType);
        }
        catch (AssemblyResolutionException)
        {
            // A type of an assembly which is not there is not one which the value on the stack can be told to be, and
            // the arguments are left to be compared by name, which the walk refuses rather than guesses.
            return false;
        }
    }

    /// <summary>
    /// Whether the type is represented as a 4-byte integer on the CLR evaluation stack,
    /// i.e. loaded via the <c>ldc.i4.*</c> opcodes and thus not distinguishable by opcode alone.
    /// </summary>
    private static bool IsI4Compatible(TypeReference type)
        => type.MetadataType is MetadataType.Boolean
                             or MetadataType.Char
                             or MetadataType.SByte
                             or MetadataType.Byte
                             or MetadataType.Int16
                             or MetadataType.UInt16
                             or MetadataType.Int32
                             or MetadataType.UInt32;

    /// <summary>
    /// Consider how the instruction should pop from or push into <c>paramStack</c>.
    /// </summary>
    /// <param name="ins">Balance target.</param>
    /// <param name="paramStack">Parameter stack of current method body scanning.</param>
    /// <param name="localStack"></param>
    /// <param name="targetDef">The template method which the instructions are copied from.</param>
    /// <exception cref="ArgumentException"></exception>
    private void BalanceStack(Instruction ins, ParameterStack paramStack, TypeReference[] localStack, MethodDefinition targetDef)
    {
        // When any method call, the parameters stack should reduce by the same amount as the method parameters count.
        if ((ins.OpCode == OpCodes.Callvirt || ins.OpCode == OpCodes.Call) && ins.Operand is MethodReference callMethod)
        {
            // Pop elements with method parameter count.
            paramStack.Pop(callMethod.Resolve().IsStatic
                // Static method just pops the elements with parameter count.
                ? callMethod.Parameters.Count
                // +1 for the ldarg or other loading code.
                : callMethod.Parameters.Count + 1);
        }

        // An instruction which is handed a value and leaves another in its place takes the value it was handed off the
        // stack: what a conversion converts, what a cast casts and what a read reads out of is not left under the value
        // which is left, and the arguments of a call are the values which were pushed last, so a value left under them
        // would be read in their place.
        if (LeavesAValueInPlaceOfTheOneItIsHanded(ins) && paramStack.Types.Count > 0) paramStack.Pop(1);

        // When the instruction push any variable to the method stack, it should be appended to the parameters stack.
        if (TryGetStackType(ins, targetDef, out var type))
        {
            // Sanity check.
            if (type == null)
            {
                throw new ArgumentException(string.Format(ErrorMessages.INVALID_INSTRUCTION_METHOD, ins, new TypeName(Source.DeclaringType), Source.Name));
            }

            paramStack.Push(ins, type);
        }
        // The final case is stloc/ldloc pattern.
        // We got stloc operand from previous instructions, and we push ldloc operand with same index to the parameters stack.
        else if (ins.TryGetStlocIndex(out var stIndex))
        {
            localStack[stIndex] = paramStack.Pop().Type;
        }
        else if (ins.TryGetLdlocIndex(out var ldIndex))
        {
            // A local keeps its value until it is stored over, which is what makes it a local: the read leaves the
            // local where it is, so a body which reads the same local twice hands the same type to both reads. A local
            // whose type nothing has recorded yet leaves the stack as it is, as every other instruction whose result
            // the walk cannot tell does, rather than putting a value of no type on the stack for a comparison to read.
            if (localStack[ldIndex] is { } localType)
            {
                paramStack.Push(ins, localType);
            }
        }
    }

    /// <summary>
    /// Whether an instruction leaves a value of a type which is not the type of the value it is handed, which is what
    /// the conversions, the casts, the boxes and the reads do.
    /// </summary>
    /// <param name="ins">The instruction which is read.</param>
    /// <returns>Whether the instruction leaves another value in place of the one it is handed.</returns>
    private static bool LeavesAValueInPlaceOfTheOneItIsHanded(Instruction ins) => ins.OpCode.Code is
        Code.Conv_I1 or Code.Conv_I2 or Code.Conv_I4 or Code.Conv_I8 or Code.Conv_U1 or Code.Conv_U2 or Code.Conv_U4
     or Code.Conv_U8 or Code.Conv_I or Code.Conv_U or Code.Conv_R4 or Code.Conv_R8 or Code.Conv_R_Un
     or Code.Conv_Ovf_I1 or Code.Conv_Ovf_I2 or Code.Conv_Ovf_I4 or Code.Conv_Ovf_I8 or Code.Conv_Ovf_U1
     or Code.Conv_Ovf_U2 or Code.Conv_Ovf_U4 or Code.Conv_Ovf_U8 or Code.Conv_Ovf_I_Un or Code.Conv_Ovf_U_Un
     or Code.Box or Code.Unbox or Code.Unbox_Any or Code.Castclass or Code.Isinst
     or Code.Ldfld or Code.Ldflda or Code.Ldind_I1 or Code.Ldind_I2 or Code.Ldind_I4 or Code.Ldind_I8
     or Code.Ldind_I or Code.Ldind_R4 or Code.Ldind_R8 or Code.Ldind_Ref or Code.Ldind_U1 or Code.Ldind_U2
     or Code.Ldind_U4;

    /// <summary>
    /// The type of the value which an instruction leaves on the stack, which is read off the instruction itself where
    /// the instruction writes that type into it, and off the member the instruction loads where it reads one.
    /// </summary>
    /// <param name="ins">The instruction which is read.</param>
    /// <param name="targetDef">The template which the instruction belongs to.</param>
    /// <param name="type">The type of the value which the instruction leaves, or null when it leaves none.</param>
    /// <returns>Whether the instruction leaves a value on the stack, <see cref="void"/> being none.</returns>
    private bool TryGetStackType(Instruction ins, MethodDefinition targetDef, out TypeReference? type)
    {
        var module = Source.Module;
        var typeSystem = module.TypeSystem;
        var code = ins.OpCode.Code;

        type = code switch
        {
            Code.Ldc_I4_M1 => typeSystem.Int32,  // Int32
            Code.Ldc_I4_0  => typeSystem.Int32,  // Int32
            Code.Ldc_I4_1  => typeSystem.Int32,  // Int32
            Code.Ldc_I4_2  => typeSystem.Int32,  // Int32
            Code.Ldc_I4_3  => typeSystem.Int32,  // Int32
            Code.Ldc_I4_4  => typeSystem.Int32,  // Int32
            Code.Ldc_I4_5  => typeSystem.Int32,  // Int32
            Code.Ldc_I4_6  => typeSystem.Int32,  // Int32
            Code.Ldc_I4_7  => typeSystem.Int32,  // Int32
            Code.Ldc_I4_8  => typeSystem.Int32,  // Int32
            Code.Ldc_I4_S  => typeSystem.Int32,  // Int32
            Code.Ldc_I8    => typeSystem.Int64,  // Int64
            Code.Ldstr     => typeSystem.String, // String
            Code.Ldc_R4    => typeSystem.Single, // Single
            Code.Ldc_R8    => typeSystem.Double, // Double
            Code.Ldnull    => typeSystem.Object, // Null
            // The conversions which an argument of a type other than the one which was computed is handed over
            // through: the value is left of the type which was converted to, and the narrow ones are all carried as
            // 4-byte integers whichever of them they are.
            Code.Conv_I1 or Code.Conv_I2 or Code.Conv_I4 or Code.Conv_U1 or Code.Conv_U2 or Code.Conv_U4
             or Code.Conv_Ovf_I1 or Code.Conv_Ovf_I2 or Code.Conv_Ovf_I4 or Code.Conv_Ovf_U1
             or Code.Conv_Ovf_U2 or Code.Conv_Ovf_U4 or Code.Conv_Ovf_I_Un or Code.Conv_Ovf_U_Un => typeSystem.Int32, // Conv, narrow
            Code.Conv_I8 or Code.Conv_Ovf_I8 => typeSystem.Int64,                                                     // Conv, Int64
            Code.Conv_U8 or Code.Conv_Ovf_U8 => typeSystem.UInt64,                                                    // Conv, UInt64
            Code.Conv_I                      => typeSystem.IntPtr,                                                    // Conv, native
            Code.Conv_U                      => typeSystem.UIntPtr,                                                   // Conv, native
            Code.Conv_R4                     => typeSystem.Single,                                                    // Conv, Single
            Code.Conv_R8 or Code.Conv_R_Un   => typeSystem.Double,                                                    // Conv, Double
            // The instructions which leave the type they name, which is the type of the value they leave: what is
            // looked for when the argument is the value which a call hands over.
            Code.Box or Code.Unbox_Any or Code.Castclass or Code.Isinst when ins.Operand is TypeReference cast => cast,            // Cast
            Code.Ldfld or Code.Ldsfld when ins.Operand is FieldReference field                                 => field.FieldType, // Field
            // The instructions which load the address of an argument or of a local rather than the value it holds, which
            // is what a template writes where it hands one to a member by `ref` or `out`: what stands on the stack is an
            // address of that type rather than a value of it, which is the type the delegate declares the argument as.
            Code.Ldarga or Code.Ldarga_S or Code.Ldloca or Code.Ldloca_S                           => GetAddressType(ins, targetDef),     // Address
            Code.Newobj when ins.Operand is MethodReference ctor                                   => ctor.DeclaringType,                 // Newobj
            Code.Call or Code.Callvirt or Code.Ldftn when ins.Operand is MethodReference methodRef => ResolveMethodReturnType(methodRef), // Call
            _                                                                                      => GetArgType(ins, targetDef)          // Args
        };

        return type != null && type != typeSystem.Void;
    }

    /// <summary>
    /// The type of the argument which an instruction loads, which is the type of the parameter at the position it
    /// loads, or the type which declares the template when it loads the receiver.
    /// </summary>
    /// <param name="instruction">The instruction which loads the argument.</param>
    /// <param name="targetDef">The template which the instruction belongs to, whose parameters and staticness the position is read against.</param>
    /// <returns>The type of the argument, or null when the instruction loads none.</returns>
    private TypeReference? GetArgType(Instruction instruction, MethodDefinition targetDef)
    {
        var isStatic = targetDef.IsStatic;
        if (!instruction.TryGetLdargIndex(!isStatic, out var slot)) return null;
        if (!isStatic && slot == 0) return targetDef.DeclaringType;

        // The parameter of a template is a token when it stands for a generic parameter of the method being woven, just
        // as the parameter of a delegate is, so it is parsed to that parameter before the type is compared with anything.
        // The load may name a slot which the template holds no parameter for, which is a body the weaving refuses with a
        // message of its own rather than a type to compare against.
        return ArgumentAt(slot, targetDef)?.ParseGenericTokens(Source, Source.Module);
    }

    /// <summary>
    /// The type of the address which an instruction which reads the address of a value leaves on the stack, which is the
    /// type of the value that the address is of, by reference.
    /// </summary>
    /// <param name="ins">The instruction which reads the address.</param>
    /// <param name="targetDef">The template which the instruction belongs to, whose arguments and locals the operand names.</param>
    /// <returns>The type of the address, or null when the operand names no argument and no local.</returns>
    private TypeReference? GetAddressType(Instruction ins, MethodDefinition targetDef)
    {
        var readsAnArgument = ins.OpCode.Code is Code.Ldarga or Code.Ldarga_S;
        var addressed = ins.Operand switch
        {
            VariableReference local                                          => local.VariableType,
            ParameterReference argument                                      => argument.ParameterType,
            int slot when readsAnArgument                                    => ArgumentAt(slot, targetDef),
            int slot when slot >= 0 && slot < targetDef.Body.Variables.Count => targetDef.Body.Variables[slot].VariableType,
            _                                                                => null
        };

        return addressed is { } type ? new ByReferenceType(type.ParseGenericTokens(Source, Source.Module)) : null;
    }

    /// <summary>
    /// The type of the argument which a slot names, the receiver being the slot which is taken first where the template
    /// belongs to an instance.
    /// </summary>
    /// <param name="slot">Slot of the argument.</param>
    /// <param name="targetDef">The template whose arguments the slot is read against.</param>
    /// <returns>The type of the argument, or null when the slot names none of them.</returns>
    private static TypeReference? ArgumentAt(int slot, MethodDefinition targetDef)
    {
        var position = slot - (targetDef.IsStatic ? 0 : 1);
        return position >= 0 && position < targetDef.Parameters.Count ? targetDef.Parameters[position].ParameterType : null;
    }

    /// <summary>
    /// What a call hands back, with the generic return of a generic method, and of a method of a generic type, resolved
    /// to the argument which the call was given.
    /// </summary>
    /// <param name="methodRef">The method which the call reads.</param>
    /// <returns>The type of the value which the call leaves on the stack.</returns>
    private static TypeReference ResolveMethodReturnType(MethodReference methodRef)
    {
        if (methodRef.ReturnType is not GenericParameter parameter) return methodRef.ReturnType;
        if (methodRef is GenericInstanceMethod genericMethod)
            return genericMethod.GenericArguments[parameter.Position];
        if (methodRef.DeclaringType is GenericInstanceType genericType)
            return genericType.GenericArguments[parameter.Position];
        return methodRef.ReturnType;
    }

    /// <summary>
    /// Resolve a delegate Invoke parameter type. When the delegate is a generic instance,
    /// open generic parameters (e.g. T1) are mapped to the actual generic arguments (e.g. Int32).
    /// </summary>
    private static TypeReference ResolveDelegateParameterType(TypeReference parameterType, Mono.Collections.Generic.Collection<TypeReference>? genericArguments)
        => parameterType is GenericParameter parameter && genericArguments != null && parameter.Position < genericArguments.Count
            ? genericArguments[parameter.Position]
            : parameterType;

    /// <summary>
    /// The stack which the body being parsed builds as it is walked, which holds the type of every value that is pushed
    /// so that the arguments a call is made with can be compared with the parameters of the member it calls.<para/>
    /// The values themselves are of no interest, and the instruction which pushed each of them is kept only so that a
    /// value which is read out of the stack again can be told apart from one which was never on it.
    /// </summary>
    private class ParameterStack
    {
        /// <summary>
        /// The instruction which pushed each of the values, in the order they were pushed.
        /// </summary>
        public readonly List<Instruction> Ins = [];

        /// <summary>
        /// The type of each of the values, in the order they were pushed, which is the order of <see cref="Ins"/>.
        /// </summary>
        public readonly List<TypeReference> Types = [];

        /// <summary>
        /// Put a value of a type on top of the stack.
        /// </summary>
        /// <param name="ins">The instruction which pushed it.</param>
        /// <param name="type">The type of the value.</param>
        public void Push(Instruction ins, TypeReference type)
        {
            Ins.Add(ins);
            Types.Add(type);
        }

        /// <summary>
        /// Take the values which were pushed last off the stack.
        /// </summary>
        /// <param name="count">How many values to take off.</param>
        public void Pop(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Ins.RemoveAt(Ins.Count - 1);
                Types.RemoveAt(Types.Count - 1);
            }
        }

        /// <summary>
        /// Take the value which was pushed last off the stack.
        /// </summary>
        /// <returns>The instruction which pushed the value and the type of it.</returns>
        public (Instruction Ins, TypeReference Type) Pop()
        {
            var result = (Ins[Ins.Count - 1], Types[Types.Count - 1]);
            Ins.RemoveAt(Ins.Count - 1);
            Types.RemoveAt(Types.Count - 1);
            return result;
        }
    }
}