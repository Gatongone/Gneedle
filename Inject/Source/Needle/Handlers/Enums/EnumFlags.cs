namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of an enum.
/// </summary>
[Flags]
public enum EnumFlags
{
    Public    = 1 << 0,
    Private   = 1 << 1,
    Protected = 1 << 2,
    Internal  = 1 << 3
}

/// <summary>
/// Extensions for <see cref="EnumFlags"/> for type conversion.
/// </summary>
internal static class EnumFlagExtensions
{
    /// <summary>
    /// Default enum type attributes, including Sealed, AutoClass, AnsiClass and BeforeFieldInit.
    /// </summary>
    private const TypeAttributes DEFAULT_ATTRIBUTE = TypeAttributes.Sealed | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit;

    /// <param name="enumFlags">Enum flags.</param>
    extension(EnumFlags enumFlags)
    {
        /// <summary>
        /// Converts <see cref="EnumFlags"/> to <see cref="TypeAttributes"/>.
        /// </summary>
        /// <returns><see cref="TypeAttributes"/> corresponding to <see cref="EnumFlags"/>.</returns>
        internal TypeAttributes ToTypeAttributes()
        {
            var typeAttributes = DEFAULT_ATTRIBUTE;
            typeAttributes |= (enumFlags & EnumFlags.Internal) != 0 ? TypeAttributes.NotPublic : TypeAttributes.Public;
            return typeAttributes;
        }

        /// <summary>
        /// Converts <see cref="EnumFlags"/> to nested <see cref="TypeAttributes"/>.
        /// </summary>
        /// <returns>Nested <see cref="TypeAttributes"/> corresponding to <see cref="EnumFlags"/>.</returns>
        internal TypeAttributes ToNestedTypeAttributes()
        {
            var typeAttributes = DEFAULT_ATTRIBUTE;

            typeAttributes |= enumFlags switch
            {
                _ when (enumFlags & EnumFlags.Public) != 0    => TypeAttributes.NestedPublic,
                _ when (enumFlags & EnumFlags.Internal) != 0  => TypeAttributes.NestedAssembly,
                _ when (enumFlags & EnumFlags.Protected) != 0 => TypeAttributes.NestedFamily,
                _ when (enumFlags & EnumFlags.Private) != 0   => TypeAttributes.NestedPrivate,
                _                                             => 0
            };

            return typeAttributes;
        }
    }
}