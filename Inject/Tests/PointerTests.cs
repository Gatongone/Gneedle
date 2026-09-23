namespace Gneedle.Inject.Test;

/// <summary>
/// Tests for the placeholders which a template reaches the members of the type it is woven into through: <c>This</c>,
/// <c>Base</c>, <c>Instance</c> and <c>Static</c>. Each of them is a call which throws when it runs, and each of them is
/// rewritten to the member of the target which it names, which is what these tests read back out of the woven body.<para/>
/// The placeholders are read one concern at a time, in the file which each of them is read in.
/// </summary>
[TestFixture]
public partial class PointerTests
{
    /// <summary>
    /// The type which the templates of <c>Instance</c> hold an instance of, and which the tests pass as the argument of a
    /// method which is woven.
    /// </summary>
    public class HelperClass
    {
        public int Calc(int a) => a * 2;
        public        int PublicField;
        public static int StaticField;
        public int PublicProperty { get; set; }

        /// <summary>
        /// A field which holds another instance of the type, which is what a template reaches a member through where the
        /// instance of <c>Instance</c> is read off an instance rather than loaded on its own.
        /// </summary>
        public HelperClass? Inner;
    }

    /// <summary>
    /// A type which derives from the one the templates of <c>Instance</c> hold, which is what a constraint that names a
    /// class is satisfied by through the base type of the type which the delegate names rather than by the type itself.
    /// </summary>
    public class DerivedOfAHelperClass : HelperClass;

    /// <summary>
    /// An enumeration whose values are of one byte, which is what tells the width of the values of an enumeration from
    /// the width of the four bytes which the enumeration of the framework holds.
    /// </summary>
    public enum ByteEnumOfTheTests : byte
    {
        /// <summary>A value of the enumeration.</summary>
        One = 1
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
        public TU Identity<TU>(TU value) => value;

        /// <summary>
        /// A member whose signature names the parameter of the type which declares it, which the lookup of a member
        /// cannot describe: the delegate which a template writes describes the parameter as a type of the assembly
        /// the delegate stands in, and the parameter of the declaring type names none of those until the
        /// instantiation of it is read.
        /// </summary>
        public T Echo(T value) => value;

        /// <summary>
        /// The same, of a parameter which stands in a wrapper: the delegate describes an address of the argument of
        /// the instantiation, and the parameter of the declaring type stands in the wrapper of an address as well, so
        /// the lookup describes the member only where the argument is written inside the wrapper.
        /// </summary>
        public void Set(ref T value) => Held = value;

        /// <summary>
        /// What <see cref="Set"/> was handed last, which is what tells that the member ran.
        /// </summary>
        public T Held = default!;
    }

    /// <summary>
    /// The type which declares a parameter of its own and holds the member of the body which is reached through an
    /// instance of another type entirely.
    /// </summary>
    public class GenericBaseOfAnInstance<T>
    {
        public int Calc(int a) => a * 3;

        /// <summary>
        /// A member whose signature names the parameter of the type which declares it, which only the instantiation of
        /// that type reads: the argument which the chain of base types hands down is what the parameter stands for.
        /// </summary>
        public T Echo(T value) => value;

        /// <summary>
        /// The same, of a member which declares a parameter of its own as well: the argument of the instantiation reads
        /// the parameter of the type and the signature of the delegate binds the one the member declares, and the member
        /// is read through the instantiation of the type which declares it rather than through the one of the type which
        /// the template named an instance of.
        /// </summary>
        public TU EchoBoth<TU>(T value, TU seed) => seed;
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

    /// <summary>
    /// The interface which a type of the tests implements as an instantiation of it, which is what the constraint of a
    /// generic parameter of the body is where a template reaches a member of it through that parameter.
    /// </summary>
    public interface ICountedOfAnInstantiation<T>
    {
        /// <summary>The member whose signature names the parameter which the interface declares.</summary>
        T Echo(T value);
    }

