namespace Gneedle.Inject;

/// <summary>
/// The slot which an instruction names, read out of the operand of it.
/// </summary>
internal static class InstructionSlots
{
    /// <param name="ins">Opcode provider.</param>
    extension(Instruction ins)
    {
        /// <summary>
        /// Get the slot of the argument which a load of one names.
        /// </summary>
        /// <remarks>
        /// The slot is the position of the argument rather than that of the parameter, so the receiver of a method which
        /// belongs to an instance takes the first one and the first parameter of such a method follows it. The macro
        /// opcodes hold that slot in the opcode, while the long ones hold it as an operand, which Cecil resolves to the
        /// parameter itself wherever the body still holds the method the operand belongs to, and leaves as the slot
        /// wherever it does not. A parameter is named by its position among the parameters of its method, which is the
        /// slot only when the method belongs to no instance, so the receiver is added back where there is one.
        /// </remarks>
        /// <param name="hasReceiver">Whether the method which the instruction belongs to holds a receiver, which takes
        /// the first slot, ahead of the first parameter of it.</param>
        /// <param name="index">Slot of the ldarg target. It would be -1 when return false.</param>
        /// <returns>False when the instruction opcode is not a ldarg type.</returns>
        internal bool TryGetLdargIndex(bool hasReceiver, out int index)
        {
            index = ins.OpCode.Code switch
            {
                Code.Ldarg_0 => 0,
                Code.Ldarg_1 => 1,
                Code.Ldarg_2 => 2,
                Code.Ldarg_3 => 3,
                Code.Ldarg or Code.Ldarg_S => ins.Operand switch
                {
                    int slot => slot,
                    ParameterReference parameter => parameter.Index + (hasReceiver ? 1 : 0),
                    _ => -1
                },
                _ => -1
            };
            return index != -1;
        }

        /// <summary>
        /// Get the slot of the local which a store names.
        /// </summary>
        /// <remarks>
        /// The slot is the position of the local among the variables of the body. The macro opcodes hold that slot in the
        /// opcode, while the long ones hold it as an operand, which Cecil resolves to the variable itself wherever the
        /// body still holds the method the operand belongs to, and leaves as the slot wherever it does not.
        /// </remarks>
        /// <param name="index">Index of the stloc target. It would be -1 when return false.</param>
        /// <returns>False when the instruction opcode is not a stloc type, or when its operand names no local.</returns>
        internal bool TryGetStlocIndex(out int index)
        {
            index = ins.OpCode.Code switch
            {
                Code.Stloc_0 => 0,
                Code.Stloc_1 => 1,
                Code.Stloc_2 => 2,
                Code.Stloc_3 => 3,
                Code.Stloc or Code.Stloc_S => ins.Operand switch
                {
                    int slot => slot,
                    VariableReference variable => variable.Index,
                    _ => -1
                },
                _ => -1
            };
            return index != -1;
        }

        /// <summary>
        /// Get the slot of the local which a load names.
        /// </summary>
        /// <remarks>
        /// The operand is read the way the one of a store is, which is described there. Both forms hand back the slot
        /// rather than the variable, because a slot is what the caller addresses a local by.
        /// </remarks>
        /// <param name="index">Index of the ldloc target. It would be -1 when return false.</param>
        /// <returns>False when the instruction opcode is not a ldloc type, or when its operand names no local.</returns>
        internal bool TryGetLdlocIndex(out int index)
        {
            index = ins.OpCode.Code switch
            {
                Code.Ldloc_0 => 0,
                Code.Ldloc_1 => 1,
                Code.Ldloc_2 => 2,
                Code.Ldloc_3 => 3,
                Code.Ldloc or Code.Ldloc_S => ins.Operand switch
                {
                    int slot => slot,
                    VariableReference variable => variable.Index,
                    _ => -1
                },
                _ => -1
            };
            return index != -1;
        }
    }
}