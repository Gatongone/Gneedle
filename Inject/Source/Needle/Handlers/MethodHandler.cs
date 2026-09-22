using System.Globalization;
using System.Reflection;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for method definitions, providing functionalities to manipulate method bodies and translate instructions for code injection purposes.
/// </summary>
internal sealed partial class MethodHandler : IMethodHandler
{
    /// <summary>
    /// The source method definition to inject code into. It is the method definition we want to manipulate and inject code into.
    /// </summary>
    internal readonly MethodDefinition Source;

    /// <summary>
    /// The type handler of the type that declares the source method definition.
    /// It is used to get the context of the source method definition when translating instructions and parsing members in target method definition.
    /// </summary>
    internal readonly TypeHandler DeclaringTypeHandler;

    /// <summary>
    /// The generated method which holds the body which the source method is woven around, or null when the source
    /// method is not woven around.<para/>
    /// The method itself is held rather than looked up on the declaring type by the name of it, because the template is
    /// parsed before the declaring type declares the generated method: what a template proceeds into is the body which
    /// was taken over, and that body belongs to this member whether or not the method which holds it is on the type yet.
    /// </summary>
    private MethodDefinition? m_ProceedMethod;

    /// <summary>
    /// The instance which the delegate of the template was made from, whose fields hold the variables which the
    /// template captured, or null when the template was given as a method rather than as the delegate of it.<para/>
    /// A lambda which captures a variable is an instance method of the type which the compiler wrote to hold it, and it
    /// reads what it captured off that instance. The values themselves are held by the delegate, which the weaving is
    /// given while the injector runs, so what the template captured is written where the template read it.
    /// </summary>
    private object? m_TemplateClosure;

    /// <summary>
    /// The types and the members which the compiler wrote for the bodies of the template's own, which the carrying has
    /// moved onto the type being woven, or null while no template is being parsed.<para/>
    /// The copy of a type the compiler wrote keeps the name of the original, so the name alone no longer tells a type
    /// which the weaving holds the instructions of from one which it does not: what was carried is held here, and the
    /// refusals ask it.
    /// </summary>
    private CarriedBodies? m_Carried;

    /// <summary>
    /// The body of the compiler's own which is being woven, or null while the body of the template itself is.<para/>
    /// A body of the compiler's own is woven in its own right rather than as instructions of the template, so the
    /// questions the parsing asks of a body - which argument a slot names, which local, which receiver - are asked of
    /// the copy rather than of the member being woven.
    /// </summary>
    private CarriedBodies.Body? m_CarriedBody;

    /// <inheritdoc/>
    public MethodFlags Flags => Source.ToMethodFlags();

    /// <summary>
    /// Gets the name of the source method definition. It is used for debugging and logging purposes to identify the method being manipulated.
    /// </summary>
    public string Name => Source.Name;

    /// <inheritdoc/>
    ITypeHandler IMethodHandler.DeclaringTypeHandler => DeclaringTypeHandler;

    /// <inheritdoc/>
    public bool ContainsAttribute(IType attributeType) => Source.CustomAttributes.Any(attribute => TypeName.HasSameName(attribute.AttributeType, attributeType));

    /// <inheritdoc/>
    public void AddAttribute(IType attributeType, params object[] arguments)
    {
        var attributeDef = DeclaringTypeHandler.AssemblyHandler.GetCecilType(attributeType).Definition;
        var attribute = attributeDef.CreateCustomAttribute(DeclaringTypeHandler.AssemblyHandler.Assembly.Source.MainModule, arguments);
        Source.CustomAttributes.Add(attribute);
    }

    /// <summary>
    /// Initialize a new instance of MethodHandler with the source method definition and its declaring type handler.
    /// The source method definition is the method definition we want to inject code into, and the declaring type handler is the type handler of the type that declares the source method definition.
    /// </summary>
    /// <param name="methodDef"></param>
    /// <param name="declaringTypeHandler"></param>
    internal MethodHandler(MethodDefinition methodDef, TypeHandler declaringTypeHandler) => (Source, DeclaringTypeHandler) = (methodDef, declaringTypeHandler);

    /// <summary>
    /// The method which a body is woven into, which is what the walks of that body read it against.
    /// </summary>
    private ParseContext Context => new(Source);

    /// <summary>
    /// Get the string representation of the method, which is the declaration of it and the IL of the body which it
    /// holds.
    /// </summary>
    /// <returns>The declaration of the method, and the IL of its body.</returns>
    public override string ToString() => IlPrinter.Print(Source);

    /// <inheritdoc/>
    public void SetBody(DefaultMethodBody defaultMethodBody)
    {
        // The body is built beside the member rather than in it, and takes the place of the body which the member holds
        // only once it is whole: the body which a base type does not hold is what CallFromBase refuses, and a refusal
        // leaves the member holding the body it held rather than the one which was being written for it.
        var previous = Source.Body;
        var body = new MethodBody(Source);
        Source.Body = body;
        try
        {
            var il = body.GetILProcessor();

            switch (defaultMethodBody)
            {
                case DefaultMethodBody.CallFromBase:
                    SetBodyCallFromBase(il);
                    break;
                case DefaultMethodBody.ThrowException:
                    SetBodyThrowException(il);
                    break;
                case DefaultMethodBody.WithDefaultReturn:
                    SetBodyWithDefaultReturn(il);
                    break;
            }
        }
        catch
        {
            Source.Body = previous;
            throw;
        }
    }

