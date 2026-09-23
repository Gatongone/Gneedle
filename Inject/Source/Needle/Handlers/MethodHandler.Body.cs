using System.Reflection;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Gneedle.Inject;

partial class MethodHandler
{
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
    /// <exception cref="WeavingException">Thrown when the base type or method is not found.</exception>
    private void SetBodyCallFromBase(ILProcessor il)
    {
        // Find the base type's method with the same name and parameter types.
        var baseType = Source.DeclaringType.BaseType;
        if (baseType == null) throw new WeavingException(string.Format(ErrorMessages.INVALID_METHOD, Source.Name));

        // No delegate describes the member which is called, so the value which it hands back names no parameter of it:
        // the member which a body calls from its base is the one which its name and its parameters name. The base is the
        // one which the type being woven declared, so a parameter which the signature of a member of it names is the
        // argument of that instantiation rather than the parameter of the definition alone.
        var baseMethod = DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(
            DeclaringTypeHandler.AssemblyHandler.GetCecilType(baseType).Definition,
            Source.Name, [.. Source.Parameters.Select(p => p.ParameterType)], instance: baseType);
        if (baseMethod == null)
        {
            throw new WeavingException(string.Format(ErrorMessages.INVALID_METHOD, Source.Name));
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
    /// <exception cref="WeavingException">Thrown when the template captured a value and the handler is not one which
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
    /// <exception cref="WeavingException">Thrown when the template captured a value and the handler is not one which
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
                throw new WeavingException(string.Format(ErrorMessages.HANDLER_HOLDS_NO_CAPTURE, handler.GetType().FullName));
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
    /// <exception cref="WeavingException">Thrown when the template captured a value and the handler is not one which
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
    /// <exception cref="WeavingException">Thrown when the template captured a value and the handler is not one which
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
                throw new WeavingException(string.Format(ErrorMessages.HANDLER_HOLDS_NO_CAPTURE, handler.GetType().FullName));
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
    /// <exception cref="WeavingException">Thrown when the method cannot be woven around, or when the template does not match it.</exception>
    public void AroundBody(MethodInfo method) => AroundBody(method, null);

    /// <summary>
    /// Set the body of the method to run around the body which it holds, which the template reaches through
    /// <see cref="Proceed"/>.
    /// </summary>
    /// <param name="method">The template which holds the body to weave around.</param>
    /// <param name="closure">The instance which holds what the template captured, or null when there is none.</param>
    /// <exception cref="WeavingException">Thrown when the method cannot be woven around, or when the template does not match it.</exception>
    internal void AroundBody(MethodInfo method, object? closure)
    {
        var templateDef = DeclaringTypeHandler.AssemblyHandler.ResolveTemplate(method);

        // Every check runs before anything is changed, so that a weave which cannot be done leaves the method as it was.
        if (Source.IsAbstract || Source.IsPInvokeImpl || Source.IsRuntime || Source.IsInternalCall || !Source.HasBody)
        {
            throw new WeavingException(string.Format(ErrorMessages.AROUND_BODY_TARGET_HAS_NO_BODY, Source.FullName));
        }

        if (Source.IsConstructor) throw new WeavingException(string.Format(ErrorMessages.AROUND_BODY_TARGET_IS_CONSTRUCTOR, Source.FullName));
        if (m_ProceedMethod != null) throw new WeavingException(string.Format(ErrorMessages.AROUND_BODY_ALREADY_SET, Source.FullName));

        // The signature of the template is resolved against the method before it is compared, because a generic method is
        // matched through the M_[0-20] tokens of the template rather than through its own generic parameters: a template
        // which takes and returns Gneedle.Inject.M_0 matches a method which takes and returns its first generic
        // parameter. Resolving first is what turns the token into that parameter, and it changes nothing for a method
        // which holds no generic parameter.
        if (templateDef.Parameters.Count != Source.Parameters.Count
            || templateDef.Parameters.Where((parameter, index) => !TypeName.HasSameName(parameter.ParameterType.ParseGenericTokens(Source, Source.Module),
                Source.Parameters[index].ParameterType)).Any())
        {
            throw new WeavingException(string.Format(ErrorMessages.AROUND_BODY_PARAMETERS_MISMATCH, Source.FullName, templateDef.FullName));
        }

        if (!TypeName.HasSameName(templateDef.ReturnType.ParseGenericTokens(Source, Source.Module), Source.ReturnType))
        {
            throw new WeavingException(string.Format(ErrorMessages.AROUND_BODY_RETURN_TYPE_MISMATCH, Source.FullName, templateDef.FullName));
        }

        var name = $"<{Source.Name}>k__Proceed";
        if (Source.DeclaringType.Methods.Any(methodDef => methodDef.Name == name) || Source.DeclaringType.Fields.Any(field => field.Name == name))
        {
            throw new WeavingException(string.Format(ErrorMessages.AROUND_BODY_GENERATED_NAME_OCCUPIED, name));
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
        ModuleLock.DeclareMember(Source.Module, Source.DeclaringType, generated);
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
            throw new WeavingException(string.Format(ErrorMessages.TEMPLATE_HAS_NO_BODY, templateDef.FullName));
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
}
