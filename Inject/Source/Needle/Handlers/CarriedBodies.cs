namespace Gneedle.Inject;

/// <summary>
/// The types and the members which the compiler wrote for the bodies of a template's own, carried onto the type which
/// is woven.<para/>
/// A template is carried instruction by instruction, except for a construct which the compiler writes as a method of
/// its own: a lambda, a local function, an iterator body and an async body each live in a member beside the template,
/// and a lambda and a captured local function live on a type of their own. The template keeps a stub which names them,
/// so carrying the stub alone would carry a call into the assembly the template was compiled into, at a type which is
/// private to it. What this does instead is the move the compiler already made, made once more: what the body reaches
/// is copied into the type being woven, and every operand which named the original names the copy.
/// </summary>
/// <remarks>
/// A copy is a nested type of the type being woven rather than a type of the module, because what a carried body
/// reaches are the members of that type, private ones included, and only a type nested in it reaches those. What a
/// body reaches of the type the template was written in is read as it stands where the two are the same type, and
/// where they are not the carrying reads the type the body is declared by rather than the one the template is - which
/// is a hole of it, named where a template reaches such a type.<para/>
/// What is carried reaches what it carries: the body of a lambda holds the pointer to a lambda written inside it, and
/// that pointer names a type the compiler wrote just as the pointer of the template does. Every copy is therefore
/// declared before any body of one is written, and a type which a copy names is read for a copy in its turn.<para/>
/// The copy keeps the name the compiler wrote, brackets and all, and is told from the type it was written from by where
/// it stands rather than by what it is called: the type the compiler wrote is nested in the type which declares the
/// template, and the copy of it in the type which is woven. A name which the latter already holds is taken by the copy
/// of the number after it.
/// </remarks>
internal sealed class CarriedBodies(ModuleDefinition module, TypeDefinition into)
{
    /// <summary>
    /// The copy whose body is being written, or null where none is.<para/>
    /// A parameter of a method is named within the body of that method and of no other, so the body which is being
    /// written is what the position of such a parameter is read against: what it declares in that position is the copy
    /// of the parameter, and there is nothing else the position could name.
    /// </summary>
    private MethodDefinition? m_Writing;

    /// <summary>
    /// The copies of the types the compiler wrote, by the full name of the type each was written from.
    /// </summary>
    private readonly Dictionary<string, TypeDefinition> m_Types = new(StringComparer.Ordinal);

    /// <summary>
    /// The same copies, in the order they were declared, which is the order their bodies are woven in.
    /// </summary>
    private readonly List<TypeDefinition> m_Copies = [];

    /// <summary>
    /// The copies of the members the compiler wrote on the type which declares the template, which are the bodies of
    /// the local functions which captured nothing: there is no type to move for one of those, and the member is moved
    /// on its own.
    /// </summary>
    private readonly List<MethodDefinition> m_MemberCopies = [];

    /// <summary>
    /// The same copies, with what each was written from, by the key which <see cref="TheKeyOf"/> makes of it: the type
    /// which declares the member, the name of it and how many parameters it takes.<para/>
    /// What a body of the template names is the member the compiler wrote rather than the copy, and the reference it
    /// holds names the instantiation of a generic type where the definition is what was carried: the key is made of
    /// the element type so that the two are one member to the lookup.
    /// </summary>
    private readonly Dictionary<string, (MethodDefinition From, MethodDefinition Copy)> m_MemberMethods = new(StringComparer.Ordinal);

    /// <summary>
    /// Every method of every type which was carried, by the key which <see cref="TheKeyOf"/> makes of it, which is
    /// what tells a body the template reaches from one it does not.
    /// </summary>
    private readonly Dictionary<string, (MethodDefinition From, MethodDefinition Copy)> m_Methods = new(StringComparer.Ordinal);

    /// <summary>
    /// The full name of every type the carry wrote a copy of, which is what tells a type which is still to be carried
    /// from one which was carried already: what is carried reaches what it carries, so the walk of it comes back to a
    /// type it has read.
    /// </summary>
    private readonly HashSet<string> m_Originals = new(StringComparer.Ordinal);

    /// <summary>
    /// The generic parameters of the types which were carried, which are the ones their copies declare in their place.
    /// </summary>
    /// <remarks>
    /// A copy declares its own parameters rather than being given the ones of the type it was carried onto, because
    /// the type it was carried out of declared its own. The key is the full name of the type which declares a
    /// parameter and the position of it, so that a parameter of the template's declaring type and a parameter of a
    /// copy are told apart.
    /// </remarks>
    private readonly Dictionary<string, GenericParameter> m_Parameters = new(StringComparer.Ordinal);

    /// <summary>
    /// Every body which the carry wrote, in the order they are declared, which is the order they are woven in.
    /// </summary>
    private readonly List<Body> m_Bodies = [];

    /// <summary>
    /// The bodies which the carry wrote, which are woven before the body of the template is.
    /// </summary>
    internal IReadOnlyList<Body> Bodies => m_Bodies;

    /// <summary>
    /// The template which is being carried, which is what a refusal names while the carrying runs and which is the one
    /// type a reference to the compiler's own is not carried for.<para/>
    /// It is held rather than handed down, because a type is read for a copy from the reading of a signature as well as
    /// from the walk of a body, and the walk of a signature begins inside the declaration of another copy.
    /// </summary>
    private MethodDefinition? m_Template;

    /// <summary>
    /// The bodies which the carrying has still to read, which are the methods of every type it took and of every member
    /// the compiler wrote on the type which declares the template.
    /// </summary>
    private readonly Queue<MethodDefinition> m_Pending = new();

    /// <summary>
    /// Whether the template which was carried is declared by the type which is woven.<para/>
    /// What a body the compiler wrote holds at its receiver is an instance of the type the template was declared in,
    /// and where that type is the one being woven the two instances are one: what a member of an instance which stands
    /// inside such a body reaches is the instance of the member being woven. A template of any other type is written
    /// against an instance of that type, which the member being woven is not.
    /// </summary>
    internal bool OfTheWovenType { get; private set; }

