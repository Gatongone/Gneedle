using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// The templates which a compiler carries a body out of, one per construct which it writes a method or a type of its
/// own for.<para/>
/// Each of them takes and returns one value, so that every one of them is a body which a member of the same signature
/// could be given, and what tells them apart is the shape the compiler wrote rather than what they compute.
/// </summary>
public static class CompilerGeneratedTemplates
{
    /// <summary>
    /// A lambda which captured nothing, whose body the compiler writes as a static method of the type which holds the
    /// lambdas of this type.
    /// </summary>
    public static int RunsALambda(int value)
    {
        Func<int, int> addOne = x => x + 1;
        return addOne(value);
    }

    /// <summary>
    /// A lambda which captured a local, whose body the compiler writes as a method of a type of its own, with the local
    /// it captured as a field of that type.
    /// </summary>
    public static int RunsACapturingLambda(int value)
    {
        var one = 1;
        Func<int, int> add = x => x + one;
        return add(value);
    }

    /// <summary>
    /// A local function, which the compiler writes as a member of the type which declares the template.
    /// </summary>
    public static int RunsALocalFunction(int value)
    {
        static int AddOne(int x) => x + 1;
        return AddOne(value);
    }

    /// <summary>
    /// An iterator body, which the compiler writes as the <c>MoveNext</c> of a state machine.
    /// </summary>
    public static IEnumerable<int> Yields(int value)
    {
        yield return value;
        yield return value + 1;
    }

    /// <summary>
    /// An async body, which the compiler writes as the <c>MoveNext</c> of a state machine.
    /// </summary>
    public static async Task<int> Awaits(int value)
    {
        await Task.Yield();
        return value;
    }

    /// <summary>
    /// A body which calls a generic method, which is a shape the weaver carries whatever the compiler did with the body
    /// of the template: the call names the method through the instantiation it makes of it.
    /// </summary>
    public static int CallsAGenericMethod(int value) => Enumerable.First([value]);

    /// <summary>
    /// A lambda which captured two locals rather than one, so that the type the compiler wrote for it holds more than
    /// one field.
    /// </summary>
    public static int CapturesTwoLocals(int value)
    {
        var one = 1;
        var two = 2;
        Func<int, int> add = x => x + one + two;
        return add(value);
    }

    /// <summary>
    /// A lambda which captured a value of a type of this assembly rather than of a type of the framework, so that the
    /// type the compiler wrote for it names a type which an assembly it is carried into does not hold.
    /// </summary>
    public static int CapturesATypeOfItsOwnAssembly(int value)
    {
        var captured = new Captured();
        Func<int, int> add = x => x + captured.One;
        return add(value);
    }

    /// <summary>
    /// A lambda written inside a lambda, where the inner one captured what the outer one holds as well as a local of
    /// its own, which is what makes the compiler write a type for the one of them.
    /// </summary>
    public static int NestsLambdas(int value)
    {
        var one = 1;
        Func<int, int> outer = x =>
        {
            var two = 2;
            var inner = () => one + x + two;
            return inner();
        };
        return outer(value);
    }
}

/// <summary>
/// A type of this assembly, which the template above captures so that what the compiler wrote for its lambda names a
/// type which an assembly it is carried into does not hold.
/// </summary>
public class Captured
{
    /// <summary>
    /// The value the template reads out of what it captured.
    /// </summary>
    public int One = 1;
}

/// <summary>
/// A type which is generic, so that the body the compiler carries out of a template of it is generic as well: the type
/// it writes for the lambda is generic over <typeparamref name="T"/>, which is what the move has to re-parent.
/// </summary>
public static class GenericCompilerGeneratedTemplates<T>
{
    /// <summary>
    /// A lambda which captured a value of the type the template is generic over.
    /// </summary>
    public static T ReturnsWhatItCaptured(T value)
    {
        var captured = () => value;
        return captured();
    }
}

/// <summary>
/// The variable of the process which the injector below weaves, which is what keeps the members which carry it from
/// being woven by every test which weaves this assembly.
/// </summary>
public static class Carried
{
    /// <summary>
    /// The environment variable which the injector below reads, which holds the type whose member is woven, the name of
    /// the template which is woven into it, and the type which declares that template, separated by bars.
    /// </summary>
    public const string VARIABLE = "GneedleCarried";
}

/// <summary>
/// An attribute which gives the member it is put on the body of the template which the variable of the process names,
/// which is one injector for every template here: what an injector of this file is gated by is which test is running,
/// and an injector per template would say the same thing a second and a third time.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CarriedInjectorAttribute : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler)
    {
        if (Environment.GetEnvironmentVariable(Carried.VARIABLE) is not { } wanted) return;

        var parts = wanted.Split('|');
        if (parts[0] != method.DeclaringType!.FullName) return;

        var holder = parts.Length > 2 ? typeof(GenericCompilerGeneratedTemplates<>).Assembly.GetType(parts[2])! : typeof(CompilerGeneratedTemplates);
        handler.SetBody(holder.GetMethod(parts[1])!);
    }
}

/// <summary>
/// The type whose member carries the injector above, and onto which the type which the compiler wrote for the lambda is
/// carried by the tests below.
/// </summary>
public class CarriedClosureFixture
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static int Run(int value) => -1;
}

/// <summary>
/// The type whose member carries the injector above, which the lambda that captured two locals is woven into.
/// </summary>
public class CarriedTwoLocalsFixture
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static int Run(int value) => -1;
}

