namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a class.
/// </summary>
[Flags]
public enum ClassFlags
{
    Sealed    = 1 << 0,
    Public    = 1 << 1,
    Internal  = 1 << 2,
    Protected = 1 << 3,
    Private   = 1 << 4,
    Static    = 1 << 5,
    Abstract  = 1 << 6
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

    /// <param name="classFlags">Class flags.</param>
    extension(ClassFlags classFlags)
    {
        /// <summary>
        /// Converts <see cref="ClassFlags"/> to <see cref="TypeAttributes"/>.
        /// </summary>
        /// <returns><see cref="TypeAttributes"/> corresponding to <see cref="ClassFlags"/>.</returns>
        internal TypeAttributes ToTypeAttributes()
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
                _                                              => 0
            };

            return typeAttributes;
        }

        /// <summary>
        /// Converts <see cref="ClassFlags"/> to nested <see cref="TypeAttributes"/>.
        /// </summary>
        /// <returns>Nested <see cref="TypeAttributes"/> corresponding to <see cref="ClassFlags"/>.</returns>
        internal TypeAttributes ToNestedTypeAttributes()
        {
            var typeAttributes = DEFAULT_ATTRIBUTE;

            // Check access level.
            typeAttributes |= classFlags switch
            {
                _ when (classFlags & ClassFlags.Public) != 0    => TypeAttributes.NestedPublic,
                _ when (classFlags & ClassFlags.Internal) != 0  => TypeAttributes.NestedAssembly,
                _ when (classFlags & ClassFlags.Protected) != 0 => TypeAttributes.NestedFamily,
                _ when (classFlags & ClassFlags.Private) != 0   => TypeAttributes.NestedPrivate,
                _                                               => 0 // No access modifier flag is set
            };

            // Process method type.
            typeAttributes |= classFlags switch
            {
                _ when (classFlags & ClassFlags.Abstract) != 0 => TypeAttributes.Abstract,
                _ when (classFlags & ClassFlags.Sealed) != 0   => TypeAttributes.Sealed,
                _ when (classFlags & ClassFlags.Static) != 0   => TypeAttributes.Abstract | TypeAttributes.Sealed,
                _                                              => 0 // No method type flag is set
            };

            return typeAttributes;
        }
    }
}