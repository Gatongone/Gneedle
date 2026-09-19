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

        /// <summary>
        /// A field which holds another instance of the type, which is what a template reaches a member through where the
        /// instance of <c>Instance</c> is read off an instance rather than loaded on its own.
        /// </summary>
        public HelperClass? Inner;
    }

    /// <summary>
    /// The same, of a type which declares a parameter of its own: every member which a template reaches through an
    /// instance of it belongs to the definition of the type, which is what the body which is woven cannot name.
    /// </summary>
    public class GenericHelper<T>
    {
        public int Calc(int a) => a * 2;
        public int PublicField;
        public int PublicProperty => 42;
        public U Identity<U>(U value) => value;
    }

    /// <summary>
    /// The type which declares a parameter of its own and holds the member of the body which is reached through an
    /// instance of another type entirely.
    /// </summary>
    public class GenericBaseOfAnInstance<T>
    {
        public int Calc(int a) => a * 3;
    }

    /// <summary>
    /// The type which derives from an instantiation of the type above: the member belongs to the definition of that
    /// base, and the type which the template named an instance of is this one rather than that base.
    /// </summary>
    public class DerivedOfAGenericBase : GenericBaseOfAnInstance<int>;

    /// <summary>
    /// The type which derives from the type above where the instantiation is written with a parameter of its own: the
    /// argument which reaches the base of it is the parameter the declaration handed down, and the type which reads the
    /// member is the base of this one.
    /// </summary>
    public class MiddleOfAGenericBase<T> : GenericBaseOfAnInstance<T>;

    /// <summary>
    /// The type which derives from an instantiation of the type above, which is the type the template is handed.
    /// </summary>
    public class DerivedOfAMiddleOfAGenericBase : MiddleOfAGenericBase<int>;

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

        /// <summary>
        /// The value which is written holds an element of an array which the template creates, which stands between the
        /// value of the write and the accessor which writes it.
        /// </summary>
        public static void AddTheFirstElementOfAnArrayToTheField()
            => This.Field<int>("Value").Set(This.Field<int>("Value").Get() + new[] { 1 }[0]);

        // Two of the cases begin where the name of a member stands rather than where a value is loaded, and the
        // compiler writes the switch as a table of the instructions the cases begin at.
        public static int ReadAFieldPerCase(int value)
        {
            switch (value)
            {
                case 0: return This.Field<int>("Value").Get();
                case 1: return This.Field<int>("Other").Get();
                case 2: return 20;
                case 3: return 30;
                case 4: return 40;
                default: return -1;
            }
        }

        // The handle which the placeholder handed back is held in a local, and the member is read and written through
        // that local rather than where the name stands: the accessors belong to the local, whose value is the handle.
        public static int BumpAHeldHandle(int by)
        {
            var count = This.Field<int>("Value");
            count.Set(count.Get() + by);
            return count.Get();
        }

        // The same, when the field is also named where it is read, so that what the template holds and what it names
        // are woven into one body.
        public static int BumpAHeldHandleAndTheFieldItself(int by)
        {
            var count = This.Field<int>("Value");
            count.Set(count.Get() + by);
            return count.Get() + This.Field<int>("Value").Get();
        }

        // The same, of a field which belongs to no instance, which every read through the handle is written without a
        // receiver for.
        public static int BumpAHeldHandleOfAStaticField(int by)
        {
            var count = This.Field<int>("Value");
            count.Set(count.Get() + by);
            return count.Get();
        }

        // The same, of a property, which is read and written through the accessors which the handle calls stand for.
        public static int BumpAHeldHandleOfAProperty(int by)
        {
            var prop = This.Property<int>("Prop");
            prop.Set(prop.Get() + by);
            return prop.Get();
        }

        // The handle is read for something other than the member which it stands for, which is a value which the
        // weaving has no way to write.
        public static int ReadAHeldHandleAsAValue()
        {
            var count = This.Field<int>("Value");
            var copy  = count;
            return copy.Get();
        }

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

        // The argument of the call is computed along a branch, so the value which the member is handed is pushed by one
        // of the paths which the branch leaves for rather than in a row with the value after it: the invocation which
        // the symbol stands for is the one every path reaches with the arguments of it.
        public static int InvokeWithAConditionalArgument(int a, int b, bool first)
            => This.Method<IntBinaryOp>("Add")(first ? a : b, a);

        // A symbol which names a member on each arm of a branch, whose value the local holds and which the template
        // invokes once after the join: the invocation is reached by the path of either arm, so what it is made on is the
        // delegate which the arm which ran built rather than the one which either symbol names, and the store which the
        // local is written by is reached by the path of either arm as well.
        public static int InvokeAMemberWhichAConditionNames(int a, int b, bool first)
        {
            var op = first ? This.Method<IntBinaryOp>("Add") : This.Method<IntBinaryOp>("Subtract");
            return op(a, b);
        }

        // The same, of symbols which stand where they are invoked: the invocation is reached by the path of either arm,
        // and neither of the two stands on every path which reaches it, so neither stands for the value it is made on.
        public static int InvokeAMemberWhichAConditionNamesWhereItStands(int a, int b, bool first)
            => (first ? This.Method<IntBinaryOp>("Add") : This.Method<IntBinaryOp>("Subtract"))(a, b);

        // A symbol which stands inside a region which the template protects, so that the walk of the paths of the body
        // reads what the region leaves for as well as what stands in a row: the handler is entered where the runtime
        // hands the control to it, and every path which the body takes to the invocation of the delegate passes through
        // the symbol still.
        public static int InvokeAMemberInsideAProtectedRegion(int a, int b)
        {
            try
            {
                return This.Method<IntBinaryOp>("Add")(a, b);
            }
            catch (InvalidOperationException)
            {
                return -1;
            }
        }

        // The delegate is held in a local and invoked by it rather than where the symbol stands, which is what a
        // template which reads the member through a delegate of its own and then calls it writes. The local is read
        // twice, so each read is written as a receiver of its own.
        public static int InvokeAHeldDelegate(int a)
        {
            var add = This.Method<Func<int, int, int>>("Add");
            return add(a, a) + add(a, 1);
        }

        // The delegate which the local holds is invoked with the value of its own invocation as an argument, so the
        // invocation of the inner read stands among the arguments of the outer one: the delegate which the outer
        // invocation is made with is the one the local holds no less than the inner one, and the two reads were answered
        // for the same instruction, which the second answer wrote over.
        public static int InvokeAHeldDelegateWithTheValueOfItsOwnInvocation(int a)
        {
            var add = This.Method<Func<int, int, int>>("Add");
            return add(add(a, a), a);
        }

        // Two symbols which name the same member stand one within the arguments of the other, so neither of them is
        // stored anywhere: the invocation of the inner symbol is the first call of the delegate's type after the outer
        // symbol stands, and the arguments of the outer invocation matched it for the outer symbol as well.
        public static int InvokeASymbolWithTheValueOfAnother(int a)
            => This.Method<Func<int, int, int>>("Add")(This.Method<Func<int, int, int>>("Add")(a, a), a);

        // The member which the symbol names declares a parameter of its own, and the delegate is written with no token
        // of it: the types of the parameters of the delegate are what the member is looked up by, so the member is
        // found, and the call of it stands on the definition of the member with the parameter of it left open.
        public delegate int IntOp(int a);

        /// <inheritdoc cref="IntOp"/>
        public static int InvokeAMemberWhichDeclaresAParameterOfItsOwn(int a) => This.Method<IntOp>("Touch")(a);

        // The member which the symbol names declares a parameter of its own, and the delegate stands for it with the
        // token of a parameter of the body which bears the name of that parameter and stands at another position than it
        // does: the member is looked up by the types of the parameters of the delegate, which are compared by name
        // rather than by position, so what names the parameter of the member is the name of the token and not where it
        // stands among the parameters of the body.
        public delegate M_1 LaterOp(M_1 value);

        /// <inheritdoc cref="LaterOp"/>
        public static M_1 InvokeAMemberWhichTheNameOfAParameterNames(M_0 key, M_1 value) => This.Method<LaterOp>("IdentityOfTheLater")(value);

        // The member which the symbol names declares a parameter of its own which its own signature names, and the token
        // of the template stands for the parameter of the type which the body is a member of, which bears the name of
        // that parameter as well: the member is looked up by the names of the types of the parameters of the delegate,
        // and the body declares no parameter of its own to name the parameter of the member with.
        public delegate T_0 ShadowedOp(T_0 value);

        /// <inheritdoc cref="ShadowedOp"/>
        public static T_0 InvokeAMemberWhoseParameterTheTypeNames(T_0 value) => This.Method<ShadowedOp>("Identity")(value);

        // The same, of a member which belongs to no instance, whose invocation is written with no receiver at all.
        public static long InvokeAHeldDelegateOfAStaticMember(long a)
        {
            var widen = This.Method<Func<long, long>>("Widen");
            return widen(a);
        }

        /// <summary>
        /// The local which holds the delegate is read for a call of another member as well as invoked by it, so what the
        /// local stands for is more than the invocation of the delegate: the delegate is built into the local, and every
        /// read of it stands where it stood.
        /// </summary>
        public static int InvokeAHeldDelegateWhichWasHandedOn(int a)
        {
            var add = This.Method<Func<int, int, int>>("Add");
            Consume(add);
            return add(a, a);
        }

        /// <summary>
        /// Take a delegate for a call of its own, which is what a template hands a delegate it holds to.
        /// </summary>
        public static int Consume(Func<int, int, int> unused) => 7;

        /// <summary>
        /// The local which holds the delegate is handed to a member which answers the value that the field is written
        /// into, on the way to the invocation: the store takes the value it writes as well as the value it is read off,
        /// which is the one the member answered from the delegate, so the read which handed the delegate over is one
        /// which the invocation has nowhere to read, and the delegate is built into the local.
        /// </summary>
        public static int InvokeAHeldDelegateWhichIsStoredThroughAValueItWasHandedTo(int a)
        {
            var add = This.Method<Func<int, int, int>>("Add");
            HeldBy(add).Number = 5;
            return add(a, a);
        }

        /// <summary>
        /// Hold the delegate which was handed over and answer the value which holds it.
        /// </summary>
        public static DelegateHolder HeldBy(Func<int, int, int> slot)
        {
            Held.Slot = slot;
            return Held;
        }

        /// <summary>
        /// A read of the local is handed to a member which answers a value of another type, and the invocation is made
        /// with that value as an argument: what the call leaves stands in the place of the delegate which the read held,
        /// so the read is one which the invocation has nowhere to read, and the delegate is built into the local.
        /// </summary>
        public static int InvokeAHeldDelegateWhichWasCountedFirst(int a)
        {
            var add = This.Method<Func<int, int, int>>("Add");
            return add(CountTheDelegate(add), a);
        }

        /// <summary>
        /// Take a delegate for what it holds and answer a value of another type, which is what a member which reads
        /// something off the delegate it was handed answers.
        /// </summary>
        public static int CountTheDelegate(Func<int, int, int> slot) => 7;

        /// <summary>
        /// The instance which a template stores a delegate it holds into on its way.
        /// </summary>
        public static readonly DelegateHolder Held = new DelegateHolder();

        /// <summary>
        /// The fields of an instance which a template writes on its way.
        /// </summary>
        public class DelegateHolder
        {
            public Func<int, int, int>? Slot;
            public int Number;
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

        // The member takes an argument by address rather than by value, which the template writes by handing it a local
        // with the `out` modifier: what stands on the stack where the delegate is called is the address of the local.
        public delegate bool TryOp(int value, out int half);

        public static int InvokeWithAnOutArgument(int value)
        {
            This.Method<TryOp>("TryHalf")(value, out var half);
            return half;
        }

        public delegate void RefOp(ref int value);

        // The same, of an argument which the member writes back into: the address which is handed over is the address of
        // the argument of the template itself rather than of a local which it holds.
        public static int InvokeWithARefArgument(int value)
        {
            This.Method<RefOp>("BumpByRef")(ref value);
            return value;
        }
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

        /// <summary>
        /// The member which <see cref="BaseMethod"/> reaches through <c>Base</c> is reached through the type being woven
        /// here: a member of a base of a base is a member of the type which derives from it as well.
        /// </summary>
        public static int ThisMethodOfABaseOfABase(int a) => This.Method<IntOp>("Calc")(a);
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
        /// name of the field is written, so the value which the member is reached through stands where the template
        /// stored it rather than where the name stands.
        /// </summary>
        public static int InstanceField_OfAnInstanceInALocal(HelperClass h)
        {
            var instance = h;
            return new Instance(instance).Field<int>("PublicField").Get();
        }

        /// <summary>
        /// The instance of <c>Instance</c> is the value which another placeholder handed back, so what the member is
        /// reached through is a sequence of instructions rather than one load.
        /// </summary>
        public static int InstanceField_OfAValueWhichAMemberHandedBack() => new Instance(This.Field<HelperClass>("Helper").Get()).Field<int>("PublicField").Get();

        /// <summary>
        /// The same, of a field which belongs to the type of the instance alone: the member takes no receiver at all, so
        /// the value which the template computed stands for nothing and goes with the array which carried it.
        /// </summary>
        public static int InstanceStaticField_OfAValueWhichAMemberHandedBack() => new Instance(This.Field<HelperClass>("Helper").Get()).Field<int>("StaticField").Get();

        public static int InstanceProperty_OfAValueWhichAMemberHandedBack() => new Instance(This.Field<HelperClass>("Helper").Get()).Property<int>("PublicProperty").Get();

        public static int InstanceMethod_OfAValueWhichAMemberHandedBack(int a) => new Instance(This.Field<HelperClass>("Helper").Get()).Method<IntOp>("Calc")(a);

        /// <summary>
        /// The delegate of <c>Instance.Method</c> is handed back rather than invoked, and the instance is a value which
        /// another placeholder handed back: the pointer of the member is taken out of the value where it stands.
        /// </summary>
        public static IntOp InstanceMethod_OfAValueWhichAMemberHandedBackAsADelegate() => new Instance(This.Field<HelperClass>("Helper").Get()).Method<IntOp>("Calc");

        /// <summary>
        /// The instance of <c>Instance</c> is an element of an array, which names the type of what it reads nowhere, so
        /// the member which the name stands for cannot be looked up.
        /// </summary>
        public static int InstanceField_OfAnElementOfAnArray(HelperClass[] helpers) => new Instance(helpers[0]).Field<int>("PublicField").Get();

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

        /// <summary>
        /// The instance of <c>Instance</c> is one of a type which declares a parameter of its own, which the template
        /// names as an instantiation of it: the member belongs to the definition of the type, and the call holds what
        /// the template declared rather than that definition.
        /// </summary>
        public static int InstanceMethod_OfAGenericType(GenericHelper<int> helper, int a) => new Instance(helper).Method<IntOp>("Calc")(a);

        /// <inheritdoc cref="InstanceMethod_OfAGenericType"/>
        public static int InstanceProperty_OfAGenericType(GenericHelper<int> helper) => new Instance(helper).Property<int>("PublicProperty").Get();

        /// <summary>
        /// The same, of a field which belongs to the definition of the type: the type of the field is the value which is
        /// read rather than the parameter of the type, which is what the reference to it used to be built from.
        /// </summary>
        public static int InstanceField_OfAGenericType(GenericHelper<int> helper) => new Instance(helper).Field<int>("PublicField").Get();

        /// <summary>
        /// The member belongs to a base type of the type which the instance is one of, and that base declares a parameter
        /// of its own: the call names the instantiation which the base was handed where the type was declared rather
        /// than the definition of the base.
        /// </summary>
        public static int InstanceMethod_OfABaseOfAGenericType(DerivedOfAGenericBase derived, int a) => new Instance(derived).Method<IntOp>("Calc")(a);

        /// <summary>
        /// The instance of <c>Instance</c> is of a type which derives from an instantiation of a type which derives from
        /// the base that declares the member: the argument which the middle type hands down is a parameter of its own,
        /// so the instantiation the base was declared with names no type the body could write on its own.
        /// </summary>
        public static int InstanceMethod_OfABaseOfABaseOfAGenericType(DerivedOfAMiddleOfAGenericBase derived, int a)
            => new Instance(derived).Method<IntOp>("Calc")(a);

        /// <summary>
        /// The delegate of a member which declares a parameter of its own is written with the token which stands for the
        /// parameter of the member being woven, which is the only name such a signature has: the member of the type
        /// which declares a parameter as well is matched by it, and the call is one of the instantiation of the member
        /// which reaches the body.
        /// </summary>
        public delegate M_0 IdentityOfTheMethod(M_0 value);

        /// <inheritdoc cref="IdentityOfTheMethod"/>
        public static M_0 InstanceMethodOfAGenericMemberOfAGenericType(GenericHelper<int> helper, M_0 value)
            => new Instance(helper).Method<IdentityOfTheMethod>("Identity")(value);

        /// <summary>
        /// The same, of a body which declares a parameter of its own which no token of the template stands for: the
        /// member which is reached declares fewer parameters than the body does, so the arguments of the instantiation
        /// are the ones which the delegate names rather than every parameter of the body.
        /// </summary>
        public static M_0 InstanceMethodOfAGenericMemberOfABodyOfAGreaterArity(GenericHelper<int> helper, M_0 value)
            => new Instance(helper).Method<IdentityOfTheMethod>("Identity")(value);

        /// <summary>
        /// The instance of <c>Instance</c> is the field of an instance which the template was handed, so the value which
        /// the member is reached through is read off that instance rather than loaded on its own: the instructions which
        /// leave the value take one as well as leaving one.
        /// </summary>
        public static int InstanceMethod_OfAFieldOfAnInstance(HelperClass outer, int a) => new Instance(outer.Inner!).Method<IntOp>("Calc")(a);
    }

    private static MethodInfo Template(Type holder, string name) => holder.GetMethod(name)!;

    /// <summary>
    /// Assert that the assembly which was woven names nothing of the weaver: the weaving writes what the template asked
    /// for rather than a value of its own, so the assembly stands alone at runtime.
    /// </summary>
    /// <param name="host">The host which the template was woven into.</param>
    private static void DoesNotReferToTheWeaver(TypeHandler host)
    {
        var weaver = typeof(This).Assembly.GetName().Name;

        Assert.That(host.Source.Module.AssemblyReferences.Any(reference => reference.Name == weaver), Is.False,
                    $"the assembly which was woven refers to {weaver}.");
    }

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
    /// Give a host the constructor which a type needs for an instance of it to be made, and load the assembly which
    /// declares it, so that a weave of the member can be run rather than read.
    /// </summary>
    /// <param name="assembly">The assembly which declares the host, which the loader loads by its name.</param>
    /// <param name="host">The host which the member was woven into.</param>
    /// <returns>The type of the host, as the runtime read it.</returns>
    private static Type LoadHostOf(Assembly assembly, TypeHandler host)
    {
        var module = assembly.Source.MainModule;
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

        return assembly.Load().GetType($"{Ns}.Host")!;
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
    public void A_Value_Which_Holds_An_Array_Creation_Is_Written_Into_The_Field_It_Names()
    {
        // The array which the template creates stands between the value of the write and the accessor which writes it.
        // Creating an array takes the length off the stack and leaves the array in its place, and the walk which counts
        // the values above the value of the placeholder read it as an instruction which leaves one more than it took:
        // the accessor of the write was taken for one which stands above the value, and the read of the same field was
        // answered for the write as well, which wrote one instruction twice.
        var host = NewHostWithField("Value", isStatic: false, "FieldArrayValueAssembly");
        var method = host.AddMethod("Bump", typeof(void).ToGneedleType(), [], [], MethodFlags.Public);
        Assert.DoesNotThrow(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.AddTheFirstElementOfAnArrayToTheField))),
                            "the write of a value which holds an array was refused rather than woven.");
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Count(i => i.OpCode == OpCodes.Ldfld), Is.EqualTo(1), "the field was not read exactly once.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Stfld), Is.EqualTo(1), "the field was not written exactly once.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type);
        type.GetField("Value")!.SetValue(instance, 41);
        type.GetMethod("Bump")!.Invoke(instance, null);

        Assert.That(type.GetField("Value")!.GetValue(instance), Is.EqualTo(42));
    }

    [Test]
    public void A_Template_Which_Holds_A_Handle_Reads_And_Writes_The_Field_It_Names()
    {
        // The name stands where the handle is built rather than where the member is read or written, so the accessors
        // which the template wrote belong to the local: the local has to be read as the member which the name found.
        var host = NewHostWithField("Value", isStatic: false, "FieldHeldHandleAssembly");
        var ins = Rewrite(host, "Bump", typeof(int), [new Parameter(typeof(int).ToGneedleType())], nameof(ThisMemberTemplates.BumpAHeldHandle), MethodFlags.Public);

        // The two reads are the one which the write is given and the one which the member hands back.
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Ldfld), Is.EqualTo(2), "the field was not read exactly twice.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Stfld), Is.EqualTo(1), "the field was not written exactly once.");
        Assert.That(ins.Any(i => i.Operand is MemberReference { DeclaringType.Namespace: "Gneedle.Inject" }), Is.False,
                    "the handle which the template holds was left in the body.");

        // The local which holds the handle is emptied by the weaving, and a local which is declared with a type of the
        // weaver is what would leave the reference behind after the handle itself was written away.
        DoesNotReferToTheWeaver(host);

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type);
        type.GetField("Value")!.SetValue(instance, 41);

        Assert.That(type.GetMethod("Bump")!.Invoke(instance, [3]), Is.EqualTo(44));
        Assert.That(type.GetField("Value")!.GetValue(instance), Is.EqualTo(44));
    }

    [Test]
    public void A_Template_Which_Holds_A_Handle_And_Names_The_Field_Itself_Reads_The_Field()
    {
        // What the template holds and what it names stand in one body, and each of them is woven where it stands.
        var host = NewHostWithField("Value", isStatic: false, "FieldHeldHandleAndNameAssembly");
        var ins = Rewrite(host, "Bump", typeof(int), [new Parameter(typeof(int).ToGneedleType())], nameof(ThisMemberTemplates.BumpAHeldHandleAndTheFieldItself), MethodFlags.Public);

        // The three reads are the ones of the write, of the member which is handed back and of the name which is read.
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Ldfld), Is.EqualTo(3), "the field was not read exactly three times.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Stfld), Is.EqualTo(1), "the field was not written exactly once.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type);
        type.GetField("Value")!.SetValue(instance, 41);

        Assert.That(type.GetMethod("Bump")!.Invoke(instance, [3]), Is.EqualTo(88));
        Assert.That(type.GetField("Value")!.GetValue(instance), Is.EqualTo(44));
    }

    [Test]
    public void A_Template_Which_Holds_A_Handle_Of_A_Static_Field_Reads_And_Writes_It()
    {
        // A field which belongs to no instance takes no receiver, so the read of the local is written as nothing.
        var host = NewHostWithField("Value", isStatic: true, "StaticFieldHeldHandleAssembly");
        var ins = Rewrite(host, "Bump", typeof(int), [new Parameter(typeof(int).ToGneedleType())], nameof(ThisMemberTemplates.BumpAHeldHandleOfAStaticField), MethodFlags.Public | MethodFlags.Static);

        Assert.That(ins.Count(i => i.OpCode == OpCodes.Ldsfld), Is.EqualTo(2), "the field was not read exactly twice.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Stsfld), Is.EqualTo(1), "the field was not written exactly once.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldfld || i.OpCode == OpCodes.Stfld), Is.False,
                    "a field which belongs to no instance was read through a receiver.");
        DoesNotReferToTheWeaver(host);
    }

    [Test]
    public void A_Template_Which_Reads_A_Held_Handle_For_Something_Else_Throws()
    {
        // The handle of a value member is a value which only the weaving writes, so a local which holds one has no
        // value where it is read for anything but the member.
        var host = NewHostWithField("Value", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadAHeldHandleAsAValue))));

        Assert.That(thrown!.Message, Does.Contain("Value"));
        Assert.That(thrown.Message, Does.Contain("no way to write"));
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

    [Test]
    public void A_Switch_Of_A_Template_Reaches_The_Body_Of_Each_Case_It_Was_Woven_With()
    {
        // The table of a switch names the instruction each case begins at, and a case which begins with the name of a
        // member begins at an instruction which the weaving replaces: the entry is carried to what stood where that
        // instruction stood, as the branch of an `if` is, or the case reaches into the template rather than into the
        // body it was woven into.
        var host = NewHostWithField("Value", isStatic: false, "FieldSwitchAssembly");
        host.Source.Fields.Add(new FieldDefinition("Other", FieldAttributes.Public, host.Source.Module.TypeSystem.Int32));
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadAFieldPerCase)));

        var body = ((MethodHandler) method).Source.Body;
        var table = body.Instructions.SelectMany(instruction => instruction.Operand as Instruction[] ?? []).ToArray();

        Assert.That(table, Is.Not.Empty, "the switch of the template was not carried as a table.");
        Assert.That(table.All(entry => body.Instructions.Contains(entry)), Is.True,
                    "an entry of the table named an instruction which the body does not hold.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        var instance = Activator.CreateInstance(type);
        type.GetField("Value")!.SetValue(instance, 7);
        type.GetField("Other")!.SetValue(instance, 9);
        var read = type.GetMethod("Read")!;

        Assert.That(read.Invoke(instance, [0]), Is.EqualTo(7), "the case which names the first field reached another case.");
        Assert.That(read.Invoke(instance, [1]), Is.EqualTo(9), "the case which names the second field reached another case.");
        Assert.That(read.Invoke(instance, [4]), Is.EqualTo(40), "the case which holds a value of its own reached another case.");
        Assert.That(read.Invoke(instance, [9]), Is.EqualTo(-1), "the default of the table reached another case.");
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
    public void A_Template_Which_Holds_A_Handle_Of_A_Property_Calls_Its_Accessors()
    {
        // A property is read and written through the accessors rather than the field, and the handle which the template
        // holds names the property rather than one of them: each accessor which a read of the local calls is written as
        // the accessor which the value member of that read stands for.
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: false);
        var ins = Rewrite(host, "Bump", typeof(int), [new Parameter(typeof(int).ToGneedleType())], nameof(ThisMemberTemplates.BumpAHeldHandleOfAProperty), MethodFlags.Public);

        Assert.That(ins.Count(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "get_Prop"), Is.EqualTo(2), "the getter was not called exactly twice.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "set_Prop"), Is.EqualTo(1), "the setter was not called exactly once.");
        Assert.That(ins.Any(i => i.Operand is MemberReference { DeclaringType.Namespace: "Gneedle.Inject" }), Is.False,
                    "the handle which the template holds was left in the body.");
        DoesNotReferToTheWeaver(host);
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

    #region This: a member of a generic type which is run

    /// <summary>
    /// Create a host which is generic in one parameter, which declares <c>int Add(int a, int b)</c> and a property whose
    /// accessors read and write a field, so that a member of a generic type has one of every shape which a template
    /// reaches to be called and run.<para/>
    /// The members belong to the definition of the type, and the runtime refuses to run a call of a method of a type
    /// which stands open, which is what a test which loads the assembly sees and an assertion on the instructions alone
    /// does not.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly, which a test which runs its host gives one of its own.</param>
    private static TypeHandler NewRunnableGenericHost(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public)
                                        .WithGenericParameter("T")
                                        .GetHandler();
        var module = host.Source.Module;

        var add = new MethodDefinition("Add", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Int32) { DeclaringType = host.Source };
        add.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, module.TypeSystem.Int32));
        add.Parameters.Add(new ParameterDefinition("b", ParameterAttributes.None, module.TypeSystem.Int32));
        var addIl = add.Body.GetILProcessor();
        addIl.Emit(OpCodes.Ldarg_1); addIl.Emit(OpCodes.Ldarg_2); addIl.Emit(OpCodes.Add); addIl.Emit(OpCodes.Ret);
        host.Source.Methods.Add(add);

        var value = new FieldDefinition("m_Value", FieldAttributes.Private, module.TypeSystem.Int32);
        host.Source.Fields.Add(value);

        var accessorAttributes = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
        var getter = new MethodDefinition("get_Prop", accessorAttributes, module.TypeSystem.Int32) { DeclaringType = host.Source };
        var getterIl = getter.Body.GetILProcessor();
        getterIl.Emit(OpCodes.Ldarg_0); getterIl.Emit(OpCodes.Ldfld, value); getterIl.Emit(OpCodes.Ret);

        var setter = new MethodDefinition("set_Prop", accessorAttributes, module.TypeSystem.Void) { DeclaringType = host.Source };
        setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
        var setterIl = setter.Body.GetILProcessor();
        setterIl.Emit(OpCodes.Ldarg_0); setterIl.Emit(OpCodes.Ldarg_1); setterIl.Emit(OpCodes.Stfld, value); setterIl.Emit(OpCodes.Ret);

        host.Source.Methods.Add(getter);
        host.Source.Methods.Add(setter);
        host.Source.Properties.Add(new PropertyDefinition("Prop", PropertyAttributes.None, module.TypeSystem.Int32) { GetMethod = getter, SetMethod = setter });
        return host;
    }

    [Test]
    public void A_Call_Of_A_Member_Of_A_Generic_Type_Runs_The_Member()
    {
        var host = NewRunnableGenericHost("GenericMemberCallAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeInstanceMethod)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [3, 4]), Is.EqualTo(7));
    }

    [Test]
    public void A_Call_Whose_Argument_Holds_A_Conditional_Runs_The_Member()
    {
        // The argument of the call is computed along a branch, and the value which the symbol left stands under both of
        // the paths which the branch leaves for: the instructions between the two are walked as the graph which they
        // are rather than in a row, so the invocation which every path reaches with the two arguments of the delegate is
        // the one which the symbol stands for, and the call of the member is written in its place.
        var host = NewRunnableGenericHost("GenericMemberConditionalArgumentAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(bool).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithAConditionalArgument)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "Add"), Is.True,
                    "the member is not called where the delegate was invoked.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False,
                    "the member was built into a delegate rather than called.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [3, 4, false]), Is.EqualTo(7),
                    "the woven assembly does not run the member which the symbol stands for.");
    }

    [Test]
    public void A_Member_Which_A_Template_Invokes_Inside_A_Protected_Region_Is_Called_There()
    {
        // The symbol stands inside a region which the template protects, and the walk of the paths of the body reads
        // the region as well: the runtime hands the control to the beginning of the handler as well as to the beginning
        // of the body, and every path which reaches the invocation of the delegate passes through the symbol either
        // way, so the call of the member stands where the delegate was invoked rather than a delegate being built.
        var host = NewRunnableGenericHost("GenericMemberInsideAProtectedRegionAssembly");
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberInsideAProtectedRegion)));

        var body = ((MethodHandler) method).Source.Body;
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "Add"), Is.True,
                    "the member is not called where the delegate was invoked.");
        Assert.That(body.Instructions.Any(i => i.OpCode == OpCodes.Ldftn), Is.False,
                    "the member was built into a delegate rather than called.");
        Assert.That(body.ExceptionHandlers, Is.Not.Empty, "the region which the template protects was not carried.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [3, 4]), Is.EqualTo(7),
                    "the woven assembly does not run the member which the symbol stands for.");
    }

    [Test]
    public void A_Held_Delegate_Which_A_Condition_Names_The_Member_Of_Runs_The_Member_Of_The_Arm_Which_Ran()
    {
        // The value which the local holds is the delegate which one of the two arms built, and the invocation which
        // stands after the join is made on it. The store of the local is reached by the path of either arm, so the value
        // it writes is not the one which either symbol left, and the delegate which each arm built is what the local
        // holds: the invocation is left as the invocation of that delegate, which runs the member of the arm which ran.
        var host = NewRunnableGenericHost("GenericMemberConditionalNameAssembly");
        AddASubtract(host);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(bool).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhichAConditionNames)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));
        var instance = Activator.CreateInstance(type);

        Assert.That(type.GetMethod("Run")!.Invoke(instance, [3, 4, true]), Is.EqualTo(7),
                    "the woven assembly does not run the member which the arm which ran names.");
        Assert.That(type.GetMethod("Run")!.Invoke(instance, [3, 4, false]), Is.EqualTo(-1),
                    "the woven assembly does not run the member which the arm which ran names.");
    }

    [Test]
    public void A_Member_Which_A_Condition_Names_Where_It_Stands_Runs_The_Member_Of_The_Arm_Which_Ran()
    {
        // The same of a symbol which stands where it is invoked, which both arms reach as well: the value which the
        // invocation is made on is the one which the arm which ran left, so neither symbol stands for it, and the
        // delegates which the two arms build are what it invokes.
        var host = NewRunnableGenericHost("GenericMemberConditionalNameWhereItStandsAssembly");
        AddASubtract(host);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
                                    [new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(int).ToGneedleType()), new Parameter(typeof(bool).ToGneedleType())],
                                    MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhichAConditionNamesWhereItStands)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));
        var instance = Activator.CreateInstance(type);

        Assert.That(type.GetMethod("Run")!.Invoke(instance, [3, 4, true]), Is.EqualTo(7),
                    "the woven assembly does not run the member which the arm which ran names.");
        Assert.That(type.GetMethod("Run")!.Invoke(instance, [3, 4, false]), Is.EqualTo(-1),
                    "the woven assembly does not run the member which the arm which ran names.");
    }

    /// <summary>
    /// Declare <c>int Subtract(int a, int b)</c> on the host which the conditional-name tests weave into, so that a
    /// template which names a member on each arm of a branch names two members of the same signature.
    /// </summary>
    /// <param name="host">The host which the member is declared on.</param>
    private static void AddASubtract(TypeHandler host)
    {
        var module = host.Source.Module;
        var subtract = new MethodDefinition("Subtract", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Int32) { DeclaringType = host.Source };
        subtract.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, module.TypeSystem.Int32));
        subtract.Parameters.Add(new ParameterDefinition("b", ParameterAttributes.None, module.TypeSystem.Int32));
        var il = subtract.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(subtract);
    }

    [Test]
    public void A_Delegate_Of_A_Member_Of_A_Generic_Type_Runs_The_Member()
    {
        var host = NewRunnableGenericHost("GenericMemberDelegateAssembly");
        var method = host.AddMethod("Run", typeof(ThisMethodTemplates.IntBinaryOp).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.GetInstanceMethodDelegate)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));
        var built = (Delegate) type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), null)!;

        Assert.That(built.DynamicInvoke(3, 4), Is.EqualTo(7));
    }

    [Test]
    public void A_Property_Of_A_Generic_Type_Reads_And_Writes_It()
    {
        var host = NewRunnableGenericHost("GenericMemberPropertyAssembly");
        var read = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        read.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceProperty)));

        var call = ((MethodHandler) read).Source.Body.Instructions.First(instruction => instruction.Operand is MethodReference { Name: "get_Prop" });
        Assert.That(((MethodReference) call.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                    "the accessor is called on the definition of the generic type rather than on the instantiation of it.");

        var write = host.AddMethod("Write", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        write.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteInstanceProperty)));

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));
        var instance = Activator.CreateInstance(type);
        type.GetMethod("Write")!.Invoke(instance, [41]);

        Assert.That(type.GetMethod("Read")!.Invoke(instance, null), Is.EqualTo(41));
    }

    [Test]
    public void A_Member_Of_A_Generic_Base_Type_Runs_On_The_Type_Which_Derives_From_It()
    {
        var asm = Assembly.Create("GenericBaseMemberAssembly");
        var mod = asm.Source.MainModule;
        var baseDef = new TypeDefinition(Ns, "BaseType", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        baseDef.GenericParameters.Add(new GenericParameter("T", baseDef));
        var calc = new MethodDefinition("Calc", MethodAttributes.Public | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
        calc.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, mod.TypeSystem.Int32));
        var calcIl = calc.Body.GetILProcessor();
        calcIl.Emit(OpCodes.Ldarg_1); calcIl.Emit(OpCodes.Ret);
        baseDef.Methods.Add(calc);
        mod.Types.Add(baseDef);

        var host = (TypeHandler) ((AssemblyHandler) asm.Handler).AddClass("Host", Ns, ClassFlags.Public)
                                                                .WithGenericParameter("T")
                                                                .GetHandler();
        // The host hands the parameter which it declares itself down to its base, which is what the body of a member of
        // it names where it reaches the base: the parameter of the base stands for the parameter of the host.
        var baseInstance = new GenericInstanceType(baseDef);
        baseInstance.GenericArguments.Add(host.Source.GenericParameters[0]);
        host.Source.BaseType = baseInstance;

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseMethod)));

        var type = LoadHostOf(asm, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41));
    }

    /// <summary>
    /// Create a host which derives from a type which declares a parameter of its own and derives from a generic type in
    /// turn, so that a member which a template reaches belongs to a base of a base of the type being woven.<para/>
    /// The member belongs to a declaration which stands two steps away from the body being woven, and the instantiation
    /// which the body can name for it is the one the chain of base types names rather than one which stands where the
    /// member is reached.
    /// </summary>
    /// <param name="assemblyName">The name of the assembly to build, which a test which runs its host gives one of its own.</param>
    /// <param name="addBaseMembers">Adds the members which a template reaches to the generic base type.</param>
    /// <param name="theHostDeclaresTheParameter">
    /// Whether the host declares the parameter which the chain hands down itself rather than naming a type where it hands
    /// one over, so that the instantiation of the base of the base holds the parameter of the body which reaches it.
    /// </param>
    private static TypeHandler NewHostWhichDerivesFromAGenericBaseOfAGenericBase(string assemblyName, Action<TypeDefinition, ModuleDefinition> addBaseMembers, bool theHostDeclaresTheParameter = false)
    {
        var asm = Assembly.Create(assemblyName);
        var mod = asm.Source.MainModule;
        var baseDef = new TypeDefinition(Ns, "BaseType", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        baseDef.GenericParameters.Add(new GenericParameter("T", baseDef));
        addBaseMembers(baseDef, mod);
        mod.Types.Add(baseDef);

        var middleDef = new TypeDefinition(Ns, "MiddleType", TypeAttributes.Public | TypeAttributes.Class, mod.TypeSystem.Object);
        var middleParameter = new GenericParameter("T", middleDef);
        middleDef.GenericParameters.Add(middleParameter);
        var baseOfTheMiddle = new GenericInstanceType(baseDef);
        baseOfTheMiddle.GenericArguments.Add(middleParameter);
        middleDef.BaseType = baseOfTheMiddle;
        mod.Types.Add(middleDef);

        var handler = (AssemblyHandler) asm.Handler;
        var host = (TypeHandler) (theHostDeclaresTheParameter
            ? handler.AddClass("Host", Ns, ClassFlags.Public).WithGenericParameter("T").GetHandler()
            : handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler());
        // The middle type hands the argument over to its own base, so the base of the base of the host is reached through
        // an instantiation which is written where the middle type is declared rather than where the host is.
        var middleOfTheHost = new GenericInstanceType(middleDef);
        middleOfTheHost.GenericArguments.Add(theHostDeclaresTheParameter ? host.Source.GenericParameters[0] : mod.TypeSystem.Int32);
        host.Source.BaseType = middleOfTheHost;
        return host;
    }

    /// <summary>
    /// Add the method which the templates of the tests below call to the generic base type.
    /// </summary>
    /// <param name="baseDef">The generic base type which the host derives from through a middle type.</param>
    /// <param name="mod">The module which the type is declared in.</param>
    private static void AddCalcToTheGenericBase(TypeDefinition baseDef, ModuleDefinition mod)
    {
        var calc = new MethodDefinition("Calc", MethodAttributes.Public | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
        calc.Parameters.Add(new ParameterDefinition("a", ParameterAttributes.None, mod.TypeSystem.Int32));
        var calcIl = calc.Body.GetILProcessor();
        calcIl.Emit(OpCodes.Ldarg_1); calcIl.Emit(OpCodes.Ret);
        baseDef.Methods.Add(calc);
    }

    [Test]
    public void A_Member_Of_A_Generic_Base_Of_A_Generic_Base_Is_Called_On_The_Instantiation_Which_The_Chain_Names()
    {
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddleMemberAssembly", AddCalcToTheGenericBase);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseMethod)));

        var call = ((MethodHandler) method).Source.Body.Instructions
                                           .First(instruction => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                                                                 && ((MethodReference) instruction.Operand).Name == "Calc");
        var declaring = ((MethodReference) call.Operand).DeclaringType;

        Assert.That(declaring, Is.InstanceOf<GenericInstanceType>(),
                    "the member is called on the definition of the type which declares it, which stands open where the chain of base types names an instantiation of it.");
        Assert.That(((GenericInstanceType) declaring).GenericArguments.Select(argument => argument.FullName), Is.EqualTo(new[] { "System.Int32" }),
                    "the instantiation which the call names is not the one which the chain of base types hands down.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41));
    }

    [Test]
    public void A_Member_Of_A_Generic_Base_Of_A_Generic_Base_Of_An_Open_Type_Is_Called_On_The_Instantiation_Which_Names_The_Parameter()
    {
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddleOfAnOpenTypeAssembly", AddCalcToTheGenericBase, theHostDeclaresTheParameter: true);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseMethod)));

        var call = ((MethodHandler) method).Source.Body.Instructions
                                           .First(instruction => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                                                                 && ((MethodReference) instruction.Operand).Name == "Calc");
        var declaring = ((MethodReference) call.Operand).DeclaringType;

        Assert.That(declaring, Is.InstanceOf<GenericInstanceType>(),
                    "the member is called on the definition of the type which declares it, which stands open where the chain of base types hands the parameter of the body down to it.");
        Assert.That(((GenericInstanceType) declaring).GenericArguments.Select(argument => argument.Name), Is.EqualTo(new[] { "T" }),
                    "the instantiation which the call names does not stand for the parameter which the chain hands down.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41));
    }

    [Test]
    public void A_Member_Of_A_Generic_Base_Of_A_Generic_Base_Is_Called_On_The_Instantiation_Through_This()
    {
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddleThisMemberAssembly", AddCalcToTheGenericBase);
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.ThisMethodOfABaseOfABase)));

        var call = ((MethodHandler) method).Source.Body.Instructions
                                           .First(instruction => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                                                                 && ((MethodReference) instruction.Operand).Name == "Calc");
        var declaring = ((MethodReference) call.Operand).DeclaringType;

        Assert.That(declaring, Is.InstanceOf<GenericInstanceType>(),
                    "the member is called on the definition of the type which declares it, which stands open where the chain of base types names an instantiation of it.");
        Assert.That(((GenericInstanceType) declaring).GenericArguments.Select(argument => argument.FullName), Is.EqualTo(new[] { "System.Int32" }),
                    "the instantiation which the call names is not the one which the chain of base types hands down.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41));
    }

    [Test]
    public void A_Field_Of_A_Generic_Base_Of_A_Generic_Base_Is_Read_Off_The_Instantiation_Which_The_Chain_Names()
    {
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddleFieldAssembly",
                                                                    (baseDef, mod) => baseDef.Fields.Add(new FieldDefinition("Value", FieldAttributes.Public, mod.TypeSystem.Int32)));
        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BaseFieldGet)));

        var read = ((MethodHandler) method).Source.Body.Instructions.First(instruction => instruction.OpCode == OpCodes.Ldfld);
        var declaring = ((FieldReference) read.Operand).DeclaringType;

        Assert.That(declaring, Is.InstanceOf<GenericInstanceType>(),
                    "the field is read off the definition of the type which declares it, which stands open where the chain of base types names an instantiation of it.");
        Assert.That(((GenericInstanceType) declaring).GenericArguments.Select(argument => argument.FullName), Is.EqualTo(new[] { "System.Int32" }),
                    "the instantiation which the read names is not the one which the chain of base types hands down.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), null), Is.EqualTo(0));
    }

    [Test]
    public void A_Property_Of_A_Generic_Base_Of_A_Generic_Base_Is_Read_Off_The_Instantiation_Which_The_Chain_Names()
    {
        var host = NewHostWhichDerivesFromAGenericBaseOfAGenericBase("GenericBaseOfAMiddlePropertyAssembly", (baseDef, mod) =>
        {
            var getter = new MethodDefinition("get_Prop", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, mod.TypeSystem.Int32) { DeclaringType = baseDef };
            var getterIl = getter.Body.GetILProcessor();
            getterIl.Emit(OpCodes.Ldc_I4_1); getterIl.Emit(OpCodes.Ret);
            baseDef.Methods.Add(getter);
            baseDef.Properties.Add(new PropertyDefinition("Prop", PropertyAttributes.None, mod.TypeSystem.Int32) { GetMethod = getter });
        });

        var method = host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(BaseTemplates), nameof(BaseTemplates.BasePropertyGet)));

        var call = ((MethodHandler) method).Source.Body.Instructions
                                           .First(instruction => (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                                                                 && ((MethodReference) instruction.Operand).Name == "get_Prop");
        var declaring = ((MethodReference) call.Operand).DeclaringType;

        Assert.That(declaring, Is.InstanceOf<GenericInstanceType>(),
                    "the accessor is called on the definition of the type which declares it, which stands open where the chain of base types names an instantiation of it.");
        Assert.That(((GenericInstanceType) declaring).GenericArguments.Select(argument => argument.FullName), Is.EqualTo(new[] { "System.Int32" }),
                    "the instantiation which the call names is not the one which the chain of base types hands down.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), null), Is.EqualTo(1));
    }

    #endregion

    #region This: a method

    /// <summary>
    /// Create a host which declares a real instance method <c>int Add(int, int)</c>, so that a template which reaches a
    /// method of it has one to be rewritten to.
    /// </summary>
    /// <param name="isVirtual">Whether the member which is added is one which a type of its own may override.</param>
    /// <param name="assemblyName">The name of the assembly to build, which a test which runs its host gives one of its own.</param>
    private static TypeHandler NewHostWithAdd(bool isVirtual, string assemblyName = "MethodInjectionAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
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
    /// Create a host which declares a real instance method <c>bool TryHalf(int, out int)</c>, which hands back half of
    /// the value it is given, and a real instance method <c>void BumpByRef(ref int)</c>, so that a template which hands
    /// an argument of its own to one of them by address has a member to be rewritten to.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAnArgumentTakenByAddress(string assemblyName = "MethodInjectionByRefAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var module = host.Source.Module;
        var tryHalf = new MethodDefinition("TryHalf", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Boolean)
        {
            DeclaringType = host.Source,
        };
        tryHalf.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
        tryHalf.Parameters.Add(new ParameterDefinition("half", ParameterAttributes.Out, new ByReferenceType(module.TypeSystem.Int32)));
        var il = tryHalf.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stind_I4); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(tryHalf);

        var bump = new MethodDefinition("BumpByRef", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void)
        {
            DeclaringType = host.Source,
        };
        bump.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, new ByReferenceType(module.TypeSystem.Int32)));
        var bumpIl = bump.Body.GetILProcessor();
        bumpIl.Emit(OpCodes.Ldarg_1); bumpIl.Emit(OpCodes.Ldarg_1); bumpIl.Emit(OpCodes.Ldind_I4); bumpIl.Emit(OpCodes.Ldc_I4_1);
        bumpIl.Emit(OpCodes.Add); bumpIl.Emit(OpCodes.Stind_I4); bumpIl.Emit(OpCodes.Ret);
        host.Source.Methods.Add(bump);
        return host;
    }

    /// <summary>
    /// Create a host which declares the instance method <c>U Touch&lt;U&gt;(int value)</c>, which declares a parameter
    /// of its own: a template which names that member through a delegate of its own carries no token of the parameter,
    /// because the delegate has no parameter of the member to write one for.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAMemberWhichDeclaresAParameterOfItsOwn(string assemblyName = "MethodInjectionUnnamedParameterAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        host.AddMethod(
            "Touch",
            typeof(M_0).ToGneedleType(),
            [new GenericParameterType("U")],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);

        return host;
    }

    [Test]
    public void InvokeWithAnArgumentWhichIsHandedByAddress_Rewrites_To_Direct_Call()
    {
        // A template which hands an argument of its own to a member by `ref` or `out` writes the address of the local
        // which holds it rather than the value itself, and the walk of the stack modelled neither of the instructions
        // which take an address: the call of the delegate popped as many arguments as the delegate declares where the
        // walk had left the stack holding fewer, and the weaving threw out of the walk rather than rewriting the call.
        var host = NewHostWithAnArgumentTakenByAddress();
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithAnOutArgument)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "TryHalf"), Is.True,
                    "the delegate was not rewritten to a direct call to TryHalf.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);
        Assert.That(ins.First(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "TryHalf").Operand,
                    Is.Not.InstanceOf<GenericInstanceMethod>(),
                    "the call stands on a method specification rather than on the member which it names, and the specification of a member which declares no parameter of its own names no argument.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [9]), Is.EqualTo(4));
    }

    [Test]
    public void InvokeWithTheAddressOfAnArgumentOfItsOwn_Rewrites_To_Direct_Call()
    {
        // The address which the template hands over is of an argument of its own here rather than of a local it holds,
        // which is another of the two instructions which take an address and another operand to read the type off. The
        // member is reached through `This`, so it is called on the instance which the member being woven belongs to,
        // and the argument of the template stands one slot higher in that member than it does in the template.
        var host = NewHostWithAnArgumentTakenByAddress("MethodInjectionByRefArgumentAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithARefArgument)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "BumpByRef"), Is.True,
                    "the delegate was not rewritten to a direct call to BumpByRef.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);
        Assert.That(ReceiverOf(ins, "BumpByRef", arguments: 1).OpCode, Is.EqualTo(OpCodes.Ldarg_0),
                    "the member is not called on the instance which the member being woven belongs to.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host);
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(42));
    }

    [Test]
    public void An_Instance_Member_Which_A_Static_Member_Reaches_Through_This_Is_Refused()
    {
        // The receiver of a member which a template reaches through `This` is the instance which the member being woven
        // belongs to, and a member which is static belongs to none: the first argument of it stands where that receiver
        // would be loaded from, which the template was handed for something else, so the call would be written on an
        // argument rather than on an instance. The instance which a static member reaches a member of is one it was
        // handed, which is what `Instance` names, so the weave is refused rather than written.
        var host = NewHostWithAnArgumentTakenByAddress("MethodInjectionStaticThisAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeWithARefArgument))));

        Assert.That(thrown!.Message, Does.Contain("is static and belongs to none"));
    }

    /// <summary>
    /// Create a host which declares the static method <c>T IdentityOfTheLater&lt;T&gt;(T value)</c>, which hands back the
    /// value it was given: the parameter of the member is declared with the parameter of the member itself, and a body
    /// which declares a parameter of that name at another position than it stands at is what the name of the two ties
    /// together, because the types of the parameters of a delegate are compared by name.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAMemberNamedByTheLaterParameter(string assemblyName = "MethodInjectionNamedParameterAssembly")
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var module = host.Source.Module;
        var identity = new MethodDefinition("IdentityOfTheLater", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void)
        {
            DeclaringType = host.Source,
        };
        var own = new GenericParameter("TRes", identity);
        identity.GenericParameters.Add(own);
        identity.ReturnType = own;
        identity.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, own));
        var il = identity.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(identity);
        return host;
    }

    [Test]
    public void A_Member_Whose_Parameter_A_Later_Parameter_Of_The_Body_Names_Is_Called_With_That_Parameter()
    {
        // The member which the symbol names declares a parameter of its own, and the token of the delegate stands for a
        // parameter of the body which bears the name of that parameter and stands at another position of the body than
        // it stands at of the member: the member is looked up by the types of the parameters of the delegate, which are
        // compared by name, so the argument of the instantiation is the parameter of the body which the name ties it to
        // rather than the one which stands at the position of the parameter of the member, which is of another type.
        var host = NewHostWithAMemberNamedByTheLaterParameter();
        var method = (MethodHandler) host.AddMethod(
            "Run",
            typeof(M_1).ToGneedleType(),
            [new GenericParameterType("TKey"), new GenericParameterType("TRes")],
            [new Parameter(typeof(M_0).ToGneedleType()), new Parameter(typeof(M_1).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhichTheNameOfAParameterNames)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                         .FirstOrDefault(reference => reference.Name == "IdentityOfTheLater");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.That(call, Is.InstanceOf<GenericInstanceMethod>(),
                    "the call stands on the definition of the member rather than on an instantiation of it.");
        Assert.That(((GenericInstanceMethod) call!).GenericArguments.Select(argument => argument.Name), Is.EqualTo(new[] { "TRes" }),
                    "the call does not name the parameter of the body which the name of the parameter of the member ties it to.");

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;

        Assert.That(type.GetMethod("Run")!.MakeGenericMethod(typeof(int), typeof(long)).Invoke(null, [1, 2L]), Is.EqualTo(2L),
                    "the woven assembly does not hand back the value of the parameter which the member is named by.");
    }

    /// <summary>
    /// Create a host which declares a parameter of its own and holds <c>T Identity&lt;T&gt;(T value)</c>, whose own
    /// parameter bears the name of the parameter of the type: a template which names that member through a delegate
    /// whose parameter is the token of the parameter of the type names the parameter of the member with it.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static TypeHandler NewHostWithAMemberWhoseParameterTheTypeNames(string assemblyName)
    {
        var handler = (AssemblyHandler) Assembly.Create(assemblyName).Handler;
        var host = (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).WithGenericParameter("T").GetHandler();
        var module = host.Source.Module;

        var identity = new MethodDefinition("Identity", MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Object)
        {
            DeclaringType = host.Source,
        };
        var own = new GenericParameter("T", identity);
        identity.GenericParameters.Add(own);
        identity.ReturnType = own;
        identity.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, own));
        var il = identity.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ret);
        host.Source.Methods.Add(identity);
        return host;
    }

    [Test]
    public void A_Member_Whose_Parameter_The_Type_Names_Is_Called_With_The_Parameter_Of_The_Type()
    {
        // The member which the symbol names declares a parameter of its own which the signature of the member names, and
        // the token of the delegate stands for the parameter of the type which the body is a member of, which bears the
        // name of that parameter: the body declares no parameter of its own, and the argument of the instantiation is
        // the parameter of the type, which the body names, rather than the parameter which would stand at the position
        // of the parameter of the member.
        var host = NewHostWithAMemberWhoseParameterTheTypeNames("ShadowedParameterAssembly");
        var method = (MethodHandler) host.AddMethod(
            "Run",
            typeof(T_0).ToGneedleType(),
            [],
            [new Parameter(typeof(T_0).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhoseParameterTheTypeNames)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                         .FirstOrDefault(reference => reference.Name == "Identity");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.That(call, Is.InstanceOf<GenericInstanceMethod>(),
                    "the call stands on the definition of the member rather than on an instantiation of it.");
        Assert.That(((GenericInstanceMethod) call!).GenericArguments.Select(argument => argument.Name), Is.EqualTo(new[] { "T" }),
                    "the call does not name the parameter of the type which named the parameter of the member.");

        var type = LoadHostOf(host.AssemblyHandler.Assembly, host).MakeGenericType(typeof(int));

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [41]), Is.EqualTo(41),
                    "the woven assembly does not hand back the value which the member was called with.");
    }

    [Test]
    public void A_Member_Which_Declares_A_Parameter_The_Template_Names_None_Of_Is_Refused()
    {
        // The member which the template names declares a parameter of its own which no token of the template stands for,
        // and neither the body which is woven nor the type which declares it holds a parameter which the name of that
        // parameter ties it to, nor one which stands at its position: the call would stand on the definition of the
        // member with the parameter of it left open, which is a body the runtime refuses to run rather than one which
        // names the member, and the weave is refused instead.
        var host = NewHostWithAMemberWhichDeclaresAParameterOfItsOwn("MethodInjectionUnnamedParameterAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(
            () => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAMemberWhichDeclaresAParameterOfItsOwn))));

        Assert.That(thrown!.Message, Does.Contain("declares a generic parameter of its own which no parameter of the member being woven stands for"));
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
    public void InvokeAHeldDelegate_Rewrites_To_Direct_Call()
    {
        // The symbol stands where the delegate is stored into a local rather than where it is invoked, and what invokes
        // it is a read of that local: the fold used to drop the call and the name alone, which left the second read
        // invoking a delegate which nothing had built and the call which stood in the place of the first one reading
        // the receiver of the member rather than the arguments it was written with.
        var host = NewHostWithAdd(isVirtual: false);
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegate)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Count(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                   && ((MethodReference) i.Operand).Name == "Add"), Is.EqualTo(2),
                    "the reads of the local were not both rewritten to a direct call to Add.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(64));
    }

    [Test]
    public void A_Held_Delegate_Which_Is_Invoked_With_The_Value_Of_Its_Own_Invocation_Rewrites_To_Direct_Calls()
    {
        // The invocation of the inner read stands among the arguments of the outer one, and the arguments of the outer
        // invocation match the ones which the inner invocation is made with: the inner instruction was answered for the
        // outer read as well, and the second answer wrote over the work of the first.
        var host = NewHostWithAdd(isVirtual: false, "MethodInjectionNestedHeldDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegateWithTheValueOfItsOwnInvocation)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Count(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                   && ((MethodReference) i.Operand).Name == "Add"), Is.EqualTo(2),
                    "the reads of the local were not both rewritten to a direct call to Add.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference { Name: "Invoke" }), Is.False,
                    "an invocation of the delegate was left standing rather than folded.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(63));
    }

    [Test]
    public void A_Symbol_Which_Stands_Within_The_Arguments_Of_Another_Rewrites_To_Direct_Calls()
    {
        // Neither symbol is stored anywhere, so each of them is invoked where it stands and the inner invocation is the
        // first call of the delegate's type after the outer symbol: the outer symbol was answered for it as well, and
        // the invocation of the inner symbol was written twice.
        var host = NewHostWithAdd(isVirtual: false, "MethodInjectionNestedSymbolAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeASymbolWithTheValueOfAnother)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Count(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                   && ((MethodReference) i.Operand).Name == "Add"), Is.EqualTo(2),
                    "the symbols were not both rewritten to a direct call to Add.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference { Name: "Invoke" }), Is.False,
                    "an invocation of the delegate was left standing rather than folded.");

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(63));
    }

    [Test]
    public void A_Held_Delegate_Which_Is_Handed_On_As_Well_Is_Built_Rather_Than_Folded()
    {
        // The local holds the delegate for a call of another member as well, so its reads are not the invocations of the
        // delegate alone: the delegate is built where the symbol stands and every read stands where it stood. The
        // invocation was answered for the read which hands the delegate on as well, which wrote the one instruction
        // twice, and the second read was left invoking a delegate which nothing had built.
        var host = NewHostWithAdd(isVirtual: false, "MethodInjectionHandedOnDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        Assert.DoesNotThrow(() => method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegateWhichWasHandedOn))),
                            "the template which hands the delegate it holds on was refused rather than woven.");
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.True,
                    "the delegate was not built into the local which holds it.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference { Name: "Invoke" }), Is.EqualTo(1),
                    "the invocation was folded into a call of the member rather than left standing on the delegate of the local.");

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(42));
    }

    [Test]
    public void A_Held_Delegate_Which_Is_Stored_Through_A_Value_It_Was_Handed_To_Is_Built_Rather_Than_Folded()
    {
        // The store of a field of an instance takes two values, the value it writes and the value it is read off, and the
        // walk counted the one it took away as the one which stood under the arguments of the invocation: the read which
        // handed the delegate over was answered for the invocation as well, which wrote the hand-over with the receiver of
        // the member rather than with the delegate which the local holds.
        ThisMethodTemplates.Held.Slot = null;
        ThisMethodTemplates.Held.Number = 0;
        var host = NewHostWithAdd(isVirtual: false, "MethodInjectionStoredOnItsWayDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegateWhichIsStoredThroughAValueItWasHandedTo)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.True,
                    "the delegate was not built into the local which holds it.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference { Name: "Invoke" }), Is.EqualTo(1),
                    "the invocation was folded into a call of the member rather than left standing on the delegate of the local.");

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(42));
        Assert.That(ThisMethodTemplates.Held.Slot, Is.Not.Null, "the delegate which the template handed over was not held.");
        Assert.That(ThisMethodTemplates.Held.Number, Is.EqualTo(5), "the field which the template wrote on its way was not written.");
    }

    [Test]
    public void A_Held_Delegate_Which_A_Call_Took_Under_The_Arguments_Is_Built_Rather_Than_Folded()
    {
        // A read of the local is handed to a member which answers a value of another type, and that value is one of the
        // arguments of the invocation: the call takes the delegate the read left and leaves another value in its place,
        // which the walk counted as the read still standing, so the read was answered for the invocation as well and
        // every read of the local was written as the receiver of the member.
        var host = NewHostWithAdd(isVirtual: false, "MethodInjectionCountedDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(int).ToGneedleType(),
            [],
            [new Parameter(typeof(int).ToGneedleType())],
            MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegateWhichWasCountedFirst)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.True,
                    "the delegate was not built into the local which holds it.");
        Assert.That(ins.Count(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference { Name: "Invoke" }), Is.EqualTo(1),
                    "the invocation was folded into a call of the member rather than left standing on the delegate of the local.");

        var assembly = host.AssemblyHandler.Assembly;
        var module = assembly.Source.MainModule;
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        host.Source.Methods.Add(constructor);

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [21]), Is.EqualTo(28));
    }

    [Test]
    public void InvokeAHeldDelegate_Of_A_Static_Member_Rewrites_To_Direct_Call()
    {
        // The store of the delegate is the whole of what the symbol stands for, and a member which belongs to no
        // instance is reached with no receiver: the store was left reading a stack which nothing had pushed, which is a
        // body the runtime refuses to run.
        var host = NewHostWithWiden("MethodInjectionHeldDelegateAssembly");
        var method = host.AddMethod(
            "Run",
            typeof(long).ToGneedleType(),
            [],
            [new Parameter(typeof(long).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(ThisMethodTemplates), nameof(ThisMethodTemplates.InvokeAHeldDelegateOfAStaticMember)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "Widen"), Is.True,
                    "the delegate was not rewritten to a direct call to Widen.");
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldftn), Is.False);
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Newobj), Is.False);

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;
        Assert.That(type.GetMethod("Run")!.Invoke(null, [3L]), Is.EqualTo(4L));
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
    public void InstanceField_Of_An_Instance_Which_Is_Held_In_A_Local_Reads_That_Local()
    {
        // The instance which the template holds in a local stands where the template stored it, which is not where the
        // name of the field stands: the load of the local is what the field is read off, and the sequence which built the
        // array around the instance goes with the name.
        var (assembly, host, method) = NewInstanceHost("InstanceFieldInALocalAssembly", [typeof(HelperClass)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_OfAnInstanceInALocal)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(instruction => instruction.Operand is MemberReference reference && reference.DeclaringType.FullName == Instance.TYPE_NAME), Is.False,
                    "the array which built the instance of `Instance` was left in the body.");

        var receiver = ReceiverOf(ins, "PublicField");
        Assert.That(receiver.TryGetLdlocIndex(out _), Is.True,
                    "the field is read off `this` rather than off the local which holds the instance.");

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var helper = new HelperClass { PublicField = 21 };

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [helper]), Is.EqualTo(21));
    }

    [Test]
    public void InstanceField_Of_An_Instance_Whose_Type_Is_Not_Named_Where_It_Is_Read_Throws()
    {
        // The type which the field is looked up on is named by the sequence which leads to the name of the field, and a
        // sequence which names no type names none at all. The name is not looked up on the member being woven instead,
        // which holds a field of that name of its own here: the field of the template is one of the instance the
        // template holds, and a name which is woven into another member than the one it names is worse than a name which
        // is refused.
        var host = NewHostWithField("PublicField", isStatic: false);
        var method = host.AddMethod("Read", typeof(int).ToGneedleType(), [], [new Parameter(typeof(HelperClass[]).ToGneedleType())], MethodFlags.Public);

        var thrown = Assert.Throws<ArgumentException>(() => method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_OfAnElementOfAnArray))));

        Assert.That(thrown!.Message, Does.Contain("PublicField"));
    }

    [Test]
    public void InstanceField_Of_A_Value_Which_The_Template_Computed_Reads_That_Value()
    {
        // The instance which the placeholder was built around is the value which another placeholder handed back, which
        // is a sequence of instructions rather than one load: the member is written off the value where the template left
        // it, and the array which carried it is dropped.
        var (assembly, host, method) = NewInstanceHost("InstanceComputedValueAssembly", []);
        AddHelperField(host);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_OfAValueWhichAMemberHandedBack)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(instruction => instruction.Operand is MemberReference reference && reference.DeclaringType.FullName == Instance.TYPE_NAME), Is.False,
                    "the array which built the instance of `Instance` was left in the body.");
        Assert.That(ReceiverOf(ins, "PublicField"), Is.SameAs(ins.First(instruction => instruction.Operand is FieldReference { Name: "Helper" })),
                    "the field is read off `this` rather than off the value which the template computed.");

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetField("Helper")!.SetValue(instance, new HelperClass { PublicField = 21 });

        Assert.That(type.GetMethod("Run")!.Invoke(instance, null), Is.EqualTo(21));
    }

    [Test]
    public void InstanceMethod_Of_A_Field_Which_The_Template_Reads_Is_Reached_Through_That_Field()
    {
        // The value which the placeholder was built around is read off an instance which the template was handed, so the
        // instruction which leaves it takes one value as well: the walk which counts the values of the value read the
        // field as leaving one more than it does, so the sequence which the placeholder was built around was never
        // recognized and the template was refused rather than woven.
        var (assembly, host, method) = NewInstanceHost("InstanceFieldValueAssembly", [typeof(HelperClass), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAFieldOfAnInstance)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(instruction => instruction.Operand is MemberReference reference && reference.DeclaringType.FullName == Instance.TYPE_NAME), Is.False,
                    "the array which built the instance of `Instance` was left in the body.");
        Assert.That(ReceiverOf(ins, "Calc", arguments: 1).OpCode, Is.EqualTo(OpCodes.Ldfld),
                    "the member is called on a value which the template did not name.");

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var outer = new HelperClass { Inner = new HelperClass() };

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [outer, 21]), Is.EqualTo(42));
    }

    [Test]
    public void InstanceStaticField_Of_A_Value_Which_The_Template_Computed_Is_Reached_Through_No_Receiver()
    {
        // The field which the instance of `Instance` names belongs to the type alone, so the value which the template
        // computed is read for nothing: it goes with the array which carried it, which is what leaves the body without a
        // value on the stack where the member takes none.
        var (assembly, host, method) = NewInstanceHost("InstanceComputedStaticValueAssembly", []);
        AddHelperField(host);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceStaticField_OfAValueWhichAMemberHandedBack)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && field.Name == "StaticField"), Is.True,
                    "the static field was not read through the type which the template named.");
        Assert.That(ins.Any(instruction => instruction.OpCode == OpCodes.Ldarg_0), Is.False,
                    "a receiver was written where the member being woven holds none.");

        HelperClass.StaticField = 7;
        var type = assembly.Load().GetType($"{Ns}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), null), Is.EqualTo(7));
    }

    [Test]
    public void InstanceProperty_Of_A_Value_Which_The_Template_Computed_Calls_The_Accessor()
    {
        var (assembly, host, method) = NewInstanceHost("InstanceComputedPropertyAssembly", []);
        AddHelperField(host);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceProperty_OfAValueWhichAMemberHandedBack)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ins.Count(instruction => instruction.Operand is MethodReference { Name: "get_PublicProperty" }), Is.EqualTo(1), "the getter was not called exactly once.");
        Assert.That(ReceiverOf(ins, "get_PublicProperty"), Is.SameAs(ins.First(instruction => instruction.Operand is FieldReference { Name: "Helper" })),
                    "the property is read off `this` rather than off the value which the template computed.");

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetField("Helper")!.SetValue(instance, new HelperClass { PublicProperty = 5 });

        Assert.That(type.GetMethod("Run")!.Invoke(instance, null), Is.EqualTo(5));
    }

    [Test]
    public void InstanceMethod_Of_A_Value_Which_The_Template_Computed_Calls_The_Method()
    {
        var (assembly, host, method) = NewInstanceHost("InstanceComputedMethodAssembly", [typeof(int)]);
        AddHelperField(host);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAValueWhichAMemberHandedBack)));

        var ins = method.Source.Body.Instructions.ToArray();
        Assert.That(ReceiverOf(ins, "Calc", arguments: 1), Is.SameAs(ins.First(instruction => instruction.Operand is FieldReference { Name: "Helper" })),
                    "the method is called on `this` rather than on the value which the template computed.");

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetField("Helper")!.SetValue(instance, new HelperClass());

        Assert.That(type.GetMethod("Run")!.Invoke(instance, [21]), Is.EqualTo(42));
    }

    [Test]
    public void InstanceMethod_Of_A_Value_Which_The_Template_Computed_Is_Handed_Back_As_A_Delegate()
    {
        // The pointer of the member is taken out of the value which the template computed where the value stands, so no
        // receiver is written where the delegate is built.
        var (assembly, host, method) = NewInstanceHost("InstanceComputedDelegateAssembly", []);
        AddHelperField(host);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAValueWhichAMemberHandedBackAsADelegate)));

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var instance = Activator.CreateInstance(type)!;
        type.GetField("Helper")!.SetValue(instance, new HelperClass());
        var calc = (InstanceStaticTemplates.IntOp) type.GetMethod("Run")!.Invoke(instance, null)!;

        Assert.That(calc(21), Is.EqualTo(42));
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
    /// The same host, of a static member <c>U Run&lt;U&gt;(GenericHelper&lt;int&gt; helper, U value)</c> which declares a
    /// parameter of its own, which is what a template that reaches a member declaring one as well is woven into: the
    /// delegate of such a template is written with the token of the parameter of the member, which the member of the
    /// type is matched by.<para/>
    /// The parameter is named <c>U</c> because the name is all which ties the two together: the token of the delegate
    /// stands for the parameter of the member being woven, and the member which is reached declares its own as
    /// <c>U</c>, so the names are what the lookup compares.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static (Assembly Assembly, TypeHandler Host, MethodHandler Method) NewInstanceHostOfAGenericMember(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var method = (MethodHandler) host.AddMethod(
            "Run",
            typeof(M_0).ToGneedleType(),
            [new GenericParameterType("U")],
            [new Parameter(typeof(GenericHelper<int>).ToGneedleType()), new Parameter(typeof(M_0).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);

        return (assembly, host, method);
    }

    /// <summary>
    /// The same host, of a body which declares a parameter of its own which no token of a template stands for: the
    /// delegate names the parameter of the member which the body declares first, and the body declares one beyond it
    /// which the member does not have.
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to build, which a test which loads its host gives one of its own
    /// because two assemblies of the name cannot be loaded into one run.</param>
    private static (Assembly Assembly, TypeHandler Host, MethodHandler Method) NewInstanceHostOfAGenericMemberOfABodyOfAGreaterArity(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        var method = (MethodHandler) host.AddMethod(
            "Run",
            typeof(M_0).ToGneedleType(),
            [new GenericParameterType("U"), new GenericParameterType("V")],
            [new Parameter(typeof(GenericHelper<int>).ToGneedleType()), new Parameter(typeof(M_0).ToGneedleType())],
            MethodFlags.Public | MethodFlags.Static);

        return (assembly, host, method);
    }

    /// <summary>
    /// Give a host a field which holds a <see cref="HelperClass"/>, which is what a template of <c>Instance</c> which
    /// computes its instance reaches one through.
    /// </summary>
    /// <param name="host">The host which the field is declared on.</param>
    private static void AddHelperField(TypeHandler host)
    {
        var fieldType = host.Source.Module.ImportReference(typeof(HelperClass));
        host.Source.Fields.Add(new FieldDefinition("Helper", FieldAttributes.Public, fieldType));
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

    [Test]
    public void InstanceMethod_Of_A_Generic_Type_Runs_The_Member()
    {
        // The instance is one of a type which declares a parameter of its own, and its member belongs to the definition
        // of that type: the call names the instantiation which the template declared, which is the type of the value
        // the member is reached through rather than a type of the body which is woven.
        var (_, host, method) = NewInstanceHost("InstanceGenericTypeAssembly", [typeof(GenericHelper<int>), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfAGenericType)));

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new GenericHelper<int>(), 21]), Is.EqualTo(42));
    }

    [Test]
    public void InstanceProperty_Of_A_Generic_Type_Runs_The_Accessor()
    {
        var (_, host, method) = NewInstanceHost("InstanceGenericPropertyAssembly", [typeof(GenericHelper<int>)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceProperty_OfAGenericType)));

        var type = host.AssemblyHandler.Assembly.Load().GetType($"{Ns}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new GenericHelper<int>()]), Is.EqualTo(42));
    }

    [Test]
    public void InstanceField_Of_A_Generic_Type_Runs_The_Read()
    {
        // The field belongs to the definition of a type which declares a parameter of its own, and the type of the field
        // is the value which is read rather than that parameter: the read names the instantiation which the template
        // declared, which is the type of the value the field is reached through rather than a type of the body woven.
        var (assembly, _, method) = NewInstanceHost("InstanceGenericFieldAssembly", [typeof(GenericHelper<int>)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceField_OfAGenericType)));

        var ldfld = method.Source.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldfld);
        Assert.That(ldfld, Is.Not.Null, "the field was not read.");
        Assert.That(((FieldReference) ldfld!.Operand).DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                    "the field was read off the definition of the type rather than off the instantiation which was named.");

        var type = assembly.Load().GetType($"{Ns}.Host")!;
        var helper = new GenericHelper<int> { PublicField = 42 };

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [helper]), Is.EqualTo(42),
                    "the woven assembly does not run.");
    }

    [Test]
    public void InstanceMethod_Of_A_Base_Of_A_Generic_Type_Runs_The_Member()
    {
        // The type which the template named the instance through derives from an instantiation of the type which
        // declares the member: the member belongs to the definition of that base, and the call names the instantiation
        // which the type was handed where it was declared, which is the type of the value the member is reached through
        // rather than a type of the body which is woven.
        var (assembly, _, method) = NewInstanceHost("InstanceGenericBaseAssembly", [typeof(DerivedOfAGenericBase), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfABaseOfAGenericType)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().FirstOrDefault(reference => reference.Name == "Calc");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.That(call!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                    "the call names the definition of the base rather than the instantiation which the type was handed.");

        var type = assembly.Load().GetType($"{Ns}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new DerivedOfAGenericBase(), 14]), Is.EqualTo(42),
                    "the woven assembly does not run.");
    }

    [Test]
    public void InstanceMethod_Of_A_Base_Of_A_Base_Of_A_Generic_Type_Runs_The_Member()
    {
        // The base which declares the member is written where the type between it and the instance the template was
        // handed declares it, with a parameter of that type: the argument which reaches the base is the one the middle
        // instantiation was handed, which the walk has to carry down rather than read off the declaration it stands in.
        var (assembly, _, method) = NewInstanceHost("InstanceGenericBaseOfABaseAssembly", [typeof(DerivedOfAMiddleOfAGenericBase), typeof(int)]);
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethod_OfABaseOfABaseOfAGenericType)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().FirstOrDefault(reference => reference.Name == "Calc");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.That(call!.DeclaringType, Is.InstanceOf<GenericInstanceType>(),
                    "the call names the definition of the base rather than an instantiation of it.");
        Assert.That(((GenericInstanceType) call.DeclaringType).GenericArguments.Select(argument => argument.FullName),
                    Is.EqualTo(new[] { method.Source.Module.TypeSystem.Int32.FullName }),
                    "the call names the parameter which the base is written with rather than the argument which the chain handed down.");

        var type = assembly.Load().GetType($"{Ns}.Host")!;

        Assert.That(type.GetMethod("Run")!.Invoke(Activator.CreateInstance(type), [new DerivedOfAMiddleOfAGenericBase(), 14]), Is.EqualTo(42),
                    "the woven assembly does not run.");
    }

    [Test]
    public void InstanceMethod_Of_A_Generic_Member_Of_A_Generic_Type_Runs_The_Member()
    {
        // The member declares a parameter of its own as well as belonging to a type which declares one, and the member
        // which is woven declares one too, which the delegate of the template stands for: the call is one of the member
        // instanced with the parameter of the body, so the reference which the call stands on has to declare the
        // parameter of the member, which the instantiation of the call is an argument of.
        var (assembly, _, method) = NewInstanceHostOfAGenericMember("InstanceGenericMemberOfAGenericTypeAssembly");
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethodOfAGenericMemberOfAGenericType)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().FirstOrDefault(reference => reference.Name == "Identity");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.That(call!.GetElementMethod().GenericParameters.Count, Is.EqualTo(1),
                    "the reference which the call stands on does not declare the parameter of the member.");

        var type = assembly.Load().GetType($"{Ns}.Host")!;

        Assert.That(type.GetMethod("Run")!.MakeGenericMethod(typeof(int)).Invoke(null, [new GenericHelper<int>(), 42]), Is.EqualTo(42),
                    "the woven assembly does not run.");
    }

    [Test]
    public void InstanceMethod_Of_A_Generic_Member_Of_A_Body_Of_A_Greater_Arity_Runs_The_Member()
    {
        // The member which the symbol names declares a parameter of its own, and the body which is woven declares one
        // beyond it which no token of the delegate stands for: the arguments of an instantiation are the parameters of
        // the body which stand at the positions of the parameters of the member, so the call names as many of them as
        // the member declares rather than every parameter of the body, which would name the member with a parameter the
        // body declares for another purpose.
        var (assembly, _, method) = NewInstanceHostOfAGenericMemberOfABodyOfAGreaterArity("InstanceGenericMemberOfAGreaterArityAssembly");
        method.SetBody(Template(typeof(InstanceStaticTemplates), nameof(InstanceStaticTemplates.InstanceMethodOfAGenericMemberOfABodyOfAGreaterArity)));

        var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().FirstOrDefault(reference => reference.Name == "Identity");
        Assert.That(call, Is.Not.Null, "the member which the template named was not called.");
        Assert.That(call, Is.InstanceOf<GenericInstanceMethod>(),
                    "the call stands on the definition of the member rather than on an instantiation of it.");
        Assert.That(((GenericInstanceMethod) call!).GenericArguments.Select(argument => argument.Name), Is.EqualTo(new[] { "U" }),
                    "the call does not name the parameter of the body which the token of the delegate stands for.");

        var type = assembly.Load().GetType($"{Ns}.Host")!;

        Assert.That(type.GetMethod("Run")!.MakeGenericMethod(typeof(int), typeof(string)).Invoke(null, [new GenericHelper<int>(), 42]), Is.EqualTo(42),
                    "the woven assembly does not run.");
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