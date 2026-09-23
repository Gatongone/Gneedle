namespace Gneedle.Inject;

partial class MethodHandler
{
    /// <summary>
    /// Whether the two instructions which stand ahead of the name of a member are the arrangement of
    /// <c>Static.From("TypeName")</c>, which is the call of <c>From</c> on the placeholder with the text it reads ahead
    /// of it. The two are consumed by the weaving rather than woven, and the name of the member stands after them.<para/>
    /// What the text is is not read here, because what a caller wants of it differs: one which writes the name of the
    /// member needs the name of the type, and one which reads the pattern alone needs only where the name stands.
    /// </summary>
    /// <param name="filter">The filter which holds the instructions of the template.</param>
    /// <param name="nameIndex">Index of the instruction which loads the name of the member.</param>
    /// <returns>Whether the name of the member stands after that arrangement.</returns>
    private static bool IsAStaticFrom(InstructionFilter filter, int nameIndex)
    {
        if (nameIndex < 2) return false;

        var callFrom = filter.Target[nameIndex - 1];
        return callFrom.OpCode == OpCodes.Call
            && callFrom.Operand is MethodReference {Name: "From", DeclaringType: var declaringType}
            && declaringType.FullName == Static.TYPE_NAME
            && filter.Target[nameIndex - 2].OpCode == OpCodes.Ldstr;
    }

    /// <summary>
    /// The type which the arrangement of <c>Static.From("TypeName")</c> ahead of the name of a member names, which is
    /// the type the member is reached on rather than the type which declares the placeholder.<para/>
    /// The type is resolved from the text of the name, so a member of a type of another assembly is named the way that
    /// assembly names it.
    /// </summary>
    /// <param name="filter">The filter which holds the instructions of the template.</param>
    /// <param name="nameIndex">Index of the instruction which loads the name of the member.</param>
    /// <returns>The definition of the type which was named, or null when the arrangement is not there or what it reads
    /// is not the text of a name.</returns>
    private TypeDefinition? TypeNamedByAStaticFrom(InstructionFilter filter, int nameIndex)
    {
        if (!IsAStaticFrom(filter, nameIndex)) return null;

        return filter.Target[nameIndex - 2].Operand is string fullTypeName
            ? DeclaringTypeHandler.AssemblyHandler.GetCecilType(fullTypeName).Definition
            : null;
    }

    /// <summary>
    /// The value which an instance of <see cref="Instance"/> was built around, which the member that the name stands for
    /// is reached through.<para/>
    /// The placeholder is handed an array which holds the value as its only element, and the sequence which builds that
    /// array is dropped by the weaving: what is left of the instance is the value itself.
    /// </summary>
    /// <remarks>
    /// The value is an expression of the template rather than one instruction of it, and which instructions of the body
    /// hold it is read off the stack: the store which fills the array consumes the value, so the instructions which leave
    /// exactly one value between the index of the array and that store are the value.
    /// </remarks>
    /// <param name="filter">The filter which the body is written through.</param>
    /// <param name="nameIndex">Index of the instruction which loads the name of the member.</param>
    /// <param name="targetDef">The template which the instructions are read out of.</param>
    /// <param name="instance">The value which the instance was built around.</param>
    /// <returns>Whether the member was reached through an instance of <see cref="Instance"/> which holds such a value.</returns>
    private static bool TryGetInstanceValue(InstructionFilter filter, int nameIndex, MethodDefinition targetDef, out StackWalk.InstanceValue instance)
    {
        instance = default;
        var bodyInstructions = filter.Target;

        // The name of such a member is preceded by the construction of the instance, which is handed an array of one
        // element: what the value is, is what that element holds.
        if (nameIndex < 2) return false;

        var constructor = bodyInstructions[nameIndex - 1];
        if (constructor.OpCode != OpCodes.Newobj
            || constructor.Operand is not MethodReference {Name: ".ctor", DeclaringType: var declaringType}
            || declaringType.FullName != Instance.TYPE_NAME)
        {
            return false;
        }

        if (bodyInstructions[nameIndex - 2].OpCode != OpCodes.Stelem_Ref) return false;

        var values = 0;
        for (var index = nameIndex - 3; index >= 0; index--)
        {
            var instruction = bodyInstructions[index];

            // A step which writes nothing leaves the count where it was, and one whose count the walk cannot tell leaves
            // the whole of the value unknown: a value which is not read is not written into the body either.
            if (instruction.OpCode == OpCodes.Nop) continue;
            if (StackWalk.StackDelta(instruction) is not { } delta) return false;

            values += delta;
            if (values != 1) continue;

            // What stands ahead of the value is the array which is being filled: the value is an element of a new array of
            // one, and the sequence which builds that array is the whole of what the placeholder was built around.
            if (index < 4
                || bodyInstructions[index - 1].OpCode.Code != Code.Ldc_I4_0
                || bodyInstructions[index - 2].OpCode != OpCodes.Dup
                || bodyInstructions[index - 3].OpCode != OpCodes.Newarr
                || bodyInstructions[index - 4].OpCode.Code != Code.Ldc_I4_1)
            {
                return false;
            }

            // The load of an argument is held at the same slot by every member which the template is woven into, so it is
            // written where the name of the member stands rather than where it stood. What the template computed is held
            // where it computed it, and nothing of the weaving could write it anywhere else.
            var element = index == nameIndex - 3 ? bodyInstructions[index] : null;
            instance = new StackWalk.InstanceValue(index, nameIndex - 3,
                element != null && element.TryGetLdargIndex(!targetDef.IsStatic, out _) ? element : null);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The type of the value which an instance of <see cref="Instance"/> was built around, which is the type which the
    /// member that the name stands for is looked up on.
    /// </summary>
    /// <param name="filter">The filter which the body is written through.</param>
    /// <param name="index">Index of the instruction which leaves the value on the stack.</param>
    /// <param name="targetDef">The template which the instructions are read out of.</param>
    /// <returns>The type of the value, or null when the walk cannot tell it.</returns>
    private TypeReference? GetValueType(InstructionFilter filter, int index, MethodDefinition targetDef)
    {
        // What the weaving wrote for the value is what its type is read off rather than what the template held there,
        // because the instructions ahead of the name were parsed before it: a value which another placeholder was built
        // around stands in the body as the member which that one stands for rather than as the handle it was read through.
        var instruction = filter.Replaced(index) ?? filter.Target[index];

        // An instruction which the body does not hold stands for nothing which could be read, and a value whose end was
        // dropped is one which the walk cannot tell the type of.
        if (instruction.OpCode == OpCodes.Nop) return null;

        // A local holds a value of a type which its declaration names and the instruction which reads it does not.
        if (instruction.TryGetLdlocIndex(out var local) && local >= 0 && local < targetDef.Body.Variables.Count)
        {
            return targetDef.Body.Variables[local].VariableType.ParseGenericTokens(Source, Source.Module);
        }

        return StackWalk.TryGetStackType(Context, instruction, targetDef, out var type) ? type : null;
    }
}
