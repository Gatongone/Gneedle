using System.Reflection;
using System.Text;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for method definitions, providing functionalities to manipulate method bodies and translate instructions for code injection purposes.
/// </summary>
internal sealed partial class MethodHandler : IMethodHandler
{
    /// <summary>
    /// The source method definition to inject code into. It is the method definition we want to manipulate and inject code into.
    /// </summary>
    internal readonly MethodDefinition Source;

    /// <summary>
    /// The type handler of the type that declares the source method definition.
    /// It is used to get the context of the source method definition when translating instructions and parsing members in target method definition.
    /// </summary>
    internal readonly TypeHandler DeclaringTypeHandler;

    /// <summary>
    /// Name of the generated method which holds the body which the source method is woven around, or null when the
    /// source method is not woven around.
    /// </summary>
    private string? m_ProceedMethodName;

    /// <summary>
    /// Gets the name of the source method definition. It is used for debugging and logging purposes to identify the method being manipulated.
    /// </summary>
    public string Name => Source.Name;

    /// <inheritdoc/>
    ITypeHandler IMethodHandler.DeclaringTypeHandler => DeclaringTypeHandler;

    /// <inheritdoc/>
    public bool ContainsAttribute(IType attributeType) => Source.CustomAttributes.Any(attribute => TypeName.HasSameName(attribute.AttributeType, attributeType));

    /// <inheritdoc/>
    public void AddAttribute(IType attributeType, params object[] arguments)
    {
        var attributeDef = DeclaringTypeHandler.AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = attributeDef.CreateCustomAttribute(DeclaringTypeHandler.AssemblyHandler.Assembly.Source.MainModule, arguments);
        Source.CustomAttributes.Add(attribute);
    }

    /// <summary>
    /// Initialize a new instance of MethodHandler with the source method definition and its declaring type handler.
    /// The source method definition is the method definition we want to inject code into, and the declaring type handler is the type handler of the type that declares the source method definition.
    /// </summary>
    /// <param name="methodDef"></param>
    /// <param name="declaringTypeHandler"></param>
    internal MethodHandler(MethodDefinition methodDef, TypeHandler declaringTypeHandler) => (Source, DeclaringTypeHandler) = (methodDef, declaringTypeHandler);

    /// <summary>
    /// Get the string representation of the method body for debugging. It will return the IL code of the method body in source method definition,
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($".method {(Source.Attributes | MethodAttributes.CompilerControlled).ToString().Replace(", ", " ").ToLower()} {Source.FullName}");
        foreach (var item in Source.Body.Instructions)
        {
            sb.AppendLine(item.ToString());
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public void SetBody(DefaultMethodBody defaultMethodBody)
    {
        Source.Body = new MethodBody(Source);
        var il = Source.Body.GetILProcessor();

        switch (defaultMethodBody)
        {
            case DefaultMethodBody.CallFromBase:
                SetBodyCallFromBase(il);
                break;
            case DefaultMethodBody.ThrowException:
                SetBodyThrowException(il);
                break;
            case DefaultMethodBody.WithDefaultReturn:
                SetBodyWithDefaultReturn(il);
                break;
        }
    }

    /// <summary>
    /// Set the method body to call the base type's method with the same name and parameters.
    /// </summary>
    /// <param name="il">The IL processor of the source method body.</param>
    /// <exception cref="ArgumentException">Thrown when the base type or method is not found.</exception>
    private void SetBodyCallFromBase(ILProcessor il)
    {
        // Find the base type's method with the same name and parameter types.
        var baseType = Source.DeclaringType.BaseType;
        if (baseType == null) throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, Source.Name));

