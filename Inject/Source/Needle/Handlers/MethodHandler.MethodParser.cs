namespace Gneedle.Inject;

partial class MethodHandler
{
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
    /// <param name="currentIndex">Index of the instruction of `ldstr {member_name}`.</param>
    /// <param name="filter">The final instruction's container.</param>
    /// <param name="targetDef">The template method which the instructions are copied from.</param>
    /// <exception cref="InvalidILException">Thrown when the instructions around the name of the member are not the call which it stands for.</exception>
    /// <exception cref="ArgumentException">Thrown when the method is invalid.</exception>
    private void ParseMethod(string memberName, MemberSymbols memberSymbol, int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        if ((filter.Target[currentIndex + 1].Operand as GenericInstanceMethod)?.GenericArguments.FirstOrDefault() is not { } delegateRef)
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

        // Detect Object.Method with new Object(param) syntax: need to skip the array init sequence.
        var skipArrayInitCount = 0;
        if (memberSymbol.HasFlag(MemberSymbols.Object) && currentIndex >= 1)
        {
            var prevIns = filter.Target[currentIndex - 1];
            if (prevIns.OpCode == OpCodes.Newobj && prevIns.Operand is MethodReference { Name: ".ctor", DeclaringType: var declType }
                && declType.FullName == Object.TYPE_NAME)
            {
                // Verify the full array init pattern exists.
                var baseIdx = currentIndex - 7;
                if (baseIdx >= 0
                    && filter.Target[baseIdx].OpCode.Code == Code.Ldc_I4_1
                    && filter.Target[baseIdx + 1].OpCode == OpCodes.Newarr
                    && filter.Target[baseIdx + 2].OpCode == OpCodes.Dup
                    && filter.Target[baseIdx + 3].OpCode.Code == Code.Ldc_I4_0
                    && (filter.Target[baseIdx + 4].OpCode.Code is Code.Ldarg or Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg_S)
                    && filter.Target[baseIdx + 5].OpCode == OpCodes.Stelem_Ref)
                {
                    skipArrayInitCount = 7; // ldc.i4.1 through newobj
                }
            }
        }

        // Detect Static.Method with Static.From("typename"): need to skip ldstr+call Static::From.
        var skipStaticFromCount = 0;
        if (memberSymbol.HasFlag(MemberSymbols.Static) && currentIndex >= 2)
        {
            var callFromIns = filter.Target[currentIndex - 1];
            if (callFromIns.OpCode == OpCodes.Call && callFromIns.Operand is MethodReference { Name: "From", DeclaringType: var declType }
                && declType.FullName == Static.TYPE_NAME)
            {
                var ldstrIns = filter.Target[currentIndex - 2];
                if (ldstrIns.OpCode == OpCodes.Ldstr)
                {
                    skipStaticFromCount = 2; // ldstr + call Static::From
                }
            }
        }

        // var methodDef = memberSymbol.HasFlag(MemberSymbols.Base) ? GetMethodInBase(memberName, parameters) : GetMethodInThis(memberName, parameters);
        var methodDef = GetMethod(memberSymbol, memberName, currentIndex, filter.Target, parameters, targetDef);
        if (methodDef == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, memberName));
        }

        // If there is `Invoke` method of the  target delegate is in following instructions, then replace it to the actual method calling.
        if (TryGetNextInvoke(filter.Target, currentIndex + 1, delegateRef, targetDef, out var callvirtIndex))
        {
            // Skip the array init sequence if this is Object.Method with new Object(param).
            if (skipArrayInitCount > 0)
            {
                for (var i = currentIndex - skipArrayInitCount; i < currentIndex; i++)
                {
                    filter.Skip(i);
                }
            }

            // Skip the Static.From sequence if this is Static.Method.
            if (skipStaticFromCount > 0)
            {
                for (var i = currentIndex - skipStaticFromCount; i < currentIndex; i++)
                {
                    filter.Skip(i);
                }
            }

            if (!methodDef.IsStatic)
            {
                // ldstr {method_name} -> ldarg.0
                filter.Replace(currentIndex, Instruction.Create(OpCodes.Ldarg_0));
            }
            else
            {
                // ldstr {method_name} -> nop
                filter.Skip(currentIndex);
            }

            // Skip `call [Gneedle.Inject]Gneedle.Inject.This::Method<class {delegate_type}>({parameter_types})`
            filter.Skip(currentIndex + 1);
            // callvirt instance class {delegate_type}::Invoke({parameter_types}) -> callvirt/call instance class {declaring_type}::{method_name}({parameter_types})
            filter.Replace(callvirtIndex, Instruction.Create(methodDef.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, GetCallableReference(methodDef)));
        }
        // Or create delegate by method pointer and call it right now.
        else
        {
            var importedMethod = GetCallableReference(methodDef);
            var delegateCtor = Source.Module.ImportReference(delegateDef.GetConstructors().FirstOrDefault());

            if (!methodDef.IsStatic)
            {
                // ldstr {method_name} -> ldarg.0
                filter.Replace(currentIndex, Instruction.Create(OpCodes.Ldarg_0));
                // call [Gneedle.Inject]Gneedle.Inject.This::Method<class {delegate_type}>({parameter_types}) -> ldftn {return_type} {declaring_type}::{method_name}({parameter_types})
                filter.Replace(currentIndex + 1, Instruction.Create(OpCodes.Ldftn, importedMethod));
                // insert `newobj System.Void {delegate_type}::.ctor({parameter_types})`
                filter.Insert(currentIndex + 2, Instruction.Create(OpCodes.Newobj, delegateCtor));
            }
            else
            {
                // ldstr {method_name} -> ldftn {return_type} {declaring_type}::{method_name}({parameter_types})
                filter.Replace(currentIndex, Instruction.Create(OpCodes.Ldftn, importedMethod));
                // call [Gneedle.Inject]Gneedle.Inject.This::Method<class {delegate_type}>({parameter_types}) -> newobj System.Void {delegate_type}::.ctor({parameter_types})
                filter.Replace(currentIndex + 1, Instruction.Create(OpCodes.Newobj, delegateCtor));
            }
        }
    }

    /// <summary>
    /// Get the reference which the instruction of the body has to hold to call <paramref name="methodDef"/>.
    /// </summary>
    /// <remarks>
    /// A generic method is called through a method specification rather than through the definition itself, because the
    /// call instruction has to name the generic arguments of the call. The template named them by the
    /// <c>Gneedle.Inject.M_[0-20]</c> tokens which were resolved to the generic parameters of the method being woven, so
    /// those parameters are the arguments here. The generic method which the template proceeds through is a generated
    /// one which declares a parameter of every position, so the instantiation is the one a compiler emits for a call to
    /// a method of the generic parameters of its own caller.
    /// </remarks>
    /// <param name="methodDef">The method which the body calls.</param>
    /// <returns>The reference which the call instruction holds.</returns>
    private MethodReference GetCallableReference(MethodDefinition methodDef)
    {
        var importedMethod = Source.Module.ImportReference(methodDef);

        // A method which holds fewer or more generic parameters than the method being woven cannot be instantiated from
        // the template, so it is left as the plain reference it was, which is what the call held before.
        if (methodDef.GenericParameters.Count != Source.GenericParameters.Count) return importedMethod;

        var genericInstance = new GenericInstanceMethod(importedMethod);
        foreach (var genericParameter in Source.GenericParameters) genericInstance.GenericArguments.Add(genericParameter);
        return genericInstance;
    }

    /// <summary>
    /// The method which a symbol of a template stands for: a member of the type being woven, a member of its base type,
    /// a member of the instance the template names, a member of the type it names statically, or the method which holds
    /// the body that was taken over.<para/>
    /// The instructions and the position of the symbol among them are read as well, because the instance which a member
    /// reached through <see cref="MemberSymbols.Object"/> or through <see cref="MemberSymbols.Static"/> belongs to is
    /// written in the instruction before the name, and the type of it is what the member is looked up on.
    /// </summary>
    /// <param name="memberSymbol">The symbol which the template reached the member through.</param>
    /// <param name="methodName">Name of the member.</param>
    /// <param name="currentIndex">Index of the instruction which loads the name of the member.</param>
    /// <param name="instructions">The instructions of the body which is parsed.</param>
    /// <param name="parameters">The types of the arguments which the member is called with, which the member that is found has to be described by.</param>
    /// <param name="targetDef">The template which the instructions are read out of.</param>
    /// <returns>The method which the symbol stands for, or null when the symbol is not one which names a method.</returns>
    /// <exception cref="ArgumentException">Thrown when the member cannot be resolved, or when the template proceeds without a body being woven around.</exception>
    private MethodDefinition? GetMethod(MemberSymbols memberSymbol, string methodName, int currentIndex, IReadOnlyList<Instruction> instructions, IReadOnlyList<TypeReference> parameters, MethodDefinition targetDef)
    {
        if (memberSymbol.HasFlag(MemberSymbols.Base))
        {
            return DeclaringTypeHandler.GetMethodInBase(methodName, parameters);
        }

        if (memberSymbol.HasFlag(MemberSymbols.This))
        {
            return DeclaringTypeHandler.GetMethodInThis(methodName, parameters);
        }

        if (memberSymbol.HasFlag(MemberSymbols.Proceed))
        {
            // The template proceeds without a body being woven around, which is a mistake of its own rather than a
            // method which could not be found.
            if (m_ProceedMethodName == null)
            {
                throw new ArgumentException(string.Format(ErrorMessages.PROCEED_WITHOUT_AROUND_BODY, methodName));
            }

            // The declaring type alone is searched, rather than the base types which GetMethodInThis walks: the body
            // which was moved belongs to a method of the declaring type, and a method of a base type of the same name
            // and signature is a different one.
            return Source.DeclaringType.Methods.FirstOrDefault(
                methodDef => methodDef.Name == m_ProceedMethodName && methodDef.Parameters.SameWith(parameters.ToArray()));
        }

        if (memberSymbol.HasFlag(MemberSymbols.Object))
        {
            // The instruction before ldstr memberName may be:
            // 1. ldarg (direct parameter use) or
            // 2. newobj Object::.ctor(object[]) — from new Object(instance) syntax.
            //    Pattern: ldc.i4.1, newarr Object, dup, ldc.i4.0, ldarg.X, stelem.ref, newobj
            var prevIns = instructions[currentIndex - 1];
            Instruction? instanceIns = null;

            if (prevIns.OpCode == OpCodes.Newobj && prevIns.Operand is MethodReference { Name: ".ctor", DeclaringType: var declType }
                && declType.FullName == Object.TYPE_NAME)
            {
                // Backtrack to find the ldarg in the array initializer sequence.
                // Expected: [currentIndex-7] ldc.i4.1, [-6] newarr, [-5] dup, [-4] ldc.i4.0, [-3] ldarg.X, [-2] stelem.ref, [-1] newobj
                var baseIdx = currentIndex - 7;
                if (baseIdx >= 0
                    && instructions[baseIdx].OpCode.Code == Code.Ldc_I4_1
                    && instructions[baseIdx + 1].OpCode == OpCodes.Newarr
                    && instructions[baseIdx + 2].OpCode == OpCodes.Dup
                    && instructions[baseIdx + 3].OpCode.Code == Code.Ldc_I4_0
                    && instructions[baseIdx + 4].OpCode.Code is Code.Ldarg or Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg_S
                    && instructions[baseIdx + 5].OpCode == OpCodes.Stelem_Ref)
                {
                    instanceIns = instructions[baseIdx + 4]; // The ldarg
                }
            }
            else if (prevIns.OpCode.Code is Code.Ldarg or Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg_S)
            {
                instanceIns = prevIns;
            }

            var argType = (instanceIns != null ? GetArgType(instanceIns, targetDef) : null)
                          ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, methodName));

            // A token as the instance type is just another spelling of the generic parameter of the injected method or of
            // its declaring type, so it is resolved exactly like a real generic parameter: the method is looked up on the
            // constraints of the generic parameter. The generic parameter itself has no definition to look the method up on
            // (Cecil's GenericParameter.Resolve() returns null), and a call on an unconstrained one would be invalid IL.
            var instanceType = argType.TryGetParsedGenericParameter(Source, out var genericParameter) ? genericParameter! : argType;

            if (instanceType is GenericParameter parameter)
            {
                return GetMethodFromConstraint(parameter, methodName, parameters);
            }

            // The instance type may stand for the type of another assembly, in which case the method is looked up on the real one.
            return DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(instanceType.ResolveDefinition(Source.Module), (string) instructions[currentIndex].Operand, parameters);
        }

        if (memberSymbol.HasFlag(MemberSymbols.Static))
        {
            // Static.From("FullTypeName").Method<D>("methodName") pattern:
            // [currentIndex-2]: ldstr "FullTypeName"
            // [currentIndex-1]: call Static::From
            // [currentIndex]:   ldstr "methodName"
            if (currentIndex < 2) throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, methodName));
            var typeNameIns = instructions[currentIndex - 2];
            if (typeNameIns.OpCode != OpCodes.Ldstr || typeNameIns.Operand is not string fullTypeName)
            {
                throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, methodName));
            }

            // Resolve the type using GetCecilType (checks cache + Type.GetType reflection + current assembly).
            var staticType = DeclaringTypeHandler.AssemblyHandler.GetCecilType(fullTypeName).Definition;
            return DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(staticType, (string) instructions[currentIndex].Operand, parameters);
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
    /// instruction after the symbol that calls a member of that delegate's type with the arguments the stack holds.<para/>
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
            if (i > callIndex && MatchTargetInvoke(ins))
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

        // The delegate Invoke expects the stack to hold [delegate-receiver, arg1, ...argN].
        // We only compare the top N entries (the arguments) against the delegate's Invoke
        // parameters, ignoring the receiver sitting below them.
        /// <summary>
        /// Whether an instruction is the one which invokes the delegate that is looked for, which is a call of the
        /// <c>Invoke</c> of that delegate's type with the arguments the stack holds the types of.
        /// </summary>
        /// <param name="ins">The instruction which is asked about.</param>
        /// <returns>Whether the instruction invokes the delegate.</returns>
        bool MatchTargetInvoke(Instruction ins)
            => (ins.OpCode == OpCodes.Callvirt || ins.OpCode == OpCodes.Call)   // It's not double that the instruction must be 'call' type.
                && ins.Operand is MethodReference {Name: "Invoke"} callMethod   // We only check the 'invoke' method from Delegate.
                && TypeName.HasSameName(delegateType, callMethod.DeclaringType) // Make sure declaring types are the same.
                && TopOfStackMatches(callMethod);                               // Make sure the top-of-stack types match the invoke parameters.

        /// <summary>
        /// Whether the top of the stack holds the arguments which a call of a member is made with.
        /// </summary>
        /// <param name="callMethod">The member which the call reads.</param>
        /// <returns>Whether the values which were pushed last are the arguments of the call.</returns>
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
                if (!StackTypeMatches(expected, paramStack.Types[offset + i])) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Compare an expected parameter type against a type inferred from the evaluation stack.
    /// Integer-family types (bool/char/[s]byte/[u]short/int) are all loaded via <c>ldc.i4.*</c>
    /// and therefore indistinguishable on the stack, so they are treated as compatible.
    /// </summary>
    private static bool StackTypeMatches(TypeReference expected, TypeReference actual) => TypeName.HasSameName(expected, actual) || (IsI4Compatible(expected) && IsI4Compatible(actual));

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
            paramStack.Push(ins, localStack[ldIndex]);
            localStack[ldIndex] = null!;
        }
    }

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
            Code.Ldc_I4_M1                                                                         => typeSystem.Int32,                   // Int32
            Code.Ldc_I4_0                                                                          => typeSystem.Int32,                   // Int32
            Code.Ldc_I4_1                                                                          => typeSystem.Int32,                   // Int32
            Code.Ldc_I4_2                                                                          => typeSystem.Int32,                   // Int32
            Code.Ldc_I4_3                                                                          => typeSystem.Int32,                   // Int32
            Code.Ldc_I4_4                                                                          => typeSystem.Int32,                   // Int32
            Code.Ldc_I4_5                                                                          => typeSystem.Int32,                   // Int32
            Code.Ldc_I4_6                                                                          => typeSystem.Int32,                   // Int32
            Code.Ldc_I4_7                                                                          => typeSystem.Int32,                   // Int32
            Code.Ldc_I4_8                                                                          => typeSystem.Int32,                   // Int32
            Code.Ldc_I4_S                                                                          => typeSystem.Int32,                   // Int32
            Code.Ldc_I8                                                                            => typeSystem.Int64,                   // Int64
            Code.Ldstr                                                                             => typeSystem.String,                  // String
            Code.Ldc_R4                                                                            => typeSystem.Single,                  // Single
            Code.Ldc_R8                                                                            => typeSystem.Double,                  // Double
            Code.Ldfld or Code.Ldsfld when ins.Operand is FieldReference field                     => field.FieldType,                    // Field
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
        if (instruction.OpCode == OpCodes.Ldarg_S && instruction.Operand is ParameterReference parameter)
        {
            return parameter.ParameterType.ParseGenericTokens(Source, Source.Module);
        }

        if (!instruction.TryGetLdargIndex(out var index)) return null;
        // The parameter of a template is a token when it stands for a generic parameter of the method being woven, just
        // as the parameter of a delegate is, so it is parsed to that parameter before the type is compared with anything.
        return (!isStatic && index == 0
            ? targetDef.DeclaringType
            : targetDef.Parameters[index + (isStatic ? 0 : -1)].ParameterType).ParseGenericTokens(Source, Source.Module);
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
        public readonly List<Instruction>   Ins   = [];

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