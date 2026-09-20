namespace Gneedle.Inject;

/// <summary>
/// The instruction filter to apply instruction translations. It holds the target collection of instructions and the translation results,
/// and applies the translations to source collection when calling ApplyTo.
/// </summary>
/// <param name="target">The instructions of the template which the translations are read against.</param>
internal sealed class InstructionFilter(Mono.Collections.Generic.Collection<Instruction> target)
{
    /// <summary>
    /// The instruction to replace the original instruction at index. If null, it means to keep the original instruction.
    /// </summary>
    private readonly Instruction?[] m_Replacements = new Instruction[target.Count];

    /// <summary>
    /// The instructions to insert before the original instruction at index, in the order they were inserted. If
    /// null, it means no instruction to insert.
    /// </summary>
    private readonly List<Instruction>?[] m_Insert = new List<Instruction>?[target.Count];

    /// <summary>
    /// The instruction which was written for each instruction of the target, at the same index, or null for one
    /// which was written as nothing at all.<para/>
    /// Where an instruction was written as more than one, this is the first of them, which is where a region or a
    /// branch which named that instruction begins. Where it was written as none, what stands in its place is the
    /// first instruction written after it, which is what <see cref="Emitted"/> answers with.
    /// </summary>
    private readonly Instruction?[] m_Written = new Instruction?[target.Count];

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
    public readonly Instruction[] Target = target.ToArray();

    /// <summary>
    /// The index which each instruction of <see cref="Target"/> stands at, which is where a branch of the body, an
    /// entry of a table of a switch, and the walk which asks whether a symbol stands on every path to an instruction
    /// read the instruction which they name off.<para/>
    /// The instructions of the template are the ones the filter was built around and the array is a copy of the
    /// collection which nothing writes, so the one table answers for as long as the filter stands.
    /// </summary>
    public readonly IReadOnlyDictionary<Instruction, int> Index = BuildIndexOf(target);

    /// <summary>
    /// The index of each instruction of the given collection. Where an instruction stands in it more than once it is
    /// the first of them which is held, which is the one a walk of the collection from the beginning finds.
    /// </summary>
    /// <param name="instructions">The instructions of a body, in the order they are read.</param>
    /// <returns>The index of each of them.</returns>
    private static IReadOnlyDictionary<Instruction, int> BuildIndexOf(Mono.Collections.Generic.Collection<Instruction> instructions)
    {
        var index = new Dictionary<Instruction, int>(instructions.Count);
        for (var at = instructions.Count - 1; at >= 0; at--) index[instructions[at]] = at;
        return index;
    }

    /// <summary>
    /// Check if there is a replacement instruction for the instruction at index.
    /// If true, it means the instruction at index will be replaced with another instruction, and the original instruction will be skipped.
    /// </summary>
    /// <param name="index">Index of the instruction in target collection.</param>
    /// <returns></returns>
    public bool HasReplaced(int index) => m_Replacements[index] != null;

    /// <summary>
    /// The instruction which was written in place of the one at index, or null when the instruction at index is the
    /// one which stands in the body. An instruction which is written as nothing is written as a nop, which is what
    /// the weaving reads for one the body does not hold.
    /// </summary>
    /// <param name="index">Index of the instruction in target collection.</param>
    public Instruction? Replaced(int index) => m_Replacements[index];

    /// <summary>
    /// Replace the instruction at index with Nop, which means to skip the instruction.
    /// </summary>
    /// <param name="index"></param>
    public void Skip(int index) => m_Replacements[index] = Instruction.Create(OpCodes.Nop);

    /// <summary>
    /// Replace the instruction at index with the given instruction. The instruction which was replaced and the one
    /// which replaces it are remembered together, so that an instruction which branches to the first is pointed at
    /// the second: what a branch was written to reach is whatever stands where that instruction stood.<para/>
    /// The replacement which stands is the one which was written last, which is the one the collection of the
    /// replacements holds as well: an instruction which is replaced twice is written over rather than refused,
    /// so that the two answers say the same thing about it.
    /// </summary>
    /// <param name="index">Index of the instruction to be replaced.</param>
    /// <param name="ins">The instruction to replace with.</param>
    public void Replace(int index, Instruction ins)
    {
        m_Replacements[index] = ins;

        // An instruction which is replaced with a nop is one which the body does not hold at all, and a branch to
        // it is pointed at what stands after it instead, which is what ApplyTo does once the body is written.
        if (ins.OpCode != OpCodes.Nop)
        {
            m_Operands[target[index]] = ins;
        }
    }

