using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Assembly = Gneedle.Inject.Assembly;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Gneedle.Inject.Test;

/// <summary>
/// Tests for the placeholders which a template reaches the members of the type it is woven into through: <c>This</c>,
/// <c>Base</c>, <c>Object</c> and <c>Static</c>. Each of them is a call which throws when it runs, and each of them is
/// rewritten to the member of the target which it names, which is what these tests read back out of the woven body.
/// </summary>
[TestFixture]
public class PointerTests
{
    private const string Ns = "Gneedle.Test.Generated";

    /// <summary>
    /// The type which the templates of <c>Object</c> hold an instance of, and which the tests pass as the argument of a
    /// method which is woven.
    /// </summary>
    public class HelperClass
    {
        public int Calc(int a) => a * 2;
        public int PublicField;
        public int PublicProperty { get; set; }
    }

    // The templates live in the test assembly, so that Cecil resolves them from disk, and each names a member of the
    // type being woven through one placeholder. The type argument of a placeholder tells the member type.

    /// <summary>
    /// Templates which reach a field and a property of the type being woven through <c>This</c>.
    /// </summary>
    public static class ThisMemberTemplates
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

    /// <summary>
    /// Templates which call a method of the type being woven through <c>This.Method</c>.<para/>
    /// The delegate names the signature of the member which is looked for, and its <c>Invoke</c> drives the matching of
    /// the arguments against the parameters of the real method.
    /// </summary>
    public static class ThisMethodTemplates
    {
        // Non-generic delegate so ParseMethod's Invoke-parameter extraction sees concrete
        // parameter types (int,int) rather than open generic parameters.
        public delegate int IntBinaryOp(int a, int b);

        // Delegates whose parameter is loaded via ldc.i4.* literals in IL, which lose the
        // distinction between char/bool/short/int on the evaluation stack.
        public delegate char CharOp(char c);
        public delegate bool BoolOp(bool b);

        // Immediately invokes the returned delegate -> branch that rewrites to a direct call.
        public static int InvokeInstanceMethod(int a, int b) => This.Method<IntBinaryOp>("Add")(a, b);

        // Returns the delegate without invoking -> branch that builds a delegate (ldftn+newobj).
        public static IntBinaryOp GetInstanceMethodDelegate() => This.Method<IntBinaryOp>("Add");

        // Generic delegate (Func<>) currently trips ParseMethod: see MethodParser.cs:40-50.
        public static int InvokeViaGenericDelegate(int a, int b) => This.Method<Func<int, int, int>>("Add")(a, b);

        // char literal 'A' compiles to `ldc.i4.s 65` — same IL as int 65.
        public static char InvokeCharLiteral() => This.Method<CharOp>("Echo")('A');

        // bool literal true compiles to `ldc.i4.1` — same IL as int 1.
        public static bool InvokeBoolLiteral() => This.Method<BoolOp>("Echo")(true);
    }

    /// <summary>
    /// Templates which reach a member of the type which the type being woven derives from, through <c>Base</c>.
    /// </summary>
    public static class BaseTemplates
    {
        public delegate int IntOp(int a);

        public static int BaseMethod(int a) => Base.Method<IntOp>("Calc")(a);
        public static int BaseFieldGet() => Base.Field<int>("Value").Get();
        public static int BasePropertyGet() => Base.Property<int>("Prop").Get();
    }

    /// <summary>
    /// Templates which reach a member of an instance the template holds, through <c>Object</c>, and of a type which the
    /// template names as a string, through <c>Static</c>.
    /// </summary>
    public static class ObjectStaticTemplates
    {
        public delegate int IntOp(int a);

        // Object.Method with new Object(param) syntax
        public static int ObjectMethod_NewSyntax(HelperClass h, int a) => new Object(h).Method<IntOp>("Calc")(a);

        // Object.Field get/set
        public static int ObjectField_Get(HelperClass h) => new Object(h).Field<int>("PublicField").Get();
        public static void ObjectField_Set(HelperClass h, int v) => new Object(h).Field<int>("PublicField").Set(v);

        // Object.Property get/set
        public static int ObjectProperty_Get(HelperClass h) => new Object(h).Property<int>("PublicProperty").Get();
        public static void ObjectProperty_Set(HelperClass h, int v) => new Object(h).Property<int>("PublicProperty").Set(v);

        // Static.Method with BCL type
        public static string StaticMethod_BCL() => Static.From("System.Environment").Method<Func<string>>("get_CommandLine")();

        // Static.Method with local assembly type
        public static int StaticMethod_Local() => Static.From("Gneedle.Test.Generated.LocalStatic").Method<Func<int>>("GetValue")();

        // Static.Field get/set (will use LocalStatic type from test setup)
        public static int StaticField_Get() => Static.From("Gneedle.Test.Generated.LocalStatic").Field<int>("StaticField").Get();
        public static void StaticField_Set(int v) => Static.From("Gneedle.Test.Generated.LocalStatic").Field<int>("StaticField").Set(v);

