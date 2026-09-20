using System.Text;

namespace Gneedle.Inject;

/// <summary>
/// The text of a member, of a type and of an assembly: the declaration of what is written out and the IL of the body
/// which it holds, each line indented by the number of levels which the member it stands in is written at.
/// </summary>
internal static class IlPrinter
{
    /// <summary>
    /// The width of one level of the indentation which the text of a handler is written with.
    /// </summary>
    internal const int Indentation = 4;

    /// <summary>
    /// Get the string representation of the method, which is the declaration of it and the IL of the body which it
    /// holds.
    /// </summary>
    /// <param name="method">The method which is written out.</param>
    /// <returns>The declaration of the method, and the IL of its body.</returns>
    internal static string Print(MethodDefinition method) => Print(method, 0);

    /// <summary>
    /// The same, written at the given number of levels of indentation, which is what a member of a type is written at.
    /// </summary>
    /// <param name="method">The method which is written out.</param>
    /// <param name="level">The number of levels of indentation which the declaration stands at.</param>
    /// <returns>The declaration of the method, and the IL of its body, each line indented by that many levels.</returns>
    internal static string Print(MethodDefinition method, int level)
    {
        var declaration = new string(' ', level * Indentation);
        var line        = new string(' ', (level + 1) * Indentation);
        var text        = new StringBuilder();

        text.Append(declaration).Append(".method ").Append(TheAttributesOf(method)).Append(' ')
            .Append(method.ReturnType.FullName).Append(' ')
            .Append(method.DeclaringType.FullName).Append("::").Append(method.Name)
            .Append('(').Append(string.Join(", ", method.Parameters.Select(parameter => TheNameOf(parameter.ParameterType, parameter.Name)))).Append(')')
            .AppendLine();

        text.Append(declaration).AppendLine("{");

        if (!method.HasBody)
        {
            text.Append(line).AppendLine("// The member declares no body.");
            text.Append(declaration).AppendLine("}");
            return text.ToString();
        }

        var body = method.Body;

        if (body.HasVariables)
        {
            text.Append(line).Append(".locals init (").Append(string.Join(", ", body.Variables.Select(variable => $"{variable.VariableType.FullName} {variable}")))
                .AppendLine(")");
            text.AppendLine();
        }

        // The offsets are computed here rather than read off the instructions, because the offset of an instruction is
        // the one it stands at where the body is written, and a body which was woven holds the offsets of the template
        // it was written from, or none at all where the weaving built the instruction.
        var labels = TheLabelsOf(body.Instructions);
        foreach (var instruction in body.Instructions)
        {
            text.Append(line).Append(labels[instruction]).Append(": ").Append(instruction.OpCode.Name);
            switch (instruction.Operand)
            {
                case null:
                    break;
                case Instruction target:
                    text.Append(' ').Append(labels[target]);
                    break;
                case Instruction[] targets:
                    text.Append(" (").Append(string.Join(", ", targets.Select(target => labels[target]))).Append(')');
                    break;
                case string literal:
                    text.Append(" \"").Append(literal).Append('"');
                    break;
                default:
                    text.Append(' ').Append(instruction.Operand);
                    break;
            }

            text.AppendLine();
        }

        // A region is not a shape which the instructions hold, so the handlers are written where the instructions end:
        // each names the range it covers rather than holding the instructions of it.
        if (body.HasExceptionHandlers)
        {
            text.AppendLine();

            foreach (var handler in body.ExceptionHandlers)
            {
                text.Append(line).Append(".try ").Append(TheRangeOf(labels, handler.TryStart, handler.TryEnd));

                if (handler.HandlerType == ExceptionHandlerType.Filter)
                {
                    text.Append(" filter ").Append(TheRangeOf(labels, handler.FilterStart, handler.HandlerStart));
                }

                text.Append(' ').Append(TheKeywordOf(handler.HandlerType));
                if (handler.CatchType != null)
                {
                    text.Append(' ').Append(handler.CatchType.FullName);
                }

                text.Append(" handler ").Append(TheRangeOf(labels, handler.HandlerStart, handler.HandlerEnd)).AppendLine();
            }
        }

        text.Append(declaration).AppendLine("}");

        return text.ToString();
    }

    /// <summary>
    /// The attributes of a member, as the IL writes them: the names of the flags in lower case, separated by a space.
    /// </summary>
    /// <param name="member">The member whose attributes are written.</param>
    /// <returns>The attributes of the member.</returns>
    internal static string TheAttributesOf(IMemberDefinition member)
        => member switch
        {
            MethodDefinition method     => TheNamesOf(method.Attributes.ToString()),
            FieldDefinition field       => TheNamesOf(field.Attributes.ToString()),
            PropertyDefinition property => property.Attributes == PropertyAttributes.None ? "" : TheNamesOf(property.Attributes.ToString()),
            TypeDefinition type         => TheAttributesOfAType(type),
            _                           => ""
        };

    /// <summary>
    /// The name of a type and the name which the member declares it with, which is written alone where the member declares none.
    /// </summary>
    /// <param name="type">The type of the member.</param>
    /// <param name="name">The name which the member declares the parameter with, or null.</param>
    /// <returns>The name of the type, and the name of it where it declares one.</returns>
    private static string TheNameOf(TypeReference type, string? name)
        => string.IsNullOrEmpty(name) ? type.FullName : $"{type.FullName} {name}";