/// <summary>
/// The type whose member carries the injector above, which the lambda written inside a lambda is woven into.
/// </summary>
public class CarriedNestedFixture
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static int Run(int value) => -1;
}

/// <summary>
/// The type whose member carries the injector above, which the local function is woven into.
/// </summary>
public class CarriedLocalFunctionFixture
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static int Run(int value) => -1;
}

/// <summary>
/// The type whose member carries the injector above, which the state machine the compiler wrote for the iterator is
/// carried onto.
/// </summary>
public class CarriedIteratorFixture
{
    /// <summary>
    /// The member which the injector above weaves, which hands back what the template hands back.
    /// </summary>
    [CarriedInjector]
    public static IEnumerable<int> Run(int value) => [];
}

/// <summary>
/// The type whose member carries the injector above, which the state machine the compiler wrote for the async body is
/// carried onto.
/// </summary>
public class CarriedAsyncFixture
{
    /// <summary>
    /// The member which the injector above weaves, which hands back what the template hands back.
    /// </summary>
    [CarriedInjector]
    public static Task<int> Run(int value) => Task.FromResult(-1);
}

/// <summary>
/// The type whose member carries the injector above, which is generic so that the type the compiler wrote for the lambda
/// of its template is generic as well.
/// </summary>
public class CarriedGenericClosureFixture<T>
{
    /// <summary>
    /// The member which the injector above weaves.
    /// </summary>
    [CarriedInjector]
    public static T Run(T value) => value;
}