    /// <summary>
    /// Carry everything the compiler wrote for the bodies of a template onto the type which is woven.
    /// </summary>
    /// <param name="template">The template whose bodies are carried.</param>
    /// <exception cref="WeavingException">Thrown when a body which the compiler wrote cannot be read, or holds no body
    /// to copy.</exception>
    internal void Carry(MethodDefinition template)
    {
        // The parameters of the type which declares the template are named by the parameters of the type being woven,
        // by the position they hold: the two stand for the same types, and a copy of a type the compiler wrote has to
        // be written against the ones the member being woven has.
        for (var index = 0; index < template.DeclaringType.GenericParameters.Count; index++)
        {
            if (index < into.GenericParameters.Count)
            {
                m_Parameters[$"{template.DeclaringType.FullName}/{index}"] = into.GenericParameters[index];
            }
        }

        OfTheWovenType = ReferenceEquals(template.DeclaringType, into);

        m_Template = template;
        m_Pending.Enqueue(template);

        while (m_Pending.Count > 0)
        {
            var method = m_Pending.Dequeue();
            if (!method.HasBody) continue;

            foreach (var instruction in method.Body.Instructions)
            {
                var operand = instruction.Operand;

                // A type is named by an operand of its own as well as by the declaring type of a member which one
                // names: the stub of an iterator boxes the state machine, and the token of a type may stand in a body.
                if (operand is TypeReference named)
                {
                    Reached(named);
                    continue;
                }

                if (operand is not MemberReference member) continue;
                if (member.DeclaringType is null) continue;

                if (TheCompilersOwn(member.DeclaringType))
                {
                    Reached(member.DeclaringType);

                    // The body which the operand names is copied and woven, and the others are not: what a type holds is
                    // what the pointer to one of its bodies reaches, and a body nothing points at holds nothing of the
                    // template - so a member of one which names nothing the carrying could write stays where it stands
                    // rather than being carried.
                    if (member is MethodReference pointed
                        && pointed.ResolveOrNull() is { } pointedAt
                        && m_Originals.Contains(pointedAt.DeclaringType.FullName))
                    {
                        DeclareTheBodyOf(pointedAt.DeclaringType, pointedAt);
                    }

                    continue;
                }

                CarryTheMember(member, template);
            }
        }

        // Every copy is given the body of what it was written from, whether or not the template reaches it: a type whose
        // method holds no instructions is a type the runtime refuses to load, and what the template does not reach is
        // re-pointed and left rather than woven.
        // The list is read until nothing new is added to it, because writing the body of a copy names types as well -
        // a local of a state machine is one of those - and a type the body of a copy names is carried while that body
        // is written. What was written is held by the key of what it was written from, which names the type which
        // declares it as well, so the two lists are told apart there.
        var written = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var methods = m_Methods.Where(entry => !written.Contains(entry.Key)).ToArray();
            var members = m_MemberMethods.Where(entry => !written.Contains(entry.Key)).ToArray();
            if (methods.Length == 0 && members.Length == 0) break;

            foreach (var method in methods)
            {
                written.Add(method.Key);
                CarryTheBody(method.Value.From, method.Value.Copy);
            }

            foreach (var member in members)
            {
                written.Add(member.Key);
                CarryTheBody(member.Value.From, member.Value.Copy);
            }
        }

        m_Template = null;

