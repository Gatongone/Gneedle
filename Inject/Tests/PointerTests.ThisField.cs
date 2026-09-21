using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// Tests for the field of the type which the template is woven into, which are the tests of <see cref="PointerTests"/> for that one placeholder.
/// </summary>
public partial class PointerTests
{
    /// <summary>
    /// Create a host which declares a field of the given name, which is static when it is asked for.
    /// </summary>
    /// <param name="fieldName">Name of the field which the host declares.</param>
    /// <param name="isStatic">Whether the field is declared static.</param>
    /// <param name="assemblyName">Name of the assembly to build, which a test which runs its host gives one of its own.</param>
    private static TypeHandler NewHostWithField(string fieldName, bool isStatic, string assemblyName = "MemberInjectionAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        var attrs = FieldAttributes.Public | (isStatic ? FieldAttributes.Static : 0);
        host.Source.Fields.Add(new FieldDefinition(fieldName, attrs, host.Source.Module.TypeSystem.Int32));
        return host;
    }

    /// <summary>
    /// Give a host the constructor which a type needs for an instance of it to be made, and load the assembly which
    /// declares it, so that a weave of the member can be run rather than read.
    /// </summary>
    /// <param name="assembly">The assembly which declares the host, which the loader loads by its name.</param>
    /// <param name="host">The host which the member was woven into.</param>
    /// <returns>The type of the host, as the runtime read it.</returns>
    private static Type LoadHostOf(Assembly assembly, TypeHandler host)
    {
        var module = assembly.Source.MainModule;
        AddAnInstanceConstructor(host);

        return assembly.Load().GetType($"{NS}.Host")!;
    }

    /// <summary>
    /// Add a method to the host, weave the template into it, and hand back the instructions which came out.
    /// </summary>
    private static Instruction[] Rewrite(TypeHandler host, string methodName, Type returnType, Parameter[] parameters, string template, MethodFlags flags)
    {
        var method = host.AddMethod(methodName, returnType.ToGneedleType(), [], parameters, flags);
        method.SetBody(Template(typeof(ThisMemberTemplates), template));
        return [.. ((MethodHandler) method).Source.Body.Instructions];
    }

