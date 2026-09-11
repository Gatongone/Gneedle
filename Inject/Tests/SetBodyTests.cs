using Mono.Cecil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

[TestFixture]
public class SetBodyTests
{
    private const string Ns = "Gneedle.Test.Generated";

    // Template method bodies copied by the injector. Kept in the test assembly so
    // Cecil can resolve them from disk via the default assembly resolver.
    public static class BodyTemplates
    {
        public static int Add(int a, int b) => a + b;
        public static int Echo(int x) => x;

        // Five parameters, so the fifth is addressed by ldarg.s with a parameter as its operand rather than by one of the
        // macro opcodes, which carry no operand at all.
        public static int Sum(int a, int b, int c, int d, int e) => a + b + c + d + e;
    }

    private static (AssemblyHandler handler, TypeHandler host) NewHost()
    {
        var handler = (AssemblyHandler) Assembly.Create("SetBodyTestAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Calc", Ns, ClassFlags.Public).GetHandler();
        return (handler, host);
    }

    [Test]
    public void SetBody_Copies_Instructions_From_Template()
    {
        var (_, host) = NewHost();
        var method = host.AddMethod(
            "Add",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);

        method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Add))!);

        var body = ((MethodHandler) method).Source.Body;
        Assert.That(body.Instructions, Is.Not.Empty);
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Add), Is.True);
        Assert.That(body.Instructions.Last().OpCode, Is.EqualTo(OpCodes.Ret));
    }

    [Test]
    public void SetBody_Produces_Structurally_Valid_Assembly()
    {
        var assembly = Assembly.Create("SetBodyValidAssembly");
        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass("Calc", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod(
            "Add",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Add))!);

        // The runtime loader can't load this net5.0-targeted image here, but Cecil
        // re-reading the emitted bytes proves the produced image is well-formed.
        using var stream = new MemoryStream();
        assembly.SaveTo(stream);
        stream.Position = 0;

        var reread = AssemblyDefinition.ReadAssembly(stream);
        var type = reread.MainModule.GetType($"{Ns}.Calc");
        Assert.That(type, Is.Not.Null);
        var emitted = type.Methods.First(m => m.Name == "Add");
        Assert.That(emitted.Body.Instructions.Any(i => i.OpCode == OpCodes.Add), Is.True);
    }

    [Test]
    public void SetBody_Maps_A_Parameter_Addressed_By_Operand_To_The_Parameter_Of_The_Same_Position()
    {
        // The parameters of the template and the parameters of the method are two different sets, and the name of one
        // is not the name of the other. A parameter which the body addresses by operand is therefore matched by the
        // position it holds, which is the only thing the two sets share. A body which is copied by name alone leaves
        // the operand null where the names differ, which Cecil rejects while the instruction is built.
        var assembly = Assembly.Create("SetBodyParameterOperandAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Calc", Ns, ClassFlags.Public).GetHandler();
        var intType = typeof(int).ToGneedleType();
        var method = host.AddMethod("Sum", intType, [], [new Parameter(intType), new Parameter(intType), new Parameter(intType),
                                                         new Parameter(intType), new Parameter(intType)],
                                    MethodFlags.Public | MethodFlags.Static);

        method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Sum))!);

        // 15 rather than 1, 5 or 10 tells the arguments apart from a body which reached the wrong parameters.
        var sum = assembly.Load().GetType($"{Ns}.Calc")!.GetMethod("Sum")!;
        Assert.That(sum.Invoke(null, [1, 2, 3, 4, 5]), Is.EqualTo(15));
    }

    [Test]
    public void SetBody_With_More_Parameters_Than_The_Method_Holds_Throws()
    {
        var (_, host) = NewHost();
        var method = host.AddMethod("Echo", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public | MethodFlags.Static);

        var thrown = Assert.Throws<ArgumentException>(() => method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Sum))!));

        Assert.That(thrown!.Message, Does.Contain("position"));
    }

    [Test]
    public void SetBody_Remaps_Ldarg_For_Static_Template_Into_Instance_Method()
    {
        var (_, host) = NewHost();
        // Instance method receiving a static template: the template's ldarg.0 (its first
        // parameter) must be remapped to ldarg.1 because arg0 of the instance method is `this`.
        var method = host.AddMethod(
            "Echo",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(typeof(BodyTemplates).GetMethod(nameof(BodyTemplates.Echo))!);

        var body = ((MethodHandler) method).Source.Body;
        Assert.That(((MethodHandler) method).Source.IsStatic, Is.False);
        // The single load must target ldarg.1 (the real parameter), not ldarg.0 (this).
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_1), Is.True);
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_0), Is.False);
    }
}