        ReadTheChains();
    }

    /// <summary>
    /// Whether <paramref name="type"/> is the copy of a type the carry wrote.
    /// </summary>
    /// <param name="type">The type which is asked about.</param>
    /// <returns>Whether the carry wrote it, so that the weaving holds the instructions of what it holds.</returns>
    internal bool HoldsType(TypeReference? type)
    {
        var element = type?.GetElementType();
        if (element is null) return false;

        // A body of the template is read as the compiler wrote it - the carrying does not write to it, because that body
        // is a member of the assembly being woven and another weave of it reads it again - so what reaches the refusals
        // is the type the compiler wrote as often as the copy of it which the carrying made.
        return m_Copies.Exists(copy => ReferenceEquals(copy, element)) || m_Types.ContainsKey(element.FullName);
    }

    /// <summary>
    /// Whether <paramref name="member"/> is the copy of a member the carry wrote.
    /// </summary>
    /// <param name="member">The member which is asked about.</param>
    /// <returns>Whether the carry wrote it, so that the weaving holds the instructions of what it holds.</returns>
    internal bool HoldsMember(MemberReference? member)
        => member is not null
            && (m_MemberCopies.Exists(copy => ReferenceEquals(copy, member))
                || m_MemberMethods.ContainsKey(TheKeyOf(member.DeclaringType, member.Name, member is MethodReference called ? called.Parameters.Count : 0)));

    /// <summary>
    /// Append the copies to the type which is woven: the nested ones under the lock of the module, which is the write
    /// which makes them types of the module, and the members among the members of that type, under the same lock,
    /// which is the one every other member the weaving adds is appended under.
    /// </summary>
    internal void Attach()
    {
        foreach (var copy in m_Copies)
        {
            ModuleLock.DeclareNested(module, into, copy);
        }

        foreach (var copy in m_MemberCopies)
        {
            ModuleLock.DeclareMember(into.Module, into, copy);
        }
    }

    /// <summary>
    /// Take the copies back off the type which is woven, which is what undoes <see cref="Attach"/> and leaves the type
    /// as it was found: a carry whose weaving was refused leaves the type declaring nothing of the compiler's own.
    /// </summary>
    internal void Detach()
    {
        foreach (var copy in m_Copies)
        {
            ModuleLock.UndeclareNested(module, into, copy);
        }

        foreach (var copy in m_MemberCopies)
        {
            ModuleLock.UndeclareMember(into.Module, into, copy);
        }
    }

    /// <summary>
    /// Declare the copy of a member which the compiler wrote on the type which declares the template, when the member
    /// is one the weaving writes, and follow what that member reaches.
    /// </summary>
    /// <remarks>
    /// The member is the body of a local function which captured nothing, which the compiler writes on the type which
    /// declares the template rather than on a type of its own. The bracket alone is not what makes such a member one
    /// of the template's: the compiler writes members under a bracket for reasons of its own, and the ones which hold
    /// something of the template are named with the bracket which closes before the name of the body follows.
    /// </remarks>
    /// <param name="member">The member which a body named.</param>
    /// <param name="template">The template which is carried.</param>
    private void CarryTheMember(MemberReference member, MethodDefinition template)
    {
        if (!member.Name.StartsWith("<", StringComparison.Ordinal)) return;
        if (member.Name.IndexOf(">b__", StringComparison.Ordinal) < 0 && member.Name.IndexOf(">g__", StringComparison.Ordinal) < 0) return;
        if (member.DeclaringType?.GetElementType().FullName != template.DeclaringType.FullName) return;
        if (member is not MethodReference loose) return;

        var looseDefinition = loose.ResolveOrNull()
            ?? throw new WeavingException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_BODY_OF_ITS_OWN, member.FullName, template.FullName));
        if (!looseDefinition.HasBody)
        {
            throw new WeavingException(string.Format(ErrorMessages.A_BODY_OF_ITS_OWN_HOLDS_NO_BODY, looseDefinition.FullName));
        }

        var key = TheKeyOf(looseDefinition.DeclaringType, looseDefinition.Name, looseDefinition.Parameters.Count);
        if (m_MemberMethods.ContainsKey(key)) return;

        var copy = DeclareMethod(looseDefinition, into);

        // A member which the type being woven already declares under that name and with that many parameters is the one
        // the compiler wrote for a template of that type itself, which stands on it: a template declared in the type it
        // is woven into has that member already, and a type which declares two methods of one name and one signature is
        // a type which the runtime refuses to load. The copy takes the number after the name, as a type copy does.
        copy.Name = AMemberNameWhich(copy);
        m_MemberCopies.Add(copy);
        m_MemberMethods[key] = (looseDefinition, copy);
        AddBody(copy);
        m_Pending.Enqueue(looseDefinition);
    }

    /// <summary>
    /// The name a copy of a member takes on the type which is woven, which is the name the compiler wrote unless that
    /// type already declares a member of it with the same number of parameters, or a copy of this carrying does.
    /// </summary>
    /// <param name="copy">The copy which is named.</param>
    /// <returns>The name of the copy.</returns>
    private string AMemberNameWhich(MethodDefinition copy)
        => ANameWhichIsFree(copy.Name, name => m_MemberCopies.Any(method => method.Name == name)
                                               || into.Methods.Any(method => method.Name == name
                                                                             && method.Parameters.Count == copy.Parameters.Count));

    /// <summary>
    /// Declare the copy of a type the compiler wrote, when the type is one of those, and follow what that type holds.
    /// </summary>
    /// <param name="reached">The type which a body or a signature named.</param>
    /// <exception cref="WeavingException">Thrown when the type is one which cannot be read.</exception>
    private void Reached(TypeReference reached)
    {
        var element = reached.GetElementType();
        if (!TheCompilersOwn(element)) return;

        // The type which declares the template is the type the template is a member of rather than a body the compiler
        // wrote for it: what the body of a template of that kind reads through its receiver is what the template
        // captured, which is written where it is read rather than carried, and the type it belongs to stands.
        if (m_Template is not { } template || ReferenceEquals(element, template.DeclaringType)) return;

        var from = element.ResolveOrNull()
            ?? throw new WeavingException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_METHOD_OF_ITS_OWN, element.FullName, template.FullName));
        if (!m_Originals.Add(from.FullName)) return;
        var copy = Declare(from);
        m_Copies.Add(copy);

        // What a type is written against is carried as well: a field of a display class names what the lambda captured,
        // and a state machine is reached through the interfaces it implements.
        Reached(from.BaseType);
        foreach (var implementation in from.Interfaces) Reached(implementation.InterfaceType);
        foreach (var field in from.Fields) Reached(field.FieldType);

        // The constructor of the type is copied with it whether or not anything names it, because what it writes are the
        // fields which the copy holds: the type which holds the lambdas of a type caches the delegate of a lambda which
        // captured nothing in a field of its own, and a copy which left that constructor behind holds nothing there.
        if (from.Methods.FirstOrDefault(method => method.Name == ".cctor") is { } initialiser) DeclareTheBodyOf(from, initialiser);

        // A type which implements an interface is reached by the runtime through it rather than by an operand: the state
        // machine which a body that yields or awaits is written into is started by a call of a builder of the framework,
        // which calls the MoveNext of the machine without anything naming it. Every body of such a type is copied and
        // woven. A type which implements none - the type which holds the lambdas of a type, and the type which holds
        // what one of them captured - is reached by the pointer to a body of it, and a body of it is copied when it is
        // pointed at.
        if (!from.HasInterfaces) return;

        foreach (var method in from.Methods) DeclareTheBodyOf(from, method);
    }

    /// <summary>
    /// Declare the copy of one method of a type which was carried, with its signature, and record that its body is to
    /// be written.<para/>
    /// A copy need not be whole: nothing names the members which nothing reached, so a type of the compiler's own
    /// which only the body of an unreached method named stays where it stands rather than being carried, which is what
    /// the type of an anonymous object at the top level of the assembly is.
    /// </summary>
    /// <param name="from">The type which the method belongs to, which was carried.</param>
    /// <param name="method">The method which is copied.</param>
    private void DeclareTheBodyOf(TypeDefinition from, MethodDefinition method)
    {
        var key = TheKeyOf(from, method.Name, method.Parameters.Count);
        if (m_Methods.ContainsKey(key)) return;

        var copy = DeclareMethod(method, m_Types[from.FullName]);
        ModuleLock.DeclareMember(copy.Module, copy.DeclaringType, copy);
        m_Methods[key] = (method, copy);
        AddBody(copy);

        Reached(method.ReturnType);
        foreach (var parameter in method.Parameters) Reached(parameter.ParameterType);
        m_Pending.Enqueue(method);
    }

    /// <summary>
    /// Declare the copy of a type the compiler wrote, with everything of it which is not a body: what the runtime
    /// reaches a type by, which is its base type and its interfaces, and its fields. Its methods are declared where
    /// the walk reaches them, which is what <see cref="Reached"/> does.
    /// </summary>
    /// <param name="from">The type which is copied.</param>
    /// <returns>The copy of it, which is declared by the type being woven.</returns>
    private TypeDefinition Declare(TypeDefinition from)
    {
        var copy = new TypeDefinition("", Taken(from), from.Attributes, into.Module.TypeSystem.Object)
        {
            DeclaringType = into
        };

        // The copy is held before its members are written, because a member of it names the type which declares it.
        m_Types[from.FullName] = copy;

        foreach (var parameter in from.GenericParameters)
        {
            var declared = new GenericParameter(parameter.Name, copy) {Attributes = parameter.Attributes};
            copy.GenericParameters.Add(declared);
            m_Parameters[$"{from.FullName}/{parameter.Position}"] = declared;
        }

        CarryTheConstraints(from, copy);

        // The base type is read after the parameters of the copy stand, as the signature of a method is written after
        // them: it may name one of them, which no copy stands for while the type is being declared.
        copy.BaseType = TypeOf(from.BaseType);

        foreach (var implementation in from.Interfaces)
        {
            copy.Interfaces.Add(new InterfaceImplementation(TypeOf(implementation.InterfaceType)));
        }

        foreach (var field in from.Fields)
        {
            ModuleLock.DeclareMember(copy.Module, copy, new FieldDefinition(field.Name, field.Attributes, TypeOf(field.FieldType)));
        }

        // The methods of the type are not declared here: which of them are copied is what the walk of what the type
        // holds decides, and a copy need not be whole. See Reached, which declares them.
        return copy;
    }

    /// <summary>
    /// Declare on <paramref name="copy"/> the constraints of every generic parameter which <paramref name="from"/>
    /// declares.<para/>
    /// What a parameter is constrained to is a part of what the copy of it is: a body which calls a member of an
    /// interface through a parameter of its own is a body the runtime reads as having that interface, and a copy whose
    /// parameter declares none is a member which the runtime refuses to load. The constraints are read once every
    /// parameter stands, because one of them may name another parameter of the same type or method.
    /// </summary>
    /// <param name="from">The type or the method whose parameters are copied.</param>
    /// <param name="copy">The copy whose parameters were declared.</param>
    private void CarryTheConstraints(MethodDefinition from, MethodDefinition copy)
        => CarryTheConstraints(from.GenericParameters, copy.GenericParameters);

    /// <inheritdoc cref="CarryTheConstraints(MethodDefinition, MethodDefinition)"/>
    private void CarryTheConstraints(TypeDefinition from, TypeDefinition copy)
        => CarryTheConstraints(from.GenericParameters, copy.GenericParameters);

    /// <inheritdoc cref="CarryTheConstraints(MethodDefinition, MethodDefinition)"/>
    private void CarryTheConstraints(Mono.Collections.Generic.Collection<GenericParameter> from, Mono.Collections.Generic.Collection<GenericParameter> copy)
    {
        for (var position = 0; position < from.Count; position++)
        {
            foreach (var constraint in from[position].Constraints)
            {
                copy[position].Constraints.Add(new GenericParameterConstraint(TypeOf(constraint.ConstraintType)));
            }
        }
    }

    /// <summary>
    /// Refuse a type which the compiler wrote and which no copy stands for.<para/>
    /// The names the compiler gives the types it writes open with the bracket no identifier of C# holds, and the types
    /// the carrying wrote are read by it: what stands here is a type of that kind which was not read - the type of an
    /// anonymous object, or the one a collection expression stands in, which the compiler writes at the top level of
    /// the assembly rather than beside the template. A member written against one names a type which is internal to
    /// that assembly, so it is refused rather than written.
    /// </summary>
    /// <param name="reference">The type which the reference names, or which declares the member it names, or null.</param>
    /// <exception cref="WeavingException">Thrown when the type is one the compiler wrote and no copy stands for.</exception>
    private void RefuseATypeWrittenAtTheTopLevel(TypeReference? reference)
    {
        if (reference is null || !reference.Name.StartsWith("<", StringComparison.Ordinal)) return;

        // The type which declares the template is one of those names and is not carried, and it stands: what the body
        // of a template reads through its receiver is the instance of that type rather than a member of a copy of it.
        if (ReferenceEquals(reference.GetElementType(), m_Template?.DeclaringType)) return;
        if (m_Types.ContainsKey(reference.GetElementType().FullName)) return;

        throw new WeavingException(string.Format(ErrorMessages.TEMPLATE_REACHES_A_TYPE_OF_THE_TOP_LEVEL, reference.FullName, m_Template!.FullName));
    }

    /// <summary>
    /// The key which tells a member of a carried type apart, which is the type which declares it, the name of it, and
    /// how many parameters it takes.<para/>
    /// The type is read as its element type, because the reference a body holds names the instantiation of a generic
    /// type while the definition is what was carried: the two are one type to the lookup.
    /// </summary>
    /// <param name="declaring">The type which declares the member, or a reference to an instantiation of it.</param>
    /// <param name="name">Name of the member.</param>
    /// <param name="parameters">Number of parameters the member takes.</param>
    /// <returns>The key of the member.</returns>
    private static string TheKeyOf(TypeReference? declaring, string name, int parameters)
        => $"{declaring?.GetElementType().FullName}::{name}/{parameters}";

    /// <summary>
    /// Record that a body the compiler wrote is one which the template reaches, and so is woven.
    /// </summary>
    /// <param name="copy">The copy of the body the compiler wrote.</param>
    private void AddBody(MethodDefinition copy)
    {
        if (m_Bodies.Exists(body => ReferenceEquals(body.Copy, copy))) return;

        m_Bodies.Add(new Body(copy));
    }

    /// <summary>
    /// Read the chain of fields which an instance of the member being woven is reached out of, for every body which is
    /// woven.
    /// </summary>
    /// <remarks>
    /// The chain is read once the whole of what is carried is known rather than as each body is found, because a field
    /// of the chain names a type which is carried in its turn: which copies stand where is not settled until nothing is
    /// left to carry, and a walk which ran earlier would miss a hop which the carrying made after it.
    /// </remarks>
    private void ReadTheChains()
    {
        foreach (var body in m_Bodies)
        {
            if (body.Copy.DeclaringType is not { } holder) continue;

            foreach (var field in TheFieldsWhichReachTheInstance(holder)) body.ReachedBy(field);
        }
    }

    /// <summary>
    /// The name the compiler gives the field which holds the instance a body of its own was written in.
    /// </summary>
    private const string INSTANCE_FIELD = "<>4__this";

    /// <summary>
    /// The fields which are read, one after the next, to load an instance of the type being woven out of the receiver
    /// of a body of the compiler's own, or nothing when that body reaches no such instance.
    /// </summary>
    /// <remarks>
    /// The instance a body was written in is a field of the type the compiler wrote for it, which the compiler names
    /// <c>&lt;&gt;4__this</c>, and what that field holds is an instance of the type the body was written in: the name
    /// alone tells nothing, because the compiler gives a field of that name to the type it writes for every body which
    /// has a receiver, and what it holds there is the receiver of that body - for the machine of an async lambda, the
    /// display class of the lambda rather than the instance of the member being woven. The field is therefore read for
    /// the type it holds as well, and one which holds a type the carry wrote is followed as a hop in its turn.
    /// A hop is taken only where exactly one field of the type holds a type which was carried, because a hop taken
    /// wrongly reads a member of another instance rather than refusing.
    /// </remarks>
    /// <param name="holder">The type the compiler wrote, which declares the body.</param>
    /// <returns>The fields of the chain, in the order they are read.</returns>
    private List<FieldReference> TheFieldsWhichReachTheInstance(TypeDefinition holder)
    {
        var chain = new List<FieldReference>();

        // A type which a field of the chain names may be the type the walk started at, which the metadata of a
        // hand-written assembly can hold: the walk stops where it has been rather than following such a field forever.
        var walked = new HashSet<string>(StringComparer.Ordinal);

        for (var at = holder; at is not null && walked.Add(at.FullName);)
        {
            // What the field holds is read as well as its name, because the two together are what tells the instance of
            // the member being woven from the receiver of a body which is written inside it.
            var instance = at.Fields.FirstOrDefault(field => field.Name == INSTANCE_FIELD
                                                             && field.FieldType.GetElementType().FullName == into.FullName);
            if (instance is not null)
            {
                chain.Add(instance);
                return chain;
            }

            var hops = at.Fields.Where(field => m_Copies.Exists(copy => ReferenceEquals(copy, field.FieldType.GetElementType()))).ToArray();
            if (hops.Length != 1) return [];

            chain.Add(hops[0]);
            at = hops[0].FieldType.GetElementType().ResolveOrNull();
        }

        return [];
    }

    /// <summary>
    /// Declare the copy of a method, with its signature and the rows of the method implementation table which say which
    /// interface methods it answers for.
    /// </summary>
    /// <remarks>
    /// An implementation of an interface which the compiler wrote is explicit rather than by name, which is such a row
    /// rather than the name of the method alone: a copy which carries the interfaces and not the rows is a type which
    /// says it implements one and holds nothing which answers to it, and the runtime refuses to load it.
    /// </remarks>
    /// <param name="from">The method which is copied.</param>
    /// <param name="holder">The copy which declares the method.</param>
    /// <returns>The copy of the method.</returns>
    private MethodDefinition DeclareMethod(MethodDefinition from, TypeDefinition holder)
    {
        // The signature is written after the parameters of the method are registered rather than beside them, because a
        // parameter of the method is what the return type and the parameters below name: a local function which is
        // generic in its own right declares one, and its own return type may be it.
        var copy = new MethodDefinition(from.Name, from.Attributes, holder.Module.TypeSystem.Void)
        {
            DeclaringType     = holder,
            HasThis           = from.HasThis,
            ExplicitThis      = from.ExplicitThis,
            CallingConvention = from.CallingConvention
        };

        foreach (var parameter in from.GenericParameters)
        {
            var declared = new GenericParameter(parameter.Name, copy) {Attributes = parameter.Attributes};
            copy.GenericParameters.Add(declared);
            m_Parameters[$"{from.FullName}/{parameter.Position}"] = declared;
        }

        copy.ReturnType = TypeOf(from.ReturnType);
        CarryTheConstraints(from, copy);

        foreach (var parameter in from.Parameters)
        {
            copy.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, TypeOf(parameter.ParameterType)));
        }

        foreach (var overridden in from.Overrides)
        {
            copy.Overrides.Add(ModuleLock.Import(module, overridden));
        }

        return copy;
    }

    /// <summary>
    /// Write the body of one of the copies from the body of what it was written from, with every operand which named
    /// something the carry wrote naming the copy of it.
    /// </summary>
    /// <remarks>
    /// The instructions are laid down before any operand is written, because an operand may name an instruction which
    /// stands after it: a branch names where it branches to, and a handler names where its region begins. What the
    /// second pass reads is the map which the first pass made.
    /// </remarks>
    /// <param name="from">The method whose body is written from.</param>
    /// <param name="copy">The copy whose body is written.</param>
    private void CarryTheBody(MethodDefinition from, MethodDefinition copy)
    {
        if (!from.HasBody) return;

        m_Writing = copy;
        try
        {
            WriteTheBody(from, copy);
        }
        finally
        {
            // The copy is dropped whether the body was written or the writing of it was refused, so that a body which is
            // written next reads the copy of itself rather than the one this held.
            m_Writing = null;
        }
    }

    /// <summary>
    /// The same, which the body above holds the copy of for as long as it runs: a parameter of a method is read against
    /// the copy of that method while the body of the copy is written and nowhere else.
    /// </summary>
    /// <inheritdoc cref="CarryTheBody(MethodDefinition, MethodDefinition)"/>
    private void WriteTheBody(MethodDefinition from, MethodDefinition copy)
    {
        var placed = new Dictionary<Instruction, Instruction>();

        foreach (var variable in from.Body.Variables)
        {
            copy.Body.Variables.Add(new VariableDefinition(TypeOf(variable.VariableType)));
        }

        foreach (var instruction in from.Body.Instructions)
        {
            var placedInstruction = CopyOf(instruction, from);
            placed[instruction] = placedInstruction;
            copy.Body.Instructions.Add(placedInstruction);
        }

        for (var index = 0; index < from.Body.Instructions.Count; index++)
        {
            var instruction = from.Body.Instructions[index];

            placed[instruction].Operand = instruction.Operand switch
            {
                Instruction branch           => placed[branch],
                Instruction[] table          => table.Select(target => placed[target]).ToArray(),
                VariableDefinition variable  => copy.Body.Variables[variable.Index],
                ParameterDefinition argument => copy.Parameters[argument.Index],
                FieldReference field         => Point(field),
                MethodReference called       => Point(called),
                TypeReference type           => TypeOf(type),
                _                            => instruction.Operand
            };
        }

        // Whether the locals of the body are zeroed before it runs is a property of the body rather than of what it
        // holds, and the body which is written is one of those: what the compiler wrote it for is carried with it.
        copy.Body.InitLocals = from.Body.InitLocals;

        foreach (var handler in from.Body.ExceptionHandlers)
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
    /// The field of a copy which a reference names, or null where the carry wrote no copy of the type which declares
    /// the field.
    /// </summary>
    /// <remarks>
    /// The declaring type of a field of a generic type is written as the instantiation the reference names rather than
    /// as the definition, so the field of the copy is written against that instantiation as well: what a reference to a
    /// member of a generic type is written as wherever one is reached.
    /// </remarks>
    /// <param name="field">The field which is asked about.</param>
    /// <returns>The field of the copy, or null.</returns>
    internal FieldReference? TheCopyOf(FieldReference field)
    {
        if (field.DeclaringType is null || !m_Types.TryGetValue(field.DeclaringType.GetElementType().FullName, out var copy))
        {
            return null;
        }

        var open = copy.Fields.SingleOrDefault(candidate => candidate.Name == field.Name)
                   ?? throw new WeavingException(string.Format(ErrorMessages.A_COPY_DOES_NOT_DECLARE_THE_MEMBER_WHICH_IS_NAMED, field.FullName, copy.FullName));
        if (field.DeclaringType is not GenericInstanceType instance) return open;

        return new FieldReference(open.Name, TypeOf(open.FieldType), TypeOf(instance));
    }

    /// <summary>
    /// The member of a copy which a reference names, or null where the carry wrote no copy of what declares it.
    /// </summary>
    /// <param name="called">The member which is asked about.</param>
    /// <returns>The member of the copy, or null.</returns>
    internal MethodReference? TheCopyOf(MethodReference called)
    {
        // The key names the type which declares the member rather than the member itself, because the reference a body
        // holds names the instantiation of a generic type where the definition is what was carried.
        if (m_MemberMethods.TryGetValue(TheKeyOf(called.DeclaringType, called.Name, called.Parameters.Count), out var carried))
        {
            var loose = carried.Copy;

            // A member which the compiler wrote may be generic in its own right, and the call names it through the
            // instantiation it makes of it while what the carrying wrote is the open member: what is written is the
            // instantiation of the copy, because a call of the open member is a body the runtime does not run.
            if (called is not GenericInstanceMethod looseSpecification) return loose;

            var instantiatedLoose = new GenericInstanceMethod(loose);
            foreach (var argument in looseSpecification.GenericArguments) instantiatedLoose.GenericArguments.Add(TypeOf(argument));
            return instantiatedLoose;
        }

        var holder = called.DeclaringType;
        if (holder is null || !m_Types.TryGetValue(holder.GetElementType().FullName, out var copy))
        {
            // A member of a type the carry did not write is not a member of a copy, but a specification of it may still
            // name a type which the carry wrote: the stub of an async body hands the builder the state machine as the
            // argument of the member which starts it, and the builder belongs to the framework.
            return TheSpecification(called);
        }

        // What the copy declares is looked for by the name of the member and how many parameters it takes, which is not
        // the whole of a signature: two members of one name and one count which differ in the types they take or hand
        // back are told apart by nothing here. The reading refuses to choose between them rather than writing a call of
        // whichever it found first, so a body which reached one of those is refused - by the library rather than by the
        // runtime, which is the difference that matters - instead of being woven into a member which fails when it runs.
        // What the compiler writes beside a template declares no two members of one name and one count: an
        // implementation of an interface which is explicit carries the name of the interface in its own name, which is
        // what tells the two `get_Current` of an iterator's machine apart. So nothing reaches that refusal today, and
        // what it guards is the shape of the lookup rather than a case which is there.
        // The one member of that name and count, which is what the comment above refuses to choose between: a copy
        // which declares two of them is one this cannot tell apart, and one which declares none is one the call cannot
        // be read against at all. Both are refused by name rather than by the message of `Single`, which named no
        // member and did not say which of the two it was.
        var open = copy.Methods.SingleOrDefault(candidate => candidate.Name == called.Name && candidate.Parameters.Count == called.Parameters.Count)
                   ?? throw new WeavingException(string.Format(ErrorMessages.A_COPY_DOES_NOT_DECLARE_THE_MEMBER_WHICH_IS_NAMED, called.FullName, copy.FullName));

        // The declaring type of a call into a type which the compiler wrote is written as the instantiation the call
        // makes of it rather than as the definition. What a call names is the instantiation of the copy, which is what
        // the declaring type of a reference is for: writing the open copy instead is a call the runtime does not read.
        if (holder is not GenericInstanceType instance) return open;

        var reference = new MethodReference(open.Name, TypeOf(open.ReturnType), TypeOf(instance))
        {
            HasThis           = open.HasThis,
            ExplicitThis      = open.ExplicitThis,
            CallingConvention = open.CallingConvention
        };

        foreach (var parameter in open.Parameters)
        {
            reference.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, TypeOf(parameter.ParameterType)));
        }

        if (called is not GenericInstanceMethod specification) return reference;

        var instantiated = new GenericInstanceMethod(reference);
        foreach (var argument in specification.GenericArguments) instantiated.GenericArguments.Add(TypeOf(argument));
        return instantiated;
    }

    /// <summary>
    /// A specification of a member the carry did not write, with the arguments of it re-pointed where one of them
    /// names a type which the carry wrote, or null when none of them does.<para/>
    /// What is answered here is whether there is anything of a copy in the reference at all, which is what tells a
    /// caller that it may be written as it stands; what is written where there is not, which is the reference imported
    /// into the module being woven, is <see cref="Pointed"/>. The two are one question asked twice on purpose: the
    /// first is asked of every operand of a body, and the import of one which names nothing of a copy is work which is
    /// not done for it.
    /// </summary>
    /// <param name="called">The member which is asked about.</param>
    /// <returns>The specification with its arguments re-pointed, or null.</returns>
    private MethodReference? TheSpecification(MethodReference called)
    {
        if (called is not GenericInstanceMethod specification) return null;

        var changed = false;
        var carried = new TypeReference[specification.GenericArguments.Count];
        for (var index = 0; index < carried.Length; index++)
        {
            var argument = specification.GenericArguments[index];
            carried[index] =  TheCopyOf(argument) ?? argument;
            changed        |= !ReferenceEquals(carried[index], argument);
        }

        if (!changed) return null;

        // What the specification instantiates belongs to the module the template was read out of, so it is imported
        // into the one which is being written: the same as every other member which the carrying did not write.
        var instantiated = new GenericInstanceMethod(ModuleLock.Import(module, specification.ElementMethod));
        foreach (var argument in carried) instantiated.GenericArguments.Add(argument);
        return instantiated;
    }

    /// <summary>
    /// A type as the copy of it, or null where the carry wrote no copy of it.
    /// </summary>
    /// <param name="type">The type which is asked about.</param>
    /// <returns>The copy of it, or null.</returns>
    internal TypeReference? TheCopyOf(TypeReference type)
        => m_Types.ContainsKey(type.GetElementType().FullName) ? TypeOf(type) : null;

    /// <summary>
    /// The field which the woven body names, which is the field of a copy where the carry wrote one and the field
    /// imported into the module which is being written otherwise.
    /// </summary>
    /// <param name="field">The field which is re-pointed.</param>
    /// <returns>The field which the woven body names.</returns>
    private FieldReference Point(FieldReference field) => TheCopyOf(field) ?? ModuleLock.Import(module, field);

    /// <inheritdoc cref="Point(FieldReference)"/>
    private MethodReference Point(MethodReference called)
        => TheCopyOf(called) ?? Pointed(called);

    /// <summary>
    /// A member of a type the carry did not write, as the module which is being written holds it: the member itself,
    /// with the arguments of a specification of it re-pointed where one of them names a type the carry wrote.
    /// </summary>
    /// <param name="called">The member which is re-pointed.</param>
    /// <returns>The member which the woven body names.</returns>
    private MethodReference Pointed(MethodReference called)
    {
        RefuseATypeWrittenAtTheTopLevel(called.DeclaringType);

        if (called is not GenericInstanceMethod specification)
        {
            // A signature which names a parameter of the method whose body it stands in is one the importer of Cecil
            // reads against the declaring type rather than against that method, so the parameter is one it has nothing
            // standing for: `where T : IComparable<T>` is a constraint of a parameter of the method applied to itself,
            // and the argument of the declaring type of the call of it is such a parameter, which the import answers
            // with nothing for rather than refusing by a name. What such a member names is written rather than imported,
            // which reads every type of the signature for the copy of it.
            return NamesAParameterOfTheMethod(called) ? Written(called) : ModuleLock.Import(module, called);
        }

        var instantiated = new GenericInstanceMethod(ModuleLock.Import(module, specification.ElementMethod));
        foreach (var argument in specification.GenericArguments) instantiated.GenericArguments.Add(TypeOf(argument));
        return instantiated;
    }

    /// <summary>
    /// Whether a reference names a parameter which the method whose body it stands in declares.<para/>
    /// A body names a parameter of a method within that body and of no other, so a parameter of a method which a
    /// reference standing in one names is a parameter of that method, and the copy of the body is what declares it. A
    /// parameter of a type is not one of those: it is named by the signature it stands in, and the declaring type of
    /// the reference is what supplies the type the position of it stands for.
    /// </summary>
    /// <param name="called">The member which is asked about.</param>
    /// <returns>Whether the signature of the reference names such a parameter.</returns>
    private static bool NamesAParameterOfTheMethod(MethodReference called)
        => NamesAParameterOfTheMethod(called.DeclaringType)
           || NamesAParameterOfTheMethod(called.ReturnType)
           || called.Parameters.Any(parameter => NamesAParameterOfTheMethod(parameter.ParameterType));

    /// <inheritdoc cref="NamesAParameterOfTheMethod(MethodReference)"/>
    private static bool NamesAParameterOfTheMethod(TypeReference? type)
        => type switch
        {
            GenericParameter parameter => parameter.Type == Mono.Cecil.GenericParameterType.Method,
            GenericInstanceType instantiation => NamesAParameterOfTheMethod(instantiation.ElementType)
                                                 || instantiation.GenericArguments.Any(NamesAParameterOfTheMethod),
            TypeSpecification specification => NamesAParameterOfTheMethod(specification.ElementType),
            _ => false
        };

    /// <summary>
    /// The reference to a member which the carry did not write, written rather than imported.<para/>
    /// What the copy of the body declares in the place of every parameter of its own is what the body names, so nothing
    /// of the signature is left for the importer of Cecil to read.
    /// </summary>
    /// <param name="called">The member which is written.</param>
    /// <returns>The reference which the body names.</returns>
    private MethodReference Written(MethodReference called)
    {
        var reference = new MethodReference(called.Name, TypeOf(called.ReturnType), TypeOf(called.DeclaringType))
        {
            HasThis           = called.HasThis,
            ExplicitThis      = called.ExplicitThis,
            CallingConvention = called.CallingConvention
        };

        foreach (var parameter in called.Parameters)
        {
            reference.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, TypeOf(parameter.ParameterType)));
        }

        return reference;
    }

    /// <summary>
    /// A type as the copy of it, where the carry wrote one, and the type of the module which is being written
    /// otherwise.
    /// </summary>
    /// <param name="reference">The type which is re-pointed, or null where there is none.</param>
    /// <returns>The type which the woven body names.</returns>
    private TypeReference TypeOf(TypeReference? reference)
    {
        if (reference is null) return module.TypeSystem.Void;

        if (reference is GenericParameter parameter && OwnerOf(parameter) is { } owner)
        {
            // A parameter which a copy of what declared it stands for names that copy.
            if (m_Parameters.TryGetValue($"{owner}/{parameter.Position}", out var declared)) return declared;

            // A parameter of a type is named by the signature it stands in rather than by what Cecil records as its
            // owner, which is the reference it was read out of: the position it holds is what it means, and the body
            // which is written declares the parameter of the copy in that position. What the reference names is
            // therefore left as it was written, and it is the declaring type of the reference - which is written as the
            // instantiation the call makes of it - which supplies the type the position stands for.
            if (parameter.Type == Mono.Cecil.GenericParameterType.Type) return parameter;

            // A parameter of a method is one which the copy of that method declares in the same position: a body names
            // a parameter within that body and of no other, so a parameter of a method which a reference standing in one
            // names is a parameter of the method whose body it stands in.
            if (m_Writing is { } writing && parameter.Position < writing.GenericParameters.Count)
            {
                return writing.GenericParameters[parameter.Position];
            }

            return ModuleLock.Import(module, reference);
        }

        if (reference is GenericInstanceType instantiation)
        {
            var instance = new GenericInstanceType(TypeOf(instantiation.ElementType));
            foreach (var argument in instantiation.GenericArguments) instance.GenericArguments.Add(TypeOf(argument));
            return instance;
        }

        // An array, a pointer and a byref are types of their own which name another type, and what they name may be one
        // the carry wrote: the element is read as the copy of it and the shape around it stands as it was written. What
        // is not read is the modifier, the pin and the function pointer, whose names are read as those of any other
        // type - the shape of a type which a body of the compiler's own holds is not one a template writes by hand.
        if (reference is ArrayType array) return new ArrayType(TypeOf(array.ElementType), array.Rank);
        if (reference is PointerType pointer) return new PointerType(TypeOf(pointer.ElementType));
        if (reference is ByReferenceType byref) return new ByReferenceType(TypeOf(byref.ElementType));

        // A type the carry wrote stands for the type it was written from, so a reference which names the original names
        // the copy: what is carried reaches what it carries, and a copy which still named the type it came from would
        // leave the body pointing at what the move left behind. A copy is a type of the module which is being written.
        if (m_Types.TryGetValue(reference.FullName, out var carried)) return carried;

        // A type the compiler wrote which is named before the walk has reached it is carried here rather than imported.
        // What names one is the signature of a copy - a field, a parameter, the local of a body - and those are written
        // before the walk reaches what they name: a copy which named the type the compiler wrote would be a member of
        // the assembly being woven pointing at a private type of the assembly the template came from.
        Reached(reference);
        if (m_Types.TryGetValue(reference.FullName, out carried)) return carried;

        // A type the compiler wrote which no copy of stands for is one the carrying does not read, and what it writes
        // instead is a reference to a type which is internal to the assembly the template came from: it is refused
        // here, where every operand of every copy passes, as it is where the weaving reads the body of the template.
        RefuseATypeWrittenAtTheTopLevel(reference);

        return ModuleLock.Import(module, reference);
    }

    /// <summary>
    /// What a generic parameter belongs to, which is the type or the method which declares it, or null where it is
    /// declared by neither.
    /// </summary>
    /// <param name="parameter">The parameter which is asked about.</param>
    /// <returns>The full name of what declares it, or null.</returns>
    private static string? OwnerOf(GenericParameter parameter)
        => parameter.Owner switch
        {
            TypeReference type     => type.FullName,
            MethodReference method => method.FullName,
            _                      => null
        };

    /// <summary>
    /// The name a copy takes on the type which is woven, which is the name the compiler wrote unless the type already
    /// holds one of that name.
    /// </summary>
    /// <param name="from">The type which is copied.</param>
    /// <returns>The name of the copy.</returns>
    private string Taken(TypeDefinition from)
        => ANameWhichIsFree(from.Name, name => m_Copies.Exists(copy => copy.Name == name)
                                               || into.NestedTypes.Any(nested => nested.Name == name));

    /// <summary>
    /// The name which a copy takes, which is the name it was written from unless that name is taken on the type which
    /// is woven: two types of one name which one type declares are a type the runtime refuses to load, and so are two
    /// members of one name and one signature.
    /// </summary>
    /// <param name="name">The name which the compiler wrote.</param>
    /// <param name="isTaken">Whether a name is one which the type being woven it was taken on already holds.</param>
    /// <returns>The name of the copy.</returns>
    private static string ANameWhichIsFree(string name, Func<string, bool> isTaken)
    {
        var free = name;
        for (var index = 1; isTaken(free); index++)
        {
            free = $"{name}_{index}";
        }

        return free;
    }

    /// <summary>
    /// Whether a type is one which the compiler wrote beside the template, which is a type it declared inside another
    /// one under a name which opens with the bracket no identifier of C# holds.
    /// </summary>
    /// <param name="type">The type which is asked about.</param>
    /// <returns>Whether it is a type the compiler wrote beside the template.</returns>
    /// <remarks>
    /// What is read is the type which stands beside the template rather than every type the compiler writes: the type
    /// of an anonymous object, and the one a collection expression stands in, are written at the top level of the
    /// assembly the template was compiled into. A template which reaches one of those reaches an <c>internal</c> type
    /// of that assembly, which a member woven into another one cannot run, and the carrying neither writes a copy of it
    /// nor refuses it. That is a hole of the carrying rather than of the shape read here, and a carrying which read the
    /// types at the top level as well would have to read what a parameter of such a type is named, which is a bracket
    /// as well and is no type at all.
    /// </remarks>
    private static bool TheCompilersOwn(TypeReference? type)
        => type is {IsNested: true} && type.Name.StartsWith("<", StringComparison.Ordinal);

    /// <summary>
    /// The instruction which a body is written from, made with the operand it holds.
    /// </summary>
    /// <remarks>
    /// The operand is the one of the original, which is a legal operand of that opcode by the only reading which made
    /// it, and the pass which follows is what re-points the copies of them. <see cref="Instruction.Create(OpCode)"/>
    /// is not enough on its own, because it refuses every opcode which carries an operand.
    /// </remarks>
    /// <param name="instruction">The instruction which is copied.</param>
    /// <param name="from">The method whose body holds it, which the report of an operand the carrying cannot write names.</param>
    /// <returns>The copy of the instruction.</returns>
    /// <exception cref="WeavingException">Thrown when the operand is of a kind the carrying does not write.</exception>
    private static Instruction CopyOf(Instruction instruction, MethodDefinition from)
        => instruction.Operand switch
        {
            null                         => Instruction.Create(instruction.OpCode),
            Instruction branch           => Instruction.Create(instruction.OpCode, branch),
            Instruction[] table          => Instruction.Create(instruction.OpCode, table),
            VariableDefinition variable  => Instruction.Create(instruction.OpCode, variable),
            ParameterDefinition argument => Instruction.Create(instruction.OpCode, argument),
            FieldReference field         => Instruction.Create(instruction.OpCode, field),
            MethodReference method       => Instruction.Create(instruction.OpCode, method),
            TypeReference type           => Instruction.Create(instruction.OpCode, type),
            CallSite site                => Instruction.Create(instruction.OpCode, site),
            string text                  => Instruction.Create(instruction.OpCode, text),
            sbyte number                 => Instruction.Create(instruction.OpCode, number),
            byte number                  => Instruction.Create(instruction.OpCode, number),
            int number                   => Instruction.Create(instruction.OpCode, number),
            long number                  => Instruction.Create(instruction.OpCode, number),
            float number                 => Instruction.Create(instruction.OpCode, number),
            double number                => Instruction.Create(instruction.OpCode, number),
            _                            => throw new WeavingException(string.Format(ErrorMessages.A_BODY_OF_ITS_OWN_HOLDS_AN_OPERAND, instruction, from.FullName))
        };

    /// <summary>
    /// One body which the carry wrote, which is woven in its own right rather than as instructions of the template: the
    /// copy of the member the compiler wrote, and the chain of fields which loads the instance of the member being
    /// woven out of the receiver of that copy.
    /// </summary>
    internal sealed class Body(MethodDefinition copy)
    {
        /// <summary>
        /// The copy of the member which the compiler wrote, which the weaving is written into.
        /// </summary>
        internal MethodDefinition Copy { get; } = copy;

        /// <summary>
        /// The fields which are read, one after the next, to load an instance of the type being woven out of the
        /// receiver of this body, in the order they are read, or nothing when the body reaches no such instance.
        /// </summary>
        /// <remarks>
        /// A lambda which captured a variable is a method of a type which holds what it captured, and the instance it
        /// was written in is a field of that type, which the compiler names for what it holds. The name is not what the
        /// field is read for alone, because it is given to the field which holds the receiver of any body: a field of
        /// that name is the one whose type is the type being woven, and one whose type is a type which was carried is
        /// followed in its turn.
        /// </remarks>
        internal IReadOnlyList<FieldReference> Receiver => m_Receiver;

        private readonly List<FieldReference> m_Receiver = [];

        /// <summary>
        /// Record that the instance of the type being woven is reached out of this body through
        /// <paramref name="field"/>, which is read after the fields which were recorded before it.
        /// </summary>
        /// <param name="field">The field which the chain reads next.</param>
        internal void ReachedBy(FieldReference field) => m_Receiver.Add(field);
    }
}