    [Test]
    public void ReadInstanceField_Rewrites_To_Ldfld()
    {
        var host = NewHostWithField("Value", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceField)));

        var body = ((MethodHandler) method).Source.Body;
        Assert.Multiple(() =>
        {
            Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld), Is.True);
            Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_0), Is.True);
        });
    }

    [Test]
    public void WriteInstanceField_Rewrites_To_Stfld()
    {
        var host = NewHostWithField("Value", isStatic: false);
        var ins = Rewrite(host, "Write", typeof(void), [new Parameter(typeof(int).ToGneedleType())], nameof(ThisMemberTemplates.WriteInstanceField), MethodFlags.Public);
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(i => i.OpCode == OpCodes.Stfld), Is.True);
            Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld), Is.False);
        });
    }

    [Test]
    public void A_Template_Which_Reads_And_Writes_A_Field_Writes_The_Field_It_Read()
    {
        // The two placeholders name the same field, and the accessor which each is paired with is the one which the
        // value it pushed is the receiver of: the write used to be paired with the read of the inner placeholder,
        // because the read is the first accessor after the name, and the parse then refused the second of them.
        var host = NewHostWithField("Value", isStatic: false, "FieldReadAndWriteAssembly");
        var ins = Rewrite(host, "Bump", typeof(void), [], nameof(ThisMemberTemplates.AddOneToInstanceField), MethodFlags.Public);
        Assert.Multiple(() =>
        {
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Ldfld), Is.EqualTo(1), "the field was not read exactly once.");
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Stfld), Is.EqualTo(1), "the field was not written exactly once.");
        });
        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        AddAnInstanceConstructor(host);

        var type = assembly.Load().GetType($"{NS}.Host")!;
        var instance = Activator.CreateInstance(type);
        type.GetField("Value")!.SetValue(instance, 41);
        type.GetMethod("Bump")!.Invoke(instance, null);

        Assert.That(type.GetField("Value")!.GetValue(instance), Is.EqualTo(42));
    }

    [Test]
    public void A_Value_Which_Holds_An_Array_Creation_Is_Written_Into_The_Field_It_Names()
    {
        // The array which the template creates stands between the value of the write and the accessor which writes it.
        // Creating an array takes the length off the stack and leaves the array in its place, and the walk which counts
        // the values above the value of the placeholder read it as an instruction which leaves one more than it took:
        // the accessor of the write was taken for one which stands above the value, and the read of the same field was
        // answered for the write as well, which wrote one instruction twice.
        var host = NewHostWithField("Value", isStatic: false, "FieldArrayValueAssembly");
        var method = host.AddMethod("Bump", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        Assert.DoesNotThrow(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.AddTheFirstElementOfAnArrayToTheField))),
            "the write of a value which holds an array was refused rather than woven.");
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Ldfld), Is.EqualTo(1), "the field was not read exactly once.");
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Stfld), Is.EqualTo(1), "the field was not written exactly once.");
        });
        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type);
        type.GetField("Value")!.SetValue(instance, 41);
        type.GetMethod("Bump")!.Invoke(instance, null);

        Assert.That(type.GetField("Value")!.GetValue(instance), Is.EqualTo(42));
    }

    [Test]
    public void A_Template_Which_Holds_A_Handle_Reads_And_Writes_The_Field_It_Names()
    {
        // The name stands where the handle is built rather than where the member is read or written, so the accessors
        // which the template wrote belong to the local: the local has to be read as the member which the name found.
        var host = NewHostWithField("Value", isStatic: false, "FieldHeldHandleAssembly");
        var ins = Rewrite(host, "Bump", typeof(int), [new Parameter(typeof(int).ToGneedleType())], nameof(ThisMemberTemplates.BumpAHeldHandle), MethodFlags.Public);
        Assert.Multiple(() =>
        {

            // The two reads are the one which the write is given and the one which the member hands back.
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Ldfld), Is.EqualTo(2), "the field was not read exactly twice.");
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Stfld), Is.EqualTo(1), "the field was not written exactly once.");
            Assert.That(ins.Any(i => i.Operand is MemberReference { DeclaringType.Namespace: "Gneedle.Inject" }), Is.False,
                "the handle which the template holds was left in the body.");
        });

        // The local which holds the handle is emptied by the weaving, and a local which is declared with a type of the
        // weaver is what would leave the reference behind after the handle itself was written away.
        DoesNotReferToTheWeaver(host);

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type);
        type.GetField("Value")!.SetValue(instance, 41);
        Assert.Multiple(() =>
        {
            Assert.That(type.GetMethod("Bump")!.Invoke(instance, [3]), Is.EqualTo(44));
            Assert.That(type.GetField("Value")!.GetValue(instance), Is.EqualTo(44));
        });
    }

    [Test]
    public void A_Template_Which_Holds_A_Handle_And_Names_The_Field_Itself_Reads_The_Field()
    {
        // What the template holds and what it names stand in one body, and each of them is woven where it stands.
        var host = NewHostWithField("Value", isStatic: false, "FieldHeldHandleAndNameAssembly");
        var ins = Rewrite(host, "Bump", typeof(int), [new Parameter(typeof(int).ToGneedleType())], nameof(ThisMemberTemplates.BumpAHeldHandleAndTheFieldItself), MethodFlags.Public);
        Assert.Multiple(() =>
        {

            // The three reads are the ones of the write, of the member which is handed back and of the name which is read.
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Ldfld), Is.EqualTo(3), "the field was not read exactly three times.");
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Stfld), Is.EqualTo(1), "the field was not written exactly once.");
        });
        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type);
        type.GetField("Value")!.SetValue(instance, 41);
        Assert.Multiple(() =>
        {
            Assert.That(type.GetMethod("Bump")!.Invoke(instance, [3]), Is.EqualTo(88));
            Assert.That(type.GetField("Value")!.GetValue(instance), Is.EqualTo(44));
        });
    }

    [Test]
    public void A_Template_Which_Holds_A_Handle_Of_A_Static_Field_Reads_And_Writes_It()
    {
        // A field which belongs to no instance takes no receiver, so the read of the local is written as nothing.
        var host = NewHostWithField("Value", isStatic: true, "StaticFieldHeldHandleAssembly");
        var ins = Rewrite(host, "Bump", typeof(int), [new Parameter(typeof(int).ToGneedleType())], nameof(ThisMemberTemplates.BumpAHeldHandleOfAStaticField), MethodFlags.Public | MethodFlags.Static);
        Assert.Multiple(() =>
        {
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Ldsfld), Is.EqualTo(2), "the field was not read exactly twice.");
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Stsfld), Is.EqualTo(1), "the field was not written exactly once.");
            Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld || i.OpCode == OpCodes.Stfld), Is.False,
                "a field which belongs to no instance was read through a receiver.");
        });
        DoesNotReferToTheWeaver(host);
    }

    [Test]
    public void A_Template_Which_Reads_A_Held_Handle_For_Something_Else_Throws()
    {
        // The handle of a value member is a value which only the weaving writes, so a local which holds one has no
        // value where it is read for anything but the member.
        var host = NewHostWithField("Value", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadAHeldHandleAsAValue))));
        Assert.Multiple(() =>
        {
            Assert.That(thrown!.Message, Does.Contain("Value"));
            Assert.That(thrown.Message, Does.Contain("no way to write"));
        });
    }

    [Test]
    public void ReadStaticField_Rewrites_To_Ldsfld_Without_Ldarg0()
    {
        var host = NewHostWithField("Value", isStatic: true);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadStaticField)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldsfld), Is.True);
            Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld), Is.False);
        });
    }

    [Test]
    public void WriteStaticField_Rewrites_To_Stsfld()
    {
        var host = NewHostWithField("Value", isStatic: true);
        var method = host.AddMethod("Write", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteStaticField)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Stsfld), Is.True);
    }

    [Test]
    public void ReadMissingField_Throws()
    {
        var host = NewHostWithField("Value", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);

        Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadMissingField))));
    }

    [Test]
    public void A_Switch_Of_A_Template_Reaches_The_Body_Of_Each_Case_It_Was_Woven_With()
    {
        // The table of a switch names the instruction each case begins at, and a case which begins with the name of a
        // member begins at an instruction which the weaving replaces: the entry is carried to what stood where that
        // instruction stood, as the branch of an `if` is, or the case reaches into the template rather than into the
        // body it was woven into.
        var host = NewHostWithField("Value", isStatic: false, "FieldSwitchAssembly");
        host.Source.Fields.Add(new FieldDefinition("Other", FieldAttributes.Public, host.Source.Module.TypeSystem.Int32));
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadAFieldPerCase)));

        var body = ((MethodHandler) method).Source.Body;
        var table = body.Instructions.SelectMany(instruction => instruction.Operand as Instruction[] ?? []).ToArray();

        Assert.That(table, Is.Not.Empty, "the switch of the template was not carried as a table.");
        Assert.That(table.All(entry => body.Instructions.Contains(entry)), Is.True,
            "an entry of the table named an instruction which the body does not hold.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type);
        type.GetField("Value")!.SetValue(instance, 7);
        type.GetField("Other")!.SetValue(instance, 9);
        var read = type.GetMethod("Read")!;
        Assert.Multiple(() =>
        {
            Assert.That(read.Invoke(instance, [0]), Is.EqualTo(7), "the case which names the first field reached another case.");
            Assert.That(read.Invoke(instance, [1]), Is.EqualTo(9), "the case which names the second field reached another case.");
            Assert.That(read.Invoke(instance, [4]), Is.EqualTo(40), "the case which holds a value of its own reached another case.");
            Assert.That(read.Invoke(instance, [9]), Is.EqualTo(-1), "the default of the table reached another case.");
        });
    }

    /// <summary>
    /// Create a host which is generic in one parameter, and which declares a field of that type.
    /// </summary>
    private static TypeHandler NewGenericHostWithField(string fieldName)
    {
        var handler = (AssemblyHandler) Assembly.Create("MemberInjectionGenericAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Host", NS, ClassFlags.Public)
                                        .WithGenericParameter("T")
                                        .GetHandler();
        var gp = host.Source.GenericParameters[0];
        host.Source.Fields.Add(new FieldDefinition(fieldName, FieldAttributes.Public, gp));
        return host;
    }

    [Test]
    public void ReadGenericField_Rewrites_To_Ldfld_On_GenericInstanceType()
    {
        var host = NewGenericHostWithField("value");
        var method = host.AddMethod("Get", new GenericParameterType("T"), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadGenericField)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var ldfld = ins.FirstOrDefault(i => i.OpCode == OpCodes.Ldfld);
        Assert.Multiple(() =>
        {
            Assert.That(ldfld, Is.Not.Null);
            // The field reference's declaring type must be the generic instance Host<T>, not the open definition.
            Assert.That(((FieldReference) ldfld!.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>());
        });
    }

    [Test]
    public void WriteGenericField_Rewrites_To_Stfld_On_GenericInstanceType()
    {
        var host = NewGenericHostWithField("value");
        var method = host.AddMethod("Set", typeof(void).ToGneedleType(), [], [new Parameter(new GenericParameterType("T"))], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteGenericField)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var stfld = ins.FirstOrDefault(i => i.OpCode == OpCodes.Stfld);
        Assert.Multiple(() =>
        {
            Assert.That(stfld, Is.Not.Null);
            Assert.That(((FieldReference) stfld!.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>());
        });
    }
}