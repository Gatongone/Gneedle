using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Gneedle.Inject;

partial class MethodHandler
{
    /// <summary>
    /// Parse the method body. We need to translate the instructions in target method body to make them work in source method body,
    /// and then apply the translated instructions to source method body.
    /// </summary>
    /// <param name="from">The body whose instructions and regions are parsed, which is the body of the template or the
    /// body which the compiler wrote for one of its own.</param>
    /// <param name="targetDef">The target method definition to check when parsing instructions.</param>
    /// <param name="into">The method whose body the woven body takes the place of, which is the member being woven for
    /// the template and the copy of it for a body of the compiler's own.</param>
    private void ParseBody(MethodBody from, MethodDefinition targetDef, MethodDefinition into)
    {
        var instructions = from.Instructions;
        var filter = new InstructionFilter(instructions);

        // Translate the accessor codes to standard IL codes.
        for (var index = 0; index < instructions.Count; index++)
        {
            // Skip replacement item.
            if (filter.HasReplaced(index)) continue;

            var instruction = instructions[index];

            // Write what the template captured where it read it.
            if (TryInlineCapture(instructions, index, targetDef, filter)) continue;

            // Replace Ldarg. Every form of the load is translated rather than the two which carry the first slot, because
            // each of them names an argument of the template, and what the member being woven holds at that slot is
            // another argument whenever the two do not agree on belonging to an instance. A body which the compiler
            // wrote for a body of the template's own holds the arguments of its own, which the copy of it declares in
            // the same order and which the carrying re-pointed every operand of, so a load of one stands where it was
            // written and is not translated.
            if (m_CarriedBody is null && instruction.TryGetLdargIndex(!targetDef.IsStatic, out var slot))
            {
                filter.Replace(index, CreateLdarg(slot, targetDef));
                continue;
            }

            // Replace operand.
            if (instruction.Operand != null)
            {
                ReplaceOperand(index, filter, targetDef);
            }
        }

        // Apply translations to body.
        filter.ApplyTo(into.Body.Instructions);

        CarryExceptionHandlers(from.ExceptionHandlers, filter, into);
    }

    /// <summary>
    /// Write the regions which the template protects into the body which is woven, so that what the template catches,
    /// disposes in a `finally` or takes a `lock` on is protected where it was woven as well.<para/>
    /// The boundaries of a region are instructions of the template, and each of them stands where it stood: what the
    /// weaving wrote for it. The types which are caught are imported, so that a region which catches a type of another
    /// assembly is written against that assembly in the body which is woven, as every other operand is.
    /// </summary>
    /// <param name="handlers">The regions of the body which was parsed.</param>
    /// <param name="filter">The instruction filter which wrote the body.</param>
    /// <param name="into">The method whose body the woven body takes the place of.</param>
    /// <exception cref="InvalidILException">Thrown when a region begins at an instruction which the body does not hold.</exception>
    private void CarryExceptionHandlers(Mono.Collections.Generic.Collection<ExceptionHandler> handlers, InstructionFilter filter, MethodDefinition into)
    {
        foreach (var handler in handlers)
        {
            var carried = new ExceptionHandler(handler.HandlerType)
            {
                TryStart     = filter.Emitted(handler.TryStart),
                TryEnd       = filter.Emitted(handler.TryEnd),
                HandlerStart = filter.Emitted(handler.HandlerStart),
                HandlerEnd   = filter.Emitted(handler.HandlerEnd),
                FilterStart  = filter.Emitted(handler.FilterStart),
                CatchType = handler.CatchType == null
                    ? null
                    : ModuleLock.Import(Source.Module, handler.CatchType).ParseGenericTokens(Source, Source.Module)
            };

            // A region which begins at nothing would protect nothing, and a boundary which the template holds and the
            // body does not stand for not one of them: a region which ends at nothing is one which is taken for one
            // which reaches the end of the body, so it would protect more than the template wrote it to. A boundary
            // which the template holds none of stands for the end of the body, and is written by leaving it out rather
            // than by naming an instruction.
            if (carried.TryStart == null || carried.HandlerStart == null
                || (handler.TryEnd != null && carried.TryEnd == null)
                || (handler.HandlerEnd != null && carried.HandlerEnd == null)
                || (handler.FilterStart != null && carried.FilterStart == null))
            {
                throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, "$" + filter.Target.Length));
            }

