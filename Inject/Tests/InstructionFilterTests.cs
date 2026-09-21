using Mono.Cecil.Cil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

/// <summary>
/// Tests for the filter which holds the instructions of a template and the translation which the weaving writes for
/// them, which reads the instruction which a branch names and the one which a boundary stands for off the index of the
/// instructions rather than off a walk of them for each one.<para/>
/// The index is what the two answers are read through: the instruction of the body which stands where an instruction of
/// the template stood, and the index which an instruction of the template is reported by when nothing stands for it.
/// </summary>
[TestFixture]
public class InstructionFilterTests
{
    [Test]
    public void The_Index_Of_An_Instruction_Is_Where_It_Stands_In_The_Target()
    {
        var instructions = Instructions(3);
        var filter = new InstructionFilter(instructions);

        Assert.That(filter.Target, Is.EqualTo(instructions.ToArray()));
        for (var index = 0; index < instructions.Count; index++)
        {
            Assert.That(filter.Index[instructions[index]], Is.EqualTo(index), "an instruction is held at another index than the one it stands at.");
        }
    }

    [Test]
    public void An_Instruction_Which_The_Template_Does_Not_Hold_Stands_For_Nothing()
    {
        var filter = new InstructionFilter(Instructions(2));
        filter.ApplyTo(new List<Instruction>());
        Assert.Multiple(() =>
        {
            Assert.That(filter.Emitted(null), Is.Null, "the boundary which the template does not hold was answered with an instruction.");
            Assert.That(filter.Emitted(Instruction.Create(OpCodes.Nop)), Is.Null, "an instruction of another body was answered with one of this body.");
        });
    }

    [Test]
    public void An_Instruction_Which_Nothing_Was_Written_For_Stands_Where_The_One_After_It_Was_Written()
    {
        var instructions = Instructions(3);
        var filter = new InstructionFilter(instructions);
        filter.Skip(1);

        var body = new List<Instruction>();
        filter.ApplyTo(body);
        Assert.Multiple(() =>
        {
            Assert.That(body, Has.Count.EqualTo(2));
            Assert.That(filter.Emitted(instructions[0]), Is.SameAs(body[0]));
            Assert.That(filter.Emitted(instructions[1]), Is.SameAs(body[1]), "an instruction which was written as nothing does not stand where the instruction after it was written.");
            Assert.That(filter.Emitted(instructions[2]), Is.SameAs(body[1]));
        });
    }

    [Test]
    public void An_Instruction_Of_Another_Body_Is_Reported_By_An_Index_Of_Nowhere()
    {
        var filter = new InstructionFilter(Instructions(2));
        var body = new List<Instruction> {Instruction.Create(OpCodes.Br, Instruction.Create(OpCodes.Nop))};

        var thrown = Assert.Throws<InvalidILException>(() => filter.ApplyTo(body));
        Assert.That(thrown!.Message, Is.EqualTo(string.Format(ErrorMessages.INVALID_IL, "$-1")),
            "an instruction which the template does not hold is reported by an index rather than by what stands nowhere.");
    }

    [Test]
    public void An_Instruction_Which_Nothing_Stands_For_Is_Reported_By_The_Index_Of_It()
    {
        var instructions = Instructions(3);
        var filter = new InstructionFilter(instructions);
        for (var index = 0; index < instructions.Count; index++) filter.Skip(index);

        var body = new List<Instruction> {Instruction.Create(OpCodes.Br, instructions[1])};

        var thrown = Assert.Throws<InvalidILException>(() => filter.ApplyTo(body));
        Assert.That(thrown!.Message, Is.EqualTo(string.Format(ErrorMessages.INVALID_IL, "$1")),
            "an instruction of the template which nothing stands for is not reported by the index it stands at.");
    }

    [Test]
    public void An_Entry_Of_A_Table_Which_Names_Nothing_Is_Refused_By_The_Message_Of_The_Filter()
    {
        // A table which names nothing at all names no instruction of the template either, and what is refused is the
        // entry rather than the filter: the reading of the place of a thing which stands nowhere answers with the
        // place of nothing, which is what the message reports, and it is not the reading of a table which breaks.
        var filter = new InstructionFilter(Instructions(2));
        var body = new List<Instruction> {Instruction.Create(OpCodes.Switch, [null!])};

        var thrown = Assert.Throws<InvalidILException>(() => filter.ApplyTo(body));
        Assert.That(thrown!.Message, Is.EqualTo(string.Format(ErrorMessages.INVALID_IL, "$-1")),
            "an entry which names nothing is not refused by the message which the filter writes.");
    }

    /// <summary>
    /// The instructions of a template which holds the given number of them, which stand for no member and are read
    /// through nothing but their identity.
    /// </summary>
    /// <param name="count">The number of the instructions.</param>
    /// <returns>The instructions.</returns>
    private static Mono.Collections.Generic.Collection<Instruction> Instructions(int count)
    {
        var instructions = new Mono.Collections.Generic.Collection<Instruction>(count);
        for (var index = 0; index < count; index++) instructions.Add(Instruction.Create(OpCodes.Nop));
        return instructions;
    }
}