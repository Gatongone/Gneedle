namespace Gneedle.Inject;

/// <summary>
/// The reading of a reference into the definition which it names, where the assembly that declares it may not be
/// readable at all.<para/>
/// What a reference is read for is the instructions and the members of what it names, and an assembly which cannot be
/// read is one whose reference there is nothing to read of: that is answered as a reference which names nothing rather
/// than as a fault of the library, so that a caller refuses it by the name of what it names. A reference of an
/// assembly which is there but which cannot be read for another reason is left to throw, because that is a fault of
/// the assembly rather than of the reading.
/// </summary>
internal static class Resolving
{
    /// <summary>
    /// The type which <paramref name="reference"/> names, or null where the assembly which declares it cannot be read.
    /// </summary>
    /// <param name="reference">The reference which is read.</param>
    /// <returns>The definition which it names, or null.</returns>
    internal static TypeDefinition? ResolveOrNull(this TypeReference reference)
    {
        try
        {
            return reference.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    /// <inheritdoc cref="ResolveOrNull(TypeReference)"/>
    internal static MethodDefinition? ResolveOrNull(this MethodReference reference)
    {
        try
        {
            return reference.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }
}