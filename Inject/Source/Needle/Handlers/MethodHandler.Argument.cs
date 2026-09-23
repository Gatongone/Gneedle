namespace Gneedle.Inject;

partial class MethodHandler
{
    /// <summary>
    /// The instruction which loads the argument at <paramref name="slot"/> of the template, written so that it loads the
    /// argument which the member being woven holds at that slot.
    /// </summary>
    /// <param name="slot">The slot which the template names, as the IL of the template holds it: the receiver of the
    /// template takes the first slot when the template belongs to an instance.</param>
    /// <param name="templateDef">The template whose body names the slot.</param>
    /// <returns>The instruction which loads the argument in the member being woven.</returns>
    /// <exception cref="WeavingException">Thrown when the template reads its own instance, or when the member being woven holds no such argument.</exception>
    private Instruction CreateLdarg(int slot, MethodDefinition templateDef)
    {
        // A macro form carries the slot in the opcode rather than as an operand, so the macro is chosen by the slot
        // which the argument holds in the member being woven, which is not the position of the parameter: the receiver
        // of an instance member takes the slot ahead of the first of them. A slot which no macro holds is loaded
        // through the operand form, which names the parameter itself and leaves the slot of it to be written from the
        // parameter, where the one form of the slot is settled rather than written twice. The parameter is read for
        // that form alone, because the slot which a macro carries is the receiver of a member as often as a parameter
        // of one, and a receiver names no parameter.
        return GetShiftedSlot(slot, templateDef) switch
        {
            0 => Instruction.Create(OpCodes.Ldarg_0),
            1 => Instruction.Create(OpCodes.Ldarg_1),
            2 => Instruction.Create(OpCodes.Ldarg_2),
            3 => Instruction.Create(OpCodes.Ldarg_3),
            _ => Instruction.Create(OpCodes.Ldarg, GetParameterAt(slot, templateDef))
        };
    }

    /// <summary>
    /// The instruction which loads the receiver of a member which a template reached through an instance of its own.
    /// </summary>
    /// <remarks>
    /// A template which names an instance of <c>Instance</c> holds that instance in an argument of its own, and the member
    /// being woven holds the same argument at a slot which is the one the template names shifted by the receivers of the
    /// two. Writing the receiver of the member being woven instead is right only where the instance the template named
    /// is that receiver, which nothing makes it: the argument is what the template reached the member through, so it is
    /// what the member has to be reached through in the body it is woven into.<para/>
    /// A template which named no instance of its own reaches the member of the member being woven, whose receiver is the
    /// load of <c>this</c> which the symbols that name a member of this type are written with: a member which is static
    /// holds no slot for that receiver, so a template which reaches a member of an instance from one is refused.
    /// </remarks>
    /// <param name="instanceIns">The instruction of the template which loads the instance, or null when the template
    /// named none of its own.</param>
    /// <param name="templateDef">The template which the instance is read out of.</param>
    /// <param name="index">Index of the instruction which the receiver is written for, which the fields of a chain
    /// which leads to it are written before.</param>
    /// <param name="filter">The filter which writes the body.</param>
    /// <returns>The instruction which loads the receiver.</returns>
    /// <exception cref="WeavingException">Thrown when the member being woven is static, and therefore holds no receiver
    /// for a member of an instance which the template reached through <c>This</c> or <c>Base</c>.</exception>
    private Instruction CreateReceiver(Instruction? instanceIns, MethodDefinition templateDef, int index, InstructionFilter filter)
    {
        if (instanceIns is { } ins && ins.TryGetLdargIndex(!templateDef.IsStatic, out var slot))
        {
            return CreateLdarg(slot, templateDef);
        }

        // What a body of the compiler's own holds in its receiver is the value the compiler put there: the type it
        // wrote for a lambda which captured, or the state machine of a body which yields or awaits. The instance of the
        // member being woven is not that value, and the body reaches it through the field the compiler wrote for it, so
        // a body which captured no instance reaches it nowhere.
        if (m_CarriedBody is { } carried)
        {
            // A local function which captured nothing is a member of the type which declares the template rather than of
            // a type of its own, and an instance one of those is written against an instance of that type: its receiver
            // is the instance of the member being woven. A local function which reaches an instance without being one -
            // which the compiler writes as a static member of that type - holds no such instance, and the member is
            // reached through the fields of the chain below or refused with it.
            // The receiver of a member copy which the compiler wrote on the type being woven is one of that type, which
            // is what a local function is: what stands in its receiver is an instance of the type being woven, and that
            // is the instance of the member being woven where the template was declared in that same type - which is
            // what the carrying read. A copy of a type the compiler wrote holds an instance of that type instead, which
            // is not the instance of the member, so those are reached through the chain below.
            if (carried.Copy.DeclaringType is { } holder && ReferenceEquals(holder, Source.DeclaringType)
                && !carried.Copy.IsStatic && !Source.IsStatic && m_Carried?.OfTheWovenType == true)
            {
                return Instruction.Create(OpCodes.Ldarg_0);
            }

            // What the body reaches at its receiver is the type the compiler wrote rather than the instance of the member
            // being woven, and that instance lies behind the fields the carrying found: each of them is read before the
            // one after it, and the last is the value the member is reached through.
            if (carried.Receiver.Count == 0)
            {
                throw new WeavingException(string.Format(ErrorMessages.A_BODY_OF_ITS_OWN_REACHES_NO_INSTANCE, Source.FullName));
            }

            var receiver = Instruction.Create(OpCodes.Ldarg_0);
            foreach (var field in carried.Receiver)
            {
                filter.Insert(index, receiver);
                receiver = Instruction.Create(OpCodes.Ldfld, field);
            }

            return receiver;
        }

        // The instance which the member is reached through is the one which the member being woven belongs to, and a
        // member which is static belongs to none: the slot which `this` takes holds its first argument instead, so the
        // member would be called on a value the template was handed for something else.
        if (Source.IsStatic)
        {
            throw new WeavingException(string.Format(ErrorMessages.STATIC_MEMBER_REACHES_AN_INSTANCE_MEMBER, Source.FullName));
        }

        return Instruction.Create(OpCodes.Ldarg_0);
    }

