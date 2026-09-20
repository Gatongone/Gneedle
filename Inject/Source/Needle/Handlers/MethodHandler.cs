using System.Globalization;
using System.Reflection;
using System.Text;
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
    /// Get the string representation of the method body for debugging. It will return the IL code of the method body in source method definition,
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($".method {(Source.Attributes | MethodAttributes.CompilerControlled).ToString().Replace(", ", " ").ToLower()} {Source.FullName}");
        foreach (var item in Source.Body.Instructions)
        {
            sb.AppendLine(item.ToString());
        }

        return sb.ToString();
    }

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
        // the member which a body calls from its base is the one which its name and its parameters name.
        var baseMethod = DeclaringTypeHandler.AssemblyHandler.GetMethodFromType(
            DeclaringTypeHandler.AssemblyHandler.GetCecilType(baseType).Definition,
            Source.Name, Source.Parameters.Select(p => p.ParameterType).ToArray());
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

        il.Emit(baseMethod.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, Source.Module.ImportReference(baseMethod));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Set the method body to throw a <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="il">The IL processor of the source method body.</param>
    private void SetBodyThrowException(ILProcessor il)
    {
        var module = Source.Module;
        var ctor = module.ImportReference(typeof(NotSupportedException).GetConstructor(Type.EmptyTypes));
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
        var woven = ParseIntoABodyOfItsOwn(targetDef, closure);
        ParseReturnType(targetDef.ReturnType);
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
        var instructions = templateDef.Body.Instructions;
        var previous = Source.Body;
        var woven = new MethodBody(Source);

        // Every read and write of the body of the member for as long as the parse runs reaches the new body rather than
        // the one which the member holds, which is what leaves the member as it was when the parse is refused. The
        // parse is the only thing which runs in the meanwhile, so nothing else is written to the body which is left
        // behind: a step which is added to the weave belongs after this call rather than inside it.
        Source.Body = woven;
        try
        {
            // Copy target method variables to source.
            CopyVariables(templateDef, Source, HandleLocals(instructions));

            m_TemplateClosure = closure;
            ParseBody(instructions, templateDef);
        }
        catch
        {
            Source.Body = previous;
            throw;
        }
        finally
        {
            // The closure is dropped whether the parse wrote it or was refused, so that a parse which runs next reads
            // what it is given rather than what a parse which failed was given.
            m_TemplateClosure = null;
        }

        Source.Body = previous;
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
    /// <param name="instructions">The instructions in target method body to parse.</param>
    /// <param name="targetDef">The target method definition to check when parsing instructions.</param>
    private void ParseBody(Mono.Collections.Generic.Collection<Instruction> instructions, MethodDefinition targetDef)
    {
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
            // another argument whenever the two do not agree on belonging to an instance.
            if (instruction.TryGetLdargIndex(!targetDef.IsStatic, out var slot))
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
        filter.ApplyTo(Source.Body.Instructions);

        CarryExceptionHandlers(instructions, targetDef, filter);
    }

    /// <summary>
    /// Write the regions which the template protects into the body which is woven, so that what the template catches,
    /// disposes in a `finally` or takes a `lock` on is protected where it was woven as well.<para/>
    /// The boundaries of a region are instructions of the template, and each of them stands where it stood: what the
    /// weaving wrote for it. The types which are caught are imported, so that a region which catches a type of another
    /// assembly is written against that assembly in the body which is woven, as every other operand is.
    /// </summary>
    /// <param name="instructions">The instructions of the body which was parsed.</param>
    /// <param name="targetDef">The template which the instructions belong to.</param>
    /// <param name="filter">The instruction filter which wrote the body.</param>
    /// <exception cref="InvalidILException">Thrown when a region begins at an instruction which the body does not hold.</exception>
    private void CarryExceptionHandlers(Mono.Collections.Generic.Collection<Instruction> instructions, MethodDefinition targetDef, InstructionFilter filter)
    {
        foreach (var handler in targetDef.Body.ExceptionHandlers)
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
                    : Source.Module.ImportReference(handler.CatchType).ParseGenericTokens(Source, Source.Module)
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
                throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, "$" + instructions.Count));
            }

            Source.Body.ExceptionHandlers.Add(carried);
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
        var parameter = GetParameterAt(slot, templateDef);

        // A macro form carries the slot in the opcode rather than as an operand, so the macro is chosen by the slot
        // which the argument holds in the member being woven, which is not the position of the parameter: the receiver
        // of an instance member takes the slot ahead of the first of them. A slot which no macro holds is loaded
        // through the operand form, which names the parameter itself and leaves the slot of it to be written from the
        // parameter, where the one form of the slot is settled rather than written twice.
        return GetShiftedSlot(slot, templateDef) switch
        {
            0 => Instruction.Create(OpCodes.Ldarg_0),
            1 => Instruction.Create(OpCodes.Ldarg_1),
            2 => Instruction.Create(OpCodes.Ldarg_2),
            3 => Instruction.Create(OpCodes.Ldarg_3),
            _ => Instruction.Create(OpCodes.Ldarg, parameter)
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
    /// <returns>The instruction which loads the receiver.</returns>
    /// <exception cref="ArgumentException">Thrown when the member being woven is static, and therefore holds no receiver
    /// for a member of an instance which the template reached through <c>This</c> or <c>Base</c>.</exception>
    private Instruction CreateReceiver(Instruction? instanceIns, MethodDefinition templateDef)
    {
        if (instanceIns is { } ins && ins.TryGetLdargIndex(!templateDef.IsStatic, out var slot))
        {
            return CreateLdarg(slot, templateDef);
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
        // What the template reads at the slot of its own receiver is that receiver rather than an argument, and no
        // member can be given it: a static member holds no such argument, and an instance one holds another instance in
        // its place. A lambda which captures a variable is an instance method of the type which holds the capture, so a
        // template written as one is a template of this kind.
        if (!templateDef.IsStatic && slot == 0)
        {
            throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_READS_ITS_OWN_INSTANCE, Source.FullName));
        }

        // The two hold the same arguments in the same order, so an argument moves by one slot exactly when one of them
        // belongs to an instance and the other does not: the receiver of the one which does takes the first slot.
        return slot + (Source.IsStatic ? 0 : 1) - (templateDef.IsStatic ? 0 : 1);
    }

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
                var importedMethod = Source.Module.ImportReference(methodRef).ParseGenericTokens(Source, Source.Module);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, importedMethod));
                break;
            // The parameter of the template is matched to the parameter of the same position rather than to the one of
            // the same name: they are the parameters of two different methods, and the name which the template gave its
            // own takes no part in it. The opcode is kept, because the load or store is an instruction of the member
            // being woven, which accounts for its receiver by itself. The forms of ldarg never reach here: each of them
            // is translated by ParseBody, which reaches the macro forms as well.
            case ParameterDefinition parameterDef:
                var parameter = GetParameterAt(parameterDef.Index, targetDef);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, parameter));
                break;
            // The type of the field may hold generic parameter tokens, just like List<Gneedle.Inject.T_0>::SomeField.
            // It may also hold a declaring type which stands for the type of another assembly, which is replaced below.
            case FieldReference fieldRef:
                RefuseTheCompilersOwnType(fieldRef.DeclaringType, fieldRef.FullName);
                var importedField = Source.Module.ImportReference(fieldRef).ParseGenericTokens(Source, Source.Module);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, importedField));
                break;
            // We need to find the variable with the same index in source method definition, and replace the operand with it.
            case VariableDefinition varDef:
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, Source.Body.Variables[varDef.Index]));
                break;
            // The type may hold generic parameter tokens itself, just like box Gneedle.Inject.T_0.
            case TypeReference typeRef:
                RefuseTheCompilersOwnType(typeRef, typeRef.FullName);
                filter.Replace(currentIndex, Instruction.Create(currentIns.OpCode, typeRef.ParseGenericTokens(Source, Source.Module)));
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
    /// Refuse a reference into a type which the compiler wrote for a body of the template's own.<para/>
    /// A lambda, a local function, an async body and an iterator body are each a method of a type which the compiler
    /// writes beside the template, and which is nested inside what declares it with a name the compiler writes. The
    /// instructions of the template are carried, but such a method is not: the woven member would reach into the
    /// assembly the template was compiled into, at a type which is private to it, and fail when it ran rather than here.
    /// </summary>
    /// <param name="type">The type which the reference names, or which declares the member it names.</param>
    /// <param name="reference">The reference itself, which the message names.</param>
    /// <exception cref="ArgumentException">Thrown when the reference reaches a type which the compiler wrote.</exception>
    private void RefuseTheCompilersOwnType(TypeReference? type, string reference)
    {
        for (var at = type; at is {IsNested: true}; at = at.DeclaringType)
        {
            if (!at.Name.StartsWith("<", StringComparison.Ordinal)) continue;

            throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_METHOD_OF_ITS_OWN, reference, Source.FullName));
        }
    }

    /// <summary>
    /// Refuse a member which the compiler wrote for a body of the template's own.<para/>
    /// A local function which captured nothing is not a type of its own the way a lambda is: it is a method of the type
    /// which declares the template, named with the bracket which no identifier of C# holds. Its body is a body of the
    /// template's, so carrying the call to it carries a call into a member which the weaving has no instructions of.
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
    /// Parse the field member, and replace the instruction with the one to get the field reference.
    /// </summary>
    /// <param name="from">The source method definition to copy variables from.</param>
    /// <param name="to">The target method definition to copy variables to.</param>
    /// <param name="emptied">Index of the locals which the weaving empties, which are the ones which <see cref="HandleLocals"/> names.</param>
    private static void CopyVariables(MethodDefinition from, MethodDefinition to, ISet<int> emptied)
    {
        var srcVariables = from.Body.Variables;
        if (srcVariables == null) return;

        var desVariables = to.Body.Variables;
        to.Body.Variables.Clear();

        for (var i = 0; i < srcVariables.Count; i++)
        {
            // A local which the weaving empties is one which names nothing of the body which is woven, while the type it
            // is declared with is one of the weaver: copying that type would leave the assembly being woven referring to
            // the weaver for a type which nothing of it names, so the local is copied as a type which every assembly
            // holds instead.
            // If the variable type holds a generic parameter token which could be parsed from Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20],
            // then get the actual generic parameter type.
            var typeRef = emptied.Contains(i)
                ? to.Module.TypeSystem.Object
                : srcVariables[i].VariableType.ParseGenericTokens(to, to.Module);
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

            if (HeldLocal(bodyInstructions, index) is { } handle) locals.Add(handle.Local);
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
    /// Try to get the accessor of the placeholder which is written where the value was pushed, which is the call of
    /// `ValuableMember.Get` or `ValuableMember.Set` that the value is the receiver of.<para/>
    /// The accessor of another placeholder may stand between the two, and it is the one which its own value is the
    /// receiver of: the values which the instructions between push and take off the stack are counted, and the accessor
    /// which is looked for is the one which is reached with exactly as many values above the placeholder's own as it
    /// takes arguments. An instruction whose count the walk cannot tell ends it, and what the first accessor of the body
    /// is stands for the one which was looked for.
    /// </summary>
    /// <param name="bodyInstructions">The instruction collection to search.</param>
    /// <param name="startIndex">The index of the instruction which follows the call which pushed the value.</param>
    /// <param name="isGet">Output whether the accessor is `get` or `set`.</param>
    /// <param name="index">Output the index of the `call` instruction if found.</param>
    /// <returns>True if the `call` instruction of `ValuableMember.Get` or `ValuableMember.Set` is found; otherwise, false.</returns>
    private static bool TryGetNextGetOrSet(IReadOnlyList<Instruction> bodyInstructions, int startIndex, out bool isGet, out int index)
    {
        if (TryWalkToTheAccessor(bodyInstructions, startIndex, out isGet, out index)) return true;

        return TryGetFirstGetOrSet(bodyInstructions, startIndex, out isGet, out index);
    }

    /// <summary>
    /// Try to get the accessor which the value pushed ahead of the start index is the receiver of, which is the call of
    /// `ValuableMember.Get` or `ValuableMember.Set` that the value stands under.<para/>
    /// The accessor of another value may stand between the two, and it is the one which its own value is the receiver
    /// of: the values which the instructions between push and take off the stack are counted, and the accessor which is
    /// looked for is the one which is reached with exactly as many values above the value which is looked for as it
    /// takes arguments. An instruction whose count the walk cannot tell, or one which takes the value itself off the
    /// stack, ends it and nothing is answered.
    /// </summary>
    /// <param name="bodyInstructions">The instruction collection to search.</param>
    /// <param name="startIndex">The index of the instruction which follows the one which pushed the value.</param>
    /// <param name="isGet">Output whether the accessor is `get` or `set`.</param>
    /// <param name="index">Output the index of the `call` instruction if found.</param>
    /// <returns>True if the `call` instruction of `ValuableMember.Get` or `ValuableMember.Set` is found; otherwise, false.</returns>
    private static bool TryWalkToTheAccessor(IReadOnlyList<Instruction> bodyInstructions, int startIndex, out bool isGet, out int index)
    {
        // How many values stand on the stack above the one which the placeholder pushed. Every push counts up and every
        // take counts down, and a count below zero is one which took the placeholder's value itself off the stack.
        var above = 0;
        for (var i = startIndex; i < bodyInstructions.Count; i++)
        {
            if (IsAnAccessor(bodyInstructions[i], out var accessorIsGet))
            {
                if (above == (accessorIsGet ? 0 : 1))
                {
                    isGet = accessorIsGet;
                    index = i;
                    return true;
                }

                // The accessor of a value which is not this one: it takes the receiver off the stack and leaves the
                // value which it reads there, or nothing at all where it writes one.
                above += accessorIsGet ? 0 : -2;
                continue;
            }

            if (StackDelta(bodyInstructions[i]) is not { } delta || above + delta < 0) break;
            above += delta;
        }

        isGet = false;
        index = 0;
        return false;
    }

    /// <summary>
    /// Every accessor of the value member which a local holds the handle of, which is each read of the local together
    /// with the accessor which that read is the receiver of.<para/>
    /// The handle which a placeholder stands for is a value which only the weaving can write, so a local which holds one
    /// stands for the member wherever it is read and for nothing else: a read which is something other than the receiver
    /// of an accessor, or a second write to the local, is a use which nothing can be written for, and nothing is
    /// answered for it.
    /// </summary>
    /// <param name="bodyInstructions">The instructions of the body which is parsed.</param>
    /// <param name="local">Index of the local which holds the handle.</param>
    /// <returns>Index of every read of the local, of the accessor which it is the receiver of and of whether that
    /// accessor reads the member or writes it, or null when the local stands for more than the member.</returns>
    private static List<(int Read, int Accessor, bool IsGet)>? AccessorsOfAHeldHandle(IReadOnlyList<Instruction> bodyInstructions, int local)
    {
        var accessors = new List<(int Read, int Accessor, bool IsGet)>();
        var stores = 0;

        for (var i = 0; i < bodyInstructions.Count; i++)
        {
            var ins = bodyInstructions[i];
            if (ins.TryGetStlocIndex(out var written) && written == local)
            {
                stores++;
                continue;
            }

            if (!ins.TryGetLdlocIndex(out var read) || read != local)
            {
                // A form which names the local without loading it, such as the address which `ldloca` takes, is a use
                // which the accessors which are read here say nothing about.
                if (ins.Operand is VariableReference variable && variable.Index == local) return null;

                continue;
            }

            if (!TryWalkToTheAccessor(bodyInstructions, i + 1, out var isGet, out var accessor)) return null;

            accessors.Add((i, accessor, isGet));
        }

        return stores == 1 ? accessors : null;
    }

    /// <summary>
    /// The first accessor of the body after the start index, which is what the value of a placeholder stood for before
    /// the accessors were told apart from each other.
    /// </summary>
    /// <param name="bodyInstructions">The instruction collection to search.</param>
    /// <param name="startIndex">The start index to search from.</param>
    /// <param name="isGet">Output whether the accessor is `get` or `set`.</param>
    /// <param name="index">Output the index of the `call` instruction if found.</param>
    /// <returns>True if an accessor is found; otherwise, false.</returns>
    private static bool TryGetFirstGetOrSet(IReadOnlyList<Instruction> bodyInstructions, int startIndex, out bool isGet, out int index)
    {
        isGet = false;
        for (var i = startIndex; i < bodyInstructions.Count; i++)
        {
            if (!IsAnAccessor(bodyInstructions[i], out var accessorIsGet)) continue;

            isGet = accessorIsGet;
            index = i;
            return true;
        }

        index = 0;
        return false;
    }

    /// <summary>
    /// Whether an instruction is the call of an accessor of a value member, which is what reads or writes the field or
    /// the property which a placeholder stands for.
    /// </summary>
    /// <param name="instruction">The instruction which is read.</param>
    /// <param name="isGet">Output whether the accessor reads the member rather than writing it.</param>
    /// <returns>Whether the instruction is such a call.</returns>
    private static bool IsAnAccessor(Instruction instruction, out bool isGet)
    {
        isGet = false;
        if (instruction.OpCode != OpCodes.Callvirt || instruction.Operand is not MethodReference
        {
            DeclaringType:
            {
                Name     : nameof(ValuableMember) or nameof(ValuableMember) + "`1",
                Namespace: nameof(Gneedle) + "." + nameof(Inject)
            }
        } method) return false;

        if (method.Name is not (nameof(ValuableMember.Get) or nameof(ValuableMember.Set))) return false;

        isGet = method.Name.Equals(nameof(ValuableMember.Get));
        return true;
    }

    /// <summary>
    /// The values which an instruction takes off the stack and the values it leaves on it, or null when the walk cannot
    /// tell.<para/>
    /// The two are what tells the accessor of a placeholder from the accessor of one which is written inside the
    /// expression of it, and what tells an expression which is written beside a delegate from the call of it: a value
    /// which a member is handed is taken off the stack where the call of it is reached, and what the call leaves in its
    /// place is a value of its own rather than the one it was handed. They are read off the instruction alone rather
    /// than off the member it names where the instruction carries one, which is what lets them be carried over a member
    /// the assembly being woven cannot resolve.
    /// </summary>
    /// <param name="instruction">The instruction which is counted.</param>
    /// <returns>The values which the instruction takes and the values it leaves, or null when it is not one which the
    /// walk reads.</returns>
    private static (int Taken, int Left)? StackEffect(Instruction instruction)
    {
        var code = instruction.OpCode.Code;
        switch (code)
        {
            // The loads, which push a value of their own rather than one which the stack held already, and the field of
            // no instance among them, which is read off the type rather than off a value.
            case Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg or Code.Ldarg_S
              or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc or Code.Ldloc_S
              or Code.Ldarga or Code.Ldarga_S or Code.Ldloca or Code.Ldloca_S
              or Code.Ldc_I4_M1 or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or Code.Ldc_I4_3 or Code.Ldc_I4_4
              or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or Code.Ldc_I4 or Code.Ldc_I4_S
              or Code.Ldc_I8 or Code.Ldc_R4 or Code.Ldc_R8 or Code.Ldstr or Code.Ldnull or Code.Ldftn or Code.Ldtoken
              or Code.Ldsfld or Code.Ldsflda or Code.Sizeof:
                return (0, 1);

            // The loads which read what they are handed, which is the value a field is read off, the address one is read
            // through, and the array or the element which stands at it: what each of them leaves stands in the place of
            // the value it took rather than above it.
            case Code.Ldfld or Code.Ldflda or Code.Ldobj or Code.Ldlen
              or Code.Ldind_I1 or Code.Ldind_I2 or Code.Ldind_I4 or Code.Ldind_I8 or Code.Ldind_I or Code.Ldind_R4
              or Code.Ldind_R8 or Code.Ldind_Ref or Code.Ldind_U1 or Code.Ldind_U2 or Code.Ldind_U4:
                return (1, 1);

            // The stores which take what they write and nothing else, and the pop, which takes one value.
            case Code.Starg or Code.Starg_S or Code.Stloc or Code.Stloc_S or Code.Stloc_0 or Code.Stloc_1
              or Code.Stloc_2 or Code.Stloc_3 or Code.Stsfld or Code.Pop:
                return (1, 0);

            // The stores which take where they write as well as what they write, which is the receiver of a field, the
            // address of a value, and the address of an element of an array or of an element of an array of addresses.
            case Code.Stfld or Code.Stobj
                            or Code.Stind_I or Code.Stind_I1 or Code.Stind_I2 or Code.Stind_I4 or Code.Stind_I8 or Code.Stind_R4
                            or Code.Stind_R8 or Code.Stind_Ref:
                return (2, 0);

            // The instructions which leave what they were handed, of another type.
            case Code.Conv_I1 or Code.Conv_I2 or Code.Conv_I4 or Code.Conv_I8 or Code.Conv_Ovf_I1 or Code.Conv_Ovf_I2
              or Code.Conv_Ovf_I4 or Code.Conv_Ovf_I8 or Code.Conv_Ovf_U1 or Code.Conv_Ovf_U2 or Code.Conv_Ovf_U4
              or Code.Conv_Ovf_U8 or Code.Conv_Ovf_I_Un or Code.Conv_Ovf_U_Un or Code.Conv_R4 or Code.Conv_R8
              or Code.Conv_R_Un or Code.Conv_U1 or Code.Conv_U2 or Code.Conv_U4 or Code.Conv_U8
              or Code.Conv_I or Code.Conv_U or Code.Neg or Code.Not
              or Code.Box or Code.Unbox or Code.Unbox_Any or Code.Castclass or Code.Isinst or Code.Ckfinite:
                return (1, 1);

            // The instructions which take two values and leave one.
            case Code.Add or Code.Sub or Code.Mul or Code.Div or Code.Div_Un or Code.Rem or Code.Rem_Un
              or Code.And or Code.Or or Code.Xor or Code.Shl or Code.Shr or Code.Shr_Un
              or Code.Ceq or Code.Cgt or Code.Cgt_Un or Code.Clt or Code.Clt_Un
              or Code.Ldelem_Any or Code.Ldelem_I or Code.Ldelem_I1 or Code.Ldelem_I2 or Code.Ldelem_I4
              or Code.Ldelem_I8 or Code.Ldelem_R4 or Code.Ldelem_R8 or Code.Ldelem_Ref or Code.Ldelem_U1
              or Code.Ldelem_U2 or Code.Ldelem_U4:
                return (2, 1);

            // The instructions which take three values and leave none.
            case Code.Stelem_Any or Code.Stelem_I or Code.Stelem_I1 or Code.Stelem_I2 or Code.Stelem_I4
              or Code.Stelem_I8 or Code.Stelem_R4 or Code.Stelem_R8 or Code.Stelem_Ref:
                return (3, 0);

            // The instruction which takes the length of an array off the stack and leaves the array in its place.
            case Code.Newarr:
                return (1, 1);

            // The instruction which leaves the value it was handed where it was and one more above it.
            case Code.Dup:
                return (0, 1);

            // A call takes the arguments which the reference names, which the signature counts without resolving the
            // member they are named on, and leaves what it hands back.
            case Code.Call or Code.Callvirt or Code.Newobj when instruction.Operand is MethodReference method:
                var taken = method.Parameters.Count + (method.HasThis && code != Code.Newobj ? 1 : 0);
                var left = code == Code.Newobj || method.ReturnType.MetadataType != MetadataType.Void ? 1 : 0;
                return (taken, left);

            default:
                return null;
        }
    }

    /// <summary>
    /// The number of values which an instruction leaves on the stack, counted against the number it takes off it, or
    /// null when the walk cannot tell.<para/>
    /// The count is what tells the value which a placeholder was built around from the instructions which stand before
    /// it, which are walked from the last of them back to the first: what an instruction leaves stands above what it
    /// was handed rather than in the place of it where the walk goes backwards, so the two are counted against each
    /// other rather than apart.
    /// </summary>
    /// <param name="instruction">The instruction which is counted.</param>
    /// <returns>The count, or null when the instruction is not one which the walk reads.</returns>
    private static int? StackDelta(Instruction instruction)
        => StackEffect(instruction) is { } effect ? effect.Left - effect.Taken : null;

    /// <summary>
    /// The value which an instance of <see cref="Instance"/> was built around, which is what the member the placeholder
    /// names is reached through.
    /// </summary>
    /// <param name="first">Index of the first instruction of the value.</param>
    /// <param name="last">Index of the last instruction of the value, which leaves it on the stack.</param>
    /// <param name="load">The instruction which loads the value where the template named an argument of its own, or null
    /// where the template computed the value instead.</param>
    private readonly struct InstanceValue(int first, int last, Instruction? load)
    {
        /// <summary>Index of the first instruction of the value.</summary>
        public readonly int First = first;

        /// <summary>Index of the last instruction of the value, which leaves it on the stack.</summary>
        public readonly int Last = last;

        /// <summary>The instruction which loads the value, or null where the template computed it.</summary>
        public readonly Instruction? Load = load;
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
    private static bool TryGetInstanceValue(InstructionFilter filter, int nameIndex, MethodDefinition targetDef, out InstanceValue instance)
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
            if (StackDelta(instruction) is not { } delta) return false;

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
            instance = new InstanceValue(index, nameIndex - 3,
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

        return TryGetStackType(instruction, targetDef, out var type) ? type : null;
    }

    /// <summary>
    /// The instruction filter to apply instruction translations. It holds the target collection of instructions and the translation results,
    /// and applies the translations to source collection when calling ApplyTo.
    /// </summary>
    /// <param name="target">The target collection of instructions to apply translations.</param>
    private class InstructionFilter(Mono.Collections.Generic.Collection<Instruction> target)
    {
        /// <summary>
        /// The instruction to replace the original instruction at index. If null, it means to keep the original instruction.
        /// </summary>
        private readonly Instruction?[] m_Replacements = new Instruction[target.Count];

        /// <summary>
        /// The instructions to insert before the original instruction at index, in the order they were inserted. If
        /// null, it means no instruction to insert.
        /// </summary>
        private readonly List<Instruction>?[] m_Insert = new List<Instruction>?[target.Count];

        /// <summary>
        /// The instruction which was written for each instruction of the target, at the same index, or null for one
        /// which was written as nothing at all.<para/>
        /// Where an instruction was written as more than one, this is the first of them, which is where a region or a
        /// branch which named that instruction begins. Where it was written as none, what stands in its place is the
        /// first instruction written after it, which is what <see cref="Emitted"/> answers with.
        /// </summary>
        private readonly Instruction?[] m_Written = new Instruction?[target.Count];

        /// <summary>
        /// Key: the instruction with non-imported operand.
        /// Value: the instruction with re-imported operand.
        /// </summary>
        private readonly Dictionary<Instruction, Instruction> m_Operands = new();

        /// <summary>
        /// The target collection of instructions to apply translations.
        /// The instruction at index in target collection will be replaced with the instruction in m_Replacements at the same index if there is one,
        /// or will be added to source collection directly if there is no replacement instruction.
        /// </summary>
        public readonly Instruction[] Target = target.ToArray();

        /// <summary>
        /// Check if there is a replacement instruction for the instruction at index.
        /// If true, it means the instruction at index will be replaced with another instruction, and the original instruction will be skipped.
        /// </summary>
        /// <param name="index">Index of the instruction in target collection.</param>
        /// <returns></returns>
        public bool HasReplaced(int index) => m_Replacements[index] != null;

        /// <summary>
        /// The instruction which was written in place of the one at index, or null when the instruction at index is the
        /// one which stands in the body. An instruction which is written as nothing is written as a nop, which is what
        /// the weaving reads for one the body does not hold.
        /// </summary>
        /// <param name="index">Index of the instruction in target collection.</param>
        public Instruction? Replaced(int index) => m_Replacements[index];

        /// <summary>
        /// Replace the instruction at index with Nop, which means to skip the instruction.
        /// </summary>
        /// <param name="index"></param>
        public void Skip(int index) => m_Replacements[index] = Instruction.Create(OpCodes.Nop);

        /// <summary>
        /// Replace the instruction at index with the given instruction. The instruction which was replaced and the one
        /// which replaces it are remembered together, so that an instruction which branches to the first is pointed at
        /// the second: what a branch was written to reach is whatever stands where that instruction stood.<para/>
        /// The replacement which stands is the one which was written last, which is the one the collection of the
        /// replacements holds as well: an instruction which is replaced twice is written over rather than refused,
        /// so that the two answers say the same thing about it.
        /// </summary>
        /// <param name="index">Index of the instruction to be replaced.</param>
        /// <param name="ins">The instruction to replace with.</param>
        public void Replace(int index, Instruction ins)
        {
            m_Replacements[index] = ins;

            // An instruction which is replaced with a nop is one which the body does not hold at all, and a branch to
            // it is pointed at what stands after it instead, which is what ApplyTo does once the body is written.
            if (ins.OpCode != OpCodes.Nop)
            {
                m_Operands[target[index]] = ins;
            }
        }

        /// <summary>
        /// Insert the given instruction before the instruction at index. If there are multiple instructions to insert before the same index, they will be added in order of insertion.
        /// </summary>
        /// <param name="index">Index of the instruction to insert before.</param>
        /// <param name="ins"> The instruction to insert.</param>
        public void Insert(int index, Instruction ins) => (m_Insert[index] ??= []).Add(ins);

        /// <summary>
        /// The instruction which stands where the given instruction of the template stood, which is the first one
        /// written for it, or the first written after it where nothing was written for it at all.
        /// </summary>
        /// <param name="instruction">The instruction of the template which is asked about, or null for a boundary which
        /// the template does not hold, which stands for the end of the body.</param>
        /// <returns>The instruction of the body which stands where that one stood, or null when there is none.</returns>
        public Instruction? Emitted(Instruction? instruction)
        {
            if (instruction == null) return null;

            for (var index = Array.IndexOf(Target, instruction); index >= 0 && index < m_Written.Length; index++)
            {
                if (m_Written[index] != null) return m_Written[index];
            }

            return null;
        }

        /// <summary>
        /// Apply the instruction translations to source collection. For each instruction in target collection,
        /// if there is an instruction to insert before it, add the instruction to source first.
        /// Then if there is a replacement instruction, add it to source. Otherwise, add the original instruction to source,
        /// but replace its operand with the one re-imported if it exists in m_Operands.
        /// </summary>
        /// <param name="source">The source collection to apply instructions.</param>
        public void ApplyTo(ICollection<Instruction> source)
        {
            for (var index = 0; index < target.Count; index++)
            {
                AddInsertInstruction(source, index);
                ReplaceOrAddInstruction(source, index);
            }

            PointBranches(source);
        }

        /// <summary>
        /// Point every branch which names an instruction of the template at the instruction which stands where that one
        /// stood, for the ones which were written as nothing: a branch to an instruction which the body does not hold
        /// would be a branch to nothing. The table of a switch is a branch of many entries, and each of its entries is
        /// pointed at what stands where it stood as well.
        /// </summary>
        /// <param name="source">The source collection which the instructions were applied to.</param>
        /// <exception cref="InvalidILException">Thrown when an instruction which a branch names stands for nothing in the
        /// body which was written, which leaves the branch reaching into the template rather than into the body.</exception>
        private void PointBranches(ICollection<Instruction> source)
        {
            var written = new HashSet<Instruction>(source);

            foreach (var instruction in source)
            {
                // The entries of a table are named by the switch which stands in front of them rather than by a branch,
                // and each of them is carried the same way. The table which is written is one of the body's own rather
                // than the table of the template, which another weave of the same template reads again.
                if (instruction.Operand is Instruction[] table)
                {
                    instruction.Operand = table.Select(entry => written.Contains(entry) ? entry : Standing(entry)).ToArray();
                    continue;
                }

                // An instruction which the body holds stands where it stood, and one of another body, which a moved
                // body carries, is not read against the instructions of this one.
                if (instruction.Operand is not Instruction ins || written.Contains(ins)) continue;

                instruction.Operand = Standing(ins);
            }
        }

        /// <summary>
        /// The instruction of the body which stands where the given instruction of the template stood, which is where a
        /// branch or an entry of a table which names it is pointed: an instruction which nothing stands for leaves what
        /// names it reaching out of the body it is written in, which is not IL the runtime accepts.
        /// </summary>
        /// <param name="ins">The instruction of the template which is named.</param>
        /// <returns>The instruction of the body which stands where it stood.</returns>
        /// <exception cref="InvalidILException">Thrown when nothing stands for the instruction.</exception>
        private Instruction Standing(Instruction ins)
            => Emitted(ins) ?? throw new InvalidILException(string.Format(ErrorMessages.INVALID_IL, "$" + Array.IndexOf(Target, ins)));

        /// <summary>
        /// If there are instructions to insert before current index, add them to source in the order they were
        /// inserted, which is the order they are read in: the receiver of a call before the arguments of it.
        /// </summary>
        /// <param name="source">The source collection to add instructions.</param>
        /// <param name="index">Index of the instruction in target collection.</param>
        private void AddInsertInstruction(ICollection<Instruction> source, int index)
        {
            if (m_Insert[index] is not { } insert) return;

            foreach (var instruction in insert)
            {
                source.Add(instruction);
                m_Written[index] ??= instruction;
            }
        }

        /// <summary>
        /// If there is a replacement instruction, add it to source. Otherwise, add the original instruction to source, but replace its operand with the one re-imported if it exists in m_Operands.
        /// </summary>
        /// <param name="source">The source collection to add instruction.</param>
        /// <param name="index">Index of the instruction in target collection.</param>
        private void ReplaceOrAddInstruction(ICollection<Instruction> source, int index)
        {
            var replacement = m_Replacements[index];
            if (replacement != null)
            {
                // Seems we don't need a nop OPCode. Do we?
                if (replacement.OpCode != OpCodes.Nop)
                {
                    source.Add(replacement);
                    m_Written[index] ??= replacement;
                }
            }
            else
            {
                AddOriginalInstruction(source, index);
            }
        }

        /// <summary>
        /// Add the original instruction to source, but replace its operand with the one re-imported if it exists in m_Operands.
        /// </summary>
        /// <param name="source">The source collection to add instruction.</param>
        /// <param name="index">Index of the instruction in target collection.</param>
        private void AddOriginalInstruction(ICollection<Instruction> source, int index)
        {
            var ins = target[index]!;
            // The operand may be re-imported, so we need to build a new one.
            if (ins.Operand is Instruction oldIns && m_Operands.TryGetValue(oldIns, out var newIns))
            {
                var pointer = Instruction.Create(ins.OpCode, newIns);
                source.Add(pointer);
                m_Written[index] ??= pointer;
            }
            // The table of a switch is the operand which the weaving writes again rather than the instruction, and the
            // instruction which carries it is the one of the template: a template which is woven a second time reads the
            // table which the first weave carried, which names the instructions of the body of that one. The body is
            // given a switch of its own, over a table of its own, so that the template keeps what it wrote.
            else if (ins.Operand is Instruction[] table)
            {
                var pointer = Instruction.Create(ins.OpCode, (Instruction[]) table.Clone());
                source.Add(pointer);
                m_Written[index] ??= pointer;
            }
            else
            {
                source.Add(ins);
                m_Written[index] ??= ins;
            }
        }
    }
}