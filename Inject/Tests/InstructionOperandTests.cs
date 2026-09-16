using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Gneedle.Inject.Test;

/// <summary>
/// Tests for reading a local off the instruction which loads or stores it.<para/>
/// The operand of such an instruction is handed back in the shape the compilation wrote it in rather than in one shape
/// of its own: the reader of Mono.Cecil resolves the operand of a long-form load or store to the variable itself,
/// while the weaving writes the slot of it, and the two are read alike.
/// </summary>
[TestFixture]
public class InstructionOperandTests
{
    /// <summary>
    /// The number of the slots of the body which the tests read an instruction of, so that the slot of the variable
    /// which is read back is not the one every body holds first.
    /// </summary>
    private const int Slots = 5;

    [Test]
    public void A_Store_Which_Names_Its_Variable_Is_Read_As_The_Slot_Of_It()
    {
        var variable = LastVariable(out _);
        var instruction = Instruction.Create(OpCodes.Stloc, variable);

        Assert.That(instruction.TryGetStlocIndex(out var index), Is.True, "the store of a variable was not read as a store.");
        Assert.That(index, Is.EqualTo(variable.Index));
        Assert.That(index, Is.EqualTo(Slots - 1), "the slot which was read is not the slot the variable stands in.");
    }

    [Test]
    public void A_Short_Store_Which_Names_Its_Variable_Is_Read_As_The_Slot_Of_It()
    {
        var variable = LastVariable(out _);
        var instruction = Instruction.Create(OpCodes.Stloc_S, variable);

        Assert.That(instruction.TryGetStlocIndex(out var index), Is.True, "the short store of a variable was not read as a store.");
        Assert.That(index, Is.EqualTo(variable.Index));
    }

    [Test]
    public void A_Load_Which_Names_Its_Variable_Is_Read_As_The_Slot_Of_It()
    {
        var variable = LastVariable(out _);
        var instruction = Instruction.Create(OpCodes.Ldloc, variable);

        Assert.That(instruction.TryGetLdlocIndex(out var index), Is.True, "the load of a variable was not read as a load.");
        Assert.That(index, Is.EqualTo(variable.Index));
    }

    [Test]
    public void A_Short_Load_Which_Names_Its_Variable_Is_Read_As_The_Slot_Of_It()
    {
        var variable = LastVariable(out _);
        var instruction = Instruction.Create(OpCodes.Ldloc_S, variable);

        Assert.That(instruction.TryGetLdlocIndex(out var index), Is.True, "the short load of a variable was not read as a load.");
        Assert.That(index, Is.EqualTo(variable.Index));
    }

    [Test]
    public void A_Store_Of_A_Slot_Is_Read_As_That_Slot()
    {
        // The other shape an operand of a long-form store takes, which is the slot itself rather than the variable the
        // slot belongs to. An instruction of it is not one which this reader of Mono.Cecil writes, so it is built the
        // way the reader of it hands one back.
        var variable = LastVariable(out _);
        var instruction = Instruction.Create(OpCodes.Stloc, variable);
        instruction.Operand = 2;

        Assert.That(instruction.TryGetStlocIndex(out var index), Is.True, "a store of a slot was not read as a store.");
        Assert.That(index, Is.EqualTo(2));
    }

    [Test]
    public void A_Store_Of_Which_No_Slot_Is_Read_Is_Not_Read_As_A_Store()
    {
        // An instruction whose operand names nothing of a local is not one which a slot is read off, whatever the
        // operand is: what a store of a slot and a store of a variable are read alike is a slot, and anything else is
        // answered with no slot rather than with a cast which fails.
        var variable = LastVariable(out _);

        var odd = Instruction.Create(OpCodes.Stloc, variable);
        odd.Operand = "not a local";

        Assert.That(odd.TryGetStlocIndex(out var index), Is.False, "an instruction whose operand names no local was read as a store.");
        Assert.That(index, Is.EqualTo(-1));

        var absent = Instruction.Create(OpCodes.Stloc, variable);
        absent.Operand = null;

        Assert.That(absent.TryGetStlocIndex(out index), Is.False, "an instruction whose operand is absent was read as a store.");
        Assert.That(index, Is.EqualTo(-1));

        // The opcode of a macro form holds the slot itself, which is no operand at all.
        var macro = Instruction.Create(OpCodes.Stloc_0);
        Assert.That(macro.TryGetStlocIndex(out index), Is.True);
        Assert.That(index, Is.Zero);

        var none = Instruction.Create(OpCodes.Nop);
        Assert.That(none.TryGetStlocIndex(out index), Is.False, "an instruction which is not a store was read as one.");
        Assert.That(none.TryGetLdlocIndex(out index), Is.False, "an instruction which is not a load was read as one.");
    }

    /// <summary>
    /// The last of the slots of a body which holds <see cref="Slots"/> of them, which is the variable at the index which
    /// no macro opcode of a local reaches.
    /// </summary>
    /// <param name="method">The method which declares the variable.</param>
    /// <returns>The variable.</returns>
    private static VariableDefinition LastVariable(out MethodDefinition method)
    {
        var module = ModuleDefinition.CreateModule("InstructionOperandModule", ModuleKind.Dll);
        var host = new TypeDefinition("Gneedle.Test.Generated", "Host", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(host);

        method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        host.Methods.Add(method);

        VariableDefinition variable = null!;
        for (var index = 0; index < Slots; index++)
        {
            variable = new VariableDefinition(module.TypeSystem.Int32);
            method.Body.Variables.Add(variable);
        }

        return variable;
    }
}