    /// <summary>
    /// Insert the given instruction before the instruction at index. If there are multiple instructions to insert before the same index, they will be added in order of insertion.
    /// </summary>
    /// <param name="index">Index of the instruction to insert before.</param>
    /// <param name="ins"> The instruction to insert.</param>
    public void Insert(int index, Instruction ins) => (m_Insert[index] ??= []).Add(ins);

    /// <summary>
    /// The instruction which stands where the given instruction of the template stood, which is the first one
    /// written for it, or the first written after it where nothing was written for it at all.
    /// </summary>
    /// <param name="instruction">The instruction of the template which is asked about, or null for a boundary which
    /// the template does not hold, which stands for the end of the body.</param>
    /// <returns>The instruction of the body which stands where that one stood, or null when there is none.</returns>
    public Instruction? Emitted(Instruction? instruction)
    {
        // An instruction which the template does not hold stands where nothing of the body stands, and one which it
        // holds is carried on from the instruction which stands where it stood.
        if (instruction == null || !Index.TryGetValue(instruction, out var index)) return null;

        for (; index < m_Written.Length; index++)
        {
            if (m_Written[index] != null) return m_Written[index];
        }

        return null;
    }

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

        PointBranches(source);
    }

    /// <summary>
    /// Point every branch which names an instruction of the template at the instruction which stands where that one
    /// stood, for the ones which were written as nothing: a branch to an instruction which the body does not hold
    /// would be a branch to nothing. The table of a switch is a branch of many entries, and each of its entries is
    /// pointed at what stands where it stood as well.
    /// </summary>
    /// <param name="source">The source collection which the instructions were applied to.</param>
    /// <exception cref="InvalidILException">Thrown when an instruction which a branch names stands for nothing in the
    /// body which was written, which leaves the branch reaching into the template rather than into the body.</exception>
    private void PointBranches(ICollection<Instruction> source)
    {
        var written = new HashSet<Instruction>(source);

        foreach (var instruction in source)
        {
            // The entries of a table are named by the switch which stands in front of them rather than by a branch,
            // and each of them is carried the same way. The table which is written is one of the body's own rather
            // than the table of the template, which another weave of the same template reads again.
            if (instruction.Operand is Instruction[] table)
            {
                instruction.Operand = table.Select(entry => written.Contains(entry) ? entry : Standing(entry)).ToArray();
                continue;
            }

            // An instruction which the body holds stands where it stood, and one of another body, which a moved
            // body carries, is not read against the instructions of this one.
            if (instruction.Operand is not Instruction ins || written.Contains(ins)) continue;

            instruction.Operand = Standing(ins);
        }
    }

    /// <summary>
    /// The instruction of the body which stands where the given instruction of the template stood, which is where a
    /// branch or an entry of a table which names it is pointed: an instruction which nothing stands for leaves what
    /// names it reaching out of the body it is written in, which is not IL the runtime accepts.
    /// </summary>
    /// <param name="ins">The instruction of the template which is named.</param>
    /// <returns>The instruction of the body which stands where it stood.</returns>
    /// <exception cref="InvalidILException">Thrown when nothing stands for the instruction.</exception>
    private Instruction Standing(Instruction ins)
        => Emitted(ins) ?? throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL,
            "$" + (Index.TryGetValue(ins, out var at) ? at : -1)));

    /// <summary>
    /// If there are instructions to insert before current index, add them to source in the order they were
    /// inserted, which is the order they are read in: the receiver of a call before the arguments of it.
    /// </summary>
    /// <param name="source">The source collection to add instructions.</param>
    /// <param name="index">Index of the instruction in target collection.</param>
    private void AddInsertInstruction(ICollection<Instruction> source, int index)
    {
        if (m_Insert[index] is not { } insert) return;

        foreach (var instruction in insert)
        {
            source.Add(instruction);
            m_Written[index] ??= instruction;
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
                m_Written[index] ??= replacement;
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
            var pointer = Instruction.Create(ins.OpCode, newIns);
            source.Add(pointer);
            m_Written[index] ??= pointer;
        }
        // The table of a switch is the operand which the weaving writes again rather than the instruction, and the
        // instruction which carries it is the one of the template: a template which is woven a second time reads the
        // table which the first weave carried, which names the instructions of the body of that one. The body is
        // given a switch of its own, over a table of its own, so that the template keeps what it wrote.
        else if (ins.Operand is Instruction[] table)
        {
            var pointer = Instruction.Create(ins.OpCode, (Instruction[]) table.Clone());
            source.Add(pointer);
            m_Written[index] ??= pointer;
        }
        else
        {
            source.Add(ins);
            m_Written[index] ??= ins;
        }
    }
}