    /// <summary>
    /// The type which implements that interface as an instantiation of it, which is the type which a body that reaches a
    /// member of the constraint is instantiated with.
    /// </summary>
    public class CountedOfAnInstantiation : ICountedOfAnInstantiation<int>
    {
        public int Echo(int value) => value;
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

        /// <summary>
        /// The value which is written holds an element of an array which the template creates, which stands between the
        /// value of the write and the accessor which writes it.
        /// </summary>
        public static void AddTheFirstElementOfAnArrayToTheField()
            => This.Field<int>("Value").Set(This.Field<int>("Value").Get() + new[] {1}[0]);

        // Two of the cases begin where the name of a member stands rather than where a value is loaded, and the
        // compiler writes the switch as a table of the instructions the cases begin at.
        public static int ReadAFieldPerCase(int value)
        {
            switch (value)
            {
                case 0:  return This.Field<int>("Value").Get();
                case 1:  return This.Field<int>("Other").Get();
                case 2:  return 20;
                case 3:  return 30;
                case 4:  return 40;
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
            var copy = count;
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

        // A symbol which stands in the handler of a region which the template protects, so that the beginning of the
        // handler is where the runtime hands the control to rather than a place which a path of the body reaches: the
        // path which begins there passes through the symbol, which is what the walk of the paths has to read it as.
        public static int InvokeAMemberInTheHandler(int a, int b)
        {
            try
            {
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
                return This.Method<IntBinaryOp>("Add")(a, b);
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
            public int                  Number;
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

        // Both arguments are computed rather than loaded, so the region between the symbol and the invocation holds two
        // values more than a walk which counts every instruction it reads says it does.
        public static int InvokeWithBothArgumentsComputed(int a, int b) => This.Method<IntBinaryOp>("Add")(a + b, a * b);

        // The argument is computed and then boxed, and the parameter of the delegate is written for the value which was
        // boxed: what the conversion leaves stands in the place of the value it took, and the instruction which pushed
        // it is the boxing rather than the arithmetic which computed it.
        public static int InvokeWithABoxedComputedArgument(int a, int b) => This.Method<Func<object, int>>("Size")(a + b);

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

        /// <summary>
        /// A member which declares a parameter of its own is one which no bare signature names, so the delegate which
        /// the template wrote is what says which instantiation of it is reached.
        /// </summary>
        public static int Identity_OfAnInt(int value) => This.Method<Func<int, int>>("Identity")(value);

        /// <summary>
        /// The member which the name stands for is one of two of that name, and the delegate describes the one whose
        /// signature the names of its types are rather than the one which declares a parameter of its own.
        /// </summary>
        public static int Filter_OfAnInt(int value) => This.Method<Func<int, int>>("Filter")(value);

        /// <summary>
        /// The member which the name stands for declares a parameter of its own which stands in an array, and the
        /// delegate describes that array: the rank of it is part of the type which describes the parameter, so a
        /// delegate of another rank describes no member of the type which declares this one.
        /// </summary>
        public static int[] Identity_OfAnArray(int[] value) => This.Method<Func<int[], int[]>>("Echo")(value);

        /// <inheritdoc cref="Identity_OfAnArray"/>
        public static int[,] Identity_OfAnArrayOfAnotherRank(int[,] value) => This.Method<Func<int[,], int[,]>>("Echo")(value);

        /// <inheritdoc cref="Identity_OfAnInt"/>
        public static string Identity_OfAString(string value) => This.Method<Func<string, string>>("Identity")(value);

        /// <summary>
        /// The same of a member whose parameter no delegate of the framework could describe: the token stands for the
        /// parameter of the member which is woven, which is what the readme writes such a signature with.
        /// </summary>
        public delegate M_0 IdentityOfTheMethod(M_0 value);

        /// <inheritdoc cref="Identity_OfAnInt"/>
        public static M_0 Identity_OfTheMethod(M_0 value) => This.Method<IdentityOfTheMethod>("Identity")(value);

        /// <summary>
        /// The delegate describes the member whole, so the type which it hands back is what tells one instantiation of
        /// the member from another: a delegate which hands back a type which the member is not instantiated with names
        /// no member at all.
        /// </summary>
        public static string Mismatched_Identity(int value) => This.Method<Func<int, string>>("Identity")(value);

        /// <summary>
        /// The argument of the call is described with the parameter of the body, which bears the name of the parameter
        /// of the member, and the value which the member hands back is described with a type of its own: the two
        /// disagree, so the delegate describes no instantiation of the member.
        /// </summary>
        public static int IdentityOfTheTokenWithAValueOfAnotherType(M_0 value) => This.Method<Func<M_0, int>>("Identity")(value);

        /// <summary>
        /// The member which the name stands for declares a parameter of its own which accepts only the types of
        /// references, and the delegate describes the member with a type of a value: the runtime refuses a member which
        /// is instantiated with a type its own parameter rejects, so no signature describes one here.
        /// </summary>
        public static int Identity_OfAnIntWhichTheConstraintRefuses(int value) => This.Method<Func<int, int>>("Identity")(value);

        /// <summary>
        /// The member which the name stands for declares a parameter of its own, and the delegate describes the member
        /// with a value which may be absent: whether such a value fits the constraints of the parameter does not follow
        /// from its kind, so both the one which names the kinds and the one which names the value types are read.
        /// </summary>
        public static int? Identity_OfANullable(int? value) => This.Method<Func<int?, int?>>("Identity")(value);

        /// <summary>
        /// The member which the name stands for declares a parameter of its own whose constraints name types, and the
        /// delegate describes it with a type which satisfies none of them: a type satisfies a constraint which names
        /// another type by being made of it, which neither a value of the framework nor the type of every value is.
        /// </summary>
        public static object Identity_OfAnObject(object value) => This.Method<Func<object, object>>("Identity")(value);

        /// <summary>
        /// The same, of a type which derives from the type a constraint names, which is what the walk of the base types
        /// of the argument reaches the constraint through.
        /// </summary>
        public static DerivedOfAHelperClass Identity_OfADerived(DerivedOfAHelperClass value)
            => This.Method<Func<DerivedOfAHelperClass, DerivedOfAHelperClass>>("Identity")(value);

        /// <summary>
        /// The same, of a type which implements an interface as an instantiation of it, which is what a constraint that
        /// names the interface has to be satisfied by as the very same instantiation.
        /// </summary>
        public static CountedOfAnInstantiation Identity_OfACounted(CountedOfAnInstantiation value)
            => This.Method<Func<CountedOfAnInstantiation, CountedOfAnInstantiation>>("Identity")(value);

        /// <summary>
        /// The same, of an array, which the metadata declares no base type and no interface of: the types which an
        /// array is made of are the ones which the runtime gives it, which is what a constraint is read against.
        /// </summary>
        public static int[] Identity_OfAnArrayOfInt(int[] value) => This.Method<Func<int[], int[]>>("Identity")(value);

        /// <inheritdoc cref="Identity_OfAnArrayOfInt"/>
        public static int[,] Identity_OfAnArrayOfTwoDimensions(int[,] value)
            => This.Method<Func<int[,], int[,]>>("Identity")(value);

        /// <summary>
        /// The same, of an array whose element is another type than the one which the constraint names: the runtime
        /// accepts an array for a collection of every type which the element of the array is accepted for, which is
        /// what covariance comes to, and which the argument of the constraint is read with.
        /// </summary>
        public static string[] Identity_OfAnArrayOfString(string[] value) => This.Method<Func<string[], string[]>>("Identity")(value);

        /// <inheritdoc cref="Identity_OfAnArrayOfString"/>
        public static List<string> Identity_OfAListOfString(List<string> value)
            => This.Method<Func<List<string>, List<string>>>("Identity")(value);

        /// <inheritdoc cref="Identity_OfAnArrayOfInt"/>
        public static List<int> Identity_OfAListOfAnInt(List<int> value) => This.Method<Func<List<int>, List<int>>>("Identity")(value);

        /// <summary>
        /// The same, of an array which stands among the arguments of an instance of a generic type rather than in the
        /// place of one of them: the covariance of the arrays is what that argument is read with as well.
        /// </summary>
        public static List<string[]> Identity_OfAListOfAnArrayOfString(List<string[]> value)
            => This.Method<Func<List<string[]>, List<string[]>>>("Identity")(value);

        /// <summary>
        /// The same, of an array whose element is an array: the conversion which the collection of the constraint names
        /// is the covariance of the arrays, which the element of the array is read with as well.
        /// </summary>
        public static string[][] Identity_OfAnArrayOfAnArrayOfString(string[][] value)
            => This.Method<Func<string[][], string[][]>>("Identity")(value);

        /// <inheritdoc cref="Identity_OfAnArrayOfAnArrayOfString"/>
        public static List<string>[] Identity_OfAnArrayOfAListOfString(List<string>[] value)
            => This.Method<Func<List<string>[], List<string>[]>>("Identity")(value);

        /// <summary>
        /// The same, of an array whose element is an enumeration: the runtime reads an array of an enumeration as an
        /// array of the type under it, which is the relation the elements of two arrays are read by.
        /// </summary>
        public static DayOfWeek[] Identity_OfAnArrayOfAnEnumeration(DayOfWeek[] value)
            => This.Method<Func<DayOfWeek[], DayOfWeek[]>>("Identity")(value);

        /// <summary>
        /// The same, of an array of values of one sign where the constraint names the other: the two are one width, and
        /// the runtime relates the values of one width whatever the sign of each of them is.
        /// </summary>
        public static uint[] Identity_OfAnArrayOfUnsignedValues(uint[] value)
            => This.Method<Func<uint[], uint[]>>("Identity")(value);

        /// <inheritdoc cref="Identity_OfAnArrayOfUnsignedValues"/>
        public static ulong[] Identity_OfAnArrayOfTheWidestUnsignedValues(ulong[] value)
            => This.Method<Func<ulong[], ulong[]>>("Identity")(value);

        /// <inheritdoc cref="Identity_OfAnArrayOfUnsignedValues"/>
        public static long[] Identity_OfAnArrayOfTheWidestSignedValues(long[] value)
            => This.Method<Func<long[], long[]>>("Identity")(value);

        /// <summary>
        /// The same, of an array of the values under an enumeration of one byte, which is what tells the width of the
        /// values of an enumeration from the width of the one which holds four bytes.
        /// </summary>
        public static byte[] Identity_OfAnArrayOfTheValuesUnderAByteEnumeration(byte[] value)
            => This.Method<Func<byte[], byte[]>>("Identity")(value);

        /// <summary>
        /// The same, of an array of the native values of one sign where the constraint names the other: the runtime
        /// relates the two to one another whatever the width it holds them at.
        /// </summary>
        public static UIntPtr[] Identity_OfAnArrayOfTheUnsignedNativeValues(UIntPtr[] value)
            => This.Method<Func<UIntPtr[], UIntPtr[]>>("Identity")(value);

        /// <summary>
        /// The same, of a sequence whose element is an interface: an interface is a reference of the type of every value
        /// as a class is, which the walk of it reads through no base type, because the metadata declares it none.
        /// </summary>
        public static IEnumerable<ICountedOfAnInstantiation<int>> Identity_OfASequenceOfAnInterface(
            IEnumerable<ICountedOfAnInstantiation<int>> value)
            => This.Method<Func<IEnumerable<ICountedOfAnInstantiation<int>>, IEnumerable<ICountedOfAnInstantiation<int>>>>("Identity")(value);

        /// <summary>
        /// The same, of a type which a constraint satisfied by another type accepts: the declaration of the interface
        /// marks that parameter contravariant, so a comparer of the values of the framework is a comparer of strings.
        /// </summary>
        public static Comparer<object> Identity_OfAComparerOfTheValuesOfTheFramework(Comparer<object> value)
            => This.Method<Func<Comparer<object>, Comparer<object>>>("Identity")(value);

        /// <summary>
        /// The member which the name stands for declares a parameter of its own which stands inside the value which the
        /// delegate hands back rather than in an argument of the call, so no argument names it.
        /// </summary>
        public static List<string> MakeAList(int value) => This.Method<Func<int, List<string>>>("Make")(value);

        /// <summary>
        /// The member which the name stands for declares a parameter of its own which stands in the value it hands back
        /// rather than in an argument of the call, so the delegate names it by that value.
        /// </summary>
        public static string MakeAString(int value) => This.Method<Func<int, string>>("Make")(value);

        /// <summary>
        /// The member which the name stands for declares a parameter of its own which stands in the value it hands back,
        /// and the delegate which names it hands nothing back: the type of nothing describes no value, so it names no
        /// parameter of the member either.
        /// </summary>
        public static void MakeOfNoValue(int value) => This.Method<Action<int>>("Make")(value);

        /// <summary>
        /// The member which the name stands for hands a value back, and the delegate hands nothing back: the value
        /// which the call leaves where the symbol stood is one which the body hands nowhere.
        /// </summary>
        public static void CallAVoidDelegate(int value) => This.Method<Action<int>>("Echo")(value);

        /// <summary>
        /// The member which the name stands for hands nothing back, and the delegate hands a value back: the body
        /// reads a value which no call of the member leaves.
        /// </summary>
        public static int CallAValueDelegate(int value) => This.Method<Func<int, int>>("Silent")(value);

        /// <summary>
        /// The member which the name stands for hands back a value of another type than the one which the delegate
        /// hands back.
        /// </summary>
        public static string CallAStringDelegate(int value) => This.Method<Func<int, string>>("Echo")(value);

        /// <summary>
        /// The same, of two types of the integer family: the stack carries the values of that family as the same
        /// 4-byte value whatever the width of the type which names them, so a member which hands back an int is one
        /// which a delegate which hands back a char describes as well.
        /// </summary>
        public static char CallACharDelegate(int value) => This.Method<Func<int, char>>("Echo")(value);

        /// <summary>
        /// The same, of a member which hands back a value under an enumeration: the value which the member leaves is
        /// the value under the enumeration rather than a value of the enumeration as a type of its own, which is the
        /// value which the delegate hands back, because no instruction of a template names the enumeration.
        /// </summary>
        public static int CallAnIntDelegateForAnEnumeration(int value) => This.Method<Func<int, int>>("Kind")(value);
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

        /// <summary>
        /// The same, of a member of the generic base whose signature names the parameter which that base declares: the
        /// body is a member of the type which derives from the instantiation of it, and the instantiation names the
        /// argument.
        /// </summary>
        public static int ThisMethodOfABaseWhichNamesTheParameter(int a) => This.Method<Func<int, int>>("Echo")(a);

        /// <summary>
        /// The same, of the member of the base which the body's own type derives from rather than of a base of a base:
        /// the member is reached through <c>Base</c>, and the type which is being woven is the one which the base was
        /// declared with the instantiation in, so that instantiation is what the parameter of the member stands for.
        /// </summary>
        public static int BaseMethodOfABaseWhichNamesTheParameter(int a) => Base.Method<Func<int, int>>("Echo")(a);
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
        /// The member which the name stands for declares a parameter of the type which declares it, which the delegate
        /// describes as a type of an assembly rather than as the parameter: nothing matches, so the member is not found.
        /// </summary>
        public static int InstanceMethod_OfAMemberWhoseSignatureNamesTheParameter(GenericHelper<int> helper, int value)
            => new Instance(helper).Method<Func<int, int>>("Echo")(value);

        /// <summary>
        /// The same member, reached through a delegate which hands back a value of another type than the one which the
        /// parameter of the type stands for under the instantiation which the template named.
        /// </summary>
        public static string InstanceMethodOfAGenericTypeWhichHandsBackAnotherType(GenericHelper<int> helper, int value)
            => new Instance(helper).Method<Func<int, string>>("Echo")(value);

        /// <summary>
        /// The instance of <c>Instance</c> is an element of an array, which names the type of what it reads nowhere, so
        /// the member which the name stands for cannot be looked up.
        /// </summary>
        public static int InstanceField_OfAnElementOfAnArray(HelperClass[] helpers) => new Instance(helpers[0]).Field<int>("PublicField").Get();

        /// <summary>
        /// The instance of <c>Instance</c> is a value which the template computed along a branch, which is left by one
        /// of the paths the branch takes rather than in a row with the name of the member: the count which the walk of
        /// the sequence before the name carries cannot be read over a branch, so the type is named nowhere as well.
        /// </summary>
        public static int InstanceField_OfAValueWhichAConditionComputed(HelperClass first, HelperClass second, bool takeFirst)
            => new Instance(takeFirst ? first : second).Field<int>("PublicField").Get();

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
        /// The member which the name stands for declares a parameter of the type which declares it, standing in a
        /// wrapper: the delegate describes the argument of the instantiation by address, and the member is the one of
        /// the definition which takes the parameter of the type by address.
        /// </summary>
        public delegate void RefOp(ref int value);

        /// <inheritdoc cref="RefOp"/>
        public static int InstanceMethod_OfAMemberWhoseParameterStandsInAWrapper(GenericHelper<int> helper, int wanted)
        {
            var value = wanted;
            new Instance(helper).Method<RefOp>("Set")(ref value);
            return helper.Held;
        }

        /// <summary>
        /// The member belongs to a base type of the type which the instance is one of, and that base declares a parameter
        /// of its own: the call names the instantiation which the base was handed where the type was declared rather
        /// than the definition of the base.
        /// </summary>
        public static int InstanceMethod_OfABaseOfAGenericType(DerivedOfAGenericBase derived, int a) => new Instance(derived).Method<IntOp>("Calc")(a);

        /// <summary>
        /// The same, of a member whose signature names the parameter which the base declares rather than a type: the
        /// argument which the chain of base types hands down to that base is what it stands for.
        /// </summary>
        public static int InstanceMethod_OfABaseOfAGenericTypeWhichNamesTheParameter(DerivedOfAGenericBase derived, int a)
            => new Instance(derived).Method<Func<int, int>>("Echo")(a);

        /// <inheritdoc cref="InstanceMethod_OfABaseOfAGenericTypeWhichNamesTheParameter"/>
        public static string InstanceMethod_OfABaseOfAGenericTypeWhichNamesTheParameterAndOneOfItsOwn(DerivedOfAGenericBase derived, int a, string b)
            => new Instance(derived).Method<Func<int, string, string>>("EchoBoth")(a, b);

        /// <summary>
        /// The instance of <c>Instance</c> is of a type which derives from an instantiation of a type which derives from
        /// the base that declares the member: the argument which the middle type hands down is a parameter of its own,
        /// so the instantiation the base was declared with names no type the body could write on its own.
        /// </summary>
        public static int InstanceMethod_OfABaseOfABaseOfAGenericType(DerivedOfAMiddleOfAGenericBase derived, int a)
            => new Instance(derived).Method<IntOp>("Calc")(a);

        /// <summary>
        /// The instance of <c>Instance</c> is the generic parameter of the body itself, whose constraint is an
        /// instantiation of an interface which declares a parameter of its own: the member is looked up on that
        /// instantiation rather than on the definition of the constraint, which names no type of the body, and the call
        /// names that instantiation.
        /// </summary>
        public static int InstanceMethod_OfAConstraintWhichIsAnInstantiation(M_0 box, int a)
            => new Instance(box).Method<IntOp>("Echo")(a);

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
}