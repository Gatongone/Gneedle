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
    /// <exception cref="InvalidILException">Thrown when the nearest 'callvirt' to `Ldstr {field_name}` doesn't exist.</exception>
    /// <exception cref="ArgumentException">Thrown when the method is invalid.</exception>
    private void ParseMethod(string memberName, MemberSymbols memberSymbol, int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        if ((filter.Target[currentIndex + 1].Operand as GenericInstanceMethod)?.GenericArguments.FirstOrDefault() is not { } delegateRef)
        {
            throw new InvalidILException();
        }

        if (delegateRef is not TypeDefinition delegateDef)
        {
            delegateDef = DeclaringTypeHandler.AssemblyHandler.GetCecilType(delegateRef).Definition;
        }

        // Generic instance arguments used to resolve open generic parameters in the Invoke signature.
        var genericArguments = (delegateRef as GenericInstanceType)?.GenericArguments;

        // Get method parameter from delegation. When the delegate is a generic instance (e.g. Func<int,int,int>),
        // the Invoke signature uses open generic parameters (T1,T2), so we map them to the actual arguments.
        var parameters = delegateDef.Methods
                                    .First(method => method.Name.Equals("Invoke"))
                                    .Parameters
                                    .Select(p => Source.Module.ImportReference(ResolveDelegateParameterType(p.ParameterType, genericArguments)))
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
            filter.Replace(callvirtIndex, Instruction.Create(methodDef.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, methodDef));
        }
        // Or create delegate by method pointer and call it right now.
        else
        {
            var importedMethod = Source.Module.ImportReference(methodDef);
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

            return DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(instanceType.Resolve(), (string) instructions[currentIndex].Operand, parameters);
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
        bool MatchTargetInvoke(Instruction ins)
            => (ins.OpCode == OpCodes.Callvirt || ins.OpCode == OpCodes.Call)   // It's not double that the instruction must be 'call' type.
                && ins.Operand is MethodReference {Name: "Invoke"} callMethod   // We only check the 'invoke' method from Delegate.
                && TypeName.HasSameName(delegateType, callMethod.DeclaringType) // Make sure declaring types are the same.
                && TopOfStackMatches(callMethod);                               // Make sure the top-of-stack types match the invoke parameters.

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
                var expected = ResolveDelegateParameterType(invokeParameters[i].ParameterType, invokeGenericArguments);
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

    private TypeReference? GetArgType(Instruction instruction, MethodDefinition targetDef)
    {
        var isStatic = targetDef.IsStatic;
        if (instruction.OpCode == OpCodes.Ldarg_S && instruction.Operand is ParameterReference parameter) return parameter.ParameterType;
        if (!instruction.TryGetLdargIndex(out var index)) return null;
        return !isStatic && index == 0
            ? targetDef.DeclaringType
            : targetDef.Parameters[index + (isStatic ? 0 : -1)].ParameterType;
    }

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

    private class ParameterStack
    {
        public readonly List<Instruction>   Ins   = [];
        public readonly List<TypeReference> Types = [];

        public void Push(Instruction ins, TypeReference type)
        {
            Ins.Add(ins);
            Types.Add(type);
        }

        public void Pop(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Ins.RemoveAt(Ins.Count - 1);
                Types.RemoveAt(Types.Count - 1);
            }
        }

        public (Instruction Ins, TypeReference Type) Pop()
        {
            var result = (Ins[Ins.Count - 1], Types[Types.Count - 1]);
            Ins.RemoveAt(Ins.Count - 1);
            Types.RemoveAt(Types.Count - 1);
            return result;
        }
    }
}