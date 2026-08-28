using Mono.Cecil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

[TestFixture]
public class BasePointerTests
{
    private const string Ns = "Gneedle.Test.Generated";

    public delegate int IntOp(int a);

    public static class Templates
    {
        public static int BaseMethod(int a) => Base.Method<IntOp>("Calc")(a);
        public static int BaseFieldGet() => Base.Field<int>("Value").Get();
        public static int BasePropertyGet() => Base.Property<int>("Prop").Get();
    }

    private static System.Reflection.MethodInfo Template(string name) => typeof(Templates).GetMethod(name)!;

    // Builds a base class (with the requested members) and a derived host that extends it.
    private static TypeHandler NewDerivedHost(Action<TypeDefinition, ModuleDefinition> addBaseMembers)
    {
        var asm = Assembly.Create("BasePointerAssembly");
        var mod = asm.Source.MainModule;
        var baseDef = new TypeDefinition(Ns, "BaseType", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        addBaseMembers(baseDef, mod);
        mod.Types.Add(baseDef);

        var host = (TypeHandler) ((AssemblyHandler) asm.Handler).AddClass("Derived", Ns, ClassFlags.Public).GetHandler();
        host.Source.BaseType = baseDef;
        return host;
    }

    [Test]
    public void BaseMethod_Rewrites_To_Direct_Call()
    {
        var host = NewDerivedHost((baseDef, mod) =>
        {
            var calc = new MethodDefinition("Calc", MethodAttributes.Public | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
            calc.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, mod.TypeSystem.Int32));
            calc.Body.GetILProcessor().Emit(OpCodes.Ret);
            baseDef.Methods.Add(calc);
        });

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [typeof(int).ToGneedleType()], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.BaseMethod)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Calc"), Is.True);
    }

    [Test]
    public void BaseField_Rewrites_To_Ldfld()
    {
        var host = NewDerivedHost((baseDef, mod) =>
            baseDef.Fields.Add(new FieldDefinition("Value", FieldAttributes.Public, mod.TypeSystem.Int32)));

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.BaseFieldGet)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld), Is.True);
    }

    [Test]
    public void BaseProperty_Rewrites_To_Call_Getter()
    {
        var host = NewDerivedHost((baseDef, mod) =>
        {
            var prop = new PropertyDefinition("Prop", PropertyAttributes.None, mod.TypeSystem.Int32);
            var getter = new MethodDefinition("get_Prop", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
            getter.Body.GetILProcessor().Emit(OpCodes.Ret);
            prop.GetMethod = getter;
            baseDef.Methods.Add(getter);
            baseDef.Properties.Add(prop);
        });

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.BasePropertyGet)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "get_Prop"), Is.True);
    }
}
