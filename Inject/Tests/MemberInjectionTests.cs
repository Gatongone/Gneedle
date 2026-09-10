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

        // Generic field on a Host<T>: exercises the field.ContainsGenericParameter branch
        // that builds a FieldReference via MakeGenericInstanceType (cecil issue #954).
        public static T_0 ReadGenericField() => This.Field<T_0>("value").Get();
        public static void WriteGenericField(T_0 v) => This.Field<T_0>("value").Set(v);

        // Generic property on a Host<T>: ImportReference handles generic context automatically.
        public static T_0 ReadGenericProp() => This.Property<T_0>("Prop").Get();
        public static void WriteGenericProp(T_0 v) => This.Property<T_0>("Prop").Set(v);
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

    #region Field rewriting

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

    #endregion

    #region Property rewriting

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

    #endregion

    #region Generic field rewriting (Host<T> with a field of type T)

    private static TypeHandler NewGenericHostWithField(string fieldName)
    {
        var handler = (AssemblyHandler) Assembly.Create("MemberInjectionGenericAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public)
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
        method.SetBody(Template(nameof(Templates.ReadGenericField)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var ldfld = ins.FirstOrDefault(i => i.OpCode == OpCodes.Ldfld);
        Assert.That(ldfld, Is.Not.Null);
        // The field reference's declaring type must be the generic instance Host<T>, not the open definition.
        Assert.That(((FieldReference) ldfld!.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>());
    }

    [Test]
    public void WriteGenericField_Rewrites_To_Stfld_On_GenericInstanceType()
    {
        var host = NewGenericHostWithField("value");
        var method = host.AddMethod("Set", typeof(void).ToGneedleType(), [], [new GenericParameterType("T")], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.WriteGenericField)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var stfld = ins.FirstOrDefault(i => i.OpCode == OpCodes.Stfld);
        Assert.That(stfld, Is.Not.Null);
        Assert.That(((FieldReference) stfld!.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>());
    }

    #endregion

    #region Generic property rewriting (Host<T> with a property of type T)

    private static TypeHandler NewGenericHostWithProperty(string propertyName, bool withGetter, bool withSetter)
    {
        var handler = (AssemblyHandler) Assembly.Create("MemberInjectionGenericPropAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public)
                                        .WithGenericParameter("T")
                                        .GetHandler();
        var gp = host.Source.GenericParameters[0];
        var module = host.Source.Module;
        var prop = new PropertyDefinition(propertyName, PropertyAttributes.None, gp);
        var methodAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        if (withGetter)
        {
            var getter = new MethodDefinition($"get_{propertyName}", methodAttrs, gp) { DeclaringType = host.Source };
            getter.Body.GetILProcessor().Emit(OpCodes.Ret);
            prop.GetMethod = getter;
            host.Source.Methods.Add(getter);
        }

        if (withSetter)
        {
            var setter = new MethodDefinition($"set_{propertyName}", methodAttrs, module.TypeSystem.Void) { DeclaringType = host.Source };
            setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, gp));
            setter.Body.GetILProcessor().Emit(OpCodes.Ret);
            prop.SetMethod = setter;
            host.Source.Methods.Add(setter);
        }

        host.Source.Properties.Add(prop);
        return host;
    }

    [Test]
    public void ReadGenericProp_Rewrites_To_Call_Getter_With_Correct_Signature()
    {
        var host = NewGenericHostWithProperty("Prop", withGetter: true, withSetter: true);
        var method = host.AddMethod("Get", new GenericParameterType("T"), [], [], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.ReadGenericProp)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var call = ins.FirstOrDefault(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                            && ((MethodReference) i.Operand).Name == "get_Prop");
        Assert.That(call, Is.Not.Null);
        // The getter's return type should be the generic parameter T.
        Assert.That(((MethodReference) call!.Operand).ReturnType, Is.InstanceOf<GenericParameter>());
    }

    [Test]
    public void WriteGenericProp_Rewrites_To_Call_Setter_With_Correct_Signature()
    {
        var host = NewGenericHostWithProperty("Prop", withGetter: true, withSetter: true);
        var method = host.AddMethod("Set", typeof(void).ToGneedleType(), [], [new GenericParameterType("T")], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.WriteGenericProp)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var call = ins.FirstOrDefault(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                            && ((MethodReference) i.Operand).Name == "set_Prop");
        Assert.That(call, Is.Not.Null);
        // The setter's parameter type should be the generic parameter T.
        var param = ((MethodReference) call!.Operand).Parameters[0];
        Assert.That(param.ParameterType, Is.InstanceOf<GenericParameter>());
    }

    #endregion
}
