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
/// <c>Base</c>, <c>Instance</c> and <c>Static</c>. Each of them is a call which throws when it runs, and each of them is
/// rewritten to the member of the target which it names, which is what these tests read back out of the woven body.
/// </summary>
[TestFixture]
public class PointerTests
{
    private const string Ns = "Gneedle.Test.Generated";

    /// <summary>
    /// The type which the templates of <c>Instance</c> hold an instance of, and which the tests pass as the argument of a
    /// method which is woven.
    /// </summary>
    public class HelperClass
    {
        public int Calc(int a) => a * 2;
        public int PublicField;
        public static int StaticField;
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

        // The field is read and written in one expression, so that the accessor of the read stands between the
        // placeholder of the write and the accessor which the write is written to.
        public static void AddOneToInstanceField() => This.Field<int>("Value").Set(This.Field<int>("Value").Get() + 1);

        public static int ReadInstanceProperty() => This.Property<int>("Prop").Get();
        public static void WriteInstanceProperty(int v) => This.Property<int>("Prop").Set(v);

        // A static property is reached through the placeholder which an instance one is, which is where the two
        // templates are the same: whether a receiver is written is decided by the member which the name finds.
        public static int ReadStaticProperty() => This.Property<int>("Value").Get();

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

        // The same local is handed to the call twice, which is what a local is for: `ldloc` reads the value of the
        // local rather than taking it away, so the second read is of the type which the first one read.
        public static int InvokeWithALocalReadTwice(int a, int b)
        {
            var sum = a + b;
            return This.Method<IntBinaryOp>("Add")(sum, sum);
        }

        /// <summary>
        /// Name the method through a value which the template computes, which is a name the weaving has nowhere to read.
        /// </summary>
        public static int InvokeByNameWhichIsComputed()
        {
            var name = "Add";
            return This.Method<IntBinaryOp>(name)(1, 2);
        }

        /// <summary>
        /// Name the method through a constant of the template, which the compiler writes where the call is, so that the
        /// weaving reads the same name a literal gives it.
        /// </summary>
        public static int InvokeByNameWhichIsAConstant()
        {
            const string name = "Add";
            return This.Method<IntBinaryOp>(name)(1, 2);
        }

        // Returns the delegate without invoking -> branch that builds a delegate (ldftn+newobj).
        public static IntBinaryOp GetInstanceMethodDelegate() => This.Method<IntBinaryOp>("Add");

        // The same, of a member which is static, whose delegate is built out of the pointer alone unless the target
        // which the constructor of a delegate takes is written for it.
        public static LongOp GetStaticMethodDelegate() => This.Method<LongOp>("Widen");

        // The same, of a generic delegate of the framework, which the template names as an instantiation: the
        // constructor of the definition which it is an instantiation of is one of a type which no assembly declares.
        public static Func<long, long> GetGenericMethodDelegate() => This.Method<Func<long, long>>("Widen");

        // Generic delegate (Func<>) currently trips ParseMethod: see MethodParser.cs:40-50.
        public static int InvokeViaGenericDelegate(int a, int b) => This.Method<Func<int, int, int>>("Add")(a, b);

        // The member takes a wider value than the one which the template computes for it, so the call is written with a
        // conversion of it, and the value which the call is made with is the one the conversion leaves.
        public delegate long LongOp(long value);

        public static long InvokeWithAConvertedArgument(int a) => This.Method<LongOp>("Widen")(a + a);

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
    /// Templates which reach a member of an instance the template holds, through <c>Instance</c>, and of a type which the
    /// template names as a string, through <c>Static</c>.
    /// </summary>
    public static class InstanceStaticTemplates
    {
        public delegate int IntOp(int a);

        // Instance.Method with new Instance(param) syntax
        public static int InstanceMethod_NewSyntax(HelperClass h, int a) => new Instance(h).Method<IntOp>("Calc")(a);

        /// <summary>
        /// The delegate of <c>Instance.Method</c> is handed back rather than invoked where the template names the method,
        /// which is the shape the weaving reads without a call of it to follow.
        /// </summary>
        public static IntOp InstanceMethod_AsADelegate(HelperClass h) => new Instance(h).Method<IntOp>("Calc");