/// <summary>
/// What the weaver does with a template which holds a construct the compiler carried out of it.<para/>
/// The constructs are read one by one rather than through one case, because which of the two refusals answers for each
/// of them is a question about the shape the compiler wrote and not about the construct which was written.
/// </summary>
[TestFixture]
public class CompilerGeneratedTemplateTests
{
    /// <summary>
    /// Weave the template into a member of the signature it has, and hand back what the weaving said of it.<para/>
    /// The member is woven rather than the template alone, because a refusal is raised while the body is carried and
    /// not while the template is read: what is read here is the refusal and not a lookup of the template.
    /// </summary>
    private static string RefusalOf(MethodInfo template)
    {
        var (_, host, _) = NewHost($"CompilerGenerated{template.Name}{Guid.NewGuid():N}");

        // The member hands back an int and takes one, which is the signature every template above has: SetBody adopts
        // the return type of the template, so what is read here is the refusal rather than a mismatch.
        var run = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
            [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        var refusal = Assert.Throws<ArgumentException>(() => run.SetBody(template));
        Assert.That(refusal, Is.Not.Null, $"'{template.Name}' was woven rather than refused.");

        return refusal!.Message;
    }

    [Test]
    public void A_Template_Which_Holds_A_Lambda_Is_Refused()
        => Assert.That(RefusalOf(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.RunsALambda))),
            Does.Contain("names a type which the compiler wrote"));

    [Test]
    public void A_Template_Which_Holds_A_Capturing_Lambda_Is_Refused()
        => Assert.That(RefusalOf(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.RunsACapturingLambda))),
            Does.Contain("names a type which the compiler wrote"));

    [Test]
    public void A_Template_Which_Holds_A_Local_Function_Is_Refused()
    {
        // This is the one of the five which the other refusal answers for. A lambda, an iterator and an async body are
        // each written into a type of their own which is nested in what declares the template, so the reference to one
        // of them names a type which the compiler wrote. A local function which captured nothing is not: it is a
        // private method of the type which declares the template, and what the reference names is the member.
        //
        // The two are carried the same way and are not reached the same way. There is no type to move for the local
        // function, which is the cheaper of the two moves and the one the other four are not.
        Assert.That(RefusalOf(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.RunsALocalFunction))),
            Does.Contain("calls a member which the compiler wrote"));
    }

    [Test]
    public void A_Template_Which_Is_An_Iterator_Is_Refused()
        => Assert.That(RefusalOf(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.Yields))),
            Does.Contain("names a type which the compiler wrote"));

    [Test]
    public void A_Template_Which_Is_An_Async_Body_Is_Refused()
        => Assert.That(RefusalOf(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.Awaits))),
            Does.Contain("names a type which the compiler wrote"));

    [Test]
    [Ignore("Blocked by a fault of the weaver rather than by this proposal: a call of a generic method throws. "
        + "TokenParsing.ParseGenericTokens assigns DeclaringType unconditionally, and the setter of a "
        + "MethodSpecification throws, because the declaring type of an instantiation is read of the method it "
        + "instantiates. The method handles a GenericInstanceMethod below that line and never reaches it. "
        + "TokenParsing.cs:291.")]
    public void A_Template_Which_Calls_A_Generic_Method_Is_Woven()
    {
        // Nothing here is a construct the compiler carried out of the template: the call names a generic method of the
        // framework through the instantiation it makes of it, which is a shape any template may write.
        var (_, host, _) = NewHost($"CompilerGeneratedAGenericCall{Guid.NewGuid():N}");
        var run = host.AddMethod("Run", typeof(int).ToGneedleType(), [],
            [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public);

        Assert.DoesNotThrow(() => run.SetBody(Template(typeof(CompilerGeneratedTemplates), nameof(CompilerGeneratedTemplates.CallsAGenericMethod))),
            "the call of a generic method was refused rather than carried.");
    }

    /// <summary>
    /// One move of what the compiler wrote for the bodies of a template onto the type which is woven.<para/>
    /// What is carried is not one type: the body of a lambda reaches the lambda written inside it, and the call it
    /// makes to it names a type which the compiler wrote as well, so the move follows what it carries until nothing
    /// new is reached. A member which the compiler wrote on the type which declares the template rather than on a type
    /// of its own - which is what a local function that captured nothing is - is carried as a member.
    /// </summary>
    private sealed class Carrier(ModuleDefinition module, TypeDefinition into)
    {
        /// <summary>
        /// The copies of the types which the compiler wrote, by the full name of the original.
        /// </summary>
        internal Dictionary<string, TypeDefinition> Types { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The copies of the members which the compiler wrote on the type which declares the template, by full name.
        /// </summary>
        internal Dictionary<string, MethodDefinition> Members { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// What each of the copies above was written from, which the bodies of them are written out of.
        /// </summary>
        private readonly Dictionary<string, TypeDefinition> m_Originals = new(StringComparer.Ordinal);

        /// <inheritdoc cref="m_Originals"/>
        private readonly Dictionary<string, MethodDefinition> m_MemberOriginals = new(StringComparer.Ordinal);

        /// <summary>
        /// The generic parameters of a type which was carried, which are the ones its copy declares in their place.<para/>
        /// A copy declares its own rather than being given the parameters of the type it was carried onto, because the
        /// type it was carried out of declared its own: the two are one type only where the weaving instantiates one
        /// with the other, which is what re-pointing the member does.
        /// </summary>
        private readonly Dictionary<string, GenericParameter> m_Parameters = new(StringComparer.Ordinal);

        /// <summary>
        /// Carry everything the compiler wrote for the bodies of a template onto the type which is woven.
        /// </summary>
        internal void Carry(MethodDefinition template)
        {
            // The generic parameters of the type which declares the template are named by the parameters of the type
            // being woven, by the position they hold: the two stand for the same types - that is what weaving a member
            // of one into a member of the other means - and a copy of the type which the compiler wrote has to be
            // written against the ones the member being woven has.
            for (var index = 0; index < template.DeclaringType.GenericParameters.Count; index++)
            {
                if (index < into.GenericParameters.Count)
                {
                    m_Parameters[$"{template.DeclaringType.FullName}/{index}"] = into.GenericParameters[index];
                }
            }

            var found = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<MethodDefinition>();
            pending.Enqueue(template);

            // What the template reaches, and then what those reach: a body which was carried is a body of the
            // template's own, and it names the types the compiler wrote for it the same way the template does.
            while (pending.Count > 0)
            {
                var method = pending.Dequeue();
                if (!method.HasBody) continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MemberReference member || member.DeclaringType is null) continue;

                    var declaring = member.DeclaringType;
                    if (declaring.Name.StartsWith("<", StringComparison.Ordinal))
                    {
                        var from = declaring.Resolve();
                        if (!found.Add(from.FullName)) continue;

                        var copy = Declare(from);
                        Types[from.FullName]       = copy;
                        m_Originals[from.FullName] = from;
                        foreach (var held in from.Methods) pending.Enqueue(held);
                        continue;
                    }

                    // A member the compiler wrote on the type which declares the template, which is a local function
                    // that captured nothing: there is no type to move, and the member is moved on its own.
                    if (!member.Name.StartsWith("<", StringComparison.Ordinal)) continue;
                    if (member is not MethodReference loose || loose.DeclaringType.GetElementType().FullName != template.DeclaringType.FullName) continue;
                    if (!found.Add(loose.FullName)) continue;

                    var looseDefinition = loose.Resolve();
                    var looseCopy = DeclareMethod(looseDefinition, into);
                    into.Methods.Add(looseCopy);

                    Members[loose.FullName]           = looseCopy;
                    m_MemberOriginals[loose.FullName] = looseDefinition;
                    pending.Enqueue(looseDefinition);
                }
            }

            // Every copy is declared before any body is written, because a body reaches a copy which is declared after
            // it: what a type is written against is the map, and the map is whole only once nothing is left to declare.
            // The two of a pair are named by their parts rather than taken apart by a deconstruction, which is a member
            // the framework the tests are built for on Windows does not hold.
            foreach (var type in Types)
            {
                Fill(type.Key, type.Value);
            }

            foreach (var member in Members)
            {
                FillMember(member.Key, member.Value);
            }

            Repoint(template);
        }

        /// <summary>
        /// The name a copy is declared with, which is the name of the original with the brackets of it taken out: the
        /// name of a type the compiler wrote is not one an identifier of C# holds, and a name which is not whole is one
        /// which a second copy of the same name would be told from by nothing.
        /// </summary>
        private static string NameOf(TypeDefinition from) => from.Name.Replace('<', '_').Replace('>', '_').Replace('|', '_');

        /// <summary>
        /// The name a copy takes on the type it is carried onto, which is the name of the original unless the type
        /// already holds one of it.
        /// </summary>
        private string Taken(TypeDefinition from)
        {
            var name = NameOf(from);
            for (var index = 1; into.NestedTypes.Any(nested => nested.Name == name) || Types.Values.Any(copy => copy.Name == name); index++)
            {
                name = $"{NameOf(from)}_{index}";
            }

            return name;
        }

        /// <summary>
        /// Declare the copy of a type the compiler wrote, with everything of it which is not a body: what the runtime
        /// reaches a type by, which is its base type and its interfaces, its fields, and the signature of its methods.
        /// </summary>
        private TypeDefinition Declare(TypeDefinition from)
        {
            var copy = new TypeDefinition("", Taken(from), from.Attributes, TypeOf(from.BaseType))
            {
                DeclaringType = into
            };
            into.NestedTypes.Add(copy);

            // A type which the compiler wrote for a body of a template of a generic type is generic itself, and the
            // copy declares the same parameters in the same places: every reference to one of them is re-pointed to the
            // copy of it, which is what lets the copy stand for the type it was carried out of.
            foreach (var parameter in from.GenericParameters)
            {
                var declared = new GenericParameter(parameter.Name, copy) {Attributes = parameter.Attributes};
                copy.GenericParameters.Add(declared);
                m_Parameters[$"{from.FullName}/{parameter.Position}"] = declared;
            }

            // What a type is, which is not only the fields and the methods it holds: a state machine is reached by the
            // runtime through the interfaces it implements, and a copy which left them behind is a type which reads
            // back and does not answer to the type it was copied from.
            foreach (var implementation in from.Interfaces)
            {
                copy.Interfaces.Add(new InterfaceImplementation(TypeOf(implementation.InterfaceType)));
            }

            foreach (var field in from.Fields)
            {
                copy.Fields.Add(new FieldDefinition(field.Name, field.Attributes, TypeOf(field.FieldType)));
            }

            foreach (var method in from.Methods)
            {
                copy.Methods.Add(DeclareMethod(method, copy));
            }

            return copy;
        }

        /// <summary>
        /// Declare the copy of a method, with its signature and the rows of the method implementation table which say
        /// which interface methods it answers for.<para/>
        /// An implementation of an interface which the compiler wrote is explicit rather than by name, which is such a
        /// row rather than the name of the method alone: a copy which carries the interfaces and not the rows is a type
        /// which says it implements one and holds nothing which answers to it, and the runtime refuses to load it.
        /// </summary>
        private MethodDefinition DeclareMethod(MethodDefinition from, TypeDefinition holder)
        {
            var copy = new MethodDefinition(from.Name, from.Attributes, TypeOf(from.ReturnType))
            {
                DeclaringType = holder
            };

            foreach (var parameter in from.GenericParameters)
            {
                var declared = new GenericParameter(parameter.Name, copy) {Attributes = parameter.Attributes};
                copy.GenericParameters.Add(declared);
                m_Parameters[$"{from.FullName}/{parameter.Position}"] = declared;
            }

            foreach (var parameter in from.Parameters)
            {
                copy.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, TypeOf(parameter.ParameterType)));
            }

            foreach (var overridden in from.Overrides)
            {
                // What a row of the table names is a method of an interface rather than a type which was carried, so
                // nothing of the move is re-pointed here.
                copy.Overrides.Add(module.ImportReference(overridden));
            }

            return copy;
        }

        /// <summary>
        /// Write the body of every method of a carried type.
        /// </summary>
        private void Fill(string fullName, TypeDefinition copy)
        {
            var from = m_Originals[fullName];

            for (var index = 0; index < from.Methods.Count; index++)
            {
                CarryTheBody(from.Methods[index], copy.Methods[index]);
            }
        }

        /// <summary>
        /// Write the body of a member which the compiler wrote on the type which declares the template.
        /// </summary>
        private void FillMember(string fullName, MethodDefinition copy) => CarryTheBody(m_MemberOriginals[fullName], copy);

        /// <summary>
        /// Carry the body of a method onto the copy of it.<para/>
        /// The body is written twice. Every instruction is put down first with the operand it holds, because an operand
        /// may name an instruction which stands after it - the target of a branch, the table of a switch, the boundary
        /// of a region which catches - and the second pass is what re-points the copies of them. The locals and the
        /// regions go with the instructions, which is the whole of what a body is, and a state machine's
        /// <c>MoveNext</c> is where all three of them are.
        /// </summary>
        private void CarryTheBody(MethodDefinition method, MethodDefinition copy)
        {
            if (!method.HasBody) return;

            var placed = new Dictionary<Instruction, Instruction>();

            foreach (var variable in method.Body.Variables)
            {
                copy.Body.Variables.Add(new VariableDefinition(TypeOf(variable.VariableType)));
            }

            foreach (var instruction in method.Body.Instructions)
            {
                var placedInstruction = CopyOf(instruction);
                placed[instruction] = placedInstruction;
                copy.Body.Instructions.Add(placedInstruction);
            }

            foreach (var instruction in method.Body.Instructions)
            {
                var placedInstruction = placed[instruction];

                placedInstruction.Operand = instruction.Operand switch
                {
                    Instruction branch          => placed[branch],
                    Instruction[] table         => table.Select(target => placed[target]).ToArray(),
                    VariableDefinition variable => copy.Body.Variables[variable.Index],
                    FieldReference field        => Point(field),
                    MethodReference called      => Point(called),
                    _                           => Imported(instruction.Operand)
                };
            }

            foreach (var handler in method.Body.ExceptionHandlers)
            {
                copy.Body.ExceptionHandlers.Add(new ExceptionHandler(handler.HandlerType)
                {
                    TryStart     = placed[handler.TryStart],
                    TryEnd       = handler.TryEnd is null ? null : placed[handler.TryEnd],
                    HandlerStart = placed[handler.HandlerStart],
                    HandlerEnd   = handler.HandlerEnd is null ? null : placed[handler.HandlerEnd],
                    FilterStart  = handler.FilterStart is null ? null : placed[handler.FilterStart],
                    CatchType    = handler.CatchType is null ? null : TypeOf(handler.CatchType)
                });
            }
        }

        /// <summary>
        /// An operand which names something the move does not carry, as the module which is being written holds it.<para/>
        /// What a body reaches outside the type it was carried out of is not left as it stands: a reference belongs to
        /// the module it was read out of, and an assembly which is written against a member of another one without
        /// naming it is an assembly which cannot be written at all. Where the two modules are one this hands back what
        /// it was given.
        /// </summary>
        private object? Imported(object? operand)
            => operand switch
            {
                MethodReference method => module.ImportReference(method),
                FieldReference field   => module.ImportReference(field),
                TypeReference type     => module.ImportReference(type),
                _                      => operand
            };

        /// <summary>
        /// Point a member reference at the copy of the type which holds it, when that type was carried, and at a
        /// carried member, when the member is one the compiler wrote on the type which declares the template.
        /// </summary>
        private FieldReference Point(FieldReference field)
        {
            if (field.DeclaringType is null || !Types.TryGetValue(field.DeclaringType.GetElementType().FullName, out var copy))
            {
                return (FieldReference) module.ImportReference(field);
            }

            return copy.Fields.Single(candidate => candidate.Name == field.Name);
        }

        /// <inheritdoc cref="Point(FieldReference)"/>
        private MethodReference Point(MethodReference called)
        {
            if (Members.TryGetValue(called.FullName, out var member)) return member;

            var holder = called.DeclaringType;
            var declaring = holder?.GetElementType().FullName;

            if (declaring is null || !Types.TryGetValue(declaring, out var copy)) return module.ImportReference(called);

            var open = copy.Methods.Single(candidate => candidate.Name == called.Name && candidate.Parameters.Count == called.Parameters.Count);

            // The declaring type of a call into a type which the compiler wrote is written as the instantiation the
            // call makes of it rather than as the definition, and a type which was carried out of a generic one is
            // generic itself: the copy is reached through the instantiation of the copy, and a reference to the open
            // method of it is not IL the runtime reads.
            if (holder is not GenericInstanceType instance) return open;

            var instantiated = new GenericInstanceMethod(open);
            foreach (var argument in instance.GenericArguments) instantiated.GenericArguments.Add(TypeOf(argument));
            return instantiated;
        }

        /// <summary>
        /// Point every operand of the template at what the move carried in place of what it named.
        /// </summary>
        private void Repoint(MethodDefinition template)
        {
            foreach (var instruction in template.Body.Instructions)
            {
                instruction.Operand = instruction.Operand switch
                {
                    FieldReference field   => Point(field),
                    MethodReference called => Point(called),
                    _                      => Imported(instruction.Operand) ?? instruction.Operand
                };
            }
        }

        /// <summary>
        /// What a generic parameter belongs to, which is the type or the method which declares it, or null where it is
        /// declared by none of them.
        /// </summary>
        private static string? OwnerOf(GenericParameter parameter)
            => parameter.Owner switch
            {
                TypeReference type     => type.FullName,
                MethodReference method => method.FullName,
                _                      => null
            };

        /// <summary>
        /// A type as the copy of it, where it is a type which was carried or a generic parameter of one, and the same
        /// type otherwise.
        /// </summary>
        private TypeReference TypeOf(TypeReference? reference)
        {
            if (reference is null) return module.TypeSystem.Void;

            if (reference is GenericParameter parameter && OwnerOf(parameter) is { } owner)
            {
                return m_Parameters.TryGetValue($"{owner}/{parameter.Position}", out var declared)
                    ? declared
                    : module.ImportReference(reference);
            }

            if (reference is GenericInstanceType instantiation)
            {
                var element = TypeOf(instantiation.ElementType);
                var instance = new GenericInstanceType(element);
                foreach (var argument in instantiation.GenericArguments) instance.GenericArguments.Add(TypeOf(argument));
                return instance;
            }

            return module.ImportReference(reference);
        }
    }

    /// <summary>
    /// The copy of an instruction which the body is written from, which is made with the operand it holds.<para/>
    /// The operand is the one of the original, which is a legal operand of that opcode by the only reading which made
    /// it, and the pass which follows is what re-points the copies of them. <see cref="Instruction.Create(OpCode)"/>
    /// is not enough on its own, because it refuses every opcode which carries an operand.
    /// </summary>
    private static Instruction CopyOf(Instruction instruction)
    {
        var opcode = instruction.OpCode;

        return instruction.Operand switch
        {
            null                          => Instruction.Create(opcode),
            Instruction branch            => Instruction.Create(opcode, branch),
            Instruction[] table           => Instruction.Create(opcode, table),
            VariableDefinition variable   => Instruction.Create(opcode, variable),
            FieldReference field          => Instruction.Create(opcode, field),
            MethodReference method        => Instruction.Create(opcode, method),
            TypeReference type            => Instruction.Create(opcode, type),
            ParameterDefinition parameter => Instruction.Create(opcode, parameter),
            CallSite site                 => Instruction.Create(opcode, site),
            string text                   => Instruction.Create(opcode, text),
            sbyte number                  => Instruction.Create(opcode, number),
            byte number                   => Instruction.Create(opcode, number),
            int number                    => Instruction.Create(opcode, number),
            long number                   => Instruction.Create(opcode, number),
            float number                  => Instruction.Create(opcode, number),
            double number                 => Instruction.Create(opcode, number),
            _                             => Unsupported(instruction)
        };
    }

    /// <summary>
    /// Fail the test and hand back nothing, which no caller reaches: an operand of a kind the carrier does not know is
    /// one which would be carried as it stands, which is a reference to a member of the type which was left behind.
    /// </summary>
    private static Instruction Unsupported(Instruction instruction)
    {
        Assert.Fail($"the body holds an operand of a kind which this does not carry: {instruction}");
        return null!;
    }

    /// <summary>
    /// The full name of the type which declares the templates above.
    /// </summary>
    private const string TEMPLATES_TYPE = "Gneedle.Inject.Test.CompilerGeneratedTemplates";

    /// <summary>
    /// The full name of the type onto which the closure of the lambda is carried.
    /// </summary>
    private const string CARRIED_CLOSURE_TYPE = "Gneedle.Inject.Test.CarriedClosureFixture";

    /// <summary>
    /// The full name of the type onto which the lambda which captured two locals is carried.
    /// </summary>
    private const string CARRIED_TWO_LOCALS_TYPE = "Gneedle.Inject.Test.CarriedTwoLocalsFixture";

    /// <summary>
    /// The full name of the type onto which the lambda written inside a lambda is carried.
    /// </summary>
    private const string CARRIED_NESTED_TYPE = "Gneedle.Inject.Test.CarriedNestedFixture";

    /// <summary>
    /// The full name of the type onto which the local function is carried.
    /// </summary>
    private const string CARRIED_LOCAL_FUNCTION_TYPE = "Gneedle.Inject.Test.CarriedLocalFunctionFixture";

    /// <summary>
    /// The full name of the type onto which the state machine of an iterator is carried.
    /// </summary>
    private const string CARRIED_STATE_MACHINE_TYPE = "Gneedle.Inject.Test.CarriedIteratorFixture";

    /// <summary>
    /// The full name of the type onto which the state machine of an async body is carried.
    /// </summary>
    private const string CARRIED_ASYNC_TYPE = "Gneedle.Inject.Test.CarriedAsyncFixture";

    /// <summary>
    /// Move everything the compiler wrote for the bodies of a template onto the type which is named.<para/>
    /// This is the move of the proposal, written out by hand: what the weaver would do when it meets a reference to a
    /// body it has no instructions of, done here before the weaving runs, so that what the weaving does with the result
    /// is read on its own.
    /// </summary>
    /// <param name="module">The module which declares the template and the type it is carried onto.</param>
    /// <param name="holder">The full name of the type which declares the template, which a template of a generic type
    /// is declared by the instantiation it is read as.</param>
    /// <param name="templateName">The name of the template whose bodies are carried.</param>
    /// <param name="intoTypeName">The full name of the type which what the compiler wrote is carried onto.</param>
    private static void Move(ModuleDefinition module, string holder, string templateName, string intoTypeName)
    {
        var template = module.GetType(holder)!.Methods.Single(method => method.Name == templateName);
        var carrier = new Carrier(module, module.GetType(intoTypeName)!);

        carrier.Carry(template);
    }

    /// <summary>
    /// The type which declares the generic template, and the generic type which its lambda is carried onto, which are
    /// named as the metadata names a generic type rather than as the runtime writes an instantiation of it.
    /// </summary>
    private const string GENERIC_TEMPLATES_TYPE = "Gneedle.Inject.Test.GenericCompilerGeneratedTemplates`1";

    /// <inheritdoc cref="GENERIC_TEMPLATES_TYPE"/>
    private const string CARRIED_GENERIC_CLOSURE_TYPE = "Gneedle.Inject.Test.CarriedGenericClosureFixture`1";

    /// <summary>
    /// Carry what the compiler wrote for a template onto the type which is named, weave the assembly of these tests once
    /// with the injector of that type turned on, and hand back the image which was woven and what the run reported.<para/>
    /// The member is woven by the injector of the fixture rather than by a call here, because what is read is what the
    /// weaving does with the whole assembly once the move has been made, which is what a build would be handed.
    /// </summary>
    private static (byte[] Result, List<string> Reported) CarriedThenWoven(string holder, string template, string intoType, Action<ModuleDefinition>? before = null)
    {
        var image = File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location);
        byte[] modified;

        using (var definition = AssemblyDefinition.ReadAssembly(new MemoryStream(image)))
        {
            before?.Invoke(definition.MainModule);
            Move(definition.MainModule, holder, template, intoType);

            using var written = new MemoryStream();
            definition.Write(written);
            modified = written.ToArray();
        }

        var reported = new List<string>();
        Environment.SetEnvironmentVariable(Carried.VARIABLE, $"{intoType}|{template}{(holder == TEMPLATES_TYPE ? "" : "|" + holder)}");

        try
        {
            var (changed, result) = Injections.Apply(AssemblyLoader.LoadFromBytes(modified), modified, reportError: reported.Add);
            Assert.That(changed, Is.True,
                $"the member which carries the injector was woven by nothing.{Environment.NewLine}{string.Join(Environment.NewLine, reported)}");

            return (result, reported);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Carried.VARIABLE, null);
        }
    }

    /// <summary>
    /// Run the member which was woven of the type which is named, and hand back what it computes.<para/>
    /// The assembly which was woven is loaded rather than read, because IL which reads back correctly is not yet IL
    /// which runs: what a move got wrong is answered here and not by the shape of the body.
    /// </summary>
    private static object? Ran(byte[] result, string intoType, string member, params object?[] arguments)
        => AssemblyLoader.LoadFromBytes(result).GetType(intoType)!.GetMethod(member)!.Invoke(null, arguments);

    [Test]
    [NonParallelizable]
    public void The_Type_Which_The_Compiler_Wrote_A_Lambda_Into_Can_Be_Carried_Onto_Another_Type()
    {
        // Tier 1 of the proposal is one move: the type which the compiler wrote for the body of a lambda goes onto the
        // type being woven, and every operand which named a member of it names the copy. What is read here is whether
        // that move is mechanical - whether an assembly which was written that way still reads, and holds no reference
        // of the body to the type which was moved.
        var (result, reported) = CarriedThenWoven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.RunsACapturingLambda), CARRIED_CLOSURE_TYPE);
        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using (var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result)))
        {
            var run = read.MainModule.GetType(CARRIED_CLOSURE_TYPE)!.Methods.Single(method => method.Name == "Run");

            var reached = run.Body.Instructions
                             .Select(instruction => instruction.Operand as MemberReference)
                             .Where(member => member?.DeclaringType?.Name.StartsWith("<", StringComparison.Ordinal) == true)
                             .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(reached, Is.Empty,
                    $"the body of the member still names what the compiler wrote: {string.Join(", ", reached.Select(member => member!.FullName))}");
                Assert.That(run.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldftn),
                    Is.True, "the member holds no delegate, so the template was not carried into it.");
            });
        }

        Assert.That(Ran(result, CARRIED_CLOSURE_TYPE, "Run", 41), Is.EqualTo(42),
            "the member which was woven did not compute what the template computes.");
    }

    [Test]
    [NonParallelizable]
    public void The_Lambda_Which_Captured_Two_Locals_Is_Carried_With_Both_Of_Its_Fields()
    {
        var (result, reported) = CarriedThenWoven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.CapturesTwoLocals), CARRIED_TWO_LOCALS_TYPE);
        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));
            Assert.That(Ran(result, CARRIED_TWO_LOCALS_TYPE, "Run", 41), Is.EqualTo(44),
                "the member which was woven did not compute what the template computes.");
        });
    }

    [Test]
    [NonParallelizable]
    public void The_Lambda_Written_Inside_A_Lambda_Is_Carried_With_What_It_Reaches_In_Turn()
    {
        // The body of the outer lambda holds the pointer to the inner one, and that pointer names a type the compiler
        // wrote just as the pointer of the template does: what the move carries is not one type but everything the
        // bodies it carries reach, and this tells a move that follows what it carries from one that does not.
        var (result, reported) = CarriedThenWoven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.NestsLambdas), CARRIED_NESTED_TYPE);
        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));
            Assert.That(Ran(result, CARRIED_NESTED_TYPE, "Run", 41), Is.EqualTo(44),
                "the member which was woven did not compute what the template computes.");
        });
    }

    [Test]
    [NonParallelizable]
    public void The_Local_Function_Is_Carried_As_A_Member_Rather_Than_As_A_Type()
    {
        // A local function which captured nothing is not a type of its own: it is a member of the type which declares
        // the template, which is the other shape the move has, and the cheaper of the two.
        var (result, reported) = CarriedThenWoven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.RunsALocalFunction), CARRIED_LOCAL_FUNCTION_TYPE);
        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));
            Assert.That(Ran(result, CARRIED_LOCAL_FUNCTION_TYPE, "Run", 41), Is.EqualTo(42),
                "the member which was woven did not compute what the template computes.");
        });
    }

    [Test]
    [NonParallelizable]
    public void A_Template_Which_Is_An_Iterator_Whose_State_Machine_Was_Carried_Is_Woven_And_Enumerated()
    {
        // Tier 2 of the proposal, and the largest thing which tells it from Tier 1: the body of the template is not the
        // stub the member is given but the MoveNext of a state machine, and what is carried is a type whose method holds
        // a switch, a region which protects instructions and locals of its own.
        //
        // What is read here is the move rather than the parsing of MoveNext: the weaving is handed the stub, which is
        // what the member holds, because the move is what the proposal turns on and it is the move which is in doubt.
        var (result, reported) = CarriedThenWoven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.Yields), CARRIED_STATE_MACHINE_TYPE);

        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using (var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result)))
        {
            var run = read.MainModule.GetType(CARRIED_STATE_MACHINE_TYPE)!.Methods.Single(method => method.Name == "Run");
            Assert.That(run.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj),
                Is.True, "the member holds no state machine, so the template was not carried into it.");
        }

        // The sequence is enumerated, because a state machine which was moved is a type whose methods still have to
        // find the fields they were written against: the switch of MoveNext turns on a field of the type.
        var produced = ((IEnumerable<int>) Ran(result, CARRIED_STATE_MACHINE_TYPE, "Run", 7)!).ToArray();
        Assert.That(produced, Is.EqualTo(new[] {7, 8}),
            "the member which was woven did not produce what the template produces.");
    }

    [Test]
    [NonParallelizable]
    public void A_Carried_Type_Whose_Name_The_Target_Already_Holds_Is_Declared_Under_Another()
    {
        // What a copy is declared under is a name of the type being woven, which may already be taken: the name of a
        // type the compiler wrote is not one an identifier of C# holds, and the brackets of it are taken out, so the
        // name a copy takes is a name a member of the type being woven may hold already.
        var (result, reported) = CarriedThenWoven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.RunsACapturingLambda), CARRIED_CLOSURE_TYPE,
            module => module.GetType(CARRIED_CLOSURE_TYPE)!
                            .NestedTypes.Add(new TypeDefinition("", "_c__DisplayClass1_0", TypeAttributes.NestedPrivate, module.TypeSystem.Object)));

        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using (var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result)))
        {
            var names = read.MainModule.GetType(CARRIED_CLOSURE_TYPE)!.NestedTypes.Select(nested => nested.Name).ToArray();

            Assert.That(names, Does.Contain("_c__DisplayClass1_0"), "the type which was there already was taken away.");
            Assert.That(names.Length, Is.EqualTo(2), $"the copy was not declared under a name of its own: {string.Join(", ", names)}");
        }

        Assert.That(Ran(result, CARRIED_CLOSURE_TYPE, "Run", 41), Is.EqualTo(42),
            "the member which was woven did not compute what the template computes.");
    }

    [Test]
    public void A_Body_Which_Names_A_Type_Outside_The_Assembly_Being_Woven_Leaves_A_Reference_To_It()
    {
        // What a body the compiler wrote reaches may leave the assembly the template was compiled into, and the move
        // carries the reference as it stands: the question of the proposal is what such a reference is worth, and this
        // reads whether the assembly which is written still reads at all once it holds one.
        var (handler, _, module) = NewHost($"CarriedAcross{Guid.NewGuid():N}");

        using (var source = AssemblyDefinition.ReadAssembly(System.Reflection.Assembly.GetExecutingAssembly().Location))
        {
            var carrier = new Carrier(module, (TypeDefinition) module.GetType($"{NS}.Host")!);
            carrier.Carry(source.MainModule.GetType(TEMPLATES_TYPE)!.Methods
                                .Single(method => method.Name == nameof(CompilerGeneratedTemplates.CapturesATypeOfItsOwnAssembly)));
        }

        using var written = new MemoryStream();
        Assert.DoesNotThrow(() => handler.Assembly.SaveTo(written),
            "the assembly which holds a body that names a type of another one could not be written.");

        written.Position = 0;
        using var read = AssemblyDefinition.ReadAssembly(written);
        var carried = read.MainModule.GetType($"{NS}.Host")!.NestedTypes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(carried.Fields.Single(field => field.Name == "captured").FieldType.FullName,
                Is.EqualTo("Gneedle.Inject.Test.Captured"),
                "the field of the copy does not name the type the template captured.");

            Assert.That(read.MainModule.AssemblyReferences.Any(reference => reference.Name == "Gneedle.Inject.Test"),
                Is.True, "the assembly names no reference to the one which declares the type its body reaches.");
        });
    }

    [Test]
    [NonParallelizable]
    [Ignore("Blocked as the two above are. The stub of an async body hands the state machine to the builder as the type "
        + "argument of Start<TStateMachine>, which is a generic instance method and is refused for the fault of "
        + "TokenParsing.cs:291; and the move here re-points the type the call is declared by and not the type it is "
        + "instantiated with, so even once the call is carried the call would name the machine which was left behind. "
        + "Neither of those is the move which Tier 3 turns on, and neither is read by this.")]
    public void A_Template_Which_Is_An_Async_Body_Whose_State_Machine_Was_Carried_Is_Woven_And_Awaited()
    {
        var (result, reported) = CarriedThenWoven(TEMPLATES_TYPE, nameof(CompilerGeneratedTemplates.Awaits), CARRIED_ASYNC_TYPE);
        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));
            Assert.That(((Task<int>) Ran(result, CARRIED_ASYNC_TYPE, "Run", 5)!).GetAwaiter().GetResult(), Is.EqualTo(5),
                "the member which was woven did not hand back what the template hands back.");
        });
    }

    [Test]
    [NonParallelizable]
    [Ignore("Blocked by the same fault as the generic call above, and this is the second of the three tiers which it "
        + "stands in front of: the stub of a lambda of a generic template reaches the type which the compiler wrote "
        + "through an instantiation of it, so every call of it is a GenericInstanceMethod and TokenParsing.cs:291 "
        + "throws on the first one. The move itself is written and reads back; what cannot be read is the weaving of "
        + "it. Nothing here is tested until that fault is answered.")]
    public void The_Lambda_Of_A_Generic_Template_Is_Carried_And_Its_Parameter_Re_Parented()
    {
        // Tier 1 where the type the compiler wrote is generic over the type which declares the template, so the copy
        // declares a parameter of its own and every reference to the original names the copy of it. This is the move
        // which CreateProceedMethod already makes for the method it generates, one level down: a generic parameter
        // belongs to the type which declares it, so a copy which was added to another type declares its own.
        var (result, reported) = CarriedThenWoven(GENERIC_TEMPLATES_TYPE, "ReturnsWhatItCaptured", CARRIED_GENERIC_CLOSURE_TYPE);

        Assert.That(reported, Is.Empty, string.Join(Environment.NewLine, reported));

        using (var read = AssemblyDefinition.ReadAssembly(new MemoryStream(result)))
        {
            var copy = read.MainModule.GetType(CARRIED_GENERIC_CLOSURE_TYPE)!.NestedTypes.Single();
            Assert.Multiple(() =>
            {
                Assert.That(copy.HasGenericParameters, Is.True,
                    "the copy declares no generic parameter, so nothing was re-parented.");
                Assert.That(copy.Fields.Single(field => field.Name == "value").FieldType, Is.SameAs(copy.GenericParameters.Single()),
                    "the field of the copy is not the parameter of the copy.");
            });
        }

        Assert.That(Ran(result, CARRIED_GENERIC_CLOSURE_TYPE, "Run", 41), Is.EqualTo(41),
            "the member which was woven did not compute what the template computes.");
    }
}