            into.Body.ExceptionHandlers.Add(carried);
        }
    }

    /// <summary>
    /// Copy the locals of one body into the body which is being written, so that every operand which names a local of
    /// the first names the local of the second which stands at the same position.
    /// </summary>
    /// <param name="from">The body whose locals are copied.</param>
    /// <param name="into">The method whose body takes them.</param>
    /// <param name="emptied">Index of the locals which the weaving empties, which are the ones which <see cref="HandleLocals"/> names.</param>
    private void CopyVariables(MethodBody from, MethodDefinition into, ISet<int> emptied)
    {
        var srcVariables = from.Variables;
        if (srcVariables == null) return;

        var desVariables = into.Body.Variables;
        desVariables.Clear();

        for (var i = 0; i < srcVariables.Count; i++)
        {
            // A local which the weaving empties is one which names nothing of the body which is woven, while the type it
            // is declared with is one of the weaver: copying that type would leave the assembly being woven referring to
            // the weaver for a type which nothing of it names, so the local is copied as a type which every assembly
            // holds instead.
            // The token of a generic parameter is read against the member being woven rather than against the body it
            // stands in, because a token names a parameter of that member however deep in a body of the compiler's own
            // it was written. If the variable type holds such a token, get the actual generic parameter type.
            // What the type of a local names is read for a copy as well, because the body of a template is not the only
            // thing the compiler wrote a type for: the local of the stub which an async body leaves behind is the state
            // machine itself, and a local of the woven body which named the machine the compiler wrote would be a member
            // of the assembly being woven pointing at a private type of the assembly the template came from.
            // A local of a body the compiler wrote is the exception: its type names the copy of what it was written
            // from already, which the carrying made, and reading it again would import the type which was carried and
            // leave the local of the woven body naming the type the compiler wrote.
            var typeRef = emptied.Contains(i)
                ? into.Module.TypeSystem.Object
                : m_CarriedBody is null
                    ? m_Carried?.TheCopyOf(srcVariables[i].VariableType)
                      ?? srcVariables[i].VariableType.ParseGenericTokens(Source, into.Module)
                    : srcVariables[i].VariableType;
            desVariables.Add(new VariableDefinition(typeRef));
        }
    }

    /// <summary>
    /// The locals which the handle of a value member is stored into, which are the ones which the weaving empties: every
    /// read of such a local is written as the member itself, and nothing else may reach it.<para/>
    /// Which local those are is the question which the parser of the member asks of the instruction which the handle is
    /// built at, and it is asked here of the same instruction, because the type of a local has to be settled where the
    /// locals are copied, which is before the body is parsed.
    /// </summary>
    /// <param name="bodyInstructions">The instructions of the template.</param>
    /// <returns>Index of every local which the handle of a value member is stored into.</returns>
    private static HashSet<int> HandleLocals(IList<Instruction> bodyInstructions)
    {
        var locals = new HashSet<int>();

        for (var index = 0; index < bodyInstructions.Count; index++)
        {
            if (bodyInstructions[index].Operand is not MethodReference member) continue;

            var memberFlag = GetInstanceMemberFlag(member);
            if (!memberFlag.HasFlag(MemberSymbols.Field) && !memberFlag.HasFlag(MemberSymbols.Property)) continue;

            if (StackWalk.HeldLocal(bodyInstructions, index) is { } handle) locals.Add(handle.Local);
        }

        return locals;
    }
}