        /// <summary>
        /// The instance of <c>Instance</c> is named from a body which holds more locals than the macro opcodes of a local
        /// address, so the ones beyond the third are stored and loaded in the operand form, whose operand the reader of
        /// Cecil hands back as the variable itself rather than as the slot of it.
        /// </summary>
        public static int InstanceMethod_OfABodyWhichHoldsManyLocals(HelperClass h, int a)
        {
            var first = 1;
            var second = first + 1;
            var third = second + 1;
            var fourth = third + 1;
            var fifth = fourth + 1;
            return new Instance(h).Method<IntOp>("Calc")(a + fifth);
        }

        // Instance.Field get/set
        public static int InstanceField_Get(HelperClass h) => new Instance(h).Field<int>("PublicField").Get();
        public static void InstanceField_Set(HelperClass h, int v) => new Instance(h).Field<int>("PublicField").Set(v);

        /// <summary>
        /// The field which the instance of <c>Instance</c> names is static, so the member being woven is reached through
        /// no receiver at all and the sequence which named the instance is dropped whole.
        /// </summary>
        public static int InstanceStaticField_Get(HelperClass h) => new Instance(h).Field<int>("StaticField").Get();

        /// <summary>
        /// The instance of <c>Instance</c> is read off a parameter which no macro opcode of the template carries, so the
        /// load of it names the parameter rather than the slot, which is read back off the parameter it names.
        /// </summary>
        public static int InstanceField_Get_OfALaterParameter(object a, object b, object c, object d, HelperClass h) => new Instance(h).Field<int>("PublicField").Get();

        /// <summary>
        /// The instance of <c>Instance</c> is held in a local of the template rather than read off a parameter where the
        /// name of the field is written, so the sequence which names the type of the instance is not the one which the
        /// weaving reads a type off.
        /// </summary>
        public static int InstanceField_OfAnInstanceInALocal(HelperClass h)
        {
            var instance = h;
            return new Instance(instance).Field<int>("PublicField").Get();
        }

        // Instance.Property get/set
        public static int InstanceProperty_Get(HelperClass h) => new Instance(h).Property<int>("PublicProperty").Get();
        public static void InstanceProperty_Set(HelperClass h, int v) => new Instance(h).Property<int>("PublicProperty").Set(v);

        // Static.Method with BCL type
        public static string StaticMethod_BCL() => Static.From("System.Environment").Method<Func<string>>("get_CommandLine")();

        // Static.Method with local assembly type
        public static int StaticMethod_Local() => Static.From("Gneedle.Test.Generated.LocalStatic").Method<Func<int>>("GetValue")();

        // Static.Field get/set (will use LocalStatic type from test setup)
        public static int StaticField_Get() => Static.From("Gneedle.Test.Generated.LocalStatic").Field<int>("StaticField").Get();
        public static void StaticField_Set(int v) => Static.From("Gneedle.Test.Generated.LocalStatic").Field<int>("StaticField").Set(v);

        /// <summary>
        /// The type which <c>Static</c> names is held in a local of the template rather than written where the name of
        /// the field is, which is a name the weaving has nowhere to read.
        /// </summary>
        public static int StaticField_OfATypeInALocal()
        {
            var typeName = "Gneedle.Test.Generated.LocalStatic";
            return Static.From(typeName).Field<int>("StaticField").Get();
        }

        // Static.Property get/set
        public static int StaticProperty_Get() => Static.From("Gneedle.Test.Generated.LocalStatic").Property<int>("StaticProperty").Get();
        public static void StaticProperty_Set(int v) => Static.From("Gneedle.Test.Generated.LocalStatic").Property<int>("StaticProperty").Set(v);
    }

    private static MethodInfo Template(Type holder, string name) => holder.GetMethod(name)!;

    #region This: a field