        var baseMethod = DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(
            DeclaringTypeHandler.AssemblyHandler.GetCecilType(baseType).Definition,
            Source.Name, Source.Parameters.Select(p => p.ParameterType).ToArray());
        if (baseMethod == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, Source.Name));
        }

        // Load arguments, call base method, return.
        var isStatic = Source.IsStatic;
        for (var i = 0; i < Source.Parameters.Count; i++)
        {
            il.Emit(OpCodes.Ldarg, i + (isStatic ? 0 : 1));
        }

        il.Emit(baseMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, Source.Module.ImportReference(baseMethod));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Set the method body to throw a <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="il">The IL processor of the source method body.</param>
    private void SetBodyThrowException(ILProcessor il)
    {
        var module = Source.Module;
        var ctor = module.ImportReference(typeof(NotSupportedException).GetConstructor(Type.EmptyTypes));
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Set the method body to return the default value of the return type.
    /// </summary>
    /// <param name="il">The IL processor of the source method body.</param>
    private void SetBodyWithDefaultReturn(ILProcessor il)
    {
        var returnType = Source.ReturnType;
        if (returnType.MetadataType == MetadataType.Void)
        {
            il.Emit(OpCodes.Ret);
            return;
        }

        // Load default value: null for reference types, default for value types.
        if (returnType.IsValueType)
        {
            var variable = new VariableDefinition(returnType);
            Source.Body.Variables.Add(variable);
            il.Emit(OpCodes.Ldloca, variable);
            il.Emit(OpCodes.Initobj, returnType);
            il.Emit(OpCodes.Ldloc, variable);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Set the method body of source method definition to be the same as the target method definition.
    /// We need to parse the instructions in target method body and translate them to make them work in source method body,
    /// </summary>
    /// <param name="method"></param>
    public void SetBody(MethodInfo method)
    {
        var targetDef = Source.Module.ImportReference(method).Resolve();
        var instructions = targetDef.Body.Instructions;

        // Create a new body.
        Source.Body = new MethodBody(Source);

        // Copy target method variables to source.
        CopyVariables(targetDef, Source);

        ParseBody(instructions, targetDef);

        ParseReturnType(targetDef.ReturnType);
    }

    /// <summary>
    /// Set the body of the method to run around the body which it holds, which the template reaches through
    /// <see cref="Proceed"/>.<para/>
    /// The template keeps the signature of the method, so its parameters and its return type have to match, and the body
    /// which the method holds is moved to a generated method of the declaring type which the template calls.
    /// </summary>
    /// <param name="method">The template which holds the body to weave around.</param>
    /// <exception cref="ArgumentException">Thrown when the method cannot be woven around, or when the template does not match it.</exception>
    public void AroundBody(MethodInfo method)
    {
        var templateDef = Source.Module.ImportReference(method).Resolve();

        // Every check runs before anything is changed, so that a weave which cannot be done leaves the method as it was.
        if (Source.IsAbstract || Source.IsPInvokeImpl || Source.IsRuntime || Source.IsInternalCall || !Source.HasBody)
        {
            throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_TARGET_HAS_NO_BODY, Source.FullName));
        }

        if (Source.IsConstructor) throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_TARGET_IS_CONSTRUCTOR, Source.FullName));
        if (m_ProceedMethodName != null) throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_ALREADY_SET, Source.FullName));

        // The signature of the template is resolved against the method before it is compared, because a generic method is
        // matched through the M_[0-20] tokens of the template rather than through its own generic parameters: a template
        // which takes and returns Gneedle.Inject.M_0 matches a method which takes and returns its first generic
        // parameter. Resolving first is what turns the token into that parameter, and it changes nothing for a method
        // which holds no generic parameter.
        if (templateDef.Parameters.Count != Source.Parameters.Count
            || templateDef.Parameters.Where((parameter, index) => !TypeName.HasSameName(parameter.ParameterType.ParseGenericTokens(Source, Source.Module),
                                                                                       Source.Parameters[index].ParameterType)).Any())
        {
            throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_PARAMETERS_MISMATCH, Source.FullName, templateDef.FullName));
        }

        if (!TypeName.HasSameName(templateDef.ReturnType.ParseGenericTokens(Source, Source.Module), Source.ReturnType))
        {
            throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_RETURN_TYPE_MISMATCH, Source.FullName, templateDef.FullName));
        }

        var name = $"<{Source.Name}>k__Proceed";
        if (Source.DeclaringType.Methods.Any(methodDef => methodDef.Name == name) || Source.DeclaringType.Fields.Any(field => field.Name == name))
        {
            throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_GENERATED_NAME_OCCUPIED, name));
        }

        var generated = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.HideBySig
                                                  | (Source.IsStatic ? MethodAttributes.Static : 0), Source.ReturnType)
        {
            DeclaringType = Source.DeclaringType
        };

        // The parameters of the method are given to the generated method as the objects they are rather than as copies
        // of them, so that every operand which names an argument keeps naming the one it named: the method of a parameter
        // is a reference which only the parameter table uses, and that table is written from the signature of the method
        // which holds it. A copy would mean rewriting every argument opcode, the ones which carry no operand included.
        foreach (var parameter in Source.Parameters) generated.Parameters.Add(parameter);

        // The generic parameters are copied rather than given, which is where they differ from the parameters above: a
        // generic parameter belongs to the method which declares it, and adding one to the collection of a second method
        // re-parents it, which leaves the method it came from declaring none of its own. The copies take the name, the
        // attributes and the constraints of the originals, so the generated method is generic in the very same way.
        // A generic parameter is named by its position in every reference to it, so a copy of the same position is the
        // very same parameter to the body which was moved and to the call which the template leaves behind.
        foreach (var genericParameter in Source.GenericParameters)
        {
            var copy = new GenericParameter(genericParameter.Name, generated) { Attributes = genericParameter.Attributes };
            foreach (var constraint in genericParameter.Constraints)
            {
                copy.Constraints.Add(new GenericParameterConstraint(constraint.ConstraintType));
            }

            generated.GenericParameters.Add(copy);
        }

        Source.DeclaringType.Methods.Add(generated);

        MoveBodyTo(generated, Source);
        Source.Body         = new MethodBody(Source);
        m_ProceedMethodName = name;

        // The template is parsed last, so that Proceed resolves through the generated method, which is on the declaring
        // type by now. The return type is deliberately left alone: the template keeps the signature of the method, which
        // was checked above, so parsing it as SetBody does would only write an equal type again.
        CopyVariables(templateDef, Source);
        ParseBody(templateDef.Body.Instructions, templateDef);
    }

    /// <summary>
    /// Move the body of <paramref name="from"/> onto <paramref name="to"/>, leaving the first empty.
    /// </summary>
    /// <remarks>
    /// The instructions are moved rather than shared, because each of them belongs to a single body: the offset of one
    /// is computed while the body which holds it is written, so a shared one would take the offset of whichever body
    /// was written last. The module of the two methods is the same, so nothing has to be imported or parsed. The
    /// variables and the exception handlers belong to the body which holds them in the same way, and they move with it.
    /// </remarks>
    /// <param name="to">The method which the body is moved onto.</param>
    /// <param name="from">The method which the body is moved from.</param>
    private static void MoveBodyTo(MethodDefinition to, MethodDefinition from)
    {
        var body = to.Body;

        body.Instructions.AddRange(from.Body.Instructions);
        from.Body.Instructions.Clear();
        body.Variables.AddRange(from.Body.Variables);
        from.Body.Variables.Clear();
        body.ExceptionHandlers.AddRange(from.Body.ExceptionHandlers);
        from.Body.ExceptionHandlers.Clear();

        body.InitLocals   = from.Body.InitLocals;
        body.MaxStackSize = from.Body.MaxStackSize;
    }

    /// <summary>
    /// Parse the return type. If the return type holds a generic parameter token which could be parsed from
    /// Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20], it would be replaced with the generic parameter of the source method.
    /// </summary>
    /// <param name="sourceReturnType">The return type to parse.</param>
    public void ParseReturnType(TypeReference sourceReturnType)
        => Source.ReturnType = sourceReturnType.ParseGenericTokens(Source, Source.Module);

    /// <summary>
    /// Parse the method body. We need to translate the instructions in target method body to make them work in source method body,
    /// and then apply the translated instructions to source method body.
    /// </summary>
    /// <param name="instructions">The instructions in target method body to parse.</param>
    /// <param name="targetDef">The target method definition to check when parsing instructions.</param>
    private void ParseBody(Mono.Collections.Generic.Collection<Instruction> instructions, MethodDefinition targetDef)
    {
        var filter = new InstructionFilter(instructions);

        // Translate the accessor codes to standard IL codes.
        for (var index = 0; index < instructions.Count; index++)
        {
            // Skip replacement item.
            if (filter.HasReplaced(index)) continue;

            var instruction = instructions[index];

            // Replace Ldarg.
            if (instruction.OpCode == OpCodes.Ldarg_0 || instruction.OpCode == OpCodes.Ldarg_1 || instruction.OpCode == OpCodes.Ldarg)
            {
                filter.Replace(index, Instruction.Create(GetLdargCode(targetDef, instruction)));
                continue;
            }

            // Replace operand.
            if (instruction.Operand != null)
            {
                ReplaceOperand(index, filter, targetDef);
            }
        }

        // Apply translations to body.
        filter.ApplyTo(Source.Body.Instructions);
    }

    /// <summary>
    /// Get the OpCode to load argument based on the source method definition and the instruction in target method definition.
    /// If the instruction is Ldarg0, we need to check if the source method is static or not, and if the target method is static or not,
    /// to determine whether to replace it with Ldarg1 or keep it as Ldarg0. If the instruction is Ldarg1,
    /// we also need to check if the source method is static and the target method is not static to determine whether to replace it with Ldarg0 or keep it as Ldarg1.
    /// For other Ldarg instructions, we keep them unchanged.
    /// </summary>
    /// <param name="methodDef">The target method definition to check.</param>
    /// <param name="instruction">The instruction to check.</param>
    /// <returns>The OpCode to load argument after translation.</returns>
    /// <exception cref="ArgumentException">Thrown when the instruction is Ldarg0 but the source and target method static-ness are not compatible.</exception>
    private OpCode GetLdargCode(MethodDefinition methodDef, Instruction instruction)
    {
        // Resolve Ldarg0.
        if (instruction.OpCode == OpCodes.Ldarg_0 || instruction.OpCode == OpCodes.Ldarg && instruction.Operand.Equals(0))
        {
            return Source.IsStatic switch
            {
                false when methodDef.IsStatic => OpCodes.Ldarg_1,
                true                          => instruction.OpCode,
                _                             => throw new ArgumentException(ErrorMessages.LDARG0_CONVERT_FAILED)
            };
        }

        // Resolve Ldarg1.
        if (instruction.OpCode == OpCodes.Ldarg_1 || instruction.OpCode == OpCodes.Ldarg && instruction.Operand.Equals(1))
        {
            return Source.IsStatic && !methodDef.IsStatic ? OpCodes.Ldarg_0 : instruction.OpCode;
        }

        return instruction.OpCode;
    }

    /// <summary>
    /// Replace the operand of instruction at current index. If the instruction is `ldstr {member_name}` and the nearest `call` instruction
    /// is one of the instance member accessors in Gneedle.Inject, parse the member name and flags, and replace the instruction with
    /// the one to get the member reference. Otherwise, replace the instruction with the one imported to current module.
    /// </summary>
    /// <param name="currentIndex">Index of the instruction to replace operand.</param>
    /// <param name="filter">The instruction filter to replace the instruction.</param>
    /// <param name="targetDef">The method definition being scanned (source of the instructions).</param>
    private void ReplaceOperand(int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        var bodyInstructions = filter.Target;
        var currentIns = bodyInstructions[currentIndex];
        var memberFlag = MemberSymbols.None;
        var memberName = string.Empty;
        var callIndex = currentIndex + 1;

        // The str may be loaded from follow parameters:
        // - Gneedle.Inject.This.Field(string)
        // - Gneedle.Inject.Base.Field(string)
        // - Gneedle.Inject.This.Property(string)
        // - Gneedle.Inject.Base.Property(string)
        // - Gneedle.Inject.This.Method(string)
        // - Gneedle.Inject.Base.Method(string)
        // - Gneedle.Inject.Proceed.Method(string), where the call is a generic instance method
        // which ILCode just look like:
        // IL_0000: ldstr {field_name}
        // IL_0005: call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Field(string)
        // The name of the declaring type settles which pointer the operand stands for, so a pointer has to be named here
        // as well as in GetInstanceMemberFlag, which maps the name to the flag.
        if (currentIns.OpCode == OpCodes.Ldstr && bodyInstructions[callIndex].Operand is MethodReference
        {
            DeclaringType:
            {
                Name     : nameof(This) or nameof(Base) or nameof(Object) or nameof(Static) or nameof(Proceed),
                Namespace: nameof(Gneedle) + "." + nameof(Inject)
            }
        } callingMethod)
        {
            // Get type instance member flags.
            memberFlag = GetInstanceMemberFlag(callingMethod);
            if (memberFlag != MemberSymbols.None)
            {
                memberName = (string) currentIns.Operand;
                // Set the instruction to the `call`.
                currentIns = bodyInstructions[callIndex];
            }
        }

        if (memberFlag is not MemberSymbols.None && currentIns.Operand is MethodReference)
        {
            ParseMember(memberName, memberFlag, currentIndex, filter, targetDef);
        }
        else
        {
            FilterOperand(currentIns, currentIndex, filter);
        }
    }

    /// <summary>
    /// Filter the operand of instruction, and replace it with the one imported to current module.
    /// </summary>
    /// <param name="currentIns">The instruction with non-imported operand.</param>
    /// <param name="currentIndex">Index of the instruction.</param>
    /// <param name="filter">The instruction filter to replace the instruction.</param>
    private void FilterOperand(Instruction currentIns, int currentIndex, InstructionFilter filter)
    {
        switch (currentIns.Operand)
        {
            // The signature of the method may hold generic parameter tokens, just like List<Gneedle.Inject.T_0>::.ctor().
            // It may also hold a declaring type which stands for the type of another assembly, which the parsing below
            // replaces by the real one.
            case MethodReference methodRef:
                var importedMethod = Source.Module.ImportReference(methodRef).ParseGenericTokens(Source, Source.Module);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, importedMethod));
                break;
            // The parameter of the template is matched to the parameter of the same position rather than to the one of
            // the same name: they are the parameters of two different methods, and the name which the template gave its
            // own takes no part in it. The opcode is kept, because the position which is written is the one which the
            // parameter holds in the method being woven, and that method accounts for its receiver by itself, just as
            // the macro opcodes which GetLdargCode translates do.
            case ParameterDefinition parameterDef:
                if (parameterDef.Index >= Source.Parameters.Count)
                {
                    throw new ArgumentException(string.Format(ErrorMessages.INVALID_TEMPLATE_PARAMETER, parameterDef.Index, Source.FullName));
                }

                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, Source.Parameters[parameterDef.Index]));
                break;
            // The type of the field may hold generic parameter tokens, just like List<Gneedle.Inject.T_0>::SomeField.
            // It may also hold a declaring type which stands for the type of another assembly, which is replaced below.
            case FieldReference fieldRef:
                var importedField = Source.Module.ImportReference(fieldRef).ParseGenericTokens(Source, Source.Module);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, importedField));
                break;
            // We need to find the variable with the same index in source method definition, and replace the operand with it.
            case VariableDefinition varDef:
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, Source.Body.Variables[varDef.Index]));
                break;
            // The type may hold generic parameter tokens itself, just like box Gneedle.Inject.T_0.
            case TypeReference typeRef:
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, typeRef.ParseGenericTokens(Source, Source.Module)));
                break;
        }
    }

    /// <summary>
    /// Parse the member based on the member flags, and replace the instruction with the one to get the member reference.
    /// </summary>
    /// <param name="memberName">Name of the member.</param>
    /// <param name="memberSymbol">Member flags about the member kind and its property.</param>
    /// <param name="currentIndex">Index of the instruction of `ldstr {member_name}`.</param>
    /// <param name="filter"></param>
    /// <param name="targetDef">The method definition being scanned (source of the instructions).</param>
    private void ParseMember(string memberName, MemberSymbols memberSymbol, int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        if (memberSymbol.HasFlag(MemberSymbols.Field))
        {
            ParseField(memberName, memberSymbol, currentIndex, filter, targetDef);
        }

        else if (memberSymbol.HasFlag(MemberSymbols.Property))
        {
            ParseProperty(memberName, memberSymbol, currentIndex, filter, targetDef);
        }

        else if (memberSymbol.HasFlag(MemberSymbols.Method))
        {
            ParseMethod(memberName, memberSymbol, currentIndex, filter, targetDef);
        }
    }

    /// <summary>
    /// Parse the field member, and replace the instruction with the one to get the field reference.
    /// </summary>
    /// <param name="from">The source method definition to copy variables from.</param>
    /// <param name="to">The target method definition to copy variables to.</param>
    private static void CopyVariables(MethodDefinition from, MethodDefinition to)
    {
        var srcVariables = from.Body.Variables;
        if (srcVariables == null) return;

        var desVariables = to.Body.Variables;
        to.Body.Variables.Clear();

        for (var i = 0; i < srcVariables.Count; i++)
        {
            // If the variable type holds a generic parameter token which could be parsed from Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20],
            // then get the actual generic parameter type.
            var typeRef = srcVariables[i].VariableType.ParseGenericTokens(to, to.Module);
            desVariables.Add(new VariableDefinition(typeRef));
        }
    }

    /// <summary>
    /// Get the member flags of the instance member based on the declaring type and method name of the `call` instruction.
    /// </summary>
    /// <param name="member">The member reference of the `call` instruction.</param>
    /// <returns>The member flags of the instance member.</returns>
    private static MemberSymbols GetInstanceMemberFlag(MemberReference member)
    {
        var memberFlag = MemberSymbols.None;

        memberFlag |= member.DeclaringType.FullName switch
        {
            This.TYPE_NAME   => MemberSymbols.This,
            Base.TYPE_NAME   => MemberSymbols.Base,
            Object.TYPE_NAME => MemberSymbols.Object,
            Static.TYPE_NAME => MemberSymbols.Static,

            // The only member which the pointer holds is the method which the advice proceeds through, so the kind of
            // the member is settled here rather than read from the name of the call.
            Proceed.TYPE_NAME => MemberSymbols.Proceed | MemberSymbols.Method,
            _                 => MemberSymbols.None
        };

        memberFlag |= member.Name switch
        {
            nameof(This.Field)    => MemberSymbols.Field,
            nameof(This.Property) => MemberSymbols.Property,
            nameof(This.Method)   => MemberSymbols.Method,
            _                     => MemberSymbols.None
        };

        return memberFlag;
    }

    /// <summary>
    /// Try to get the nearest `call` instruction of `ValuableMember.Get` or `ValuableMember.Set` after the start index, and check if it's a `get` or `set` accessor.
    /// </summary>
    /// <param name="bodyInstructions">The instruction collection to search.</param>
    /// <param name="startIndex">The start index to search from.</param>
    /// <param name="isGet">Output whether the accessor is `get` or `set`.</param>
    /// <param name="index">Output the index of the `call` instruction if found.</param>
    /// <returns>True if the `call` instruction of `ValuableMember.Get` or `ValuableMember.Set` is found; otherwise, false.</returns>
    private static bool TryGetNextGetOrSet(IReadOnlyList<Instruction> bodyInstructions, int startIndex, out bool isGet, out int index)
    {
        isGet = false;
        for (var i = startIndex; i < bodyInstructions.Count; i++)
        {
            if (bodyInstructions[i].OpCode != OpCodes.Callvirt || bodyInstructions[i].Operand is not MethodReference
            {
                DeclaringType:
                {
                    Name     : nameof(ValuableMember) or nameof(ValuableMember) + "`1",
                    Namespace: nameof(Gneedle) + "." + nameof(Inject)
                }
            } method) continue;
            if (method.Name.Equals(nameof(ValuableMember.Get))) isGet = true;
            index = i;
            return true;
        }

        index = 0;
        return false;
    }

    /// <summary>
    /// The instruction filter to apply instruction translations. It holds the target collection of instructions and the translation results,
    /// and applies the translations to source collection when calling ApplyTo.
    /// </summary>
    /// <param name="target">The target collection of instructions to apply translations.</param>
    private class InstructionFilter(Mono.Collections.Generic.Collection<Instruction> target)
    {
        /// <summary>
        /// The instruction to replace the original instruction at index. If null, it means to keep the original instruction.
        /// </summary>
        private readonly Instruction?[] m_Replacements = new Instruction[target.Count];

        /// <summary>
        /// The instruction to insert before the original instruction at index. If null, it means no instruction to insert.
        /// </summary>
        private readonly Instruction?[] m_Insert = new Instruction[target.Count];

        /// <summary>
        /// Key: the instruction with non-imported operand.
        /// Value: the instruction with re-imported operand.
        /// </summary>
        private readonly Dictionary<Instruction, Instruction> m_Operands = new();

        /// <summary>
        /// The target collection of instructions to apply translations.
        /// The instruction at index in target collection will be replaced with the instruction in m_Replacements at the same index if there is one,
        /// or will be added to source collection directly if there is no replacement instruction.
        /// </summary>
        public readonly IReadOnlyList<Instruction> Target = target.ToArray();

        /// <summary>
        /// Check if there is a replacement instruction for the instruction at index.
        /// If true, it means the instruction at index will be replaced with another instruction, and the original instruction will be skipped.
        /// </summary>
        /// <param name="index">Index of the instruction in target collection.</param>
        /// <returns></returns>
        public bool HasReplaced(int index) => m_Replacements[index] != null;

        /// <summary>
        /// Replace the instruction at index with Nop, which means to skip the instruction.
        /// </summary>
        /// <param name="index"></param>
        public void Skip(int index) => m_Replacements[index] = Instruction.Create(OpCodes.Nop);

        /// <summary>
        /// Replace the instruction at index with the given instruction. If the operand of original instruction is MethodReference,
        /// ParameterDefinition, FieldReference, VariableDefinition or TypeReference,
        /// add the original instruction and the new instruction to m_Operands for later operand replacement.
        /// </summary>
        /// <param name="index">Index of the instruction to be replaced.</param>
        /// <param name="ins">The instruction to replace with.</param>
        public void Replace(int index, Instruction ins)
        {
            m_Replacements[index] = ins;
            if (target[index].Operand is MethodReference or ParameterDefinition or FieldReference or VariableDefinition or TypeReference)
            {
                m_Operands.Add(target[index], ins);
            }
        }

        /// <summary>
        /// Insert the given instruction before the instruction at index. If there are multiple instructions to insert before the same index, they will be added in order of insertion.
        /// </summary>
        /// <param name="index">Index of the instruction to insert before.</param>
        /// <param name="ins"> The instruction to insert.</param>
        public void Insert(int index, Instruction ins) => m_Insert[index] = ins;

        /// <summary>
        /// Apply the instruction translations to source collection. For each instruction in target collection,
        /// if there is an instruction to insert before it, add the instruction to source first.
        /// Then if there is a replacement instruction, add it to source. Otherwise, add the original instruction to source,
        /// but replace its operand with the one re-imported if it exists in m_Operands.
        /// </summary>
        /// <param name="source">The source collection to apply instructions.</param>
        public void ApplyTo(ICollection<Instruction> source)
        {
            for (var index = 0; index < target.Count; index++)
            {
                AddInsertInstruction(source, index);
                ReplaceOrAddInstruction(source, index);
            }
        }

        /// <summary>
        /// If there is an instruction to insert before current index, add it to source.
        /// </summary>
        /// <param name="source">The source collection to add instruction.</param>
        /// <param name="index">Index of the instruction in target collection.</param>
        private void AddInsertInstruction(ICollection<Instruction> source, int index)
        {
            var insert = m_Insert[index];
            if (insert != null)
            {
                source.Add(insert);
            }
        }

        /// <summary>
        /// If there is a replacement instruction, add it to source. Otherwise, add the original instruction to source, but replace its operand with the one re-imported if it exists in m_Operands.
        /// </summary>
        /// <param name="source">The source collection to add instruction.</param>
        /// <param name="index">Index of the instruction in target collection.</param>
        private void ReplaceOrAddInstruction(ICollection<Instruction> source, int index)
        {
            var replacement = m_Replacements[index];
            if (replacement != null)
            {
                // Seems we don't need a nop OPCode. Do we?
                if (replacement.OpCode != OpCodes.Nop)
                {
                    source.Add(replacement);
                }
            }
            else
            {
                AddOriginalInstruction(source, index);
            }
        }

        /// <summary>
        /// Add the original instruction to source, but replace its operand with the one re-imported if it exists in m_Operands.
        /// </summary>
        /// <param name="source">The source collection to add instruction.</param>
        /// <param name="index">Index of the instruction in target collection.</param>
        private void AddOriginalInstruction(ICollection<Instruction> source, int index)
        {
            var ins = target[index]!;
            // The operand may be re-imported, so we need to build a new one.
            if (ins.Operand is Instruction oldIns && m_Operands.TryGetValue(oldIns, out var newIns))
            {
                source.Add(Instruction.Create(ins.OpCode, newIns));
            }
            else
            {
                source.Add(ins);
            }
        }
    }
}