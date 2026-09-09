using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

[TestFixture]
public class DefaultBodyTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static TypeHandler NewClass()
    {
        var handler = (AssemblyHandler) Assembly.Create("DefaultBodyAssembly").Handler;
        return (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
    }

    private static MethodDefinition SourceOf(IMethodHandler method) => ((MethodHandler) method).Source;

    // region ThrowException

    [Test]
    public void ThrowException_Emits_Newobj_And_Throw()
    {
        var host = NewClass();
        var method = host.AddMethod("Do", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.ThrowException);

        var ins = SourceOf(method).Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Throw), Is.True);
    }

    // endregion

    // region WithDefaultReturn

    [Test]
    public void WithDefaultReturn_ReferenceType_Emits_Ldnull()
    {
        var host = NewClass();
        var method = host.AddMethod("Get", typeof(string).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.WithDefaultReturn);

        var ins = SourceOf(method).Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldnull), Is.True);
        Assert.That(ins.Last().OpCode, Is.EqualTo(OpCodes.Ret));
    }

    [Test]
    public void WithDefaultReturn_ValueType_Emits_Initobj()
    {
        var host = NewClass();
        var method = host.AddMethod("Get", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.WithDefaultReturn);

        var ins = SourceOf(method).Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Initobj), Is.True);
        Assert.That(ins.Last().OpCode, Is.EqualTo(OpCodes.Ret));
    }

    [Test]
    public void WithDefaultReturn_Void_Emits_Just_Ret()
    {
        var host = NewClass();
        var method = host.AddMethod("Do", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.WithDefaultReturn);

        var ins = SourceOf(method).Body.Instructions.ToArray();
        Assert.That(ins.Count, Is.EqualTo(1));
        Assert.That(ins[0].OpCode, Is.EqualTo(OpCodes.Ret));
    }

    // endregion

    // region CallFromBase

    [Test]
    public void CallFromBase_Calls_Base_Method()
    {
        var asm = Assembly.Create("DefaultBodyBaseAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var mod = asm.Source.MainModule;

        // base class with virtual Method
        var baseDef = new TypeDefinition(Ns, "BaseType", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        var baseMethod = new MethodDefinition("Method", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
        baseMethod.Body.GetILProcessor().Emit(OpCodes.Ret);
        baseDef.Methods.Add(baseMethod);
        mod.Types.Add(baseDef);

        // derived host extends base
        var host = (TypeHandler) handler.AddClass("Derived", Ns, ClassFlags.Public).GetHandler();
        host.Source.BaseType = baseDef;

        var method = host.AddMethod("Method", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(DefaultMethodBody.CallFromBase);

        var ins = SourceOf(method).Body.Instructions.ToArray();
        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && i.Operand is MethodReference mr && mr.Name == "Method"), Is.True);
        Assert.That(ins.Last().OpCode, Is.EqualTo(OpCodes.Ret));
    }

    [Test]
    public void CallFromBase_Without_Base_Method_Throws()
    {
        var host = NewClass();
        var method = host.AddMethod("Method", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);

        // No base type with Method -> should throw
        Assert.Catch<System.ArgumentException>(() => method.SetBody(DefaultMethodBody.CallFromBase));
    }

    // endregion
}