        // Static.Property get/set
        public static int StaticProperty_Get() => Static.From("Gneedle.Test.Generated.LocalStatic").Property<int>("StaticProperty").Get();
        public static void StaticProperty_Set(int v) => Static.From("Gneedle.Test.Generated.LocalStatic").Property<int>("StaticProperty").Set(v);
    }

    private static MethodInfo Template(Type holder, string name) => holder.GetMethod(name)!;

    #region This: a field

    /// <summary>
    /// Create a host which declares a field of the given name, which is static when it is asked for.
    /// </summary>
    private static TypeHandler NewHostWithField(string fieldName, bool isStatic)
    {
        var handler = (AssemblyHandler) Assembly.Create("MemberInjectionAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var attrs = FieldAttributes.Public | (isStatic ? FieldAttributes.Static : 0);
        host.Source.Fields.Add(new FieldDefinition(fieldName, attrs, host.Source.Module.TypeSystem.Int32));
        return host;
    }

    /// <summary>
    /// Add a method to the host, weave the template into it, and hand back the instructions which came out.
    /// </summary>
    private static Instruction[] Rewrite(TypeHandler host, string methodName, Type returnType, Parameter[] parameters, string template, MethodFlags flags)
    {
        var method = host.AddMethod(methodName, returnType.ToGneedleType(), [], parameters, flags);
        method.SetBody(Template(typeof(ThisMemberTemplates), template));
        return ((MethodHandler) method).Source.Body.Instructions.ToArray();
    }

    [Test]
    public void ReadInstanceField_Rewrites_To_Ldfld()
    {
        var host = NewHostWithField("Value", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceField)));

