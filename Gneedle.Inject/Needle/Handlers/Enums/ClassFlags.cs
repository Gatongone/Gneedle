namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a class.
/// </summary>
[Flags]
public enum ClassFlags
{
    Sealed    = 0b0000001,
    Public    = 0b0000010,
    Internal  = 0b0000100,
    Protected = 0b0001000,
    Private   = 0b0010000,
    Static    = 0b0100000,
    Abstract  = 0b1000000
}

/// <summary>
/// Extensions for <see cref="ClassFlags"/> for type conversion.
/// </summary>
internal static class ClassFlagExtensions
{
    /// <summary>
    /// Default type attributes for a class, including auto layout, ANSI character set, and before field init.
    /// </summary>
    private const TypeAttributes DEFAULT_ATTRIBUTE = TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit;

    /// <summary>
    /// Converts <see cref="ClassFlags"/> to <see cref="TypeAttributes"/>.
    /// </summary>
    /// <param name="classFlags">Class flags.</param>
    /// <returns><see cref="TypeAttributes"/> corresponding to <see cref="ClassFlags"/>.</returns>
    internal static TypeAttributes ToTypeAttributes(this ClassFlags classFlags)
    {
        var typeAttributes = DEFAULT_ATTRIBUTE;

        // Check access level.
        typeAttributes |= (classFlags & ClassFlags.Internal) != 0 ? TypeAttributes.NotPublic : TypeAttributes.Public;

        // Process method type.
        typeAttributes |= classFlags switch
        {
            _ when (classFlags & ClassFlags.Abstract) != 0 => TypeAttributes.Abstract,
            _ when (classFlags & ClassFlags.Sealed) != 0   => TypeAttributes.Sealed,
            _ when (classFlags & ClassFlags.Static) != 0   => TypeAttributes.Abstract | TypeAttributes.Sealed,
            _ => 0
        };

        return typeAttributes;
    }

    /// <summary>
    /// Converts <see cref="ClassFlags"/> to nested <see cref="TypeAttributes"/>.
    /// </summary>
    /// <param name="classFlags">Class flags.</param>
    /// <returns>Nested <see cref="TypeAttributes"/> corresponding to <see cref="ClassFlags"/>.</returns>
    internal static TypeAttributes ToNestedTypeAttributes(this ClassFlags classFlags)
    {
        var typeAttributes = DEFAULT_ATTRIBUTE;

        // Check access level.
        typeAttributes |= classFlags switch
        {
            _ when (classFlags & ClassFlags.Public) != 0    => TypeAttributes.NestedPublic,
            _ when (classFlags & ClassFlags.Internal) != 0  => TypeAttributes.NestedAssembly,
            _ when (classFlags & ClassFlags.Protected) != 0 => TypeAttributes.NestedFamily,
            _ when (classFlags & ClassFlags.Private) != 0   => TypeAttributes.NestedPrivate,
            _ => 0 // No access modifier flag is set
        };

        // Process method type.
        typeAttributes |= classFlags switch
        {
            _ when (classFlags & ClassFlags.Abstract) != 0 => TypeAttributes.Abstract,
            _ when (classFlags & ClassFlags.Sealed) != 0   => TypeAttributes.Sealed,
            _ when (classFlags & ClassFlags.Static) != 0   => TypeAttributes.Abstract | TypeAttributes.Sealed,
            _ => 0 // No method type flag is set
        };

        return typeAttributes;
    }
}