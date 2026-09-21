using Mono.Cecil;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// Tests for the member of the base type which the template names through `Base`, which are the tests of <see cref="PointerTests"/> for that one placeholder.
/// </summary>
public partial class PointerTests
{
    /// <summary>
    /// Create a host which derives from a type of the assembly which carries the members which are asked for, so that a
    /// template which reaches a member of the base type has one to be rewritten to.
    /// </summary>
    private static TypeHandler NewDerivedHost(Action<TypeDefinition, ModuleDefinition> addBaseMembers)
    {
        var asm = Assembly.Create("BasePointerAssembly");
        var mod = asm.Source.MainModule;
        var baseDef = new TypeDefinition(NS, "BaseType", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        addBaseMembers(baseDef, mod);
        mod.Types.Add(baseDef);

        var host = AddAHost(asm, "Derived");
        host.Source.BaseType = baseDef;
        return host;
    }

    /// <summary>
    /// Create a host which derives from an instantiation of a type of the assembly which carries the members which are
    /// asked for, so that a member of the base which names the parameter of it has the argument of that instantiation to
    /// stand for rather than the parameter alone.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which runs its host gives one of its own.</param>
    private static TypeHandler NewHostWhichDerivesFromAnInstantiationOfAGenericType(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = AddAHost(handler);
        host.Source.BaseType = host.Source.Module.ImportReference(typeof(GenericBaseOfAnInstance<int>));
        return host;
    }

    [Test]
    public void BaseMethod_Rewrites_To_Direct_Call()
    {
        var host = NewDerivedHost((baseDef, mod) =>
        {
            var calc = new MethodDefinition("Calc", MethodAttributes.Public | MethodAttributes.HideBySig, mod.TypeSystem.Int32) {DeclaringType = baseDef};
            calc.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, mod.TypeSystem.Int32));
            calc.Body.GetILProcessor().Emit(OpCodes.Ret);
            baseDef.Methods.Add(calc);
        });

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseMethod)));
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
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseFieldGet)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld), Is.True);
    }

    [Test]
    public void BaseProperty_Rewrites_To_Call_Getter()
    {
        var host = NewDerivedHost((baseDef, mod) =>
        {
            var prop = new PropertyDefinition("Prop", PropertyAttributes.None, mod.TypeSystem.Int32);
            var getter = new MethodDefinition("get_Prop", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, mod.TypeSystem.Int32) {DeclaringType = baseDef};
            getter.Body.GetILProcessor().Emit(OpCodes.Ret);
            prop.GetMethod = getter;
            baseDef.Methods.Add(getter);
            baseDef.Properties.Add(prop);
        });

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BasePropertyGet)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            && ((MethodReference) i.Operand).Name == "get_Prop"), Is.True);
    }

    [Test]
    public void Base_Member_Of_A_Type_Which_Derives_From_Nothing_Is_Refused()
    {
        // A type which derives from nothing holds no base type to look a member up on, so the member which the template
        // names cannot be resolved, and what the caller is left with is the reason: the error names the member which was
        // looked for rather than being one which the lookup of the base type itself failed over.
        var asm = Assembly.Create("NoBasePointerAssembly");
        var host = AddAHost(asm, "Derived");
        // A class which is added derives from the object of the target framework unless the decorator is given another
        // base type, so the one which derives from nothing is the root of a hierarchy which is written out here.
        host.Source.BaseType = null;
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        var field = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        var property = host.AddMethod("ReadProp", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);

        Assert.Multiple(() =>
        {
            var memberThrown = Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseMethod))));
            Assert.That(memberThrown!.Message, Does.Contain("Calc"), "the message does not name the member which the template asked for.");

            var fieldThrown = Assert.Throws<ArgumentException>(() => field.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseFieldGet))));
            Assert.That(fieldThrown!.Message, Does.Contain("Value"), "the message does not name the field which the template asked for.");

            var propertyThrown = Assert.Throws<ArgumentException>(() => property.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BasePropertyGet))));
            Assert.That(propertyThrown!.Message, Does.Contain("Prop"), "the message does not name the property which the template asked for.");
        });
    }
}