namespace Gneedle.Inject;

partial class MethodHandler
{
    /// <summary>
    /// Replace the operand of instruction at current index. If the instruction is `ldstr {member_name}` and the nearest `call` instruction
    /// is one of the instance member accessors in Gneedle.Inject, parse the member name and flags, and replace the instruction with
    /// the one to get the member reference. Otherwise, replace the instruction with the one imported to current module.<para/>
    /// The symbol which proceeds is the one which carries no name, so the call is the whole of it and the type which
    /// declares it is what identifies it, in the place where the name of the others identifies them.
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
        // which ILCode just look like:
        // IL_0000: ldstr {field_name}
        // IL_0005: call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Field(string)
        // The name of the declaring type settles which pointer the operand stands for, so a pointer has to be named here
        // as well as in GetInstanceMemberFlag, which maps the name to the flag.
        if (currentIns.OpCode == OpCodes.Ldstr && bodyInstructions[callIndex].Operand is MethodReference
        {
            DeclaringType:
            {
                Name     : nameof(This) or nameof(Base) or nameof(Instance) or nameof(Static),
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
        // The symbol which proceeds carries no name, so there is no instruction ahead of the call which identifies it:
        // the call is the whole of the symbol, and the type which declares it does what the name of the others does.
        // Which member it stands for is settled by the member being woven, whose body was taken over rather than named.
        else if (currentIns.Operand is MethodReference proceedCall && proceedCall.DeclaringType.FullName == Proceed.TYPE_NAME)
        {
            // The type declares the two symbols which reach the body that was taken over: the one which names a
            // signature for the call to be made with, and the one which takes the arguments which the template itself
            // was given. Which of the two it is, is the name of the call.
            if (proceedCall.Name == nameof(Proceed.Invoke))
            {
                ParseProceedInvoke(currentIndex, proceedCall, filter, targetDef);
            }
            else if (proceedCall.Parameters.Count != 0)
            {
                // A call which hands the symbol a name is a template which was compiled against a weaver which read one,
                // and the name would be left on the stack ahead of the call which is written in its place.
                throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, nameof(Proceed) + "." + nameof(Proceed.Method)));
            }
            else if (m_CarriedBody is null)
            {
                ParseMethod(nameof(Proceed) + "." + nameof(Proceed.Method),
                    MemberSymbols.Proceed | MemberSymbols.Method, currentIndex, null, filter, targetDef);
            }
            else
            {
                // The call proceeds into the body which was taken over, and a body which the compiler wrote for a body
                // of the template's own is written with arguments of its own rather than with the arguments of the
                // template, which is what the call hands over: it is refused here as the call which names them is.
                throw new WeavingException(string.Format(ErrorMessages.PROCEED_IN_A_BODY_OF_ITS_OWN, proceedCall.FullName, Source.FullName));
            }
        }
        else
        {
            FilterOperand(currentIns, currentIndex, filter, targetDef);
        }
    }

