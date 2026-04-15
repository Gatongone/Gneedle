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
    private void ParseMethod(string memberName, MemberSymbols memberSymbol, int currentIndex, InstructionFilter filter)
    {
        if ((filter.Target[currentIndex + 1].Operand as GenericInstanceMethod)?.GenericArguments.FirstOrDefault() is not { } delegateRef)
        {
            throw new InvalidILException();
        }

        if (delegateRef is not TypeDefinition delegateDef)
        {
            delegateDef = DeclaringTypeHandler.AssemblyHandler.GetCecilType(delegateRef).Definition;
        }

        // Get method parameter from delegation.
        var parameters = delegateDef.Methods
                                    .First(method => method.Name.Equals("Invoke"))
                                    .Parameters
                                    .Select(p => Source.Module.ImportReference(p.ParameterType))
                                    .ToArray();

        // var methodDef = memberSymbol.HasFlag(MemberSymbols.Base) ? GetMethodInBase(memberName, parameters) : GetMethodInThis(memberName, parameters);
        var methodDef = GetMethod(memberSymbol, memberName, currentIndex, filter.Target, parameters);
        if (methodDef == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, memberName));
        }

        // If there is `Invoke` method of the  target delegate is in following instructions, then replace it to the actual method calling.
        if (TryGetNextInvoke(filter.Target, currentIndex + 1, delegateRef, parameters, out var callvirtIndex))
        {
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

    private MethodDefinition? GetMethod(MemberSymbols memberSymbol, string methodName, int currentIndex, IReadOnlyList<Instruction> instructions, IReadOnlyList<TypeReference> parameters)
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
            var argType = GetArgType(instructions[currentIndex - 1]) ?? throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, methodName));
            if (argType is GenericParameter parameter)
            {
                return GetMethodFromConstraint(parameter, methodName, parameters);
            }

            var originType = argType.Resolve();

            if (originType.TryGetParsedGenericParameter(Source, out var genericParameter))
            {
                originType = DeclaringTypeHandler.AssemblyHandler.GetCecilType(genericParameter!).Definition;
            }

            return DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(originType, (string) instructions[currentIndex].Operand, parameters);
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

    private bool TryGetNextInvoke(IReadOnlyList<Instruction> bodyInstructions, int callIndex, TypeReference delegateType, TypeReference[] parameters, out int index)
    {
        // This stack is used to ensure that the method parameters are of the same type as the method signature before they're all pushed to the stack.
        var paramStack = new ParameterStack();
        // This stack is used to cache the types of 'stloc' operand during scanning the method body.
        var localStack = new TypeReference[bodyInstructions.Count];

        // TODO: Maybe we could cache the scanning result that wouldn't simulate parameter balance every time.
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

            // Skip it because we are going to replace it.
            if (i == callIndex) continue;

            // Keep the stack balanced until we match the target delegate invoke.
            BalanceStack(ins, paramStack, localStack);
        }

        index = -1;
        return false;

        bool MatchTargetInvoke(Instruction ins) =>
            (ins.OpCode == OpCodes.Callvirt || ins.OpCode == OpCodes.Call)  // It's no double that the instruction must be 'call' type.
            && ins.Operand is MethodReference {Name: "Invoke"} callMethod   // We only check the 'invoke' method from Delegate.
            && TypeName.HasSameName(delegateType, callMethod.DeclaringType) // Make sure declaring types are the same.
            && callMethod.Parameters.SameWith(paramStack.Types)             // Make sure the method parameters types has same types with the stack.
            && callMethod.Parameters.SameWith(parameters);                  // Make sure paramStack has same types with the delegate parameters.
    }

    /// <summary>
    /// Consider how the instruction should pop from or push into <c>paramStack</c>.
    /// </summary>
    /// <param name="ins">Balance target.</param>
    /// <param name="paramStack">Parameter stack of current method body scanning.</param>
    /// <param name="localStack"></param>
    /// <exception cref="ArgumentException"></exception>
    private void BalanceStack(Instruction ins, ParameterStack paramStack, TypeReference[] localStack)
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
        if (TryGetStackType(ins, out var type))
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

    private bool TryGetStackType(Instruction ins, out TypeReference? type)
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
            _                                                                                      => GetArgType(ins)                     // Args
        };

        return type != null && type != typeSystem.Void;
    }

    private TypeReference? GetArgType(Instruction instruction)
    {
        var isStatic = Source.IsStatic;
        if (instruction.OpCode == OpCodes.Ldarg_S && instruction.Operand is ParameterReference parameter) return parameter.ParameterType;
        if (!instruction.TryGetLdargIndex(out var index)) return null;
        return !isStatic && index == 0
            ? Source.DeclaringType
            : Source.Parameters[index + (isStatic ? -1 : 0)].ParameterType;
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