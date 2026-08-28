using Mono.Cecil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

[TestFixture]
public class MethodInjectionTests
{
    private const string Ns = "Gneedle.Test.Generated";

    // Non-generic delegate so ParseMethod's Invoke-parameter extraction sees concrete
    // parameter types (int,int) rather than open generic parameters.
    public delegate int IntBinaryOp(int a, int b);

    // Delegates whose parameter is loaded via ldc.i4.* literals in IL, which lose the
    // distinction between char/bool/short/int on the evaluation stack.
    public delegate char CharOp(char c);
    public delegate bool BoolOp(bool b);

    // Templates call the placeholder This.Method<TDelegate>(name) API. The delegate's
    // Invoke signature drives parameter matching against the real target method.
    public static class Templates
    {
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

    // Adds a real instance method `int Add(int,int)` to the host so This.Method can resolve it.
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

    private static System.Reflection.MethodInfo Template(string name) => typeof(Templates).GetMethod(name)!;

    [Test]
    public void InvokeInstanceMethod_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [typeof(int).ToGneedleType(), typeof(int).ToGneedleType()],
            MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.InvokeInstanceMethod)));
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
            [typeof(int).ToGneedleType(), typeof(int).ToGneedleType()],
            MethodFlags.Public);

        Assert.DoesNotThrow(() => method.SetBody(Template(nameof(Templates.InvokeViaGenericDelegate))));
    }

    [Test]
    public void InvokeViaGenericDelegate_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [typeof(int).ToGneedleType(), typeof(int).ToGneedleType()],
            MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.InvokeViaGenericDelegate)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Add"), Is.True);
    }

    // Adds a real instance method `T Echo(T)` where T is a primitive that IL loads via ldc.i4.
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
    public void InvokeCharLiteral_Rewrites_To_Direct_Call()
    {
        var host = NewHostWithEcho(typeof(char));
        var method = host.AddMethod("Run", typeof(char).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(nameof(Templates.InvokeCharLiteral)));
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
        method.SetBody(Template(nameof(Templates.InvokeBoolLiteral)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                 && ((MethodReference) i.Operand).Name == "Echo"), Is.True);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
    }
}