    /// <summary>
    /// Set the method body to call the base type's method with the same name and parameters.
    /// </summary>
    /// <param name="il">The IL processor of the source method body.</param>
    /// <exception cref="ArgumentException">Thrown when the base type or method is not found.</exception>
    private void SetBodyCallFromBase(ILProcessor il)
    {
        // Find the base type's method with the same name and parameter types.
        var baseType = Source.DeclaringType.BaseType;
        if (baseType == null) throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, Source.Name));

        // No delegate describes the member which is called, so the value which it hands back names no parameter of it:
        // the member which a body calls from its base is the one which its name and its parameters name. The base is the
        // one which the type being woven declared, so a parameter which the signature of a member of it names is the
        // argument of that instantiation rather than the parameter of the definition alone.
        var baseMethod = DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(
            DeclaringTypeHandler.AssemblyHandler.GetCecilType(baseType).Definition,
            Source.Name, [.. Source.Parameters.Select(p => p.ParameterType)], instance: baseType);
        if (baseMethod == null)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_METHOD, Source.Name));
        }

        // Load the receiver, then the arguments, call the base method, and return. The receiver of an instance is an
        // argument of the call as much as the others are, and it is the one which no parameter of the member holds.
        // Every argument beyond it is named by its parameter rather than by the slot which it holds, because the slot
        // of an argument is the position of the parameter shifted by the receiver, and only the write knows the shift.
        if (!Source.IsStatic)
        {
            il.Emit(OpCodes.Ldarg_0);
        }

        foreach (var parameter in Source.Parameters)
        {
            il.Emit(OpCodes.Ldarg, parameter);
        }

        il.Emit(baseMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, GetMethodReference(baseMethod, Source.DeclaringType));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Set the method body to throw a <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="il">The IL processor of the source method body.</param>
    private void SetBodyThrowException(ILProcessor il)
    {
        var module = Source.Module;
        var ctor = ModuleLock.Import(module, typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Set the method body to return the default value of the return type.
    /// </summary>
    /// <param name="il">The IL processor of the source method body.</param>
    private void SetBodyWithDefaultReturn(ILProcessor il)
    {
        var returnType = Source.ReturnType;
        if (returnType.MetadataType == MetadataType.Void)
        {
            il.Emit(OpCodes.Ret);
            return;
        }

        // Load default value: null for reference types, default for value types.
        if (returnType.IsValueType)
        {
            var variable = new VariableDefinition(returnType);
            Source.Body.Variables.Add(variable);
            il.Emit(OpCodes.Ldloca, variable);
            il.Emit(OpCodes.Initobj, returnType);
            il.Emit(OpCodes.Ldloc, variable);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Set the body of the method from the delegate which holds the template, which is the template itself where the
    /// template captured nothing and the instance which holds what it captured where it did.
    /// </summary>
    /// <param name="handler">The handler of the method.</param>
    /// <param name="template">The delegate which the template was made into.</param>
    /// <exception cref="ArgumentException">Thrown when the template captured a value and the handler is not one which
    /// this library builds, which holds nothing to write the value into.</exception>
    public static void SetBody(IMethodHandler handler, Delegate template)
        => SetBody(handler, template.Method, template.Target);

    /// <summary>
    /// Set the body of the method from a template which was given as the method alone, so that what the template
    /// captured, if it captured anything, is held by nothing.
    /// </summary>
    /// <param name="handler">The handler of the method.</param>
    /// <param name="method">The template which holds the body to copy.</param>
    /// <param name="closure">The instance which holds what the template captured, or null when there is none.</param>
    /// <exception cref="ArgumentException">Thrown when the template captured a value and the handler is not one which
    /// this library builds, which holds nothing to write the value into.</exception>
    public static void SetBody(IMethodHandler handler, MethodInfo method, object? closure)
    {
        if (closure != null)
        {
            // The value which the template captured is written into the method where the body is woven, which only the
            // handler this library builds does: another implementation holds nothing for it, so the template is refused
            // rather than read for the method alone, which would weave a body without the value which the template read.
            if (handler is not MethodHandler concrete)
            {
                throw new ArgumentException(string.Format(ErrorMessages.HANDLER_HOLDS_NO_CAPTURE, handler.GetType().FullName));
            }

            concrete.SetBody(method, closure);
            return;
        }

        handler.SetBody(method);
    }

    /// <summary>
    /// Set the body of the method to run around the one which it holds, from the delegate which holds the template.
    /// </summary>
    /// <param name="handler">The handler of the method.</param>
    /// <param name="template">The delegate which the template was made into.</param>
    /// <exception cref="ArgumentException">Thrown when the template captured a value and the handler is not one which
    /// this library builds, which holds nothing to write the value into.</exception>
    public static void AroundBody(IMethodHandler handler, Delegate template)
        => AroundBody(handler, template.Method, template.Target);

    /// <summary>
    /// Set the body of the method to run around the one which it holds, from a template which was given as the method
    /// alone, so that what the template captured, if it captured anything, is held by nothing.
    /// </summary>
    /// <param name="handler">The handler of the method.</param>
    /// <param name="method">The template which holds the body to weave around.</param>
    /// <param name="closure">The instance which holds what the template captured, or null when there is none.</param>
    /// <exception cref="ArgumentException">Thrown when the template captured a value and the handler is not one which
    /// this library builds, which holds nothing to write the value into.</exception>
    public static void AroundBody(IMethodHandler handler, MethodInfo method, object? closure)
    {
        if (closure != null)
        {
            // The value which the template captured is written into the method where the body is woven, which only the
            // handler this library builds does: another implementation holds nothing for it, so the template is refused
            // rather than read for the method alone, which would weave a body without the value which the template read.
            if (handler is not MethodHandler concrete)
            {
                throw new ArgumentException(string.Format(ErrorMessages.HANDLER_HOLDS_NO_CAPTURE, handler.GetType().FullName));
            }

            concrete.AroundBody(method, closure);
            return;
        }

        handler.AroundBody(method);
    }

    /// <summary>
    /// Set the method body of source method definition to be the same as the target method definition.
    /// We need to parse the instructions in target method body and translate them to make them work in source method body,
    /// </summary>
    /// <param name="method"></param>
    public void SetBody(MethodInfo method) => SetBody(method, null);

    /// <summary>
    /// Set the method body to be the one which the template holds.
    /// </summary>
    /// <param name="method">The template which holds the body to copy.</param>
    /// <param name="closure">The instance which holds what the template captured, or null when there is none.</param>
    internal void SetBody(MethodInfo method, object? closure)
    {
        var targetDef = DeclaringTypeHandler.AssemblyHandler.ResolveTemplate(method);

        // The template is parsed into a body of its own, and the return type is parsed before that body is handed over:
        // the member holds what it held until every step which can refuse the template has run, so a template which
        // cannot be woven leaves the member as it was rather than half of the one which was to replace it.
        MethodBody woven;
        try
        {
            woven = ParseIntoABodyOfItsOwn(targetDef, closure);
            ParseReturnType(targetDef.ReturnType);
        }
        catch
        {
            // What the carrying wrote is taken back off whether it was the parse or the return type which refused: the
            // return type is read after the parse, and a template which names a token at a position the member being
            // woven does not declare is refused there rather than in the parse.
            m_Carried?.Detach();
            m_Carried = null;
            throw;
        }

        m_Carried = null;
        Source.Body = woven;
    }

    /// <summary>
    /// Set the body of the method to run around the body which it holds, which the template reaches through
    /// <see cref="Proceed"/>.<para/>
    /// The template keeps the signature of the method, so its parameters and its return type have to match, and the body
    /// which the method holds is moved to a generated method of the declaring type which the template calls.
    /// </summary>
    /// <param name="method">The template which holds the body to weave around.</param>
    /// <exception cref="ArgumentException">Thrown when the method cannot be woven around, or when the template does not match it.</exception>
    public void AroundBody(MethodInfo method) => AroundBody(method, null);

    /// <summary>
    /// Set the body of the method to run around the body which it holds, which the template reaches through
    /// <see cref="Proceed"/>.
    /// </summary>
    /// <param name="method">The template which holds the body to weave around.</param>
    /// <param name="closure">The instance which holds what the template captured, or null when there is none.</param>
    /// <exception cref="ArgumentException">Thrown when the method cannot be woven around, or when the template does not match it.</exception>
    internal void AroundBody(MethodInfo method, object? closure)
    {
        var templateDef = DeclaringTypeHandler.AssemblyHandler.ResolveTemplate(method);

        // Every check runs before anything is changed, so that a weave which cannot be done leaves the method as it was.
        if (Source.IsAbstract || Source.IsPInvokeImpl || Source.IsRuntime || Source.IsInternalCall || !Source.HasBody)
        {
            throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_TARGET_HAS_NO_BODY, Source.FullName));
        }

        if (Source.IsConstructor) throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_TARGET_IS_CONSTRUCTOR, Source.FullName));
        if (m_ProceedMethod != null) throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_ALREADY_SET, Source.FullName));

        // The signature of the template is resolved against the method before it is compared, because a generic method is
        // matched through the M_[0-20] tokens of the template rather than through its own generic parameters: a template
        // which takes and returns Gneedle.Inject.M_0 matches a method which takes and returns its first generic
        // parameter. Resolving first is what turns the token into that parameter, and it changes nothing for a method
        // which holds no generic parameter.
        if (templateDef.Parameters.Count != Source.Parameters.Count
            || templateDef.Parameters.Where((parameter, index) => !TypeName.HasSameName(parameter.ParameterType.ParseGenericTokens(Source, Source.Module),
                Source.Parameters[index].ParameterType)).Any())
        {
            throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_PARAMETERS_MISMATCH, Source.FullName, templateDef.FullName));
        }

        if (!TypeName.HasSameName(templateDef.ReturnType.ParseGenericTokens(Source, Source.Module), Source.ReturnType))
        {
            throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_RETURN_TYPE_MISMATCH, Source.FullName, templateDef.FullName));
        }

        var name = $"<{Source.Name}>k__Proceed";
        if (Source.DeclaringType.Methods.Any(methodDef => methodDef.Name == name) || Source.DeclaringType.Fields.Any(field => field.Name == name))
        {
            throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_GENERATED_NAME_OCCUPIED, name));
        }

        var generated = CreateProceedMethod(name);

        // The template is parsed before the generated method is put on the declaring type, and the body of the member
        // is not taken over until the template has been parsed, so that a template which cannot be parsed leaves the
        // member holding the body it held and the type declaring no method of its own. What the template proceeds into
        // is held by this weave rather than looked up on the type, which is what lets the parse run first.
        m_ProceedMethod = generated;
        MethodBody woven;
        try
        {
            woven = ParseIntoABodyOfItsOwn(templateDef, closure);
        }
        catch
        {
            m_ProceedMethod = null;
            throw;
        }

        // Nothing below refuses anything, which is what makes the weave whole: the generated method is declared by the
        // type, the body which the member held is moved onto it, and the body which the template was parsed into takes
        // the place of that body. The return type is deliberately left alone: the template keeps the signature of the
        // member, which was checked above, so parsing it as SetBody does would only write an equal type again.
        // What was carried is dropped here, which is where nothing else can refuse it.
        m_Carried = null;
        Source.DeclaringType.Methods.Add(generated);
        MoveBodyTo(generated, Source);
        Source.Body = woven;
    }

    /// <summary>
    /// Parse the body of a template into a body of the member which the member does not hold yet.<para/>
    /// The member holds the body it held for as long as the parse runs, so that a template which cannot be parsed
    /// leaves the member as it was. The body which is handed back is put in place of that body by the caller which was
    /// handed it, and never before: what the parse wrote goes with the body which was parsed rather than with the one
    /// which the member held, and a parse which is refused discards that body whole.
    /// </summary>
    /// <param name="templateDef">The template which holds the body to parse.</param>
    /// <param name="closure">The instance which holds what the template captured, or null when there is none.</param>
    /// <returns>The body which the template was parsed into, which the member does not hold yet.</returns>
    private MethodBody ParseIntoABodyOfItsOwn(MethodDefinition templateDef, object? closure)
    {
        // A template which holds no body is refused here rather than read: a member which is abstract, or which is a
        // pinvoke, holds no instructions to copy, and reading them is what fails for a template which was named rather
        // than for the member which it names.
        if (!templateDef.HasBody)
        {
            throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_HAS_NO_BODY, templateDef.FullName));
        }

        // The instructions are read before the body of the member is swapped, because the member may be the template
        // itself, and the parse reads those instructions for as long as it runs.
        MethodBody woven;

        // What the compiler wrote for the bodies of this template is carried onto the type being woven before the body
        // of the template is read, because what that body names is the copy rather than the type the compiler wrote,
        // and because a body of the compiler's own is woven in its own right. A carrying which is refused leaves the
        // type declaring nothing of the compiler's own, as the parse below leaves the member holding what it held.
        m_Carried = new CarriedBodies(Source.Module, Source.DeclaringType);
        try
        {
            m_Carried.Carry(templateDef);
            m_Carried.Attach();

            // What the compiler wrote is woven before the template which reaches it, because a body of the compiler's
            // own is a body of its own rather than instructions of the template. None of them is woven with the
            // closure of the template held, which is what makes the inlining of a capture inert for one of them: what
            // a body of the compiler's own reads through the type it belongs to is a real field of that type.
            foreach (var body in m_Carried.Bodies)
            {
                m_CarriedBody = body;
                try
                {
                    body.Copy.Body = WeaveTheBody(body.Copy.Body, body.Copy, body.Copy);
                }
                finally
                {
                    m_CarriedBody = null;
                }
            }

            m_TemplateClosure = closure;
            try
            {
                woven = WeaveTheBody(templateDef.Body, templateDef, Source);
            }
            finally
            {
                // The closure is dropped whether the parse wrote it or was refused, so that a parse which runs next
                // reads what it is given rather than what a parse which failed was given.
                m_TemplateClosure = null;
            }
        }
        catch
        {
            m_Carried.Detach();
            m_Carried = null;
            throw;
        }

        // What was carried is left held for the caller, which is what takes it back off where a step after the parse
        // refuses: the return type is read after this returns, and a template which names a token at a position the
        // member being woven does not declare is refused there. The caller drops it once nothing else can refuse.
        return woven;
    }

    /// <summary>
    /// Weave one body into the method which takes it: the instructions of <paramref name="from"/> are read and the body
    /// which is written takes the place of the body which <paramref name="into"/> holds.<para/>
    /// Every read and write of the body of <paramref name="into"/> for as long as the parse runs reaches the new body
    /// rather than the one which the member holds, which is what leaves the member as it was when the parse is refused.
    /// The parse is the only thing which runs in the meanwhile, so nothing else is written to the body which is left
    /// behind: a step which is added to a weave belongs after this call rather than inside it.
    /// </summary>
    /// <param name="from">The body whose instructions and regions are read.</param>
    /// <param name="targetDef">The method those instructions belong to, which names the arguments and the locals.</param>
    /// <param name="into">The method whose body the woven body takes the place of.</param>
    /// <returns>The body which was woven, which the member does not hold yet.</returns>
    private MethodBody WeaveTheBody(MethodBody from, MethodDefinition targetDef, MethodDefinition into)
    {
        var previous = into.Body;
        var woven = new MethodBody(into);

        into.Body = woven;
        try
        {
            CopyVariables(from, into, HandleLocals(from.Instructions));
            ParseBody(from, targetDef, into);
        }
        catch
        {
            into.Body = previous;
            throw;
        }

        into.Body = previous;
        return woven;
    }

    /// <summary>
    /// Create the method of the declaring type which holds the body which the member is woven around, which the
    /// template proceeds into.<para/>
    /// The method is not put on the declaring type here: the template is parsed first, and a template which cannot be
    /// parsed leaves the type declaring no method of its own.
    /// </summary>
    /// <param name="name">Name of the generated method.</param>
    /// <returns>The generated method, which the declaring type does not declare yet.</returns>
    private MethodDefinition CreateProceedMethod(string name)
    {
        var generated = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.HideBySig
            | (Source.IsStatic ? MethodAttributes.Static : 0), Source.ReturnType)
        {
            DeclaringType = Source.DeclaringType
        };

        // The parameters of the method are given to the generated method as the objects they are rather than as copies
        // of them, so that every operand which names an argument keeps naming the one it named: the method of a parameter
        // is a reference which only the parameter table uses, and that table is written from the signature of the method
        // which holds it. A copy would mean rewriting every argument opcode, the ones which carry no operand included.
        foreach (var parameter in Source.Parameters) generated.Parameters.Add(parameter);

        // The generic parameters are copied rather than given, which is where they differ from the parameters above: a
        // generic parameter belongs to the method which declares it, and adding one to the collection of a second method
        // re-parents it, which leaves the method it came from declaring none of its own. The copies take the name, the
        // attributes and the constraints of the originals, so the generated method is generic in the very same way.
        // A generic parameter is named by its position in every reference to it, so a copy of the same position is the
        // very same parameter to the body which was moved and to the call which the template leaves behind.
        foreach (var genericParameter in Source.GenericParameters)
        {
            var copy = new GenericParameter(genericParameter.Name, generated) {Attributes = genericParameter.Attributes};
            foreach (var constraint in genericParameter.Constraints)
            {
                copy.Constraints.Add(new GenericParameterConstraint(constraint.ConstraintType));
            }

            generated.GenericParameters.Add(copy);
        }

        return generated;
    }

    /// <summary>
    /// Move the body of <paramref name="from"/> onto <paramref name="to"/>, leaving the first empty.
    /// </summary>
    /// <remarks>
    /// The instructions are moved rather than shared, because each of them belongs to a single body: the offset of one
    /// is computed while the body which holds it is written, so a shared one would take the offset of whichever body
    /// was written last. The module of the two methods is the same, so nothing has to be imported or parsed. The
    /// variables and the exception handlers belong to the body which holds them in the same way, and they move with it.
    /// </remarks>
    /// <param name="to">The method which the body is moved onto.</param>
    /// <param name="from">The method which the body is moved from.</param>
    private static void MoveBodyTo(MethodDefinition to, MethodDefinition from)
    {
        var body = to.Body;

        body.Instructions.AddRange(from.Body.Instructions);
        from.Body.Instructions.Clear();
        body.Variables.AddRange(from.Body.Variables);
        from.Body.Variables.Clear();
        body.ExceptionHandlers.AddRange(from.Body.ExceptionHandlers);
        from.Body.ExceptionHandlers.Clear();

        body.InitLocals   = from.Body.InitLocals;
        body.MaxStackSize = from.Body.MaxStackSize;
    }

    /// <summary>
    /// Parse the return type. If the return type holds a generic parameter token which could be parsed from
    /// Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20], it would be replaced with the generic parameter of the source method.
    /// </summary>
    /// <param name="sourceReturnType">The return type to parse.</param>
    public void ParseReturnType(TypeReference sourceReturnType)
        => Source.ReturnType = sourceReturnType.ParseGenericTokens(Source, Source.Module);

    /// <summary>
    /// Parse the method body. We need to translate the instructions in target method body to make them work in source method body,
    /// and then apply the translated instructions to source method body.
    /// </summary>
    /// <param name="from">The body whose instructions and regions are parsed, which is the body of the template or the
    /// body which the compiler wrote for one of its own.</param>
    /// <param name="targetDef">The target method definition to check when parsing instructions.</param>
    /// <param name="into">The method whose body the woven body takes the place of, which is the member being woven for
    /// the template and the copy of it for a body of the compiler's own.</param>
    private void ParseBody(MethodBody from, MethodDefinition targetDef, MethodDefinition into)
    {
        var instructions = from.Instructions;
        var filter = new InstructionFilter(instructions);

        // Translate the accessor codes to standard IL codes.
        for (var index = 0; index < instructions.Count; index++)
        {
            // Skip replacement item.
            if (filter.HasReplaced(index)) continue;

            var instruction = instructions[index];

            // Write what the template captured where it read it.
            if (TryInlineCapture(instructions, index, targetDef, filter)) continue;

            // Replace Ldarg. Every form of the load is translated rather than the two which carry the first slot, because
            // each of them names an argument of the template, and what the member being woven holds at that slot is
            // another argument whenever the two do not agree on belonging to an instance. A body which the compiler
            // wrote for a body of the template's own holds the arguments of its own, which the copy of it declares in
            // the same order and which the carrying re-pointed every operand of, so a load of one stands where it was
            // written and is not translated.
            if (m_CarriedBody is null && instruction.TryGetLdargIndex(!targetDef.IsStatic, out var slot))
            {
                filter.Replace(index, CreateLdarg(slot, targetDef));
                continue;
            }

            // Replace operand.
            if (instruction.Operand != null)
            {
                ReplaceOperand(index, filter, targetDef);
            }
        }

        // Apply translations to body.
        filter.ApplyTo(into.Body.Instructions);

        CarryExceptionHandlers(from.ExceptionHandlers, filter, into);
    }

    /// <summary>
    /// Write the regions which the template protects into the body which is woven, so that what the template catches,
    /// disposes in a `finally` or takes a `lock` on is protected where it was woven as well.<para/>
    /// The boundaries of a region are instructions of the template, and each of them stands where it stood: what the
    /// weaving wrote for it. The types which are caught are imported, so that a region which catches a type of another
    /// assembly is written against that assembly in the body which is woven, as every other operand is.
    /// </summary>
    /// <param name="handlers">The regions of the body which was parsed.</param>
    /// <param name="filter">The instruction filter which wrote the body.</param>
    /// <param name="into">The method whose body the woven body takes the place of.</param>
    /// <exception cref="InvalidILException">Thrown when a region begins at an instruction which the body does not hold.</exception>
    private void CarryExceptionHandlers(Mono.Collections.Generic.Collection<ExceptionHandler> handlers, InstructionFilter filter, MethodDefinition into)
    {
        foreach (var handler in handlers)
        {
            var carried = new ExceptionHandler(handler.HandlerType)
            {
                TryStart     = filter.Emitted(handler.TryStart),
                TryEnd       = filter.Emitted(handler.TryEnd),
                HandlerStart = filter.Emitted(handler.HandlerStart),
                HandlerEnd   = filter.Emitted(handler.HandlerEnd),
                FilterStart  = filter.Emitted(handler.FilterStart),
                CatchType = handler.CatchType == null
                    ? null
                    : ModuleLock.Import(Source.Module, handler.CatchType).ParseGenericTokens(Source, Source.Module)
            };

            // A region which begins at nothing would protect nothing, and a boundary which the template holds and the
            // body does not stand for not one of them: a region which ends at nothing is one which is taken for one
            // which reaches the end of the body, so it would protect more than the template wrote it to. A boundary
            // which the template holds none of stands for the end of the body, and is written by leaving it out rather
            // than by naming an instruction.
            if (carried.TryStart == null || carried.HandlerStart == null
                || (handler.TryEnd != null && carried.TryEnd == null)
                || (handler.HandlerEnd != null && carried.HandlerEnd == null)
                || (handler.FilterStart != null && carried.FilterStart == null))
            {
                throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, "$" + filter.Target.Length));
            }

            into.Body.ExceptionHandlers.Add(carried);
        }
    }

    /// <summary>
    /// Write the value which the template captured where the template read it, which is the pair of the instance it
    /// belongs to being loaded and the field of that instance being read.
    /// </summary>
    /// <remarks>
    /// A lambda which captures a variable is an instance method of the type which the compiler wrote to hold what it
    /// captured, and it reads each of them off the instance of that type which the delegate was made from. That
    /// instance belongs to the run of the injector rather than to the assembly being woven, so what it holds is written
    /// into the member as the value itself, which is what makes the template reach the same value at run time.
    /// </remarks>
    /// <param name="instructions">The instructions of the body which is parsed.</param>
    /// <param name="index">Index of the instruction which may be the load of the instance.</param>
    /// <param name="templateDef">The template which the instructions belong to.</param>
    /// <param name="filter">The instruction filter to replace the instructions.</param>
    /// <returns>Whether a captured value was written, which is false when the pair is not a read of one.</returns>
    /// <exception cref="ArgumentException">Thrown when the template captured a value which cannot be written.</exception>
    private bool TryInlineCapture(Mono.Collections.Generic.Collection<Instruction> instructions, int index, MethodDefinition templateDef, InstructionFilter filter)
    {
        if (m_TemplateClosure == null || index + 1 >= instructions.Count) return false;

        // The load of the instance is the one which names the receiver of the template, and the reads of the fields
        // which are reached through it follow, one after the next.
        if (!instructions[index].TryGetLdargIndex(!templateDef.IsStatic, out var slot) || slot != 0) return false;

        // The first read is the one which reads the instance which the template belongs to, which is the instance the
        // delegate held, and each read after it reaches into the value which the one before handed back: a lambda of an
        // instance captures that instance, and what it reads off it is a member of the instance rather than of the
        // method which holds it.
        var declaringType = templateDef.DeclaringType;
        var fields = new List<FieldReference>();
        var readIndex = index + 1;
        while (readIndex < instructions.Count &&
            instructions[readIndex] is {OpCode.Code: Code.Ldfld, Operand: FieldReference field} &&
            TypeName.HasSameName(field.DeclaringType, fields.Count == 0 ? declaringType : fields[fields.Count - 1].FieldType))
        {
            fields.Add(field);
            readIndex++;
        }

        if (fields.Count == 0) return false;

        // What the template captured is read off the instance which the delegate held, which is the instance the
        // template was written in, and off what each field of the chain holds in its turn.
        var captured = m_TemplateClosure;
        foreach (var field in fields)
        {
            var member = captured?.GetType().GetField(field.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (member == null)
            {
                throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_CAPTURE_CANNOT_BE_WRITTEN, field.Name, field.FieldType.FullName, Source.FullName));
            }

            captured = member.GetValue(captured);
        }

        // A type is not a value which one instruction holds, so it is written as the pair of the token of it and the
        // call which reads the type that token names back: the pair stands where the read of the capture stood, and the
        // reads of the field and of the instance it belongs to are dropped with it.
        if (captured is Type capturedType)
        {
            WriteACapturedType(capturedType, fields[fields.Count - 1], filter, index, fields.Count);
            return true;
        }

        if (!TryCreateLiteral(fields[fields.Count - 1].FieldType, captured, out var literal))
        {
            throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_CAPTURE_CANNOT_BE_WRITTEN, fields[fields.Count - 1].Name, fields[fields.Count - 1].FieldType.FullName, Source.FullName));
        }

        filter.Replace(index, literal!);
        for (var read = index + 1; read <= index + fields.Count; read++)
        {
            filter.Skip(read);
        }

        return true;
    }

    /// <summary>
    /// Write the type which a template captured where the read of it stood, which is the token of the type and the call
    /// which reads the type that token names back.<para/>
    /// A type is a value of the run rather than of the assembly being woven, so it is written as the name of it which
    /// the runtime resolves, which is what an argument of an attribute is read through as well.
    /// </summary>
    /// <param name="captured">The type which the template captured.</param>
    /// <param name="field">The field of the capture which held it, which the report names.</param>
    /// <param name="filter">The instruction filter to replace the instructions.</param>
    /// <param name="index">Index of the instruction which is the load of the instance the capture belongs to.</param>
    /// <param name="reads">Number of the reads which follow it, which are the reads of the capture.</param>
    /// <exception cref="ArgumentException">Thrown when the type is one which the weaver itself declares.</exception>
    private void WriteACapturedType(Type captured, FieldReference field, InstructionFilter filter, int index, int reads)
    {
        // The assembly being woven must not name the weaver: the attributes which the injectors are read from, and the
        // reference to the weaver which they name, are taken out of it once they have been applied, and the token of a
        // type of the weaver would name it again. The type is refused where it is read rather than written, which is
        // where the injector which named it can be found.
        if (captured.Assembly == typeof(Injections).Assembly)
        {
            throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_CAPTURE_NAMES_THE_WEAVER, field.Name, captured.FullName, Source.FullName));
        }

        var token = DeclaringTypeHandler.AssemblyHandler.GetCecilType(captured).Reference;
        var read = ModuleLock.Import(Source.Module, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!);

        filter.Insert(index, Instruction.Create(OpCodes.Ldtoken, token));
        filter.Replace(index, Instruction.Create(OpCodes.Call, read));
        for (var at = index + 1; at <= index + reads; at++)
        {
            filter.Skip(at);
        }
    }

    /// <summary>
    /// The instruction which loads the value which a template captured, for the kinds of value which one instruction
    /// can hold.
    /// </summary>
    /// <param name="fieldType">The type which the field of the capture holds, which is the type the value is read as
    /// rather than the one the value reports: a boxed value reports the type it was boxed from.</param>
    /// <param name="value">The value which the instance holds.</param>
    /// <param name="literal">The instruction which loads the value, or null when there is none.</param>
    /// <returns>Whether the value can be written, which is false for every value but a string, a number, a character, a
    /// boolean, an enumeration and a null of a reference type.</returns>
    private static bool TryCreateLiteral(TypeReference fieldType, object? value, out Instruction? literal)
    {
        var metadata = fieldType.MetadataType;

        // An enumeration is held as a value of the type under it, which is the type the stack carries. What the instance
        // holds is the enumeration boxed, so it is read as that value before it is written, which is what the box of it
        // holds and what the metadata of the field is named by.
        if (metadata == MetadataType.ValueType && fieldType.Resolve() is {IsEnum: true} enumDef)
        {
            metadata = enumDef.GetEnumUnderlyingType().MetadataType;
            if (value != null) value = Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture);
        }

        literal = metadata switch
        {
            MetadataType.String when value is string text   => Instruction.Create(OpCodes.Ldstr, text),
            MetadataType.Boolean when value is bool flag    => Instruction.Create(OpCodes.Ldc_I4, flag ? 1 : 0),
            MetadataType.Char when value is char character  => Instruction.Create(OpCodes.Ldc_I4, character),
            MetadataType.SByte when value is sbyte number   => Instruction.Create(OpCodes.Ldc_I4, number),
            MetadataType.Byte when value is byte number     => Instruction.Create(OpCodes.Ldc_I4, number),
            MetadataType.Int16 when value is short number   => Instruction.Create(OpCodes.Ldc_I4, number),
            MetadataType.UInt16 when value is ushort number => Instruction.Create(OpCodes.Ldc_I4, number),
            MetadataType.Int32 when value is int number     => Instruction.Create(OpCodes.Ldc_I4, number),
            MetadataType.UInt32 when value is uint number   => Instruction.Create(OpCodes.Ldc_I4, unchecked((int) number)),
            MetadataType.Int64 when value is long number    => Instruction.Create(OpCodes.Ldc_I8, number),
            MetadataType.UInt64 when value is ulong number  => Instruction.Create(OpCodes.Ldc_I8, unchecked((long) number)),
            MetadataType.Single when value is float number  => Instruction.Create(OpCodes.Ldc_R4, number),
            MetadataType.Double when value is double number => Instruction.Create(OpCodes.Ldc_R8, number),
            // A null is the same value of every reference type, and the member which it is written into names the type.
            MetadataType.Class or MetadataType.Object or MetadataType.String or MetadataType.Array when value is null
                => Instruction.Create(OpCodes.Ldnull),
            _ => null
        };

        return literal != null;
    }

    /// <summary>
    /// The instruction which loads the argument at <paramref name="slot"/> of the template, written so that it loads the
    /// argument which the member being woven holds at that slot.
    /// </summary>
    /// <param name="slot">The slot which the template names, as the IL of the template holds it: the receiver of the
    /// template takes the first slot when the template belongs to an instance.</param>
    /// <param name="templateDef">The template whose body names the slot.</param>
    /// <returns>The instruction which loads the argument in the member being woven.</returns>
    /// <exception cref="ArgumentException">Thrown when the template reads its own instance, or when the member being woven holds no such argument.</exception>
    private Instruction CreateLdarg(int slot, MethodDefinition templateDef)
    {
        // A macro form carries the slot in the opcode rather than as an operand, so the macro is chosen by the slot
        // which the argument holds in the member being woven, which is not the position of the parameter: the receiver
        // of an instance member takes the slot ahead of the first of them. A slot which no macro holds is loaded
        // through the operand form, which names the parameter itself and leaves the slot of it to be written from the
        // parameter, where the one form of the slot is settled rather than written twice. The parameter is read for
        // that form alone, because the slot which a macro carries is the receiver of a member as often as a parameter
        // of one, and a receiver names no parameter.
        return GetShiftedSlot(slot, templateDef) switch
        {
            0 => Instruction.Create(OpCodes.Ldarg_0),
            1 => Instruction.Create(OpCodes.Ldarg_1),
            2 => Instruction.Create(OpCodes.Ldarg_2),
            3 => Instruction.Create(OpCodes.Ldarg_3),
            _ => Instruction.Create(OpCodes.Ldarg, GetParameterAt(slot, templateDef))
        };
    }

    /// <summary>
    /// The instruction which loads the receiver of a member which a template reached through an instance of its own.
    /// </summary>
    /// <remarks>
    /// A template which names an instance of <c>Instance</c> holds that instance in an argument of its own, and the member
    /// being woven holds the same argument at a slot which is the one the template names shifted by the receivers of the
    /// two. Writing the receiver of the member being woven instead is right only where the instance the template named
    /// is that receiver, which nothing makes it: the argument is what the template reached the member through, so it is
    /// what the member has to be reached through in the body it is woven into.<para/>
    /// A template which named no instance of its own reaches the member of the member being woven, whose receiver is the
    /// load of <c>this</c> which the symbols that name a member of this type are written with: a member which is static
    /// holds no slot for that receiver, so a template which reaches a member of an instance from one is refused.
    /// </remarks>
    /// <param name="instanceIns">The instruction of the template which loads the instance, or null when the template
    /// named none of its own.</param>
    /// <param name="templateDef">The template which the instance is read out of.</param>
    /// <param name="index">Index of the instruction which the receiver is written for, which the fields of a chain
    /// which leads to it are written before.</param>
    /// <param name="filter">The filter which writes the body.</param>
    /// <returns>The instruction which loads the receiver.</returns>
    /// <exception cref="ArgumentException">Thrown when the member being woven is static, and therefore holds no receiver
    /// for a member of an instance which the template reached through <c>This</c> or <c>Base</c>.</exception>
    private Instruction CreateReceiver(Instruction? instanceIns, MethodDefinition templateDef, int index, InstructionFilter filter)
    {
        if (instanceIns is { } ins && ins.TryGetLdargIndex(!templateDef.IsStatic, out var slot))
        {
            return CreateLdarg(slot, templateDef);
        }

        // What a body of the compiler's own holds in its receiver is the value the compiler put there: the type it
        // wrote for a lambda which captured, or the state machine of a body which yields or awaits. The instance of the
        // member being woven is not that value, and the body reaches it through the field the compiler wrote for it, so
        // a body which captured no instance reaches it nowhere.
        if (m_CarriedBody is { } carried)
        {
            // A local function which captured nothing is a member of the type which declares the template rather than of
            // a type of its own, and an instance one of those is written against an instance of that type: its receiver
            // is the instance of the member being woven. A local function which reaches an instance without being one -
            // which the compiler writes as a static member of that type - holds no such instance, and the member is
            // reached through the fields of the chain below or refused with it.
            // The instance such a body is written against is the instance of the member being woven where that instance
            // is one of the type the template was declared in: a template of another type reads members of that type
            // off the woven instance, and a member which belongs to no instance holds no receiver at all.
            if (carried.Copy.DeclaringType is { } holder && !carried.Copy.IsStatic && !Source.IsStatic
                && ReferenceEquals(holder, Source.DeclaringType) && IsDeclaredInTheWovenType(templateDef))
            {
                return Instruction.Create(OpCodes.Ldarg_0);
            }

            // What the body reaches at its receiver is the type the compiler wrote rather than the instance of the member
            // being woven, and that instance lies behind the fields the carrying found: each of them is read before the
            // one after it, and the last is the value the member is reached through.
            if (carried.Receiver.Count == 0)
            {
                throw new ArgumentException(string.Format(ErrorMessages.A_BODY_OF_ITS_OWN_REACHES_NO_INSTANCE, Source.FullName));
            }

            var receiver = Instruction.Create(OpCodes.Ldarg_0);
            foreach (var field in carried.Receiver)
            {
                filter.Insert(index, receiver);
                receiver = Instruction.Create(OpCodes.Ldfld, field);
            }

            return receiver;
        }

        // The instance which the member is reached through is the one which the member being woven belongs to, and a
        // member which is static belongs to none: the slot which `this` takes holds its first argument instead, so the
        // member would be called on a value the template was handed for something else.
        if (Source.IsStatic)
        {
            throw new ArgumentException(string.Format(ErrorMessages.STATIC_MEMBER_REACHES_AN_INSTANCE_MEMBER, Source.FullName));
        }

        return Instruction.Create(OpCodes.Ldarg_0);
    }

    /// <summary>
    /// The slot which the argument at <paramref name="slot"/> of the template holds in the member being woven.
    /// </summary>
    /// <param name="slot">The slot which the template names.</param>
    /// <param name="templateDef">The template whose body names the slot.</param>
    /// <returns>The slot which the same argument holds in the member being woven.</returns>
    /// <exception cref="ArgumentException">Thrown when the template reads its own instance.</exception>
    private int GetShiftedSlot(int slot, MethodDefinition templateDef)
    {
        // A body which the compiler wrote for a body of the template's own holds the arguments of its own: the copy of
        // it declares the same parameters in the same order as the member which the compiler wrote, and the position an
        // argument holds in the body is the position it holds in the copy. Nothing is shifted for one of those, and a
        // load of its receiver is a load of the receiver of the copy, which the carrying re-pointed.
        if (m_CarriedBody is not null) return slot;

        // What the template reads at the slot of its own receiver is that receiver rather than an argument, and no
        // member can be given it: a static member holds no such argument, and an instance one holds another instance in
        // its place. A lambda which captures a variable is an instance method of the type which holds the capture, so a
        // template written as one is a template of this kind.
        if (!templateDef.IsStatic && slot == 0)
        {
            // A template which is declared in the type being woven is written against the instance of that type, which
            // is the instance of the member being woven as well: what such a template loads at its own receiver is that
            // instance, and it stands where it was written. A template given as the delegate which holds it reads the
            // instance the delegate was made from, which is not the instance of the member, and a template which is
            // declared in another type is written against one of that type: both are refused.
            if (m_TemplateClosure is null && !Source.IsStatic && IsDeclaredInTheWovenType(templateDef))
            {
                return 0;
            }

            throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_READS_ITS_OWN_INSTANCE, Source.FullName));
        }

        // The two hold the same arguments in the same order, so an argument moves by one slot exactly when one of them
        // belongs to an instance and the other does not: the receiver of the one which does takes the first slot.
        return slot + (Source.IsStatic ? 0 : 1) - (templateDef.IsStatic ? 0 : 1);
    }

    /// <summary>
    /// Whether the type which declares <paramref name="templateDef"/> is the type being woven: an instance of the type
    /// being woven is an instance of that type, which is what makes the receiver of a template of one the receiver of
    /// the member being woven.
    /// </summary>
    /// <remarks>
    /// Only the type being woven itself is read, and not a base type of it: a base type is a type of another assembly
    /// as often as not, and resolving one here reads an assembly in the middle of a parse. A template of a base type
    /// is left refused until what it needs is read.
    /// </remarks>
    /// <param name="templateDef">The template which is asked about.</param>
    /// <returns>Whether the template belongs to the type being woven.</returns>
    private bool IsDeclaredInTheWovenType(MethodDefinition templateDef)
        => ReferenceEquals(Source.DeclaringType, templateDef.DeclaringType);

    /// <summary>
    /// The parameter which the template reads or writes at a slot, which is the parameter of the member being woven that
    /// holds the same argument.
    /// </summary>
    /// <remarks>
    /// The slot is resolved against the member being woven rather than against the template, because it is the member
    /// which holds the parameter that is written as the operand. The position of that parameter is the position which
    /// the argument holds in the template as well, so it is the slot with the receivers of the two taken off it.
    /// </remarks>
    /// <param name="slot">The slot which the template names.</param>
    /// <param name="templateDef">The template whose body names the slot.</param>
    /// <returns>The parameter of the member being woven which holds the same argument.</returns>
    /// <exception cref="ArgumentException">Thrown when the template reads its own instance, or when the member being woven holds no such argument.</exception>
    private ParameterDefinition GetParameterAt(int slot, MethodDefinition templateDef)
    {
        // The parameter which an argument of a body of the compiler's own holds is a parameter of the copy of that
        // body, which is what the carrying re-pointed every operand of it to.
        if (m_CarriedBody is { } carried)
        {
            var at = carried.Copy.IsStatic ? slot : slot - 1;
            if (at < 0 || at >= carried.Copy.Parameters.Count)
            {
                throw new ArgumentException(string.Format(ErrorMessages.INVALID_TEMPLATE_PARAMETER, at, Source.FullName));
            }

            return carried.Copy.Parameters[at];
        }

        var position = GetShiftedSlot(slot, templateDef) - (Source.IsStatic ? 0 : 1);
        if (position < 0 || position >= Source.Parameters.Count)
        {
            throw new ArgumentException(string.Format(ErrorMessages.INVALID_TEMPLATE_PARAMETER, position, Source.FullName));
        }

        return Source.Parameters[position];
    }

    /// <summary>
    /// Replace the operand of instruction at current index. If the instruction is `ldstr {member_name}` and the nearest `call` instruction
    /// is one of the instance member accessors in Gneedle.Inject, parse the member name and flags, and replace the instruction with
    /// the one to get the member reference. Otherwise, replace the instruction with the one imported to current module.<para/>
    /// The symbol which proceeds is the one which carries no name, so the call is the whole of it and the type which
    /// declares it is what identifies it, in the place where the name of the others identifies them.
    /// </summary>
    /// <param name="currentIndex">Index of the instruction to replace operand.</param>
    /// <param name="filter">The instruction filter to replace the instruction.</param>
    /// <param name="targetDef">The method definition being scanned (source of the instructions).</param>
    private void ReplaceOperand(int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        var bodyInstructions = filter.Target;
        var currentIns = bodyInstructions[currentIndex];
        var memberFlag = MemberSymbols.None;
        var memberName = string.Empty;
        var callIndex = currentIndex + 1;

        // The str may be loaded from follow parameters:
        // - Gneedle.Inject.This.Field(string)
        // - Gneedle.Inject.Base.Field(string)
        // - Gneedle.Inject.This.Property(string)
        // - Gneedle.Inject.Base.Property(string)
        // - Gneedle.Inject.This.Method(string)
        // - Gneedle.Inject.Base.Method(string)
        // which ILCode just look like:
        // IL_0000: ldstr {field_name}
        // IL_0005: call class [Gneedle.Inject]Gneedle.Inject.ValuableMember [Gneedle.Inject]Gneedle.Inject.This::Field(string)
        // The name of the declaring type settles which pointer the operand stands for, so a pointer has to be named here
        // as well as in GetInstanceMemberFlag, which maps the name to the flag.
        if (currentIns.OpCode == OpCodes.Ldstr && bodyInstructions[callIndex].Operand is MethodReference
        {
            DeclaringType:
            {
                Name     : nameof(This) or nameof(Base) or nameof(Instance) or nameof(Static),
                Namespace: nameof(Gneedle) + "." + nameof(Inject)
            }
        } callingMethod)
        {
            // Get type instance member flags.
            memberFlag = GetInstanceMemberFlag(callingMethod);
            if (memberFlag != MemberSymbols.None)
            {
                memberName = (string) currentIns.Operand;
                // Set the instruction to the `call`.
                currentIns = bodyInstructions[callIndex];
            }
        }

        if (memberFlag is not MemberSymbols.None && currentIns.Operand is MethodReference)
        {
            ParseMember(memberName, memberFlag, currentIndex, filter, targetDef);
        }
        // The symbol which proceeds carries no name, so there is no instruction ahead of the call which identifies it:
        // the call is the whole of the symbol, and the type which declares it does what the name of the others does.
        // Which member it stands for is settled by the member being woven, whose body was taken over rather than named.
        else if (currentIns.Operand is MethodReference proceedCall && proceedCall.DeclaringType.FullName == Proceed.TYPE_NAME)
        {
            // The type declares the two symbols which reach the body that was taken over: the one which names a
            // signature for the call to be made with, and the one which takes the arguments which the template itself
            // was given. Which of the two it is, is the name of the call.
            if (proceedCall.Name == nameof(Proceed.Invoke))
            {
                ParseProceedInvoke(currentIndex, proceedCall, filter, targetDef);
            }
            else if (proceedCall.Parameters.Count != 0)
            {
                // A call which hands the symbol a name is a template which was compiled against a weaver which read one,
                // and the name would be left on the stack ahead of the call which is written in its place.
                throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, nameof(Proceed) + "." + nameof(Proceed.Method)));
            }
            else
            {
                ParseMethod(nameof(Proceed) + "." + nameof(Proceed.Method),
                    MemberSymbols.Proceed | MemberSymbols.Method, currentIndex, null, filter, targetDef);
            }
        }
        else
        {
            FilterOperand(currentIns, currentIndex, filter, targetDef);
        }
    }

    /// <summary>
    /// Filter the operand of instruction, and replace it with the one imported to current module.
    /// </summary>
    /// <param name="currentIns">The instruction with non-imported operand.</param>
    /// <param name="currentIndex">Index of the instruction.</param>
    /// <param name="filter">The instruction filter to replace the instruction.</param>
    /// <param name="targetDef">The method definition being scanned (source of the instructions).</param>
    private void FilterOperand(Instruction currentIns, int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        switch (currentIns.Operand)
        {
            // The signature of the method may hold generic parameter tokens, just like List<Gneedle.Inject.T_0>::.ctor().
            // It may also hold a declaring type which stands for the type of another assembly, which the parsing below
            // replaces by the real one.
            case MethodReference methodRef:
                RefuseANameWhichIsNotWritten(methodRef, currentIndex, filter);
                RefuseTheCompilersOwnType(methodRef.DeclaringType, methodRef.FullName);
                RefuseTheCompilersOwnMember(methodRef, targetDef);

                // What the carrying wrote stands for what the body named, and what it wrote is a member of the type
                // being woven rather than a reference of another module: it is written where it stands. The body of the
                // template is not written to for it, because that body is a member of the assembly being woven as well
                // and is read again by a weave of another member of it, which would then read what this one wrote.
                var pointedMethod = m_Carried?.TheCopyOf(methodRef);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode,
                    pointedMethod ?? ModuleLock.Import(Source.Module, methodRef).ParseGenericTokens(Source, Source.Module)));
                break;
            // The parameter of the template is matched to the parameter of the same position rather than to the one of
            // the same name: they are the parameters of two different methods, and the name which the template gave its
            // own takes no part in it. The opcode is kept, because the load or store is an instruction of the member
            // being woven, which accounts for its receiver by itself. The forms of ldarg never reach here: each of them
            // is translated by ParseBody, which reaches the macro forms as well.
            // The position of a parameter is not the slot it holds, which is what GetParameterAt answers for: a parameter
            // of a body which the compiler wrote is the parameter of the copy of that body, which the carrying
            // re-pointed this operand to already, so the operand stands as it is for a body of that kind.
            case ParameterDefinition parameterDef when m_CarriedBody is null:
                var parameter = GetParameterAt(parameterDef.Index, targetDef);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, parameter));
                break;
            // The type of the field may hold generic parameter tokens, just like List<Gneedle.Inject.T_0>::SomeField.
            // It may also hold a declaring type which stands for the type of another assembly, which is replaced below.
            case FieldReference fieldRef:
                RefuseTheCompilersOwnType(fieldRef.DeclaringType, fieldRef.FullName);
                var pointedField = m_Carried?.TheCopyOf(fieldRef);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode,
                    pointedField ?? ModuleLock.Import(Source.Module, fieldRef).ParseGenericTokens(Source, Source.Module)));
                break;
            // We need to find the variable with the same index in source method definition, and replace the operand with it.
            // The local of a body which the compiler wrote is a local of the copy of that body, which the carrying
            // re-pointed: it already names the local it belongs to, and the member being woven may hold an unrelated one
            // at that position, so the operand is left as it stands for a body of that kind.
            case VariableDefinition varDef when m_CarriedBody is null:
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, Source.Body.Variables[varDef.Index]));
                break;
            // The type may hold generic parameter tokens itself, just like box Gneedle.Inject.T_0.
            case TypeReference typeRef:
                RefuseTheCompilersOwnType(typeRef, typeRef.FullName);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode,
                    m_Carried?.TheCopyOf(typeRef) ?? typeRef.ParseGenericTokens(Source, Source.Module)));
                break;
        }
    }

    /// <summary>
    /// Refuse a placeholder which is given a name the weaving has nowhere to read: the name of a member is read out of
    /// the instruction which loads it, which is the one the call follows, so what a template computes is a name which
    /// nothing holds.<para/>
    /// Every name of a placeholder is written where the call is — a literal, a `nameof`, or a constant of the template,
    /// which the compiler writes as the one instruction all the same — so a call which follows anything else is one the
    /// weaving would leave as it was written, and the member which was woven would reach the placeholder when it ran.
    /// </summary>
    /// <param name="member">The member which the call names.</param>
    /// <param name="currentIndex">Index of the instruction of the call.</param>
    /// <param name="filter">The instruction filter which holds the instructions.</param>
    /// <exception cref="ArgumentException">Thrown when the name of the placeholder is not written where the call is.</exception>
    private void RefuseANameWhichIsNotWritten(MemberReference member, int currentIndex, InstructionFilter filter)
    {
        // Only the members which a template names with a string are read this way: the instance which is pushed and the
        // type which another member is looked up on are reached through the instructions around the call.
        if (member.DeclaringType.FullName is not (This.TYPE_NAME or Base.TYPE_NAME or Instance.TYPE_NAME or Static.TYPE_NAME)) return;
        if (member.Name is not (nameof(This.Field) or nameof(This.Property) or nameof(This.Method))) return;

        // The name is what the call follows, and it is read for a name which no load stands ahead of.
        if (currentIndex == 0 || filter.Target[currentIndex - 1].OpCode != OpCodes.Ldstr)
        {
            throw new ArgumentException(string.Format(ErrorMessages.NAME_IS_NOT_WRITTEN, member.FullName, Source.FullName));
        }
    }

    /// <summary>
    /// Refuse a reference into a type which the compiler wrote for a body of the template's own, which the carrying did
    /// not write a copy of.<para/>
    /// A lambda, a local function, an async body and an iterator body are each a method of a type which the compiler
    /// writes beside the template, and which is nested inside what declares it with a name the compiler writes. What
    /// such a type holds is carried onto the type being woven, so a reference to one the carrying wrote is not refused
    /// here; what reaches this is a reference the carrying never saw, which the woven member would reach into at a type
    /// which is private to the assembly the template was compiled into, and fail when it ran rather than here.
    /// </summary>
    /// <param name="type">The type which the reference names, or which declares the member it names.</param>
    /// <param name="reference">The reference itself, which the message names.</param>
    /// <exception cref="ArgumentException">Thrown when the reference reaches a type which the compiler wrote.</exception>
    private void RefuseTheCompilersOwnType(TypeReference? type, string reference)
    {
        // What the carrying wrote keeps the name the compiler wrote, brackets and all, so the name alone no longer
        // tells a type of the compiler's own which the weaving holds the instructions of from one which it does not:
        // what the carrying wrote is read as a type the weaving has, and only what it did not write is refused.
        if (m_Carried?.HoldsType(type) == true) return;

        for (var at = type; at is {IsNested: true}; at = at.DeclaringType)
        {
            if (!at.Name.StartsWith("<", StringComparison.Ordinal)) continue;

            throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_METHOD_OF_ITS_OWN, reference, Source.FullName));
        }
    }

    /// <summary>
    /// Refuse a member which the compiler wrote for a body of the template's own, which the carrying did not write a
    /// copy of.<para/>
    /// A local function which captured nothing is not a type of its own the way a lambda is: it is a method of the type
    /// which declares the template, named with the bracket which no identifier of C# holds. Its body is a body of the
    /// template's, so the carrying writes a copy of it onto the type being woven, and a call of one the carrying wrote
    /// is not refused here; what reaches this is a member whose instructions the carrying could not read.
    /// </summary>
    /// <remarks>
    /// The bracket alone is not what makes such a member one of the template's: the compiler writes members under that
    /// bracket for reasons of its own which hold nothing of the template. A record carries the <c>&lt;Clone&gt;$</c>
    /// which a <c>with</c> expression calls, and a template which is itself declared in a record names it where it
    /// clones that record, so the member stands on the type which declares the template and belongs to the record
    /// rather than to a body of the template's. What tells the two apart is what the name is: the compiler names the
    /// body of a lambda or of a local function under the name of the method it was written in, which the bracket closes
    /// before the name of the body itself follows, while a member it wrote for any other reason carries no such body.
    /// </remarks>
    /// <param name="member">The member which the reference names.</param>
    /// <param name="targetDef">The template which the reference is read out of.</param>
    /// <exception cref="ArgumentException">Thrown when the member is one which the compiler wrote for a body of the template's own.</exception>
    private void RefuseTheCompilersOwnMember(MemberReference member, MethodDefinition targetDef)
    {
        // A member which the carrying wrote is one whose instructions the weaving holds, under the name the compiler
        // wrote it with, so it is not refused for the name alone.
        if (m_Carried?.HoldsMember(member) == true) return;

        if (!member.Name.StartsWith("<", StringComparison.Ordinal)) return;

        // The body of a lambda and the body of a local function are named `<Method>b__...` and `<Method>g__...` by the
        // compiler, so the closing bracket stands ahead of the name of the body rather than after it: a member which
        // the compiler wrote for another reason, such as the `<Clone>$` of a record, is named with the bracket alone.
        if (member.Name.IndexOf(">b__", StringComparison.Ordinal) < 0 &&
            member.Name.IndexOf(">g__", StringComparison.Ordinal) < 0)
        {
            return;
        }

        // The declaring type of a member of a generic type is written as the instantiation which the call names rather
        // than as the definition which the member was written on, and the two are one type to the comparison: what the
        // reference has to be is a member of the type which declares the template, whichever of the two forms the call
        // wrote it in. A body of the compiler's own which captured is written on a type beside the template instead,
        // which the walk of the declaring type refuses before this one is asked.
        if (member.DeclaringType?.GetElementType().FullName != targetDef.DeclaringType.FullName) return;

        throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_BODY_OF_ITS_OWN, member.FullName, Source.FullName));
    }

    /// <summary>
    /// Parse the member based on the member flags, and replace the instruction with the one to get the member reference.
    /// </summary>
    /// <param name="memberName">Name of the member.</param>
    /// <param name="memberSymbol">Member flags about the member kind and its property.</param>
    /// <param name="currentIndex">Index of the instruction of `ldstr {member_name}`.</param>
    /// <param name="filter"></param>
    /// <param name="targetDef">The method definition being scanned (source of the instructions).</param>
    /// <exception cref="InvalidILException">Thrown when the call names no member kind which the weaving reads.</exception>
    private void ParseMember(string memberName, MemberSymbols memberSymbol, int currentIndex, InstructionFilter filter, MethodDefinition targetDef)
    {
        if (memberSymbol.HasFlag(MemberSymbols.Field))
        {
            ParseField(memberName, memberSymbol, currentIndex, filter, targetDef);
        }

        else if (memberSymbol.HasFlag(MemberSymbols.Property))
        {
            ParseProperty(memberName, memberSymbol, currentIndex, filter, targetDef);
        }

        else if (memberSymbol.HasFlag(MemberSymbols.Method))
        {
            // The name is the instruction ahead of the call, which is where every symbol but the one which proceeds
            // writes it: that one is recognized by the call alone and reaches ParseMethod without a name.
            ParseMethod(memberName, memberSymbol, currentIndex + 1, currentIndex, filter, targetDef);
        }

        // A call which names no member kind is one of two things: the half of a pair which the member named beside it
        // is read with, which is the instance an Instance symbol is made of or the type which a Static symbol is written
        // from, or a call which names nothing the weaving reads at all. Leaving the second one where it is leaves a call
        // of a placeholder in the body, where nothing stands for a member of the type which is woven, so it throws for
        // the call at run time rather than for the template at the weaving.
        else if (!memberSymbol.HasFlag(MemberSymbols.Static) && !memberSymbol.HasFlag(MemberSymbols.Instance))
        {
            throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, memberName));
        }
    }

    /// <summary>
    /// Copy the locals of one body into the body which is being written, so that every operand which names a local of
    /// the first names the local of the second which stands at the same position.
    /// </summary>
    /// <param name="from">The body whose locals are copied.</param>
    /// <param name="into">The method whose body takes them.</param>
    /// <param name="emptied">Index of the locals which the weaving empties, which are the ones which <see cref="HandleLocals"/> names.</param>
    private void CopyVariables(MethodBody from, MethodDefinition into, ISet<int> emptied)
    {
        var srcVariables = from.Variables;
        if (srcVariables == null) return;

        var desVariables = into.Body.Variables;
        desVariables.Clear();

        for (var i = 0; i < srcVariables.Count; i++)
        {
            // A local which the weaving empties is one which names nothing of the body which is woven, while the type it
            // is declared with is one of the weaver: copying that type would leave the assembly being woven referring to
            // the weaver for a type which nothing of it names, so the local is copied as a type which every assembly
            // holds instead.
            // The token of a generic parameter is read against the member being woven rather than against the body it
            // stands in, because a token names a parameter of that member however deep in a body of the compiler's own
            // it was written. If the variable type holds such a token, get the actual generic parameter type.
            // A local of a body the compiler wrote is the exception: its type names the copy of what it was written
            // from already, which the carrying made, and reading it again would import the type which was carried and
            // leave the local of the woven body naming the type the compiler wrote.
            var typeRef = emptied.Contains(i)
                ? into.Module.TypeSystem.Object
                : m_CarriedBody is null
                    ? srcVariables[i].VariableType.ParseGenericTokens(Source, into.Module)
                    : srcVariables[i].VariableType;
            desVariables.Add(new VariableDefinition(typeRef));
        }
    }

    /// <summary>
    /// The locals which the handle of a value member is stored into, which are the ones which the weaving empties: every
    /// read of such a local is written as the member itself, and nothing else may reach it.<para/>
    /// Which local those are is the question which the parser of the member asks of the instruction which the handle is
    /// built at, and it is asked here of the same instruction, because the type of a local has to be settled where the
    /// locals are copied, which is before the body is parsed.
    /// </summary>
    /// <param name="bodyInstructions">The instructions of the template.</param>
    /// <returns>Index of every local which the handle of a value member is stored into.</returns>
    private static HashSet<int> HandleLocals(IList<Instruction> bodyInstructions)
    {
        var locals = new HashSet<int>();

        for (var index = 0; index < bodyInstructions.Count; index++)
        {
            if (bodyInstructions[index].Operand is not MethodReference member) continue;

            var memberFlag = GetInstanceMemberFlag(member);
            if (!memberFlag.HasFlag(MemberSymbols.Field) && !memberFlag.HasFlag(MemberSymbols.Property)) continue;

            if (StackWalk.HeldLocal(bodyInstructions, index) is { } handle) locals.Add(handle.Local);
        }

        return locals;
    }

    /// <summary>
    /// Get the member flags of the instance member based on the declaring type and method name of the `call` instruction.
    /// </summary>
    /// <param name="member">The member reference of the `call` instruction.</param>
    /// <returns>The member flags of the instance member.</returns>
    private static MemberSymbols GetInstanceMemberFlag(MemberReference member)
    {
        var memberFlag = MemberSymbols.None;

        memberFlag |= member.DeclaringType.FullName switch
        {
            This.TYPE_NAME     => MemberSymbols.This,
            Base.TYPE_NAME     => MemberSymbols.Base,
            Instance.TYPE_NAME => MemberSymbols.Instance,
            Static.TYPE_NAME   => MemberSymbols.Static,

            // The only member which the pointer holds is the method which the advice proceeds through, so the kind of
            // the member is settled here rather than read from the name of the call.
            Proceed.TYPE_NAME => MemberSymbols.Proceed | MemberSymbols.Method,
            _                 => MemberSymbols.None
        };

        memberFlag |= member.Name switch
        {
            nameof(This.Field)    => MemberSymbols.Field,
            nameof(This.Property) => MemberSymbols.Property,
            nameof(This.Method)   => MemberSymbols.Method,
            _                     => MemberSymbols.None
        };

        return memberFlag;
    }

    /// <summary>
    /// Whether the two instructions which stand ahead of the name of a member are the arrangement of
    /// <c>Static.From("TypeName")</c>, which is the call of <c>From</c> on the placeholder with the text it reads ahead
    /// of it. The two are consumed by the weaving rather than woven, and the name of the member stands after them.<para/>
    /// What the text is is not read here, because what a caller wants of it differs: one which writes the name of the
    /// member needs the name of the type, and one which reads the pattern alone needs only where the name stands.
    /// </summary>
    /// <param name="filter">The filter which holds the instructions of the template.</param>
    /// <param name="nameIndex">Index of the instruction which loads the name of the member.</param>
    /// <returns>Whether the name of the member stands after that arrangement.</returns>
    private static bool IsAStaticFrom(InstructionFilter filter, int nameIndex)
    {
        if (nameIndex < 2) return false;

        var callFrom = filter.Target[nameIndex - 1];
        return callFrom.OpCode == OpCodes.Call
            && callFrom.Operand is MethodReference {Name: "From", DeclaringType: var declaringType}
            && declaringType.FullName == Static.TYPE_NAME
            && filter.Target[nameIndex - 2].OpCode == OpCodes.Ldstr;
    }

    /// <summary>
    /// The type which the arrangement of <c>Static.From("TypeName")</c> ahead of the name of a member names, which is
    /// the type the member is reached on rather than the type which declares the placeholder.<para/>
    /// The type is resolved from the text of the name, so a member of a type of another assembly is named the way that
    /// assembly names it.
    /// </summary>
    /// <param name="filter">The filter which holds the instructions of the template.</param>
    /// <param name="nameIndex">Index of the instruction which loads the name of the member.</param>
    /// <returns>The definition of the type which was named, or null when the arrangement is not there or what it reads
    /// is not the text of a name.</returns>
    private TypeDefinition? TypeNamedByAStaticFrom(InstructionFilter filter, int nameIndex)
    {
        if (!IsAStaticFrom(filter, nameIndex)) return null;

        return filter.Target[nameIndex - 2].Operand is string fullTypeName
            ? DeclaringTypeHandler.AssemblyHandler.GetCecilType(fullTypeName).Definition
            : null;
    }

    /// <summary>
    /// The value which an instance of <see cref="Instance"/> was built around, which the member that the name stands for
    /// is reached through.<para/>
    /// The placeholder is handed an array which holds the value as its only element, and the sequence which builds that
    /// array is dropped by the weaving: what is left of the instance is the value itself.
    /// </summary>
    /// <remarks>
    /// The value is an expression of the template rather than one instruction of it, and which instructions of the body
    /// hold it is read off the stack: the store which fills the array consumes the value, so the instructions which leave
    /// exactly one value between the index of the array and that store are the value.
    /// </remarks>
    /// <param name="filter">The filter which the body is written through.</param>
    /// <param name="nameIndex">Index of the instruction which loads the name of the member.</param>
    /// <param name="targetDef">The template which the instructions are read out of.</param>
    /// <param name="instance">The value which the instance was built around.</param>
    /// <returns>Whether the member was reached through an instance of <see cref="Instance"/> which holds such a value.</returns>
    private static bool TryGetInstanceValue(InstructionFilter filter, int nameIndex, MethodDefinition targetDef, out StackWalk.InstanceValue instance)
    {
        instance = default;
        var bodyInstructions = filter.Target;

        // The name of such a member is preceded by the construction of the instance, which is handed an array of one
        // element: what the value is, is what that element holds.
        if (nameIndex < 2) return false;

        var constructor = bodyInstructions[nameIndex - 1];
        if (constructor.OpCode != OpCodes.Newobj
            || constructor.Operand is not MethodReference {Name: ".ctor", DeclaringType: var declaringType}
            || declaringType.FullName != Instance.TYPE_NAME)
        {
            return false;
        }

        if (bodyInstructions[nameIndex - 2].OpCode != OpCodes.Stelem_Ref) return false;

        var values = 0;
        for (var index = nameIndex - 3; index >= 0; index--)
        {
            var instruction = bodyInstructions[index];

            // A step which writes nothing leaves the count where it was, and one whose count the walk cannot tell leaves
            // the whole of the value unknown: a value which is not read is not written into the body either.
            if (instruction.OpCode == OpCodes.Nop) continue;
            if (StackWalk.StackDelta(instruction) is not { } delta) return false;

            values += delta;
            if (values != 1) continue;

            // What stands ahead of the value is the array which is being filled: the value is an element of a new array of
            // one, and the sequence which builds that array is the whole of what the placeholder was built around.
            if (index < 4
                || bodyInstructions[index - 1].OpCode.Code != Code.Ldc_I4_0
                || bodyInstructions[index - 2].OpCode != OpCodes.Dup
                || bodyInstructions[index - 3].OpCode != OpCodes.Newarr
                || bodyInstructions[index - 4].OpCode.Code != Code.Ldc_I4_1)
            {
                return false;
            }

            // The load of an argument is held at the same slot by every member which the template is woven into, so it is
            // written where the name of the member stands rather than where it stood. What the template computed is held
            // where it computed it, and nothing of the weaving could write it anywhere else.
            var element = index == nameIndex - 3 ? bodyInstructions[index] : null;
            instance = new StackWalk.InstanceValue(index, nameIndex - 3,
                element != null && element.TryGetLdargIndex(!targetDef.IsStatic, out _) ? element : null);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The type of the value which an instance of <see cref="Instance"/> was built around, which is the type which the
    /// member that the name stands for is looked up on.
    /// </summary>
    /// <param name="filter">The filter which the body is written through.</param>
    /// <param name="index">Index of the instruction which leaves the value on the stack.</param>
    /// <param name="targetDef">The template which the instructions are read out of.</param>
    /// <returns>The type of the value, or null when the walk cannot tell it.</returns>
    private TypeReference? GetValueType(InstructionFilter filter, int index, MethodDefinition targetDef)
    {
        // What the weaving wrote for the value is what its type is read off rather than what the template held there,
        // because the instructions ahead of the name were parsed before it: a value which another placeholder was built
        // around stands in the body as the member which that one stands for rather than as the handle it was read through.
        var instruction = filter.Replaced(index) ?? filter.Target[index];

        // An instruction which the body does not hold stands for nothing which could be read, and a value whose end was
        // dropped is one which the walk cannot tell the type of.
        if (instruction.OpCode == OpCodes.Nop) return null;

        // A local holds a value of a type which its declaration names and the instruction which reads it does not.
        if (instruction.TryGetLdlocIndex(out var local) && local >= 0 && local < targetDef.Body.Variables.Count)
        {
            return targetDef.Body.Variables[local].VariableType.ParseGenericTokens(Source, Source.Module);
        }

        return StackWalk.TryGetStackType(Context, instruction, targetDef, out var type) ? type : null;
    }
}