    /// <summary>
    /// The attributes of a type, as the IL writes them: the visibility of the type, how the values of it are laid out
    /// and read, whether it is an interface, and the three modifiers which tell how it is built.
    /// </summary>
    /// <param name="type">The type whose attributes are written.</param>
    /// <returns>The attributes of the type.</returns>
    private static string TheAttributesOfAType(TypeDefinition type)
    {
        var names = new List<string>
        {
            (type.Attributes & TypeAttributes.VisibilityMask) switch
            {
                TypeAttributes.Public            => "public",
                TypeAttributes.NestedPublic      => "nested public",
                TypeAttributes.NestedPrivate     => "nested private",
                TypeAttributes.NestedFamily      => "nested family",
                TypeAttributes.NestedAssembly    => "nested assembly",
                TypeAttributes.NestedFamANDAssem => "nested famandassem",
                TypeAttributes.NestedFamORAssem  => "nested famorassem",
                _                                => "private"
            },
            (type.Attributes & TypeAttributes.LayoutMask) switch
            {
                TypeAttributes.SequentialLayout => "sequential",
                TypeAttributes.ExplicitLayout   => "explicit",
                _                               => "auto"
            },
            (type.Attributes & TypeAttributes.StringFormatMask) switch
            {
                TypeAttributes.UnicodeClass => "unicode",
                TypeAttributes.AutoClass    => "autochar",
                _                           => "ansi"
            }
        };

        if (type.IsInterface)
        {
            names.Add("interface");
        }
        else
        {
            // An interface is declared abstract and sealed in the metadata whether the source of it says so or not.
            if (type.IsAbstract) names.Add("abstract");
            if (type.IsSealed) names.Add("sealed");
        }

        if (type.IsBeforeFieldInit) names.Add("beforefieldinit");

        return string.Join(" ", names);
    }

    /// <summary>
    /// The names of a set of flags, as the IL writes them: in lower case and separated by a space, or the state which a
    /// member whose access the metadata leaves to the compiler is written in.
    /// </summary>
    /// <param name="flags">The names of the flags, as the metadata reads them.</param>
    /// <returns>The names of the flags.</returns>
    private static string TheNamesOf(string flags)
        => flags is "" or "0" ? "compilercontrolled" : flags.Replace(", ", " ").ToLowerInvariant();

    /// <summary>
    /// The label which every instruction of a body is written with, which is the offset it stands at where the body is
    /// written: the offset of an instruction is the sum of the sizes of the ones before it.
    /// </summary>
    /// <param name="instructions">The instructions of the body, in order.</param>
    /// <returns>The label of every instruction.</returns>
    private static Dictionary<Instruction, string> TheLabelsOf(IList<Instruction> instructions)
    {
        var labels = new Dictionary<Instruction, string>();
        var offset = 0;

        foreach (var instruction in instructions)
        {
            labels.Add(instruction, $"IL_{offset:x4}");
            offset += TheSizeOf(instruction);
        }

        return labels;
    }

    /// <summary>
    /// The number of bytes which an instruction takes where the body is written, which is the width of its opcode and of
    /// the operand which stands beside it.
    /// </summary>
    /// <param name="instruction">The instruction which is measured.</param>
    /// <returns>The number of bytes of the instruction.</returns>
    private static int TheSizeOf(Instruction instruction)
    {
        var size = instruction.OpCode.Size;

        return instruction.OpCode.OperandType switch
        {
            OperandType.InlineNone                                                                    => size,
            OperandType.ShortInlineI or OperandType.ShortInlineBrTarget or OperandType.ShortInlineVar => size + 1,
            OperandType.InlineVar                                                                     => size + 2,
            OperandType.InlineI or OperandType.ShortInlineR or OperandType.InlineBrTarget or OperandType.InlineString
                or OperandType.InlineType or OperandType.InlineField or OperandType.InlineMethod
                or OperandType.InlineTok or OperandType.InlineSig                                      => size + 4,
            OperandType.InlineI8 or OperandType.InlineR                                               => size + 8,
            OperandType.InlineSwitch                                                                  => size + 4 + (4 * ((Instruction[]) instruction.Operand!).Length),
            _                                                                                         => size + 4
        };
    }

    /// <summary>
    /// The label of an instruction, or the end of the body where a region reaches it.
    /// </summary>
    /// <param name="labels">The label of every instruction of the body.</param>
    /// <param name="instruction">The instruction which the label names, or null for the end of the body.</param>
    /// <returns>The label of the instruction.</returns>
    private static string TheLabelOf(IReadOnlyDictionary<Instruction, string> labels, Instruction? instruction)
        => instruction == null ? "the end" : labels[instruction];

    /// <summary>
    /// The range of instructions which a handler covers.
    /// </summary>
    /// <param name="labels">The label of every instruction of the body.</param>
    /// <param name="from">The instruction which the range begins at, or null where it covers nothing.</param>
    /// <param name="to">The instruction which the range ends at, or null where it reaches the end of the body.</param>
    /// <returns>The range, as the labels of the two instructions.</returns>
    private static string TheRangeOf(IReadOnlyDictionary<Instruction, string> labels, Instruction? from, Instruction? to)
        => $"{TheLabelOf(labels, from)}..{TheLabelOf(labels, to)}";

    /// <summary>
    /// The keyword which the IL writes a kind of handler with.
    /// </summary>
    /// <param name="kind">The kind of the handler.</param>
    /// <returns>The keyword of the kind.</returns>
    private static string TheKeywordOf(ExceptionHandlerType kind) => kind switch
    {
        ExceptionHandlerType.Catch   => "catch",
        ExceptionHandlerType.Filter  => "filter",
        ExceptionHandlerType.Finally => "finally",
        _                            => "fault"
    };
}