    /// <summary>
    /// The slot which the argument at <paramref name="slot"/> of the template holds in the member being woven.
    /// </summary>
    /// <param name="slot">The slot which the template names.</param>
    /// <param name="templateDef">The template whose body names the slot.</param>
    /// <returns>The slot which the same argument holds in the member being woven.</returns>
    /// <exception cref="WeavingException">Thrown when the template reads its own instance.</exception>
    private int GetShiftedSlot(int slot, MethodDefinition templateDef)
    {
        // A body which the compiler wrote for a body of the template's own holds the arguments of its own: the copy of
        // it declares the same parameters in the same order as the member which the compiler wrote, and the position an
        // argument holds in the body is the position it holds in the copy. Nothing is shifted for one of those, and a
        // load of its receiver is a load of the receiver of the copy, which the carrying re-pointed.
        if (m_CarriedBody is not null) return slot;

        // What the template reads at the slot of its own receiver is that receiver rather than an argument, and no
        // member can be given it: a static member holds no such argument, and an instance one holds another instance in
        // its place. A lambda which captures a variable is an instance method of the type which holds the capture, so a
        // template written as one is a template of this kind.
        if (!templateDef.IsStatic && slot == 0)
        {
            // A template which is declared in the type being woven is written against the instance of that type, which
            // is the instance of the member being woven as well: what such a template loads at its own receiver is that
            // instance, and it stands where it was written. A template given as the delegate which holds it reads the
            // instance the delegate was made from, which is not the instance of the member, and a template which is
            // declared in another type is written against one of that type: both are refused.
            if (m_TemplateClosure is null && !Source.IsStatic && IsDeclaredInTheWovenType(templateDef))
            {
                return 0;
            }

            throw new WeavingException(string.Format(ErrorMessages.TEMPLATE_READS_ITS_OWN_INSTANCE, Source.FullName));
        }

        // The two hold the same arguments in the same order, so an argument moves by one slot exactly when one of them
        // belongs to an instance and the other does not: the receiver of the one which does takes the first slot.
        return slot + (Source.IsStatic ? 0 : 1) - (templateDef.IsStatic ? 0 : 1);
    }

    /// <summary>
    /// Whether the type which declares <paramref name="templateDef"/> is the type being woven: an instance of the type
    /// being woven is an instance of that type, which is what makes the receiver of a template of one the receiver of
    /// the member being woven.
    /// </summary>
    /// <remarks>
    /// Only the type being woven itself is read, and not a base type of it: a base type is a type of another assembly
    /// as often as not, and resolving one here reads an assembly in the middle of a parse. A template of a base type
    /// is left refused until what it needs is read.
    /// </remarks>
    /// <param name="templateDef">The template which is asked about.</param>
    /// <returns>Whether the template belongs to the type being woven.</returns>
    private bool IsDeclaredInTheWovenType(MethodDefinition templateDef)
        => ReferenceEquals(Source.DeclaringType, templateDef.DeclaringType);

    /// <summary>
    /// The parameter which the template reads or writes at a slot, which is the parameter of the member being woven that
    /// holds the same argument.
    /// </summary>
    /// <remarks>
    /// The slot is resolved against the member being woven rather than against the template, because it is the member
    /// which holds the parameter that is written as the operand. The position of that parameter is the position which
    /// the argument holds in the template as well, so it is the slot with the receivers of the two taken off it.
    /// </remarks>
    /// <param name="slot">The slot which the template names.</param>
    /// <param name="templateDef">The template whose body names the slot.</param>
    /// <returns>The parameter of the member being woven which holds the same argument.</returns>
    /// <exception cref="WeavingException">Thrown when the template reads its own instance, or when the member being woven holds no such argument.</exception>
    private ParameterDefinition GetParameterAt(int slot, MethodDefinition templateDef)
    {
        // The parameter which an argument of a body of the compiler's own holds is a parameter of the copy of that
        // body, which is what the carrying re-pointed every operand of it to.
        if (m_CarriedBody is { } carried)
        {
            var at = carried.Copy.IsStatic ? slot : slot - 1;
            if (at < 0 || at >= carried.Copy.Parameters.Count)
            {
                throw new WeavingException(string.Format(ErrorMessages.INVALID_TEMPLATE_PARAMETER, at, Source.FullName));
            }

            return carried.Copy.Parameters[at];
        }

        var position = GetShiftedSlot(slot, templateDef) - (Source.IsStatic ? 0 : 1);
        if (position < 0 || position >= Source.Parameters.Count)
        {
            throw new WeavingException(string.Format(ErrorMessages.INVALID_TEMPLATE_PARAMETER, position, Source.FullName));
        }

        return Source.Parameters[position];
    }
}
