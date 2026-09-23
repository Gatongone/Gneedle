namespace Gneedle.Inject;

partial class MethodHandler
{
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
    /// <exception cref="WeavingException">Thrown when the name of the placeholder is not written where the call is.</exception>
    private void RefuseANameWhichIsNotWritten(MemberReference member, int currentIndex, InstructionFilter filter)
    {
        // Only the members which a template names with a string are read this way: the instance which is pushed and the
        // type which another member is looked up on are reached through the instructions around the call.
        if (member.DeclaringType.FullName is not (This.TYPE_NAME or Base.TYPE_NAME or Instance.TYPE_NAME or Static.TYPE_NAME)) return;
        if (member.Name is not (nameof(This.Field) or nameof(This.Property) or nameof(This.Method))) return;

        // The name is what the call follows, and it is read for a name which no load stands ahead of.
        if (currentIndex == 0 || filter.Target[currentIndex - 1].OpCode != OpCodes.Ldstr)
        {
            throw new WeavingException(string.Format(ErrorMessages.NAME_IS_NOT_WRITTEN, member.FullName, Source.FullName));
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
    /// <exception cref="WeavingException">Thrown when the reference reaches a type which the compiler wrote.</exception>
    private void RefuseTheCompilersOwnType(TypeReference? type, string reference)
    {
        // What the carrying wrote keeps the name the compiler wrote, brackets and all, so the name alone no longer
        // tells a type of the compiler's own which the weaving holds the instructions of from one which it does not:
        // what the carrying wrote is read as a type the weaving has, and only what it did not write is refused.
        if (m_Carried?.HoldsType(type) == true) return;

        // The type itself is read as well as the types it is declared inside, rather than the nested chain alone: the
        // compiler writes a type beside the template for some of what a body holds, which the carrying reads, and a
        // type at the top level of the assembly for others - the type of an anonymous object, and the one a collection
        // expression stands in - which it does not. The latter is internal to the assembly the template was compiled
        // into, so a member woven into another one would reach a type it cannot run, and it is refused here rather than
        // written.
        for (var at = type; at is not null; at = at.DeclaringType)
        {
            if (!at.Name.StartsWith("<", StringComparison.Ordinal)) continue;

            // The type itself is told from the types it is declared inside, because the two are refused for different
            // reasons: a type the compiler wrote beside the template is one the carrying reads, and what reaches here
            // is one whose instructions could not be read, while a type it wrote at the top level of the assembly is
            // one the carrying does not read at all.
            var message = ReferenceEquals(at, type) && !at.IsNested
                ? ErrorMessages.TEMPLATE_REACHES_A_TYPE_OF_THE_TOP_LEVEL
                : ErrorMessages.TEMPLATE_HOLDS_A_METHOD_OF_ITS_OWN;

            throw new WeavingException(string.Format(message, reference, Source.FullName));
        }
    }

    /// <summary>
    /// Refuse a member which the compiler wrote for a body of the template's own, which the carrying did not write a
    /// copy of and did not refuse.<para/>
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
    /// <exception cref="WeavingException">Thrown when the member is one which the compiler wrote for a body of the template's own.</exception>
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
        // wrote it in. A body of the compiler's own which captured is written on a type beside the template instead, and
        // what refuses that one is the carrying, which could not read it: a member the carrying wrote a copy of, and one
        // the carrying refused, both return before this is asked, so what reaches here is the last guard of the two.
        if (member.DeclaringType?.GetElementType().FullName != targetDef.DeclaringType.FullName) return;

        throw new WeavingException(string.Format(ErrorMessages.TEMPLATE_HOLDS_A_BODY_OF_ITS_OWN, member.FullName, Source.FullName));
    }
}
