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
    /// Name of the generated method which holds the body which the source method is woven around, or null when the
    /// source method is not woven around.
    /// </summary>
    private string? m_ProceedMethodName;

    /// <summary>
    /// The instance which the delegate of the template was made from, whose fields hold the variables which the
    /// template captured, or null when the template was given as a method rather than as the delegate of it.<para/>
    /// A lambda which captures a variable is an instance method of the type which the compiler wrote to hold it, and it
    /// reads what it captured off that instance. The values themselves are held by the delegate, which the weaving is
    /// given while the injector runs, so what the template captured is written where the template read it.
    /// </summary>
    private object? m_TemplateClosure;

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
        Source.Body = new MethodBody(Source);
        var il = Source.Body.GetILProcessor();

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
    public static void SetBody(IMethodHandler handler, Delegate template)
        => SetBody(handler, template.Method, template.Target);

    /// <summary>
    /// Set the body of the method from a template which was given as the method alone, so that what the template
    /// captured, if it captured anything, is held by nothing.
    /// </summary>
    /// <param name="handler">The handler of the method.</param>
    /// <param name="method">The template which holds the body to copy.</param>
    /// <param name="closure">The instance which holds what the template captured, or null when there is none.</param>
    public static void SetBody(IMethodHandler handler, MethodInfo method, object? closure)
    {
        if (closure != null && handler is MethodHandler concrete)
        {
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
    public static void AroundBody(IMethodHandler handler, Delegate template)
        => AroundBody(handler, template.Method, template.Target);

    /// <summary>
    /// Set the body of the method to run around the one which it holds, from a template which was given as the method
    /// alone, so that what the template captured, if it captured anything, is held by nothing.
    /// </summary>
    /// <param name="handler">The handler of the method.</param>
    /// <param name="method">The template which holds the body to weave around.</param>
    /// <param name="closure">The instance which holds what the template captured, or null when there is none.</param>
    public static void AroundBody(IMethodHandler handler, MethodInfo method, object? closure)
    {
        if (closure != null && handler is MethodHandler concrete)
        {
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
        var targetDef = Source.Module.ImportReference(method).Resolve();
        var instructions = targetDef.Body.Instructions;

        // Create a new body.
        Source.Body = new MethodBody(Source);

        // Copy target method variables to source.
        CopyVariables(targetDef, Source);

        m_TemplateClosure = closure;
        ParseBody(instructions, targetDef);
        m_TemplateClosure = null;

        ParseReturnType(targetDef.ReturnType);
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
        var templateDef = Source.Module.ImportReference(method).Resolve();

        // Every check runs before anything is changed, so that a weave which cannot be done leaves the method as it was.
        if (Source.IsAbstract || Source.IsPInvokeImpl || Source.IsRuntime || Source.IsInternalCall || !Source.HasBody)
        {
            throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_TARGET_HAS_NO_BODY, Source.FullName));
        }

        if (Source.IsConstructor) throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_TARGET_IS_CONSTRUCTOR, Source.FullName));
        if (m_ProceedMethodName != null) throw new ArgumentException(string.Format(ErrorMessages.AROUND_BODY_ALREADY_SET, Source.FullName));

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
            var copy = new GenericParameter(genericParameter.Name, generated) { Attributes = genericParameter.Attributes };
            foreach (var constraint in genericParameter.Constraints)
            {
                copy.Constraints.Add(new GenericParameterConstraint(constraint.ConstraintType));
            }

            generated.GenericParameters.Add(copy);
        }

        Source.DeclaringType.Methods.Add(generated);

        MoveBodyTo(generated, Source);
        Source.Body         = new MethodBody(Source);
        m_ProceedMethodName = name;

        // The template is parsed last, so that Proceed resolves through the generated method, which is on the declaring
        // type by now. The return type is deliberately left alone: the template keeps the signature of the method, which
        // was checked above, so parsing it as SetBody does would only write an equal type again.
        CopyVariables(templateDef, Source);
        m_TemplateClosure = closure;
        ParseBody(templateDef.Body.Instructions, templateDef);
        m_TemplateClosure = null;
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
                CatchType    = handler.CatchType == null
                    ? null
                    : Source.Module.ImportReference(handler.CatchType).ParseGenericTokens(Source, Source.Module)
            };

            // A region which begins at nothing would protect nothing, and the two boundaries at the end of it stand for
            // the end of the body, which is written by leaving them out rather than by naming an instruction.
            if (carried.TryStart == null || carried.HandlerStart == null)
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
        var fields  = new List<FieldReference>();
        var readIndex = index + 1;
        while (readIndex < instructions.Count
               && instructions[readIndex] is { OpCode.Code: Code.Ldfld, Operand: FieldReference field }
               && TypeName.HasSameName(field.DeclaringType, fields.Count == 0 ? declaringType : fields[fields.Count - 1].FieldType))
        {
            fields.Add(field);
            readIndex++;
        }

        if (fields.Count == 0) return false;

        // What the template captured is read off the instance which the delegate held, which is the instance the
        // template was written in, and off what each field of the chain holds in its turn.
        object? captured = m_TemplateClosure;
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
        if (metadata == MetadataType.ValueType && fieldType.Resolve() is { IsEnum: true } enumDef)
        {
            metadata = enumDef.GetEnumUnderlyingType().MetadataType;
            if (value != null) value = Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture);
        }

        literal = metadata switch
        {
            MetadataType.String when value is string text          => Instruction.Create(OpCodes.Ldstr, text),
            MetadataType.Boolean when value is bool flag           => Instruction.Create(OpCodes.Ldc_I4, flag ? 1 : 0),
            MetadataType.Char when value is char character         => Instruction.Create(OpCodes.Ldc_I4, character),
            MetadataType.SByte when value is sbyte number          => Instruction.Create(OpCodes.Ldc_I4, number),
            MetadataType.Byte when value is byte number            => Instruction.Create(OpCodes.Ldc_I4, number),
            MetadataType.Int16 when value is short number          => Instruction.Create(OpCodes.Ldc_I4, number),
            MetadataType.UInt16 when value is ushort number        => Instruction.Create(OpCodes.Ldc_I4, number),
            MetadataType.Int32 when value is int number            => Instruction.Create(OpCodes.Ldc_I4, number),
            MetadataType.UInt32 when value is uint number          => Instruction.Create(OpCodes.Ldc_I4, unchecked((int) number)),
            MetadataType.Int64 when value is long number           => Instruction.Create(OpCodes.Ldc_I8, number),
            MetadataType.UInt64 when value is ulong number         => Instruction.Create(OpCodes.Ldc_I8, unchecked((long) number)),
            MetadataType.Single when value is float number         => Instruction.Create(OpCodes.Ldc_R4, number),
            MetadataType.Double when value is double number        => Instruction.Create(OpCodes.Ldc_R8, number),
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
                Name     : nameof(This) or nameof(Base) or nameof(Object) or nameof(Static),
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
        if (member.DeclaringType.FullName is not (This.TYPE_NAME or Base.TYPE_NAME or Object.TYPE_NAME or Static.TYPE_NAME)) return;
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
        for (var at = type; at is { IsNested: true }; at = at.DeclaringType)
        {
            if (!at.Name.StartsWith("<", StringComparison.Ordinal)) continue;

            throw new ArgumentException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_METHOD_OF_ITS_OWN, reference, Source.FullName));
        }
    }

    /// <summary>
    /// Parse the member based on the member flags, and replace the instruction with the one to get the member reference.
    /// </summary>
    /// <param name="memberName">Name of the member.</param>
    /// <param name="memberSymbol">Member flags about the member kind and its property.</param>
    /// <param name="currentIndex">Index of the instruction of `ldstr {member_name}`.</param>
    /// <param name="filter"></param>
    /// <param name="targetDef">The method definition being scanned (source of the instructions).</param>
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
    }

    /// <summary>
    /// Parse the field member, and replace the instruction with the one to get the field reference.
    /// </summary>
    /// <param name="from">The source method definition to copy variables from.</param>
    /// <param name="to">The target method definition to copy variables to.</param>
    private static void CopyVariables(MethodDefinition from, MethodDefinition to)
    {
        var srcVariables = from.Body.Variables;
        if (srcVariables == null) return;

        var desVariables = to.Body.Variables;
        to.Body.Variables.Clear();

        for (var i = 0; i < srcVariables.Count; i++)
        {
            // If the variable type holds a generic parameter token which could be parsed from Gneedle.Inject.T_[0-20] or Gneedle.Inject.M_[0-20],
            // then get the actual generic parameter type.
            var typeRef = srcVariables[i].VariableType.ParseGenericTokens(to, to.Module);
            desVariables.Add(new VariableDefinition(typeRef));
        }
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
            This.TYPE_NAME   => MemberSymbols.This,
            Base.TYPE_NAME   => MemberSymbols.Base,
            Object.TYPE_NAME => MemberSymbols.Object,
            Static.TYPE_NAME => MemberSymbols.Static,

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
    /// Try to get the nearest `call` instruction of `ValuableMember.Get` or `ValuableMember.Set` after the start index, and check if it's a `get` or `set` accessor.
    /// </summary>
    /// <param name="bodyInstructions">The instruction collection to search.</param>
    /// <param name="startIndex">The start index to search from.</param>
    /// <param name="isGet">Output whether the accessor is `get` or `set`.</param>
    /// <param name="index">Output the index of the `call` instruction if found.</param>
    /// <returns>True if the `call` instruction of `ValuableMember.Get` or `ValuableMember.Set` is found; otherwise, false.</returns>
    private static bool TryGetNextGetOrSet(IReadOnlyList<Instruction> bodyInstructions, int startIndex, out bool isGet, out int index)
    {
        isGet = false;
        for (var i = startIndex; i < bodyInstructions.Count; i++)
        {
            if (bodyInstructions[i].OpCode != OpCodes.Callvirt || bodyInstructions[i].Operand is not MethodReference
            {
                DeclaringType:
                {
                    Name     : nameof(ValuableMember) or nameof(ValuableMember) + "`1",
                    Namespace: nameof(Gneedle) + "." + nameof(Inject)
                }
            } method) continue;
            if (method.Name.Equals(nameof(ValuableMember.Get))) isGet = true;
            index = i;
            return true;
        }

        index = 0;
        return false;
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
        /// Replace the instruction at index with Nop, which means to skip the instruction.
        /// </summary>
        /// <param name="index"></param>
        public void Skip(int index) => m_Replacements[index] = Instruction.Create(OpCodes.Nop);

        /// <summary>
        /// Replace the instruction at index with the given instruction. The instruction which was replaced and the one
        /// which replaces it are remembered together, so that an instruction which branches to the first is pointed at
        /// the second: what a branch was written to reach is whatever stands where that instruction stood.
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
                m_Operands.Add(target[index], ins);
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
        /// would be a branch to nothing.
        /// </summary>
        /// <param name="source">The source collection which the instructions were applied to.</param>
        private void PointBranches(ICollection<Instruction> source)
        {
            var written = new HashSet<Instruction>(source);

            foreach (var instruction in source)
            {
                // An instruction which the body holds stands where it stood, and one of another body, which a moved
                // body carries, is not read against the instructions of this one.
                if (instruction.Operand is not Instruction target || written.Contains(target)) continue;

                var standing = Emitted(target);
                if (standing != null) instruction.Operand = standing;
            }
        }

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
            else
            {
                source.Add(ins);
                m_Written[index] ??= ins;
            }
        }
    }
}