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
/// reaches are the members of the type the template was written in, private ones included, and only a nested type
/// reaches those.<para/>
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
    /// Carry everything the compiler wrote for the bodies of a template onto the type which is woven.
    /// </summary>
    /// <param name="template">The template whose bodies are carried.</param>
    /// <exception cref="ArgumentException">Thrown when a body which the compiler wrote cannot be read, or holds no body
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

        var pending = new Queue<MethodDefinition>();
        pending.Enqueue(template);

        while (pending.Count > 0)
        {
            var method = pending.Dequeue();
            if (!method.HasBody) continue;

            foreach (var instruction in method.Body.Instructions)
            {
                var operand = instruction.Operand;

                // A type is named by an operand of its own as well as by the declaring type of a member which one
                // names: the stub of an iterator boxes the state machine, and the token of a type may stand in a body.
                if (operand is TypeReference named)
                {
                    Reached(named, pending, template);
                    continue;
                }

                if (operand is not MemberReference member) continue;
                if (member.DeclaringType is null) continue;

                if (TheCompilersOwn(member.DeclaringType))
                {
                    Reached(member.DeclaringType, pending, template);

                    // The body which the operand names is woven, and the others are not: what a type holds is what the
                    // pointer to one of its bodies reaches, and a body nothing points at holds nothing of the template.
                    if (member is MethodReference pointed
                        && m_Methods.TryGetValue(TheKeyOf(member.DeclaringType, pointed.Name, pointed.Parameters.Count), out var pointedAt))
                    {
                        AddBody(pointedAt.Copy);
                    }

                    continue;
                }

                CarryTheMember(member, template, pending);
            }
        }

        // Every copy is given the body of what it was written from, whether or not the template reaches it: a type whose
        // method holds no instructions is a type the runtime refuses to load, and what the template does not reach is
        // re-pointed and left rather than woven.
        foreach (var method in m_Methods.Values)
        {
            CarryTheBody(method.From, method.Copy);
        }

        foreach (var member in m_MemberMethods.Values)
        {
            CarryTheBody(member.From, member.Copy);
        }

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
    /// Append the copies to the type which is woven, under the lock of the module, which is the write which makes them
    /// types of the module and is made where every other write of that table is made.
    /// </summary>
    internal void Attach()
    {
        foreach (var copy in m_Copies)
        {
            ModuleLock.DeclareNested(module, into, copy);
        }

        foreach (var copy in m_MemberCopies)
        {
            into.Methods.Add(copy);
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
            into.Methods.Remove(copy);
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
    /// <param name="pending">The bodies which are still to be read.</param>
    private void CarryTheMember(MemberReference member, MethodDefinition template, Queue<MethodDefinition> pending)
    {
        if (!member.Name.StartsWith("<", StringComparison.Ordinal)) return;
        if (member.Name.IndexOf(">b__", StringComparison.Ordinal) < 0 && member.Name.IndexOf(">g__", StringComparison.Ordinal) < 0) return;
        if (member.DeclaringType?.GetElementType().FullName != template.DeclaringType.FullName) return;
        if (member is not MethodReference loose) return;

        var looseDefinition = loose.ResolveOrNull()
            ?? throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_BODY_OF_ITS_OWN, member.FullName, template.FullName));
        if (!looseDefinition.HasBody)
        {
            throw new ArgumentException(string.Format(ErrorMessages.A_BODY_OF_ITS_OWN_HOLDS_NO_BODY, looseDefinition.FullName));
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
        pending.Enqueue(looseDefinition);
    }

    /// <summary>
    /// The name a copy of a member takes on the type which is woven, which is the name the compiler wrote unless that
    /// type already declares a member of it with the same number of parameters, or a copy of this carrying does.
    /// </summary>
    /// <param name="copy">The copy which is named.</param>
    /// <returns>The name of the copy.</returns>
    private string AMemberNameWhich(MethodDefinition copy)
    {
        var name = copy.Name;
        for (var index = 1;
            into.Methods.Any(method => method.Name == name && method.Parameters.Count == copy.Parameters.Count)
            || m_MemberCopies.Any(method => method.Name == name);
            index++)
        {
            name = $"{copy.Name}_{index}";
        }

        return name;
    }

    /// <summary>
    /// Declare the copy of a type the compiler wrote, when the type is one of those, and follow what that type holds.
    /// </summary>
    /// <param name="reached">The type which a body or a signature named.</param>
    /// <param name="pending">The bodies which are still to be read.</param>
    /// <param name="template">The template which is carried.</param>
    /// <exception cref="ArgumentException">Thrown when the type is one which cannot be read.</exception>
    private void Reached(TypeReference reached, Queue<MethodDefinition> pending, MethodDefinition template)
    {
        var element = reached.GetElementType();
        if (!TheCompilersOwn(element)) return;

        // The type which declares the template is the type the template is a member of rather than a body the compiler
        // wrote for it: what the body of a template of that kind reads through its receiver is what the template
        // captured, which is written where it is read rather than carried, and the type it belongs to stands.
        if (ReferenceEquals(element, template.DeclaringType)) return;

        var from = element.ResolveOrNull()
            ?? throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_METHOD_OF_ITS_OWN, element.FullName, template.FullName));
        if (!m_Originals.Add(from.FullName)) return;
        var copy = Declare(from);
        m_Copies.Add(copy);

        // What a type is written against is carried as well: a field of a display class names what the lambda captured,
        // and a state machine is reached through the interfaces it implements.
        Reached(from.BaseType, pending, template);
        foreach (var implementation in from.Interfaces) Reached(implementation.InterfaceType, pending, template);
        foreach (var field in from.Fields) Reached(field.FieldType, pending, template);

        foreach (var method in from.Methods)
        {
            Reached(method.ReturnType, pending, template);
            foreach (var parameter in method.Parameters) Reached(parameter.ParameterType, pending, template);
            pending.Enqueue(method);
        }
    }

    /// <summary>
    /// Declare the copy of a type the compiler wrote, with everything of it which is not a body: what the runtime
    /// reaches a type by, which is its base type and its interfaces, its fields, and the signature of its methods.
    /// </summary>
    /// <param name="from">The type which is copied.</param>
    /// <returns>The copy of it, which is declared by the type being woven.</returns>
    private TypeDefinition Declare(TypeDefinition from)
    {
        var copy = new TypeDefinition("", Taken(from), from.Attributes, TypeOf(from.BaseType))
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

        foreach (var implementation in from.Interfaces)
        {
            copy.Interfaces.Add(new InterfaceImplementation(TypeOf(implementation.InterfaceType)));
        }

        foreach (var field in from.Fields)
        {
            copy.Fields.Add(new FieldDefinition(field.Name, field.Attributes, TypeOf(field.FieldType)));
        }

        // A type which implements an interface is reached by the runtime through it rather than by an operand: the state
        // machine which a body that yields or awaits is written into is started by a call of a builder of the framework,
        // which calls the MoveNext of the machine without anything naming it. Every body of such a type is woven. A type
        // which implements none - the type which holds the lambdas of a type, and the type which holds what one of them
        // captured - is reached by the pointer to a body of it, and only the bodies which are pointed at are woven.
        var reachedByTheRuntime = from.HasInterfaces;
        foreach (var method in from.Methods)
        {
            var methodCopy = DeclareMethod(method, copy);
            copy.Methods.Add(methodCopy);

            // The key names the type which declares the method rather than the method itself, because the reference
            // which a body holds names the instantiation of a generic type where the definition is what was carried:
            // the two are one type to the lookup, and the name of a method alone tells two of one name apart by nothing.
            m_Methods[TheKeyOf(from, method.Name, method.Parameters.Count)] = (method, methodCopy);

            if (reachedByTheRuntime) AddBody(methodCopy);
        }

        return copy;
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
    /// <c>&lt;&gt;4__this</c>. A type written inside another one holds that one rather than the instance, and the walk
    /// follows it: the hop is taken only where exactly one field of the type holds a type which was carried, because a
    /// hop taken wrongly reads a member of another instance rather than refusing.
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
            // The field which holds the instance is the one the compiler names for what it holds, and a type which holds
            // one may hold a local it captured of the same type beside it: the name tells the instance from the local,
            // which would be read as the instance otherwise.
            var instance = at.Fields.FirstOrDefault(field => field.Name == INSTANCE_FIELD);
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

        var open = copy.Fields.Single(candidate => candidate.Name == field.Name);
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

        var open = copy.Methods.Single(candidate => candidate.Name == called.Name && candidate.Parameters.Count == called.Parameters.Count);

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
    /// names a type which the carry wrote, or null when none of them does.
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
        if (called is not GenericInstanceMethod specification) return ModuleLock.Import(module, called);

        var instantiated = new GenericInstanceMethod(ModuleLock.Import(module, specification.ElementMethod));
        foreach (var argument in specification.GenericArguments) instantiated.GenericArguments.Add(TypeOf(argument));
        return instantiated;
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
            // A parameter which no copy of what declared it stands for is imported, which answers with the parameter
            // itself where the module which declares it is the one being written: a parameter of the template's own
            // method is one of those, and what the member being woven declares in its place is not held here. A
            // parameter of another module is one which the import has no context to resolve against, and it throws
            // rather than refusing by a name.
            return m_Parameters.TryGetValue($"{owner}/{parameter.Position}", out var declared)
                ? declared
                : ModuleLock.Import(module, reference);
        }

        if (reference is GenericInstanceType instantiation)
        {
            var instance = new GenericInstanceType(TypeOf(instantiation.ElementType));
            foreach (var argument in instantiation.GenericArguments) instance.GenericArguments.Add(TypeOf(argument));
            return instance;
        }

        // A type the carry wrote stands for the type it was written from, so a reference which names the original names
        // the copy: what is carried reaches what it carries, and a copy which still named the type it came from would
        // leave the body pointing at what the move left behind. A copy is a type of the module which is being written.
        if (m_Types.TryGetValue(reference.FullName, out var carried)) return carried;

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
    {
        var name = from.Name;
        for (var index = 1; m_Copies.Exists(copy => copy.Name == name) || into.NestedTypes.Any(nested => nested.Name == name); index++)
        {
            name = $"{from.Name}_{index}";
        }

        return name;
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
    /// <exception cref="ArgumentException">Thrown when the operand is of a kind the carrying does not write.</exception>
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
            _                            => throw new ArgumentException(string.Format(ErrorMessages.A_BODY_OF_ITS_OWN_HOLDS_AN_OPERAND, instruction, from.FullName))
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
        /// was written in is a field of that type. The field is found by the type it holds rather than by the name the
        /// compiler gives it, because a type written inside another one holds that one rather than the instance, and
        /// the chain of them is what the walk follows.
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