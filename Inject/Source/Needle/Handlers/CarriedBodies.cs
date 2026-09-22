using Mono.Cecil.Cil;

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
/// The copy keeps the name the compiler wrote, brackets and all, because that name is what tells it from the type it
/// was carried out of. A name which the type being woven already holds is taken by the copy of the number after it.
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
    /// The same copies, by the full name of the member each was written from.<para/>
    /// The key is the name of the original rather than the name of the copy, because a copy is declared by the type
    /// which is woven and its own full name names that type: what a body of the template names is the member the
    /// compiler wrote, which is what is looked up here.
    /// </summary>
    private readonly Dictionary<string, MethodDefinition> m_MemberTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Every method of every type which was carried, by the full name of the method each was written from, which is
    /// what tells a body the template reaches from one it does not.
    /// </summary>
    private readonly Dictionary<string, (MethodDefinition From, MethodDefinition Copy)> m_Methods = new(StringComparer.Ordinal);

    /// <summary>
    /// What each copy was written from, by the full name of the original, which the body of the copy is written out of.
    /// </summary>
    private readonly Dictionary<string, TypeDefinition> m_Originals = new(StringComparer.Ordinal);

    /// <summary>
    /// <inheritdoc cref="m_Originals"/>
    /// </summary>
    private readonly Dictionary<string, MethodDefinition> m_MemberOriginals = new(StringComparer.Ordinal);

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
    /// Whether the carry wrote nothing at all, which is the case of a template which holds no construct the compiler
    /// moved out of it.
    /// </summary>
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
                    if (member is MethodReference pointed && m_Methods.TryGetValue(pointed.FullName, out var pointedAt))
                    {
                        AddBody(pointedAt.From, pointedAt.Copy);
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

        foreach (var member in m_MemberTypes)
        {
            CarryTheBody(m_MemberOriginals[member.Key], member.Value);
        }

        Repoint(template);
    }

    /// <summary>
    /// Whether <paramref name="type"/> is the copy of a type the carry wrote.
    /// </summary>
    /// <param name="type">The type which is asked about.</param>
    /// <returns>Whether the carry wrote it, so that the weaving holds the instructions of what it holds.</returns>
    internal bool HoldsType(TypeReference? type)
    {
        var element = type?.GetElementType();
        return element is not null && m_Copies.Exists(copy => ReferenceEquals(copy, element));
    }

    /// <summary>
    /// Whether <paramref name="member"/> is the copy of a member the carry wrote.
    /// </summary>
    /// <param name="member">The member which is asked about.</param>
    /// <returns>Whether the carry wrote it, so that the weaving holds the instructions of what it holds.</returns>
    internal bool HoldsMember(MemberReference? member)
        => member is not null && m_MemberCopies.Exists(copy => ReferenceEquals(copy, member));

    /// <summary>
    /// Append the copies to the type which is woven, under the lock of the module, which is the write which makes them
    /// types of the module: a body of one names a member of itself, and the carrying of that body imports what it
    /// names.
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

        var looseDefinition = loose.Resolve()
            ?? throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_BODY_OF_ITS_OWN, member.FullName, template.FullName));
        if (!looseDefinition.HasBody)
        {
            throw new ArgumentException(string.Format(ErrorMessages.A_BODY_OF_ITS_OWN_HOLDS_NO_BODY, looseDefinition.FullName));
        }

        if (m_MemberOriginals.ContainsKey(looseDefinition.FullName)) return;

        m_MemberOriginals[looseDefinition.FullName] = looseDefinition;
        var copy = DeclareMethod(looseDefinition, into);
        m_MemberCopies.Add(copy);
        m_MemberTypes[looseDefinition.FullName] = copy;
        AddBody(looseDefinition, copy);
        pending.Enqueue(looseDefinition);
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

        var from = element.Resolve()
            ?? throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_METHOD_OF_ITS_OWN, element.FullName, template.FullName));
        if (m_Originals.ContainsKey(from.FullName)) return;

        m_Originals[from.FullName] = from;
        var copy = Declare(from);
        m_Copies.Add(copy);

        // What a type is written against is carried as well: a field of a display class names what the lambda captured,
        // and a state machine is reached through the interfaces it implements.
        foreach (var parameter in from.GenericParameters) Reached(parameter, pending, template);
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
            m_Methods[method.FullName] = (method, methodCopy);

            if (reachedByTheRuntime) AddBody(method, methodCopy);
        }

        return copy;
    }

    /// <summary>
    /// Record that a body the compiler wrote is one which the template reaches, and so is woven.
    /// </summary>
    /// <param name="from">The body the compiler wrote.</param>
    /// <param name="copy">The copy of it.</param>
    private void AddBody(MethodDefinition from, MethodDefinition copy)
    {
        if (m_Bodies.Exists(body => ReferenceEquals(body.Copy, copy))) return;

        var body = new Body(from, copy);
        if (copy.DeclaringType is { } holder)
        {
            foreach (var field in TheFieldsWhichReachTheInstance(holder)) body.ReachedBy(field);
        }

        m_Bodies.Add(body);
    }

    /// <summary>
    /// The fields which are read, one after the next, to load an instance of the type being woven out of the receiver
    /// of a body of the compiler's own, or nothing when that body reaches no such instance.
    /// </summary>
    /// <remarks>
    /// The instance a body was written in is a field of the type the compiler wrote for it, which the compiler names
    /// <c>&lt;&gt;4__this</c> when that instance is one of the type the template was declared in. A type written inside
    /// another one holds that one rather than the instance, and the walk follows it: the hop is taken only where exactly
    /// one field of the type holds a type which was carried, because a hop taken wrongly reads a member of another
    /// instance rather than refusing.
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
            var instance = at.Fields.FirstOrDefault(field => field.FieldType.GetElementType().FullName == into.FullName);
            if (instance is not null)
            {
                chain.Add(instance);
                return chain;
            }

            var hops = at.Fields.Where(field => m_Copies.Exists(copy => ReferenceEquals(copy, field.FieldType.GetElementType()))).ToArray();
            if (hops.Length != 1) return [];

            chain.Add(hops[0]);
            at = hops[0].FieldType.GetElementType().Resolve();
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
        var copy = new MethodDefinition(from.Name, from.Attributes, TypeOf(from.ReturnType))
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
                Instruction branch          => placed[branch],
                Instruction[] table         => table.Select(target => placed[target]).ToArray(),
                VariableDefinition variable => copy.Body.Variables[variable.Index],
                ParameterDefinition argument => copy.Parameters[argument.Index],
                FieldReference field        => Point(field),
                MethodReference called      => Point(called),
                TypeReference type          => TypeOf(type),
                _                           => instruction.Operand
            };
        }

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
    /// Point every operand of the body of the template at what the carry wrote in place of what it named.<para/>
    /// What the carry did not write is left exactly as the compiler wrote it: the body of the template is read by the
    /// weaving, which imports what it writes and replaces what it stands for, and an operand imported here would name
    /// the weaver in an assembly which does not.
    /// </summary>
    /// <param name="body">The body whose operands are re-pointed, which is the body of the template.</param>
    private void Repoint(MethodDefinition body)
    {
        if (!body.HasBody) return;

        foreach (var instruction in body.Body.Instructions)
        {
            instruction.Operand = instruction.Operand switch
            {
                FieldReference field   => TheCopyOf(field) ?? instruction.Operand,
                MethodReference called => TheCopyOf(called) ?? instruction.Operand,
                TypeReference type     => TheCopyOf(type) ?? instruction.Operand,
                _                      => instruction.Operand
            };
        }
    }

    /// <summary>
    /// The field of a copy which a reference names, or null where the carry wrote no copy of the type which declares
    /// the field.
    /// </summary>
    /// <param name="field">The field which is asked about.</param>
    /// <returns>The field of the copy, or null.</returns>
    private FieldReference? TheCopyOf(FieldReference field)
        => field.DeclaringType is not null && m_Types.TryGetValue(field.DeclaringType.GetElementType().FullName, out var copy)
            ? copy.Fields.Single(candidate => candidate.Name == field.Name)
            : null;

    /// <summary>
    /// The member of a copy which a reference names, or null where the carry wrote no copy of what declares it.
    /// </summary>
    /// <param name="called">The member which is asked about.</param>
    /// <returns>The member of the copy, or null.</returns>
    private MethodReference? TheCopyOf(MethodReference called)
    {
        if (m_MemberTypes.TryGetValue(called.FullName, out var loose)) return loose;

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
            carried[index] = TheCopyOf(argument) ?? argument;
            changed |= !ReferenceEquals(carried[index], argument);
        }

        if (!changed) return null;

        var instantiated = new GenericInstanceMethod(specification.ElementMethod);
        foreach (var argument in carried) instantiated.GenericArguments.Add(argument);
        return instantiated;
    }

    /// <summary>
    /// A type as the copy of it, or null where the carry wrote no copy of it.
    /// </summary>
    /// <param name="type">The type which is asked about.</param>
    /// <returns>The copy of it, or null.</returns>
    private TypeReference? TheCopyOf(TypeReference type)
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
    /// Whether a type is one which the compiler wrote for a body of a template's own, which is a type the compiler
    /// declared inside another one under a name which opens with the bracket no identifier of C# holds.
    /// </summary>
    /// <param name="type">The type which is asked about.</param>
    /// <returns>Whether it is a type the compiler wrote.</returns>
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
            null                          => Instruction.Create(instruction.OpCode),
            Instruction branch            => Instruction.Create(instruction.OpCode, branch),
            Instruction[] table           => Instruction.Create(instruction.OpCode, table),
            VariableDefinition variable   => Instruction.Create(instruction.OpCode, variable),
            ParameterDefinition argument  => Instruction.Create(instruction.OpCode, argument),
            FieldReference field          => Instruction.Create(instruction.OpCode, field),
            MethodReference method        => Instruction.Create(instruction.OpCode, method),
            TypeReference type            => Instruction.Create(instruction.OpCode, type),
            CallSite site                 => Instruction.Create(instruction.OpCode, site),
            string text                   => Instruction.Create(instruction.OpCode, text),
            sbyte number                  => Instruction.Create(instruction.OpCode, number),
            byte number                   => Instruction.Create(instruction.OpCode, number),
            int number                    => Instruction.Create(instruction.OpCode, number),
            long number                   => Instruction.Create(instruction.OpCode, number),
            float number                  => Instruction.Create(instruction.OpCode, number),
            double number                 => Instruction.Create(instruction.OpCode, number),
            _ => throw new ArgumentException(string.Format(ErrorMessages.A_BODY_OF_ITS_OWN_HOLDS_AN_OPERAND, instruction, from.FullName))
        };

    /// <summary>
    /// One body which the carry wrote, which is woven in its own right rather than as instructions of the template: the
    /// member the compiler wrote, the copy of it, and the chain of fields which loads the instance of the member being
    /// woven out of the receiver of the copy.
    /// </summary>
    internal sealed class Body(MethodDefinition from, MethodDefinition copy)
    {
        /// <summary>
        /// The member which the compiler wrote, which the body of the copy is written from.
        /// </summary>
        internal MethodDefinition From { get; } = from;

        /// <summary>
        /// The copy of it, which the weaving is written into.
        /// </summary>
        internal MethodDefinition Copy { get; } = copy;

        /// <summary>
        /// The fields which are read, one after the next, to load an instance of the type being woven out of the
        /// receiver of this body, in the order they are read, or nothing when the body reaches no such instance.
        /// </summary>
        /// <remarks>
        /// A lambda which captured a variable is a method of a type which holds what it captured, and the instance it
        /// was written in is a field of that type. The name the compiler gives that field is read here rather than the
        /// field being found by its type, because what the field holds is what tells one display class from another.
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