        var body = ((MethodHandler) method).Source.Body;
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld), Is.True);
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldarg_0), Is.True);
    }

    [Test]
    public void WriteInstanceField_Rewrites_To_Stfld()
    {
        var host = NewHostWithField("Value", isStatic: false);
        var ins = Rewrite(host, "Write", typeof(void), [new Parameter(typeof(int).ToGneedleType())], nameof(ThisMemberTemplates.WriteInstanceField), MethodFlags.Public);

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Stfld), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld), Is.False);
    }

    [Test]
    public void ReadStaticField_Rewrites_To_Ldsfld_Without_Ldarg0()
    {
        var host = NewHostWithField("Value", isStatic: true);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadStaticField)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldsfld), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld), Is.False);
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

        Assert.Catch<ArgumentException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadMissingField))));
    }

    #endregion

    #region This: a property

    /// <summary>
    /// Create a host which declares a property of the given name, with the accessors which are asked for.
    /// </summary>
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

    [Test]
    public void ReadInstanceProperty_Rewrites_To_Call_Getter()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceProperty)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "get_Prop"), Is.True);
    }

    [Test]
    public void WriteInstanceProperty_Rewrites_To_Call_Setter()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: false);
        var method = host.AddMethod("Write", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteInstanceProperty)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "set_Prop"), Is.True);
    }

    [Test]
    public void ReadVirtualProperty_Rewrites_To_Callvirt_Getter()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: true);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceProperty)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Callvirt && ((MethodReference) i.Operand).Name == "get_Prop"), Is.True);
    }

    [Test]
    public void ReadProperty_Without_Getter_Throws()
    {
        var host = NewHostWithProperty("Prop", withGetter: false, withSetter: true, isVirtual: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);

        Assert.Catch<ArgumentException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceProperty))));
    }

    [Test]
    public void WriteProperty_Without_Setter_Throws()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: false, isVirtual: false);
        var method = host.AddMethod("Write", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        Assert.Catch<ArgumentException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteInstanceProperty))));
    }

    #endregion

    #region This: a field of a generic type

    /// <summary>
    /// Create a host which is generic in one parameter, and which declares a field of that type.
    /// </summary>
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
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadGenericField)));
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
        var method = host.AddMethod("Set", typeof(void).ToGneedleType(), [], [new Parameter(new GenericParameterType("T"))], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteGenericField)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var stfld = ins.FirstOrDefault(i => i.OpCode == OpCodes.Stfld);
        Assert.That(stfld, Is.Not.Null);
        Assert.That(((FieldReference) stfld!.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>());
    }

    #endregion

    #region This: a property of a generic type

    /// <summary>
    /// Create a host which is generic in one parameter, and which declares a property of that type with the accessors
    /// which are asked for.
    /// </summary>
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
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadGenericProp)));
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
        var method = host.AddMethod("Set", typeof(void).ToGneedleType(), [], [new Parameter(new GenericParameterType("T"))], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteGenericProp)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var call = ins.FirstOrDefault(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                            && ((MethodReference) i.Operand).Name == "set_Prop");
        Assert.That(call, Is.Not.Null);
        // The setter's parameter type should be the generic parameter T.
        var param = ((MethodReference) call!.Operand).Parameters[0];
        Assert.That(param.ParameterType, Is.InstanceOf<GenericParameter>());
    }

    #endregion

    #region This: a method

    /// <summary>
    /// Create a host which declares a real instance method <c>int Add(int, int)</c>, so that a template which reaches a
    /// method of it has one to be rewritten to.
    /// </summary>
    private static TypeHandler NewHostWithAdd(bool isVirtual)
    {
        var handler = (AssemblyHandler) Assembly.Create("MethodInjectionAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var module = host.Source.Module;
        var attrs = MethodAttributes.Public | MethodAttributes.HideBySig
                    | (isVirtual ? MethodAttributes.Virtual | MethodAttributes.NewSlot : 0);
        var add = new MethodDefinition("Add", attrs, module.TypeSystem.Int32) { DeclaringType = host.Source };
        add.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, module.TypeSystem.Int32));
        add.Parameters.Add(new ParameterDefinition("b", ParameterAttributes.None, module.TypeSystem.Int32));
        var il = add.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(add);
        return host;
    }

    /// <summary>
    /// Create a host which declares a real instance method <c>T Echo(T)</c>, where T is a type which IL loads with one
    /// of the <c>ldc.i4</c> instructions.
    /// </summary>
    private static TypeHandler NewHostWithEcho(Type echoType)
    {
        var handler = (AssemblyHandler) Assembly.Create("MethodInjectionEchoAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var t = host.Source.Module.ImportReference(host.AssemblyHandler.GetCecilType(echoType).Reference);
        var echo = new MethodDefinition("Echo", MethodAttributes.Public | MethodAttributes.HideBySig, t) { DeclaringType = host.Source };
        echo.Parameters.Add(new ParameterDefinition("c", ParameterAttributes.None, t));
        echo.Body.GetILProcessor().Emit(OpCodes.Ret);
        host.Source.Methods.Add(echo);
        return host;
    }

    [Test]
    public void InvokeInstanceMethod_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeInstanceMethod)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        // The delegate Invoke must be rewritten to a direct call to Add, and no delegate
        // construction (ldftn/newobj) should remain.
        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Add"), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);
    }

    [Test]
    public void InvokeViaGenericDelegate_Does_Not_Throw()
    {
        // Bug A: generic delegate (Func<>) used to throw NRE while extracting Invoke params.
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);

        Assert.DoesNotThrow(() => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeViaGenericDelegate))));
    }

    [Test]
    public void InvokeViaGenericDelegate_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeViaGenericDelegate)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Add"), Is.True);
    }

    [Test]
    public void InvokeCharLiteral_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithEcho(typeof(char));
        var method = host.AddMethod("Run", typeof(char).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeCharLiteral)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Echo"), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
    }

    [Test]
    public void InvokeBoolLiteral_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithEcho(typeof(bool));
        var method = host.AddMethod("Run", typeof(bool).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeBoolLiteral)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Echo"), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
    }

    #endregion

    #region Base

    /// <summary>
    /// Create a host which derives from a type of the assembly which carries the members which are asked for, so that a
    /// template which reaches a member of the base type has one to be rewritten to.
    /// </summary>
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
            var getter = new MethodDefinition("get_Prop", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
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

    #endregion

    #region Object

    [Test]
    public void ObjectMethod_With_NewObject_Syntax_Rewrites_To_Direct_Call()
    {
        var asm = Assembly.Create("ObjectPointerAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
            [new Parameter(typeof(HelperClass).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.ObjectMethod_NewSyntax)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        // Should rewrite to call/callvirt HelperClass::Calc, not call Object::Method
        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && i.Operand is MethodReference mr && mr.Name == "Calc"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MethodReference mr && mr.DeclaringType.FullName == Object.TYPE_NAME), Is.False);
    }

    [Test]
    public void ObjectField_Get_Rewrites_To_Ldfld()
    {
        var asm = Assembly.Create("ObjectFieldAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.ObjectField_Get)));

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

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.ObjectField_Set)));

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

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.ObjectProperty_Get)));

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

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.ObjectProperty_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && i.Operand is MethodReference mr && mr.Name == "set_PublicProperty"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Object.TYPE_NAME), Is.False);
    }

    #endregion

    #region Static

    [Test]
    public void StaticMethod_BCL_Type_Rewrites_To_Direct_Call()
    {
        var asm = Assembly.Create("StaticPointerAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(string).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.StaticMethod_BCL)));

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
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.StaticMethod_Local)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        // Should rewrite to call LocalStatic::GetValue
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call
                                 && i.Operand is MethodReference mr && mr.Name == "GetValue"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);
    }

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
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.StaticField_Get)));

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

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.StaticField_Set)));

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
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.StaticProperty_Get)));

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

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ObjectStaticTemplates), nameof(ObjectStaticTemplates.StaticProperty_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference mr && mr.Name == "set_StaticProperty"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);
    }

    #endregion
}
