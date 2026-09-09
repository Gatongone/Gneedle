using System.Linq;
using Mono.Cecil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

[TestFixture]
public class ObjectStaticPointerTests
{
    private const string Ns = "Gneedle.Test.Generated";

    public delegate int IntOp(int a);
    public class HelperClass
    {
        public int Calc(int a) => a * 2;
        public int PublicField;
        public int PublicProperty { get; set; }
    }

    public static class Templates
    {
        // Object.Method with new Object(param) syntax
        public static int ObjectMethod_NewSyntax(HelperClass h, int a) => new Object(h).Method<IntOp>("Calc")(a);

        // Object.Field get/set
        public static int ObjectField_Get(HelperClass h) => new Object(h).Field<int>("PublicField").Get();
        public static void ObjectField_Set(HelperClass h, int v) => new Object(h).Field<int>("PublicField").Set(v);

        // Object.Property get/set
        public static int ObjectProperty_Get(HelperClass h) => new Object(h).Property<int>("PublicProperty").Get();
        public static void ObjectProperty_Set(HelperClass h, int v) => new Object(h).Property<int>("PublicProperty").Set(v);

        // Static.Method with BCL type
        public static string StaticMethod_BCL() => Static.From("System.Environment").Method<System.Func<string>>("get_CommandLine")();

        // Static.Method with local assembly type
        public static int StaticMethod_Local() => Static.From("Gneedle.Test.Generated.LocalStatic").Method<System.Func<int>>("GetValue")();

        // Static.Field get/set (will use LocalStatic type from test setup)
        public static int StaticField_Get() => Static.From("Gneedle.Test.Generated.LocalStatic").Field<int>("StaticField").Get();
        public static void StaticField_Set(int v) => Static.From("Gneedle.Test.Generated.LocalStatic").Field<int>("StaticField").Set(v);

        // Static.Property get/set
        public static int StaticProperty_Get() => Static.From("Gneedle.Test.Generated.LocalStatic").Property<int>("StaticProperty").Get();
        public static void StaticProperty_Set(int v) => Static.From("Gneedle.Test.Generated.LocalStatic").Property<int>("StaticProperty").Set(v);
    }

    private static System.Reflection.MethodInfo Template(string name) => typeof(Templates).GetMethod(name)!;

