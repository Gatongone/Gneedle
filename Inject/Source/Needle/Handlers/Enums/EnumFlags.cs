namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of an enum.
/// </summary>
[Flags]
public enum EnumFlags
{
    /// <summary>
    /// The enum is visible to the types of every assembly.
    /// </summary>
    Public = 1 << 0,

    /// <summary>
    /// The enum is visible to the type which declares it alone.
    /// </summary>
    Private = 1 << 1,

    /// <summary>
    /// The enum is visible to the types which derive from the type which declares it.
    /// </summary>
    Protected = 1 << 2,

    /// <summary>
    /// The enum is visible to the types of the assembly which declares it alone.
    /// </summary>
    Internal = 1 << 3
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

    /// <param name="typeDefinition">The enum definition which is read.</param>
    extension(TypeDefinition typeDefinition)
    {
        /// <summary>
        /// Reads the flags of the enum which the definition declares, which is the inverse of the two conversions
        /// above: an enum holds a visibility and nothing else, so the attributes of the type are what it is read from.
        /// </summary>
        /// <returns><see cref="EnumFlags"/> corresponding to the definition.</returns>
        internal EnumFlags ToEnumFlags()
        {
            var attributes = typeDefinition.Attributes;

            // The visibility of a nested enum is written in the nested shape of it, which is none of the two shapes an
            // enum declared at the top of a module is written with, so the one which declares the definition tells the
            // two kinds apart.
            if (typeDefinition.IsNested)
            {
                return (attributes & TypeAttributes.VisibilityMask) switch
                {
                    TypeAttributes.NestedPublic      => EnumFlags.Public,
                    TypeAttributes.NestedAssembly    => EnumFlags.Internal,
                    TypeAttributes.NestedFamily      => EnumFlags.Protected,
                    TypeAttributes.NestedPrivate     => EnumFlags.Private,
                    TypeAttributes.NestedFamORAssem  => EnumFlags.Protected | EnumFlags.Internal,
                    TypeAttributes.NestedFamANDAssem => EnumFlags.Private | EnumFlags.Protected,
                    _                                => 0 // No access modifier flag is set
                };
            }

            // An enum declared at the top of a module is public unless it names no visibility, and the shape which names
            // none of the visibilities is the internal one.
            return (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NotPublic ? EnumFlags.Internal : EnumFlags.Public;
        }
    }
}