    /// <summary>
    /// Create a host which declares a field of the given name, which is static when it is asked for.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which runs its host gives one of its own.</param>
    private static TypeHandler NewHostWithField(string fieldName, bool isStatic, string assemblyName = "MemberInjectionAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
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
    public void A_Template_Which_Reads_And_Writes_A_Field_Writes_The_Field_It_Read()
    {
        // The two placeholders name the same field, and the accessor which each is paired with is the one which the
        // value it pushed is the receiver of: the write used to be paired with the read of the inner placeholder,
        // because the read is the first accessor after the name, and the parse then refused the second of them.
        var host = NewHostWithField("Value", isStatic: false, "FieldReadAndWriteAssembly");
        var ins = Rewrite(host, "Bump", typeof(void), [], nameof(ThisMemberTemplates.AddOneToInstanceField), MethodFlags.Public);

        Assert.That(ins.Count(i => i.OpCode == OpCodes.Ldfld), Is.EqualTo(1), "the field was not read exactly once.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Stfld), Is.EqualTo(1), "the field was not written exactly once.");

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var instance = Activator.CreateInstance(type);
        type.GetField("Value")!.SetValue(instance, 41);
        type.GetMethod("Bump")!.Invoke(instance, null);

        Assert.That(type.GetField("Value")!.GetValue(instance), Is.EqualTo(42));
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

        Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadMissingField))));
    }

    #endregion

    #region This: a property

    /// <summary>
    /// The value which the getter of a property of a host hands back, which a weave of it is run to read.
    /// </summary>
    private const int PropertyValue = 4242;

    /// <summary>
    /// Create a host which declares a property of the given name, with the accessors which are asked for, which are
    /// static when that is asked for.
    /// </summary>
    private static TypeHandler NewHostWithProperty(string propertyName, bool withGetter, bool withSetter, bool isVirtual, bool isStatic = false)
    {
        var handler = (AssemblyHandler) Assembly.Create("MemberInjectionPropAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var module = host.Source.Module;
        var propertyType = module.TypeSystem.Int32;
        var property = new PropertyDefinition(propertyName, PropertyAttributes.None, propertyType);
        var methodAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig
                          | (isStatic ? MethodAttributes.Static : 0)
                          | (isVirtual ? MethodAttributes.Virtual | MethodAttributes.NewSlot : 0);

        if (withGetter)
        {
            var getter = new MethodDefinition($"get_{propertyName}", methodAttrs, propertyType) { DeclaringType = host.Source };
            // The getter hands back a value of its own rather than reading a field, so that a weave which calls it can
            // be run and not only read: a body which returns from a member which hands back an int without leaving one
            // on the stack is not IL which the runtime accepts.
            var il = getter.Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4, PropertyValue);
            il.Emit(OpCodes.Ret);
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

        Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceProperty))));
    }

    [Test]
    public void WriteProperty_Without_Setter_Throws()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: false, isVirtual: false);
        var method = host.AddMethod("Write", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteInstanceProperty))));
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
    public void ThisMethod_Of_A_Name_Which_Is_A_Constant_Is_Read_Like_A_Literal()
    {
        // A constant of the template is what the compiler writes where the call is, so a name which is one is the name
        // which is read out of the instruction ahead of the call.
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeByNameWhichIsAConstant)));

        Assert.That(((MethodHandler) method).Source.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference
                                                                                        && reference.Name == "Add"), Is.True);
    }

    [Test]
    public void ThisMethod_Of_A_Name_Which_Is_Not_Written_Throws()
    {
        // The name is read out of the instruction ahead of the call, so a name which the template computes is one which
        // nothing holds: the weaving used to leave the call as it was written, and the member which was woven reached
        // the placeholder and threw when it ran. It is refused where the weaving runs instead, by name.
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [],
            MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeByNameWhichIsComputed))));

        Assert.That(thrown!.Message, Does.Contain("is not written where the call is"));
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

    /// <summary>
    /// Create a host which declares a real static method <c>long Widen(long)</c>, which is <c>value + 1</c>, so that a
    /// template which hands an argument of another type to it has one to be rewritten to and a body which runs.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which runs its host gives one of its own.</param>
    private static TypeHandler NewHostWithWiden(string assemblyName = "MethodInjectionConversionAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var module = host.Source.Module;
        var widen = new MethodDefinition("Widen", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Int64)
        {
            DeclaringType = host.Source,
        };
        widen.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int64));
        var il = widen.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(widen);
        return host;
    }

    [Test]
    public void InvokeWithAConvertedArgument_Rewrites_To_Direct_Call()
    {
        // The argument is computed as an int and the member takes a long, so the call is written with the conversion of
        // it: the walk of the stack used to leave the value which was converted where the conversion had left another,
        // so the argument was compared as the type it was before the call and the call was left as a call of the
        // delegate, which reaches the placeholder rather than the member when the woven body runs.
        var host = NewHostWithWiden();
        var method = host.AddMethod(
            "Run",
            typeof(long).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithAConvertedArgument)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "Widen"), Is.True,
                    "the delegate was not rewritten to a direct call to Widen.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(null, [3]), Is.EqualTo(7L));
    }

    [Test]
    public void ThisMethod_Of_A_Static_Member_Which_Is_Handed_Back_Is_Built_With_A_Null_Target()
    {
        // The delegate is built rather than called, and the constructor of a delegate takes the pointer of the member
        // together with the instance which it is called on, which a member of no instance has none of: the body was
        // written with the pointer alone, which is a stack the constructor cannot be called with at all.
        var host = NewHostWithWiden("MethodInjectionStaticDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(ThisMethodTemplates.LongOp).ToGneedleType(),
            [],
            [],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.GetStaticMethodDelegate)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var pointer = Array.FindIndex(ins, instruction => instruction.OpCode == OpCodes.Ldftn);
        Assert.That(pointer, Is.GreaterThanOrEqualTo(0), "the delegate was not built out of the pointer of the member.");
        Assert.That(pointer > 0 && ins[pointer - 1].OpCode == OpCodes.Ldnull, Is.True,
                    "the constructor of the delegate was not given the target which a member of no instance takes.");

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        var widen = (ThisMethodTemplates.LongOp) type.GetMethod("Run")!.Invoke(null, null)!;

        Assert.That(widen(3), Is.EqualTo(4L));
    }

    [Test]
    public void ThisMethod_Of_A_Generic_Delegate_Which_Is_Handed_Back_Is_Built_Of_The_Type_It_Names()
    {
        // The delegate which is handed back is one of the framework's generic ones, which the template names as an
        // instantiation: the constructor was taken from the definition which that one is an instantiation of, and the
        // open type of a generic delegate is a type which no assembly declares, so the woven body could not be loaded.
        var host = NewHostWithWiden("MethodInjectionGenericDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(Func<long, long>).ToGneedleType(),
            [],
            [],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.GetGenericMethodDelegate)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var construction = ins.FirstOrDefault(i => i.OpCode == OpCodes.Newobj);
        Assert.That(construction, Is.Not.Null, "the delegate was not built at all.");
        Assert.That(((MethodReference) construction!.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                    "the constructor of the delegate was written on the definition rather than on the type which was named.");

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        var widen = (Func<long, long>) type.GetMethod("Run")!.Invoke(null, null)!;

        Assert.That(widen(3), Is.EqualTo(4L));
    }

    [Test]
    public void ThisMethod_Of_A_Local_Which_Is_Read_Twice_Is_Rewritten()
    {
        // A local which is read twice is read once by the walk of the stack and once more by the body, and the walk
        // used to take the type of the local away at the first read: the second read put a value with no type on the
        // stack, and the arguments were compared against it with nothing to compare. The read leaves the local where
        // it is, so both reads are of the type which the store of the local recorded.
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithALocalReadTwice)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Add"), Is.True,
                    "the delegate was not rewritten to a direct call to Add.");
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

    [Test]
    public void Base_Member_Of_A_Type_Which_Derives_From_Nothing_Is_Refused()
    {
        // A type which derives from nothing holds no base type to look a member up on, so the member which the template
        // names cannot be resolved, and what the caller is left with is the reason: the error names the member which was
        // looked for rather than being one which the lookup of the base type itself failed over.
        var asm = Assembly.Create("NoBasePointerAssembly");
        var host = (TypeHandler) ((AssemblyHandler) asm.Handler).AddClass("Derived", Ns, ClassFlags.Public).GetHandler();
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

    #endregion

    #region Instance

    [Test]
    public void InstanceMethod_With_NewInstance_Syntax_Rewrites_To_Direct_Call()
    {
        var asm = Assembly.Create("InstancePointerAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
            [new Parameter(typeof(HelperClass).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_NewSyntax)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        // Should rewrite to call/callvirt HelperClass::Calc, not call Instance::Method
        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && i.Operand is MethodReference mr && mr.Name == "Calc"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MethodReference mr && mr.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
    }

    [Test]
    public void InstanceField_Get_Rewrites_To_Ldfld()
    {
        var asm = Assembly.Create("InstanceFieldAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_Get)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference fr && fr.Name == "PublicField"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
    }

    [Test]
    public void InstanceField_Set_Rewrites_To_Stfld()
    {
        var asm = Assembly.Create("InstanceFieldAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference fr && fr.Name == "PublicField"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
    }

    [Test]
    public void InstanceProperty_Get_Rewrites_To_Call_Getter()
    {
        var asm = Assembly.Create("InstancePropertyAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceProperty_Get)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && i.Operand is MethodReference mr && mr.Name == "get_PublicProperty"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
    }

    [Test]
    public void InstanceProperty_Set_Rewrites_To_Call_Setter()
    {
        var asm = Assembly.Create("InstancePropertyAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();

        var method = host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceProperty_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && i.Operand is MethodReference mr && mr.Name == "set_PublicProperty"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
    }

    [Test]
    public void InstanceField_Of_An_Instance_Which_Is_Held_In_A_Local_Throws()
    {
        // The type which the field is looked up on is named by the sequence which leads to the name of the field, and a
        // sequence which the weaving does not recognize names no type at all. The name is not looked up on the member
        // being woven instead, which holds a field of that name of its own here: the field of the template is one of the
        // instance the template holds, and a name which is woven into another member than the one it names is worse
        // than a name which is refused.
        var host = NewHostWithField("PublicField", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [new Parameter(typeof(HelperClass).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_OfAnInstanceInALocal))));

        Assert.That(thrown!.Message, Does.Contain("PublicField"));
    }

    /// <summary>
    /// Create an assembly which holds a type of one method, which takes the arguments which the templates of
    /// <c>Instance</c> name and belongs to an instance unless it is asked not to.
    /// </summary>
    /// <param name="assemblyName">The name of the assembly, which is the identity the runtime loads it by.</param>
    /// <param name="parameters">The arguments of the member which is woven.</param>
    /// <param name="isStatic">Whether the member which is woven belongs to no instance.</param>
    private static (Assembly Assembly, TypeHandler Host, MethodHandler Method) NewInstanceHost(string assemblyName, Type[] parameters, bool isStatic = false)
    {
        var assembly = Assembly.Create(assemblyName);
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToGneedleType(), [],
            parameters.Select(type => new Parameter(type.ToGneedleType())).ToArray(),
            MethodFlags.Public | (isStatic ? MethodFlags.Static : 0));

        if (isStatic) return (assembly, host, method);

        // A type which Cecil emits carries no constructor of its own, and one is needed to create an instance of it,
        // which is what the tests below do to run the member which they wove.
        var module = assembly.Source.MainModule;
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

        return (assembly, host, method);
    }

    /// <summary>
    /// The instruction which was written ahead of the arguments of the one which reaches the member of the given name,
    /// which is the receiver of it.
    /// </summary>
    /// <param name="instructions">The body of the member which was woven.</param>
    /// <param name="member">The name of the member which the instruction reaches.</param>
    /// <param name="arguments">How many arguments the instruction which reaches the member reads, which stand between it and the receiver.</param>
    private static Instruction ReceiverOf(Instruction[] instructions, string member, int arguments = 0)
    {
        for (var index = 1; index < instructions.Length; index++)
        {
            if (instructions[index].Operand is MemberReference reference && reference.Name == member) return instructions[index - 1 - arguments];
        }

        Assert.Fail($"No instruction reaching '{member}' was written.");
        return null!;
    }

    [Test]
    public void InstanceField_Of_A_Parameter_Loads_The_Argument_Which_Holds_It()
    {
        // The instance which the template names is a parameter of the template, and the member being woven holds that
        // argument at a slot of its own: what stands ahead of the field access is the load of that argument. The load
        // of `this` which stood there instead is another object than the one the template named, which the runtime
        // refuses where the types of the two do not meet.
        var (_, _, method) = NewInstanceHost("InstanceFieldReceiverAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_Get)));

        Assert.That(ReceiverOf(method.Source.Body.Instructions.ToArray(), "PublicField").OpCode, Is.EqualTo(OpCodes.Ldarg_1),
                    "the field is reached through `this` rather than through the instance which the template named.");
    }

    [Test]
    public void InstanceField_Of_A_Later_Parameter_Loads_That_Argument_By_Its_Slot()
    {
        // A slot which no macro opcode of the member being woven carries is written as the operand form, which names the
        // parameter rather than the slot, so what the receiver is read back off is the parameter the template named.
        var (_, _, method) = NewInstanceHost("InstanceLaterParameterAssembly", [typeof(object), typeof(object), typeof(object), typeof(object), typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_Get_OfALaterParameter)));

        var receiver = ReceiverOf(method.Source.Body.Instructions.ToArray(), "PublicField");
        Assert.That(receiver.OpCode, Is.EqualTo(OpCodes.Ldarg));
        Assert.That(((ParameterReference) receiver.Operand).Index, Is.EqualTo(4),
                    "the argument was loaded from the slot of another parameter.");
    }

    [Test]
    public void InstanceProperty_Of_A_Parameter_Loads_The_Argument_Which_Holds_It()
    {
        var (_, _, method) = NewInstanceHost("InstancePropertyReceiverAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceProperty_Get)));

        Assert.That(ReceiverOf(method.Source.Body.Instructions.ToArray(), "get_PublicProperty").OpCode, Is.EqualTo(OpCodes.Ldarg_1),
                    "the property is reached through `this` rather than through the instance which the template named.");
    }

    [Test]
    public void InstanceMethod_Of_A_Parameter_Loads_The_Argument_Which_Holds_It()
    {
        var (_, _, method) = NewInstanceHost("InstanceMethodReceiverAssembly", [typeof(HelperClass), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_NewSyntax)));

        Assert.That(ReceiverOf(method.Source.Body.Instructions.ToArray(), "Calc", arguments: 1).OpCode, Is.EqualTo(OpCodes.Ldarg_1),
                    "the method is called on `this` rather than on the instance which the template named.");
    }

    [Test]
    public void InstanceField_Of_A_Static_Field_Is_Reached_Through_No_Receiver()
    {
        // The field which the instance of `Instance` names belongs to the type alone, so the member being woven holds no
        // receiver for it: the sequence which named the instance is dropped whole, and the load of a `this` written
        // where the member is static is a body which the runtime refuses to run.
        var (_, _, method) = NewInstanceHost("InstanceStaticFieldReceiverAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceStaticField_Get)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && field.Name == "StaticField"), Is.True,
                    "the static field was not read through the type which the template named.");
        Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False,
                    "a receiver was written where the member being woven holds none.");
    }

    [Test]
    public void InstanceMethod_Of_A_Parameter_Reads_The_Instance_Which_Was_Given()
    {
        // What the tests above read out of the body, run: a receiver which is the load of `this` reads the member of
        // another object than the one which was given, which is what the runtime refuses.
        var (_, host, method) = NewInstanceHost("InstanceFieldReceiverRunAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_Get)));

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        var helper = new HelperClass { PublicField = 21 };

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [helper]), Is.EqualTo(21));
    }

    [Test]
    public void InstanceMethod_Which_Is_Handed_Back_As_A_Delegate_Is_Reached_Through_The_Instance_Which_Named_It()
    {
        // The method is not invoked where the template names it, so the instructions which named the type of it stand in
        // the body until the delegate is built from the pointer of it. The sequence which built the instance of `Instance`
        // is dropped with them, which it was not: what was left of it was an array on the stack of the woven member.
        var (_, host, method) = NewInstanceHost("InstanceDelegateReceiverAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_AsADelegate)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Newarr), Is.False,
                    "the array which built the instance of `Instance` was left in the body.");
        Assert.That(ReceiverOf(ins, "Calc").OpCode, Is.EqualTo(OpCodes.Ldarg_1),
                    "the pointer of the method was taken ahead of `this` rather than of the instance which the template named.");

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        var calc = (InstanceStaticTemplates.IntOp) type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new HelperClass()])!;

        Assert.That(calc(21), Is.EqualTo(42));
    }

    [Test]
    public void InstanceMethod_Of_A_Body_Which_Holds_Many_Locals_Is_Reached_Through_The_Instance_Which_Named_It()
    {
        // The stack which the weaving carries along the body while it looks for the call of the delegate is balanced over
        // the locals of the template as well. A local beyond the third is stored and loaded in the operand form, whose
        // operand the reader of Cecil hands back as the variable itself, which is read as the slot of it rather than cast.
        var (_, host, method) = NewInstanceHost("InstanceManyLocalsAssembly", [typeof(HelperClass), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfABodyWhichHoldsManyLocals)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(instruction => instruction.Operand is MethodReference { Name: "Calc" }), Is.True,
                    "the member which the template named was not called.");
        Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False,
                    "the method is called on `this` rather than on the instance which the template named.");

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        var helper = new HelperClass();

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [helper, 16]), Is.EqualTo(42),
                    "the body which held the locals was not woven into the member which runs.");
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
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.StaticMethod_BCL)));

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
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.StaticMethod_Local)));

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
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.StaticField_Get)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference fr && fr.Name == "StaticField"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);
    }

    [Test]
    public void StaticField_Of_A_Type_Which_Is_Held_In_A_Local_Throws()
    {
        // The type which the field is looked up on is the name which the template writes where the field is named, so a
        // name which the template computed reaches the weaving nowhere: the member being woven is not the type which
        // the field is named of, and the name is refused rather than looked up on it.
        var host = NewHostWithField("StaticField", isStatic: true);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);

        var thrown = Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.StaticField_OfATypeInALocal))));

        Assert.That(thrown!.Message, Does.Contain("StaticField"));
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
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.StaticField_Set)));

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
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.StaticProperty_Get)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference mr && mr.Name == "get_StaticProperty"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);

        // The property holds a getter and no setter, and the member which is woven is static: no receiver is written,
        // because the property being reached is of a static member and the accessor of it is static as well. A receiver
        // written here is the load of a `this` which the member does not have.
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldarg_0), Is.False);
    }

    [Test]
    public void StaticReadOnlyProperty_Of_This_Runs_The_Getter()
    {
        // The live case of the two accessors of a property which are not both there: a static property which only
        // hands a value back. What the weave writes for it is a call without a receiver, and a load of `this` written
        // where the member is static is a body which the runtime refuses to run, so the member is run rather than read.
        var host = NewHostWithProperty("Value", withGetter: true, withSetter: false, isVirtual: false, isStatic: true);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadStaticProperty)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference mr && mr.Name == "get_Value"), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldarg_0), Is.False);

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(null, null), Is.EqualTo(PropertyValue));
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
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.StaticProperty_Set)));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference mr && mr.Name == "set_StaticProperty"), Is.True);
        Assert.That(ins.Any(i => i.Operand is MemberReference mr && mr.DeclaringType.FullName == Static.TYPE_NAME), Is.False);
    }

    #endregion

    #region The name which the placeholder of the instance is declared under

    /// <summary>
    /// The placeholder which wraps the instance a template holds is declared under a name which says what it holds,
    /// because a placeholder which was named after the type of the framework is the type which a template reads
    /// wherever it writes that name among the usings of this library, the keyword <c>object</c> and the full name of
    /// the type being all that is left of it: a template which declares a field, a parameter, a local or a return type
    /// of that name reads the placeholder, and the body which is written from it names a member of the type being woven
    /// where it meant to name a type of the framework.<para/>
    /// The name which the weaving answers a member reference by is the full name of the class, which is built from the
    /// name of the class where the class is declared: the class and the name are read together, so that a class which
    /// is renamed is a class which the weaving knows by the name which it holds now.
    /// </summary>
    [Test]
    public void The_Placeholder_Of_The_Instance_Is_Declared_Under_A_Name_Which_Shadows_Nothing()
    {
        var placeholder = typeof(This).Assembly.GetType("Gneedle.Inject.Instance");
        Assert.That(placeholder, Is.Not.Null, "the placeholder of the instance is not declared under a name which says what it holds.");

        var typeName = placeholder!.GetField("TYPE_NAME", BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue() as string;
        Assert.That(typeName, Is.EqualTo("Gneedle.Inject.Instance"), "the name which the weaving knows the placeholder by is not the full name of the class.");

        Assert.That(typeof(This).Assembly.GetType("Gneedle.Inject.Object"), Is.Null,
            "a class of this library is declared under a name which shadows the type of the framework.");
    }

    #endregion
}