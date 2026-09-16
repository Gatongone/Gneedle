namespace Gneedle.Inject;

/// <summary>
/// The state which the chain of a decorator holds once it has built the member which it describes.<para/>
/// A chain describes a member while it runs and appends that member to the module where it ends, and what it holds is
/// read at that one point: a part which is described after the member was built reaches no member at all, so it is
/// refused rather than passed over. There is one guard for every kind of member, because every chain is built the same
/// way, whether it describes a type, a method, a field, a property or an enum.
/// </summary>
internal static class DecoratorChain
{
    /// <summary>
    /// Refuse a part which is described after the member of the chain was built, which the member holds nothing of.
    /// </summary>
    /// <param name="builtHandler">The handler which the chain answered with where it built the member, or null while
    /// it has built none.</param>
    /// <param name="memberName">The name of the member which the chain describes.</param>
    /// <exception cref="InvalidOperationException">Thrown when the member was already built.</exception>
    internal static void RefuseDescription(object? builtHandler, string memberName)
    {
        if (builtHandler == null) return;
        throw new InvalidOperationException(string.Format(ErrorMessages.MEMBER_IS_ALREADY_BUILT, memberName));
    }
}
