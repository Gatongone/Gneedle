using Mono.Cecil;
using Mono.Cecil.Cil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

[TestFixture]
public class MemberInjectionTests
{
    private const string Ns = "Gneedle.Test.Generated";

    // Template bodies live in the test assembly so Cecil can resolve them from disk.
    // They call the placeholder This.Field/Property API to produce the IL pattern the
    // injector rewrites. The generic type argument communicates the member type.
    public static class Templates
    {
        public static int ReadInstanceField() => This.Field<int>("Value").Get();
        public static void WriteInstanceField(int v) => This.Field<int>("Value").Set(v);
        public static int ReadStaticField() => This.Field<int>("Value").Get();
        public static void WriteStaticField(int v) => This.Field<int>("Value").Set(v);
        public static int ReadMissingField() => This.Field<int>("Missing").Get();

        public static int ReadInstanceProperty() => This.Property<int>("Prop").Get();
        public static void WriteInstanceProperty(int v) => This.Property<int>("Prop").Set(v);
    }

    private static TypeHandler NewHostWithField(string fieldName, bool isStatic)
    {
        var handler = (AssemblyHandler) Assembly.Create("MemberInjectionAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var attrs = FieldAttributes.Public | (isStatic ? FieldAttributes.Static : 0);
        host.Source.Fields.Add(new FieldDefinition(fieldName, attrs, host.Source.Module.TypeSystem.Int32));
        return host;
    }

    private static System.Reflection.MethodInfo Template(string name)
        => typeof(Templates).GetMethod(name)!;

    private static TypeHandler NewHostWithProperty(string propertyName, bool withGetter, bool withSetter, bool isVirtual)
    {
        var handler = (AssemblyHandler) Assembly.Create("MemberInjectionPropAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var module = host.Source.Module;
        var propertyType = module.TypeSystem.Int32;
        var property = new PropertyDefinition(propertyName, PropertyAttributes.None, propertyType);
        var methodAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig
                          | (isVirtual ? MethodAttributes.Virtual | MethodAttributes.NewSlot : 0);

        if (withGetter)
        {
            var getter = new MethodDefinition($"get_{propertyName}", methodAttrs, propertyType) { DeclaringType = host.Source };
            getter.Body.GetILProcessor().Emit(OpCodes.Ret);
            property.GetMethod = getter;
            host.Source.Methods.Add(getter);
        }

        if (withSetter)
        {
            var setter = new MethodDefinition($"set_{propertyName}", methodAttrs, module.TypeSystem.Void) { DeclaringType = host.Source };
            setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, propertyType));
            setter.Body.GetILProcessor().Emit(OpCodes.Ret);
            property.SetMethod = setter;
            host.Source.Methods.Add(setter);
        }

        host.Source.Properties.Add(property);
        return host;
    }

    private static Instruction[] Rewrite(TypeHandler host, string methodName, System.Type returnType, IType[] parameters, string template, MethodFlags flags)
    {
        var method = host.AddMethod(methodName, returnType.ToGneedleType(), [], parameters, flags);
        method.SetBody(Template(template));
        return ((MethodHandler) method).Source.Body.Instructions.ToArray();
    }

    // region Field rewriting

    [Test]
    public void ReadInstanceField_Rewrites_To_Ldfld()
    {
        var host = NewHostWithField("Value", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.ReadInstanceField)));

        var body = ((MethodHandler) method).Source.Body;
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld), Is.True);
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_0), Is.True);
    }

    [Test]
    public void WriteInstanceField_Rewrites_To_Stfld()
    {
        var host = NewHostWithField("Value", isStatic: false);
        var ins = Rewrite(host, "Write", typeof(void), [typeof(int).ToGneedleType()], nameof(Templates.WriteInstanceField), MethodFlags.Public);

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Stfld), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld), Is.False);
    }

    [Test]
    public void ReadStaticField_Rewrites_To_Ldsfld_Without_Ldarg0()
    {
        var host = NewHostWithField("Value", isStatic: true);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(nameof(Templates.ReadStaticField)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldsfld), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld), Is.False);
    }

    [Test]
    public void WriteStaticField_Rewrites_To_Stsfld()
    {
        var host = NewHostWithField("Value", isStatic: true);
        var method = host.AddMethod("Write", typeof(void).ToGneedleType(), [], [typeof(int).ToGneedleType()], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(nameof(Templates.WriteStaticField)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Stsfld), Is.True);
    }

    [Test]
    public void ReadMissingField_Throws()
    {
        var host = NewHostWithField("Value", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);

        Assert.Catch<System.ArgumentException>(() => method.SetBody(Template(nameof(Templates.ReadMissingField))));
    }

    // endregion

    // region Property rewriting

    [Test]
    public void ReadInstanceProperty_Rewrites_To_Call_Getter()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.ReadInstanceProperty)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "get_Prop"), Is.True);
    }

    [Test]
    public void WriteInstanceProperty_Rewrites_To_Call_Setter()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: false);
        var method = host.AddMethod("Write", typeof(void).ToGneedleType(), [], [typeof(int).ToGneedleType()], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.WriteInstanceProperty)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "set_Prop"), Is.True);
    }

    [Test]
    public void ReadVirtualProperty_Rewrites_To_Callvirt_Getter()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: true);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.ReadInstanceProperty)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Callvirt && ((MethodReference) i.Operand).Name == "get_Prop"), Is.True);
    }

    [Test]
    public void ReadProperty_Without_Getter_Throws()
    {
        var host = NewHostWithProperty("Prop", withGetter: false, withSetter: true, isVirtual: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);

        Assert.Catch<System.ArgumentException>(() => method.SetBody(Template(nameof(Templates.ReadInstanceProperty))));
    }

    [Test]
    public void WriteProperty_Without_Setter_Throws()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: false, isVirtual: false);
        var method = host.AddMethod("Write", typeof(void).ToGneedleType(), [], [typeof(int).ToGneedleType()], MethodFlags.Public);

        Assert.Catch<System.ArgumentException>(() => method.SetBody(Template(nameof(Templates.WriteInstanceProperty))));
    }

    // endregion
}