    [Test]
    public void ObjectMethod_With_NewObject_Syntax_Rewrites_To_Direct_Call()
    {
        var asm = Assembly.Create("ObjectPointerAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
            [typeof(HelperClass).ToGneedleType(), typeof(int).ToGneedleType()], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.ObjectMethod_NewSyntax)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        // Should rewrite to call/callvirt HelperClass::Calc, not call Object::Method
        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && i.Operand is MethodReference mr && mr.Name == "Calc"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MethodReference mr && mr.DeclaringType.FullName == Object.TYPE_NAME), Is.False);
    }

    [Test]
    public void StaticMethod_BCL_Type_Rewrites_To_Direct_Call()
    {
        var asm = Assembly.Create("StaticPointerAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(string).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(nameof(Templates.StaticMethod_BCL)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        // Should rewrite to call System.Environment::get_CommandLine
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call
                                 && i.Operand is MethodReference mr && mr.Name == "get_CommandLine"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MethodReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);
    }

    [Test]
    public void StaticMethod_Local_Type_Rewrites_To_Direct_Call()
    {
        var asm = Assembly.Create("StaticPointerAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        // Create the local static class with GetValue method
        var staticClass = (TypeHandler) handler.AddClass("LocalStatic", Ns, ClassFlags.Public).GetHandler();
        var staticMethod = new MethodDefinition("GetValue", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, asm.Source.MainModule.TypeSystem.Int32);
        staticMethod.Body.GetILProcessor().Emit(OpCodes.Ret);
        staticClass.Source.Methods.Add(staticMethod);

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(nameof(Templates.StaticMethod_Local)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        // Should rewrite to call LocalStatic::GetValue
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call
                                 && i.Operand is MethodReference mr && mr.Name == "GetValue"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);
    }

    // region Object.Field and Object.Property tests

    [Test]
    public void ObjectField_Get_Rewrites_To_Ldfld()
    {
        var asm = Assembly.Create("ObjectFieldAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [typeof(HelperClass).ToGneedleType()], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.ObjectField_Get)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference fr && fr.Name == "PublicField"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Object.TYPE_NAME), Is.False);
    }

    [Test]
    public void ObjectField_Set_Rewrites_To_Stfld()
    {
        var asm = Assembly.Create("ObjectFieldAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [typeof(HelperClass).ToGneedleType(), typeof(int).ToGneedleType()], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.ObjectField_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference fr && fr.Name == "PublicField"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Object.TYPE_NAME), Is.False);
    }

    [Test]
    public void ObjectProperty_Get_Rewrites_To_Call_Getter()
    {
        var asm = Assembly.Create("ObjectPropertyAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [typeof(HelperClass).ToGneedleType()], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.ObjectProperty_Get)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && i.Operand is MethodReference mr && mr.Name == "get_PublicProperty"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Object.TYPE_NAME), Is.False);
    }

    [Test]
    public void ObjectProperty_Set_Rewrites_To_Call_Setter()
    {
        var asm = Assembly.Create("ObjectPropertyAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [typeof(HelperClass).ToGneedleType(), typeof(int).ToGneedleType()], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.ObjectProperty_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && i.Operand is MethodReference mr && mr.Name == "set_PublicProperty"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Object.TYPE_NAME), Is.False);
    }

    // endregion

    // region Static.Field and Static.Property tests

    [Test]
    public void StaticField_Get_Rewrites_To_Ldsfld()
    {
        var asm = Assembly.Create("StaticFieldAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        // Create LocalStatic with static field
        var staticClass = (TypeHandler) handler.AddClass("LocalStatic", Ns, ClassFlags.Public).GetHandler();
        staticClass.Source.Fields.Add(new FieldDefinition("StaticField", FieldAttributes.Public | FieldAttributes.Static, asm.Source.MainModule.TypeSystem.Int32));

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(nameof(Templates.StaticField_Get)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference fr && fr.Name == "StaticField"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);
    }

    [Test]
    public void StaticField_Set_Rewrites_To_Stsfld()
    {
        var asm = Assembly.Create("StaticFieldAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        // Create LocalStatic with static field
        var staticClass = (TypeHandler) handler.AddClass("LocalStatic", Ns, ClassFlags.Public).GetHandler();
        staticClass.Source.Fields.Add(new FieldDefinition("StaticField", FieldAttributes.Public | FieldAttributes.Static, asm.Source.MainModule.TypeSystem.Int32));

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [typeof(int).ToGneedleType()], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(nameof(Templates.StaticField_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference fr && fr.Name == "StaticField"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);
    }

    [Test]
    public void StaticProperty_Get_Rewrites_To_Call_Getter()
    {
        var asm = Assembly.Create("StaticPropertyAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        // Create LocalStatic with static property
        var staticClass = (TypeHandler) handler.AddClass("LocalStatic", Ns, ClassFlags.Public).GetHandler();
        var prop = new PropertyDefinition("StaticProperty", PropertyAttributes.None, asm.Source.MainModule.TypeSystem.Int32);
        var getter = new MethodDefinition("get_StaticProperty", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig, asm.Source.MainModule.TypeSystem.Int32) { DeclaringType = staticClass.Source };
        getter.Body.GetILProcessor().Emit(OpCodes.Ret);
        prop.GetMethod = getter;
        staticClass.Source.Methods.Add(getter);
        staticClass.Source.Properties.Add(prop);

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(nameof(Templates.StaticProperty_Get)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference mr && mr.Name == "get_StaticProperty"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);
    }

    [Test]
    public void StaticProperty_Set_Rewrites_To_Call_Setter()
    {
        var asm = Assembly.Create("StaticPropertyAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        // Create LocalStatic with static property
        var staticClass = (TypeHandler) handler.AddClass("LocalStatic", Ns, ClassFlags.Public).GetHandler();
        var prop = new PropertyDefinition("StaticProperty", PropertyAttributes.None, asm.Source.MainModule.TypeSystem.Int32);
        var setter = new MethodDefinition("set_StaticProperty", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig, asm.Source.MainModule.TypeSystem.Void) { DeclaringType = staticClass.Source };
        setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, asm.Source.MainModule.TypeSystem.Int32));
        setter.Body.GetILProcessor().Emit(OpCodes.Ret);
        prop.SetMethod = setter;
        staticClass.Source.Methods.Add(setter);
        staticClass.Source.Properties.Add(prop);

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [typeof(int).ToGneedleType()], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(nameof(Templates.StaticProperty_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference mr && mr.Name == "set_StaticProperty"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);
    }

    // endregion
}
