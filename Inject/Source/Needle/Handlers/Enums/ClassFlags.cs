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
            typeAttributes |= classFlags.HasFlag(ClassFlags.Internal) ? TypeAttributes.NotPublic : TypeAttributes.Public;

            // Process method type.
            typeAttributes |= classFlags switch
            {
                _ when classFlags.HasFlag(ClassFlags.Abstract) => TypeAttributes.Abstract,
                _ when classFlags.HasFlag(ClassFlags.Sealed)   => TypeAttributes.Sealed,
                _ when classFlags.HasFlag(ClassFlags.Static)   => TypeAttributes.Abstract | TypeAttributes.Sealed,
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
                _ when classFlags.HasFlag(ClassFlags.Public)                                               => TypeAttributes.NestedPublic,
                _ when classFlags.HasFlag(ClassFlags.Protected) && classFlags.HasFlag(ClassFlags.Internal) => TypeAttributes.NestedFamORAssem,
                _ when classFlags.HasFlag(ClassFlags.Private) && classFlags.HasFlag(ClassFlags.Protected)  => TypeAttributes.NestedFamANDAssem,
                _ when classFlags.HasFlag(ClassFlags.Internal)                                             => TypeAttributes.NestedAssembly,
                _ when classFlags.HasFlag(ClassFlags.Protected)                                            => TypeAttributes.NestedFamily,
                _ when classFlags.HasFlag(ClassFlags.Private)                                              => TypeAttributes.NestedPrivate,
                _                                                                                          => 0 // No access modifier flag is set
            };

            // Process method type.
            typeAttributes |= classFlags switch
            {
                _ when classFlags.HasFlag(ClassFlags.Abstract) => TypeAttributes.Abstract,
                _ when classFlags.HasFlag(ClassFlags.Sealed)   => TypeAttributes.Sealed,
                _ when classFlags.HasFlag(ClassFlags.Static)   => TypeAttributes.Abstract | TypeAttributes.Sealed,
                _                                              => 0 // No method type flag is set
            };

            return typeAttributes;
        }
    }
}