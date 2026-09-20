using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// The method which a template is woven into, which the text of the body of a member is read against: a token stands for
/// a generic parameter of it, and a type of the body is read out of the module which it belongs to rather than out of the
/// module which the template was compiled into.
/// </summary>
internal readonly struct ParseContext
{
    /// <summary>The method which the body is woven into, which is the member the template is written for.</summary>
    public readonly MethodDefinition Source;

    /// <summary>
    /// Hold the method which a body is read against.
    /// </summary>
    /// <param name="source">The method which the body is woven into.</param>
    public ParseContext(MethodDefinition source) => Source = source;

    /// <summary>The module which the body belongs to, which is the one a type of another assembly is read out of.</summary>
    public ModuleDefinition Module => Source.Module;
}

/// <summary>
/// What the instructions of a body do to the evaluation stack, and the walks of the graph which they make: the value
/// which an instruction leaves, the type of it, and every path which the body takes from one instruction to another.
/// </summary>
internal static class StackWalk
{
    /// <summary>
    /// Try to get the accessor of the placeholder which is written where the value was pushed, which is the call of
    /// `ValuableMember.Get` or `ValuableMember.Set` that the value is the receiver of.<para/>
    /// The accessor of another placeholder may stand between the two, and it is the one which its own value is the
    /// receiver of: the values which the instructions between push and take off the stack are counted, and the accessor
    /// which is looked for is the one which is reached with exactly as many values above the placeholder's own as it
    /// takes arguments. An instruction whose count the walk cannot tell ends it, and what the first accessor of the body
    /// is stands for the one which was looked for.
    /// </summary>
    /// <param name="bodyInstructions">The instruction collection to search.</param>
    /// <param name="startIndex">The index of the instruction which follows the call which pushed the value.</param>
    /// <param name="isGet">Output whether the accessor is `get` or `set`.</param>
    /// <param name="index">Output the index of the `call` instruction if found.</param>
    /// <returns>True if the `call` instruction of `ValuableMember.Get` or `ValuableMember.Set` is found; otherwise, false.</returns>
    internal static bool TryGetNextGetOrSet(IReadOnlyList<Instruction> bodyInstructions, int startIndex, out bool isGet, out int index)
    {
        if (TryWalkToTheAccessor(bodyInstructions, startIndex, out isGet, out index)) return true;

        return TryGetFirstGetOrSet(bodyInstructions, startIndex, out isGet, out index);
    }
    /// <summary>
    /// Try to get the accessor which the value pushed ahead of the start index is the receiver of, which is the call of
    /// `ValuableMember.Get` or `ValuableMember.Set` that the value stands under.<para/>
    /// The accessor of another value may stand between the two, and it is the one which its own value is the receiver
    /// of: the values which the instructions between push and take off the stack are counted, and the accessor which is
    /// looked for is the one which is reached with exactly as many values above the value which is looked for as it
    /// takes arguments. An instruction whose count the walk cannot tell, or one which takes the value itself off the
    /// stack, ends it and nothing is answered.
    /// </summary>
    /// <param name="bodyInstructions">The instruction collection to search.</param>
    /// <param name="startIndex">The index of the instruction which follows the one which pushed the value.</param>
    /// <param name="isGet">Output whether the accessor is `get` or `set`.</param>
    /// <param name="index">Output the index of the `call` instruction if found.</param>
    /// <returns>True if the `call` instruction of `ValuableMember.Get` or `ValuableMember.Set` is found; otherwise, false.</returns>
    internal static bool TryWalkToTheAccessor(IReadOnlyList<Instruction> bodyInstructions, int startIndex, out bool isGet, out int index)
    {
        // How many values stand on the stack above the one which the placeholder pushed. Every push counts up and every
        // take counts down, and a count below zero is one which took the placeholder's value itself off the stack.
        var above = 0;
        for (var i = startIndex; i < bodyInstructions.Count; i++)
        {
            if (IsAnAccessor(bodyInstructions[i], out var accessorIsGet))
            {
                if (above == (accessorIsGet ? 0 : 1))
                {
                    isGet = accessorIsGet;
                    index = i;
                    return true;
                }

                // The accessor of a value which is not this one: it takes the receiver off the stack and leaves the
                // value which it reads there, or nothing at all where it writes one.
                above += accessorIsGet ? 0 : -2;
                continue;
            }

            if (StackDelta(bodyInstructions[i]) is not { } delta || above + delta < 0) break;
            above += delta;
        }

        isGet = false;
        index = 0;
        return false;
    }
    /// <summary>
    /// Every accessor of the value member which a local holds the handle of, which is each read of the local together
    /// with the accessor which that read is the receiver of.<para/>
    /// The handle which a placeholder stands for is a value which only the weaving can write, so a local which holds one
    /// stands for the member wherever it is read and for nothing else: a read which is something other than the receiver
    /// of an accessor, or a second write to the local, is a use which nothing can be written for, and nothing is
    /// answered for it.
    /// </summary>
    /// <param name="bodyInstructions">The instructions of the body which is parsed.</param>
    /// <param name="local">Index of the local which holds the handle.</param>
    /// <returns>Index of every read of the local, of the accessor which it is the receiver of and of whether that
    /// accessor reads the member or writes it, or null when the local stands for more than the member.</returns>
    internal static List<(int Read, int Accessor, bool IsGet)>? AccessorsOfAHeldHandle(IReadOnlyList<Instruction> bodyInstructions, int local)
    {
        var accessors = new List<(int Read, int Accessor, bool IsGet)>();
        var stores = 0;

        for (var i = 0; i < bodyInstructions.Count; i++)
        {
            var ins = bodyInstructions[i];
            if (ins.TryGetStlocIndex(out var written) && written == local)
            {
                stores++;
                continue;
            }

            if (!ins.TryGetLdlocIndex(out var read) || read != local)
            {
                // A form which names the local without loading it, such as the address which `ldloca` takes, is a use
                // which the accessors which are read here say nothing about.
                if (ins.Operand is VariableReference variable && variable.Index == local) return null;

                continue;
            }

            if (!TryWalkToTheAccessor(bodyInstructions, i + 1, out var isGet, out var accessor)) return null;

            accessors.Add((i, accessor, isGet));
        }

        return stores == 1 ? accessors : null;
    }
    /// <summary>
    /// The first accessor of the body after the start index, which is what the value of a placeholder stood for before
    /// the accessors were told apart from each other.
    /// </summary>
    /// <param name="bodyInstructions">The instruction collection to search.</param>
    /// <param name="startIndex">The start index to search from.</param>
    /// <param name="isGet">Output whether the accessor is `get` or `set`.</param>
    /// <param name="index">Output the index of the `call` instruction if found.</param>
    /// <returns>True if an accessor is found; otherwise, false.</returns>
    internal static bool TryGetFirstGetOrSet(IReadOnlyList<Instruction> bodyInstructions, int startIndex, out bool isGet, out int index)
    {
        isGet = false;
        for (var i = startIndex; i < bodyInstructions.Count; i++)
        {
            if (!IsAnAccessor(bodyInstructions[i], out var accessorIsGet)) continue;

            isGet = accessorIsGet;
            index = i;
            return true;
        }

        index = 0;
        return false;
    }
    /// <summary>
    /// Whether an instruction is the call of an accessor of a value member, which is what reads or writes the field or
    /// the property which a placeholder stands for.
    /// </summary>
    /// <param name="instruction">The instruction which is read.</param>
    /// <param name="isGet">Output whether the accessor reads the member rather than writing it.</param>
    /// <returns>Whether the instruction is such a call.</returns>
    internal static bool IsAnAccessor(Instruction instruction, out bool isGet)
    {
        isGet = false;
        if (instruction.OpCode != OpCodes.Callvirt || instruction.Operand is not MethodReference
        {
            DeclaringType:
            {
                Name     : nameof(ValuableMember) or nameof(ValuableMember) + "`1",
                Namespace: nameof(Gneedle) + "." + nameof(Inject)
            }
        } method) return false;

        if (method.Name is not (nameof(ValuableMember.Get) or nameof(ValuableMember.Set))) return false;

        isGet = method.Name.Equals(nameof(ValuableMember.Get));
        return true;
    }
    /// <summary>
    /// The values which an instruction takes off the stack and the values it leaves on it, or null when the walk cannot
    /// tell.<para/>
    /// The two are what tells the accessor of a placeholder from the accessor of one which is written inside the
    /// expression of it, and what tells an expression which is written beside a delegate from the call of it: a value
    /// which a member is handed is taken off the stack where the call of it is reached, and what the call leaves in its
    /// place is a value of its own rather than the one it was handed. They are read off the instruction alone rather
    /// than off the member it names where the instruction carries one, which is what lets them be carried over a member
    /// the assembly being woven cannot resolve.
    /// </summary>
    /// <param name="instruction">The instruction which is counted.</param>
    /// <returns>The values which the instruction takes and the values it leaves, or null when it is not one which the
    /// walk reads.</returns>
    internal static (int Taken, int Left)? StackEffect(Instruction instruction)
    {
        var code = instruction.OpCode.Code;
        switch (code)
        {
            // The loads, which push a value of their own rather than one which the stack held already, and the field of
            // no instance among them, which is read off the type rather than off a value.
            case Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg or Code.Ldarg_S
              or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc or Code.Ldloc_S
              or Code.Ldarga or Code.Ldarga_S or Code.Ldloca or Code.Ldloca_S
              or Code.Ldc_I4_M1 or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or Code.Ldc_I4_3 or Code.Ldc_I4_4
              or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or Code.Ldc_I4 or Code.Ldc_I4_S
              or Code.Ldc_I8 or Code.Ldc_R4 or Code.Ldc_R8 or Code.Ldstr or Code.Ldnull or Code.Ldftn or Code.Ldtoken
              or Code.Ldsfld or Code.Ldsflda or Code.Sizeof:
                return (0, 1);

            // The loads which read what they are handed, which is the value a field is read off, the address one is read
            // through, and the array or the element which stands at it: what each of them leaves stands in the place of
            // the value it took rather than above it.
            case Code.Ldfld or Code.Ldflda or Code.Ldobj or Code.Ldlen
              or Code.Ldind_I1 or Code.Ldind_I2 or Code.Ldind_I4 or Code.Ldind_I8 or Code.Ldind_I or Code.Ldind_R4
              or Code.Ldind_R8 or Code.Ldind_Ref or Code.Ldind_U1 or Code.Ldind_U2 or Code.Ldind_U4:
                return (1, 1);

            // The stores which take what they write and nothing else, and the pop, which takes one value.
            case Code.Starg or Code.Starg_S or Code.Stloc or Code.Stloc_S or Code.Stloc_0 or Code.Stloc_1
              or Code.Stloc_2 or Code.Stloc_3 or Code.Stsfld or Code.Pop:
                return (1, 0);

            // The stores which take where they write as well as what they write, which is the receiver of a field, the
            // address of a value, and the address of an element of an array or of an element of an array of addresses.
            case Code.Stfld or Code.Stobj
                            or Code.Stind_I or Code.Stind_I1 or Code.Stind_I2 or Code.Stind_I4 or Code.Stind_I8 or Code.Stind_R4
                            or Code.Stind_R8 or Code.Stind_Ref:
                return (2, 0);

            // The instructions which leave what they were handed, of another type.
            case Code.Conv_I1 or Code.Conv_I2 or Code.Conv_I4 or Code.Conv_I8 or Code.Conv_Ovf_I1 or Code.Conv_Ovf_I2
              or Code.Conv_Ovf_I4 or Code.Conv_Ovf_I8 or Code.Conv_Ovf_U1 or Code.Conv_Ovf_U2 or Code.Conv_Ovf_U4
              or Code.Conv_Ovf_U8 or Code.Conv_Ovf_I_Un or Code.Conv_Ovf_U_Un or Code.Conv_R4 or Code.Conv_R8
              or Code.Conv_R_Un or Code.Conv_U1 or Code.Conv_U2 or Code.Conv_U4 or Code.Conv_U8
              or Code.Conv_I or Code.Conv_U or Code.Neg or Code.Not
              or Code.Box or Code.Unbox or Code.Unbox_Any or Code.Castclass or Code.Isinst or Code.Ckfinite:
                return (1, 1);

            // The instructions which take two values and leave one.
            case Code.Add or Code.Sub or Code.Mul or Code.Div or Code.Div_Un or Code.Rem or Code.Rem_Un
              or Code.And or Code.Or or Code.Xor or Code.Shl or Code.Shr or Code.Shr_Un
              or Code.Ceq or Code.Cgt or Code.Cgt_Un or Code.Clt or Code.Clt_Un
              or Code.Ldelem_Any or Code.Ldelem_I or Code.Ldelem_I1 or Code.Ldelem_I2 or Code.Ldelem_I4
              or Code.Ldelem_I8 or Code.Ldelem_R4 or Code.Ldelem_R8 or Code.Ldelem_Ref or Code.Ldelem_U1
              or Code.Ldelem_U2 or Code.Ldelem_U4:
                return (2, 1);

            // The instructions which take three values and leave none.
            case Code.Stelem_Any or Code.Stelem_I or Code.Stelem_I1 or Code.Stelem_I2 or Code.Stelem_I4
              or Code.Stelem_I8 or Code.Stelem_R4 or Code.Stelem_R8 or Code.Stelem_Ref:
                return (3, 0);

            // The instruction which takes the length of an array off the stack and leaves the array in its place.
            case Code.Newarr:
                return (1, 1);

            // The instruction which leaves the value it was handed where it was and one more above it.
            case Code.Dup:
                return (0, 1);

            // A call takes the arguments which the reference names, which the signature counts without resolving the
            // member they are named on, and leaves what it hands back.
            case Code.Call or Code.Callvirt or Code.Newobj when instruction.Operand is MethodReference method:
                var taken = method.Parameters.Count + (method.HasThis && code != Code.Newobj ? 1 : 0);
                var left = code == Code.Newobj || method.ReturnType.MetadataType != MetadataType.Void ? 1 : 0;
                return (taken, left);

            default:
                return null;
        }
    }
    /// <summary>
    /// The number of values which an instruction leaves on the stack, counted against the number it takes off it, or
    /// null when the walk cannot tell.<para/>
    /// The count is what tells the value which a placeholder was built around from the instructions which stand before
    /// it, which are walked from the last of them back to the first: what an instruction leaves stands above what it
    /// was handed rather than in the place of it where the walk goes backwards, so the two are counted against each
    /// other rather than apart.
    /// </summary>
    /// <param name="instruction">The instruction which is counted.</param>
    /// <returns>The count, or null when the instruction is not one which the walk reads.</returns>
    internal static int? StackDelta(Instruction instruction)
        => StackEffect(instruction) is { } effect ? effect.Left - effect.Taken : null;
    /// <summary>
    /// The value which an instance of <see cref="Instance"/> was built around, which is what the member the placeholder
    /// names is reached through.
    /// </summary>
    /// <param name="first">Index of the first instruction of the value.</param>
    /// <param name="last">Index of the last instruction of the value, which leaves it on the stack.</param>
    /// <param name="load">The instruction which loads the value where the template named an argument of its own, or null
    /// where the template computed the value instead.</param>
    internal readonly struct InstanceValue(int first, int last, Instruction? load)
    {
        /// <summary>Index of the first instruction of the value.</summary>
        public readonly int First = first;

        /// <summary>Index of the last instruction of the value, which leaves it on the stack.</summary>
        public readonly int Last = last;

        /// <summary>The instruction which loads the value, or null where the template computed it.</summary>
        public readonly Instruction? Load = load;
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
    internal static bool TheSymbolIsOnEveryPathTo(IReadOnlyList<Instruction> bodyInstructions, int callIndex, int target, MethodDefinition targetDef)
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
    internal static IEnumerable<Instruction> SuccessorsOf(Instruction ins)
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
    internal static (int Store, int Local)? HeldLocal(IList<Instruction> bodyInstructions, int callIndex)
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
    /// Compare an expected parameter type against a type inferred from the evaluation stack.
    /// Integer-family types (bool/char/[s]byte/[u]short/int) are all loaded via <c>ldc.i4.*</c>
    /// and therefore indistinguishable on the stack, so they are treated as compatible.
    /// </summary>
    /// <param name="module">The module which the body belongs to, which is the one a type of another assembly is read out of.</param>
    /// <param name="expected">The type which the call expects the value to be.</param>
    /// <param name="actual">The type of the value which the evaluation stack holds.</param>
    /// <param name="pushedBy">The instruction which pushed the value.</param>
    internal static bool StackTypeMatches(ModuleDefinition module, TypeReference expected, TypeReference actual, Instruction pushedBy)
        => TypeName.HasSameName(expected, actual)
            || (IsI4Compatible(expected) && IsI4Compatible(actual))
            // An enumeration is carried as the value under it, which is what a template which computes one out of an
            // integer leaves on the stack: there is no instruction which names the enumeration.
            || (IsI4Compatible(actual) && HasAnI4UnderlyingType(module, expected))
            // A value which was boxed is carried as the type it was boxed from, and it is the value which a call of a
            // parameter of `object` is made with rather than a reference of a type which names `object`.
            || (pushedBy.OpCode.Code == Code.Box && expected.MetadataType == MetadataType.Object);
    /// <summary>
    /// Whether the values of a type are carried by the stack as 4-byte integers, which is what an enumeration of an
    /// integer under it is, while a structure of the same width is carried as a value of its own type.
    /// </summary>
    /// <param name="module">The module which the body belongs to, which is the one a type of another assembly is read out of.</param>
    /// <param name="type">The type which is read.</param>
    /// <returns>Whether the type is an enumeration which the stack carries as a 4-byte integer.</returns>
    internal static bool HasAnI4UnderlyingType(ModuleDefinition module, TypeReference type)
    {
        try
        {
            return type.ResolveDefinition(module) is {IsEnum: true} definition
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
    /// Whether the type of the value which a call hands back is that of a value at all, which is what tells a member
    /// which hands a value back from one which hands nothing back: a call of a member which hands nothing back leaves
    /// nothing where it stood, so no value which a delegate hands back names such a call.
    /// </summary>
    /// <param name="returnType">The type which the signature of the call hands back.</param>
    /// <returns>Whether a value is handed back.</returns>
    internal static bool HandsAValueBack(TypeReference returnType) => returnType.MetadataType != MetadataType.Void;
    /// <summary>
    /// Whether the value which a member hands back is the value which the delegate describes it with: the values of the
    /// integer family are carried by the stack as the same value whatever the width of the type which names them, so a
    /// member which hands back an int32 is one which the delegate may describe with an int32 as well, and one which
    /// hands back the value under an enumeration is one which it may describe with that enumeration.
    /// </summary>
    /// <param name="module">The module which the body belongs to, which is the one a type of another assembly is read out of.</param>
    /// <param name="member">The type of the value which the member hands back.</param>
    /// <param name="described">The type of the value which the delegate hands back.</param>
    /// <returns>Whether the two name the same value.</returns>
    internal static bool TheSameValueIsHandedBack(ModuleDefinition module, TypeReference member, TypeReference described)
        => TypeName.HasSameName(member, described)
           || (IsI4Compatible(member) && IsI4Compatible(described))
           || (IsI4Compatible(described) && HasAnI4UnderlyingType(module, member));
    /// <summary>
    /// Whether the type is represented as a 4-byte integer on the CLR evaluation stack,
    /// i.e. loaded via the <c>ldc.i4.*</c> opcodes and thus not distinguishable by opcode alone.
    /// </summary>
    internal static bool IsI4Compatible(TypeReference type)
        => type.MetadataType is MetadataType.Boolean
                             or MetadataType.Char
                             or MetadataType.SByte
                             or MetadataType.Byte
                             or MetadataType.Int16
                             or MetadataType.UInt16
                             or MetadataType.Int32
                             or MetadataType.UInt32;
    /// <summary>
    /// Whether an instruction leaves a value of a type which is not the type of the value it is handed, which is what
    /// the conversions, the casts, the boxes and the reads do.
    /// </summary>
    /// <param name="ins">The instruction which is read.</param>
    /// <returns>Whether the instruction leaves another value in place of the one it is handed.</returns>
    internal static bool LeavesAValueInPlaceOfTheOneItIsHanded(Instruction ins) => ins.OpCode.Code is
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
    /// <param name="context">The method which the body is woven into, which is what the instructions of it are read against.</param>
    /// <param name="ins">The instruction which is read.</param>
    /// <param name="targetDef">The template which the instruction belongs to.</param>
    /// <param name="type">The type of the value which the instruction leaves, or null when it leaves none.</param>
    /// <returns>Whether the instruction leaves a value on the stack, <see cref="void"/> being none.</returns>
    internal static bool TryGetStackType(ParseContext context, Instruction ins, MethodDefinition targetDef, out TypeReference? type)
    {
        var module = context.Module;
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
            Code.Ldarga or Code.Ldarga_S or Code.Ldloca or Code.Ldloca_S                           => GetAddressType(context, ins, targetDef),     // Address
            Code.Newobj when ins.Operand is MethodReference ctor                                   => ctor.DeclaringType,                 // Newobj
            Code.Call or Code.Callvirt or Code.Ldftn when ins.Operand is MethodReference methodRef => ResolveMethodReturnType(methodRef), // Call
            _                                                                                      => GetArgType(context, ins, targetDef)          // Args
        };

        return type != null && type != typeSystem.Void;
    }
    /// <summary>
    /// The type of the argument which an instruction loads, which is the type of the parameter at the position it
    /// loads, or the type which declares the template when it loads the receiver.
    /// </summary>
    /// <param name="context">The method which the body is woven into, which is what the instructions of it are read against.</param>
    /// <param name="instruction">The instruction which loads the argument.</param>
    /// <param name="targetDef">The template which the instruction belongs to, whose parameters and staticness the position is read against.</param>
    /// <returns>The type of the argument, or null when the instruction loads none.</returns>
    internal static TypeReference? GetArgType(ParseContext context, Instruction instruction, MethodDefinition targetDef)
    {
        var isStatic = targetDef.IsStatic;
        if (!instruction.TryGetLdargIndex(!isStatic, out var slot)) return null;
        if (!isStatic && slot == 0) return targetDef.DeclaringType;

        // The parameter of a template is a token when it stands for a generic parameter of the method being woven, just
        // as the parameter of a delegate is, so it is parsed to that parameter before the type is compared with anything.
        // The load may name a slot which the template holds no parameter for, which is a body the weaving refuses with a
        // message of its own rather than a type to compare against.
        return ArgumentAt(slot, targetDef)?.ParseGenericTokens(context.Source, context.Module);
    }
    /// <summary>
    /// The type of the address which an instruction which reads the address of a value leaves on the stack, which is the
    /// type of the value that the address is of, by reference.
    /// </summary>
    /// <param name="context">The method which the body is woven into, which is what the instructions of it are read against.</param>
    /// <param name="ins">The instruction which reads the address.</param>
    /// <param name="targetDef">The template which the instruction belongs to, whose arguments and locals the operand names.</param>
    /// <returns>The type of the address, or null when the operand names no argument and no local.</returns>
    internal static TypeReference? GetAddressType(ParseContext context, Instruction ins, MethodDefinition targetDef)
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

        return addressed is { } type ? new ByReferenceType(type.ParseGenericTokens(context.Source, context.Module)) : null;
    }
    /// <summary>
    /// The type of the argument which a slot names, the receiver being the slot which is taken first where the template
    /// belongs to an instance.
    /// </summary>
    /// <param name="slot">Slot of the argument.</param>
    /// <param name="targetDef">The template whose arguments the slot is read against.</param>
    /// <returns>The type of the argument, or null when the slot names none of them.</returns>
    internal static TypeReference? ArgumentAt(int slot, MethodDefinition targetDef)
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
    internal static TypeReference ResolveMethodReturnType(MethodReference methodRef)
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
    internal static TypeReference ResolveDelegateParameterType(TypeReference parameterType, Mono.Collections.Generic.Collection<TypeReference>? genericArguments)
        => parameterType is GenericParameter parameter && genericArguments != null && parameter.Position < genericArguments.Count
            ? genericArguments[parameter.Position]
            : parameterType;

    /// <summary>
    /// The stack which the body being parsed builds as it is walked, which holds the type of every value that is pushed
    /// so that the arguments a call is made with can be compared with the parameters of the member it calls.<para/>
    /// The values themselves are of no interest, and the instruction which pushed each of them is kept only so that a
    /// value which is read out of the stack again can be told apart from one which was never on it.
    /// </summary>
    internal class ParameterStack
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
