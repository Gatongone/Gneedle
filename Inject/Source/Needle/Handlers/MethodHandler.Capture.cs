using System.Globalization;
using System.Reflection;

namespace Gneedle.Inject;

partial class MethodHandler
{
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
    /// <exception cref="WeavingException">Thrown when the template captured a value which cannot be written.</exception>
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
                throw new WeavingException(string.Format(ErrorMessages.TEMPLATE_CAPTURE_CANNOT_BE_WRITTEN, field.Name, field.FieldType.FullName, Source.FullName));
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
            throw new WeavingException(string.Format(ErrorMessages.TEMPLATE_CAPTURE_CANNOT_BE_WRITTEN, fields[fields.Count - 1].Name, fields[fields.Count - 1].FieldType.FullName, Source.FullName));
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
    /// <exception cref="WeavingException">Thrown when the type is one which the weaver itself declares.</exception>
    private void WriteACapturedType(Type captured, FieldReference field, InstructionFilter filter, int index, int reads)
    {
        // The assembly being woven must not name the weaver: the attributes which the injectors are read from, and the
        // reference to the weaver which they name, are taken out of it once they have been applied, and the token of a
        // type of the weaver would name it again. The type is refused where it is read rather than written, which is
        // where the injector which named it can be found.
        if (captured.Assembly == typeof(Injections).Assembly)
        {
            throw new WeavingException(string.Format(ErrorMessages.TEMPLATE_CAPTURE_NAMES_THE_WEAVER, field.Name, captured.FullName, Source.FullName));
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
}