    /// <summary>
    /// Filter the operand of instruction, and replace it with the one imported to current module.
    /// </summary>
    /// <param name="currentIns">The instruction with non-imported operand.</param>
    /// <param name="currentIndex">Index of the instruction.</param>
    /// <param name="filter">The instruction filter to replace the instruction.</param>
    /// <param name="targetDef">The method definition being scanned (source of the instructions).</param>
    private void FilterOperand(Instruction currentIns, int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        switch (currentIns.Operand)
        {
            // The signature of the method may hold generic parameter tokens, just like List<Gneedle.Inject.T_0>::.ctor().
            // It may also hold a declaring type which stands for the type of another assembly, which the parsing below
            // replaces by the real one.
            case MethodReference methodRef:
                RefuseANameWhichIsNotWritten(methodRef, currentIndex, filter);
                RefuseTheCompilersOwnType(methodRef.DeclaringType, methodRef.FullName);
                RefuseTheCompilersOwnMember(methodRef, targetDef);

                // What the carrying wrote stands for what the body named, and what it wrote is a member of the type
                // being woven rather than a reference of another module: it is written where it stands. The body of the
                // template is not written to for it, because that body is a member of the assembly being woven as well
                // and is read again by a weave of another member of it, which would then read what this one wrote.
                var pointedMethod = m_Carried?.TheCopyOf(methodRef);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode,
                    pointedMethod ?? ModuleLock.Import(Source.Module, methodRef).ParseGenericTokens(Source, Source.Module)));
                break;
            // The parameter of the template is matched to the parameter of the same position rather than to the one of
            // the same name: they are the parameters of two different methods, and the name which the template gave its
            // own takes no part in it. The opcode is kept, because the load or store is an instruction of the member
            // being woven, which accounts for its receiver by itself. The forms of ldarg never reach here: each of them
            // is translated by ParseBody, which reaches the macro forms as well.
            // The position of a parameter is not the slot it holds, which is what GetParameterAt answers for: a parameter
            // of a body which the compiler wrote is the parameter of the copy of that body, which the carrying
            // re-pointed this operand to already, so the operand stands as it is for a body of that kind.
            case ParameterDefinition parameterDef when m_CarriedBody is null:
                var parameter = GetParameterAt(parameterDef.Index, targetDef);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, parameter));
                break;
            // The type of the field may hold generic parameter tokens, just like List<Gneedle.Inject.T_0>::SomeField.
            // It may also hold a declaring type which stands for the type of another assembly, which is replaced below.
            case FieldReference fieldRef:
                RefuseTheCompilersOwnType(fieldRef.DeclaringType, fieldRef.FullName);
                var pointedField = m_Carried?.TheCopyOf(fieldRef);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode,
                    pointedField ?? ModuleLock.Import(Source.Module, fieldRef).ParseGenericTokens(Source, Source.Module)));
                break;
            // We need to find the variable with the same index in source method definition, and replace the operand with it.
            // The local of a body which the compiler wrote is a local of the copy of that body, which the carrying
            // re-pointed: it already names the local it belongs to, and the member being woven may hold an unrelated one
            // at that position, so the operand is left as it stands for a body of that kind.
            case VariableDefinition varDef when m_CarriedBody is null:
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, Source.Body.Variables[varDef.Index]));
                break;
            // The type may hold generic parameter tokens itself, just like box Gneedle.Inject.T_0.
            case TypeReference typeRef:
                RefuseTheCompilersOwnType(typeRef, typeRef.FullName);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode,
                    m_Carried?.TheCopyOf(typeRef) ?? typeRef.ParseGenericTokens(Source, Source.Module)));
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
    /// <exception cref="InvalidILException">Thrown when the call names no member kind which the weaving reads.</exception>
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
            // The name is the instruction ahead of the call, which is where every symbol but the one which proceeds
            // writes it: that one is recognized by the call alone and reaches ParseMethod without a name.
            ParseMethod(memberName, memberSymbol, currentIndex + 1, currentIndex, filter, targetDef);
        }

        // A call which names no member kind is one of two things: the half of a pair which the member named beside it
        // is read with, which is the instance an Instance symbol is made of or the type which a Static symbol is written
        // from, or a call which names nothing the weaving reads at all. Leaving the second one where it is leaves a call
        // of a placeholder in the body, where nothing stands for a member of the type which is woven, so it throws for
        // the call at run time rather than for the template at the weaving.
        else if (!memberSymbol.HasFlag(MemberSymbols.Static) && !memberSymbol.HasFlag(MemberSymbols.Instance))
        {
            throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, memberName));
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
            This.TYPE_NAME     => MemberSymbols.This,
            Base.TYPE_NAME     => MemberSymbols.Base,
            Instance.TYPE_NAME => MemberSymbols.Instance,
            Static.TYPE_NAME   => MemberSymbols.Static,

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
}
