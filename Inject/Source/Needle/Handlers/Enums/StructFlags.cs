namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a class.
/// </summary>
[Flags]
public enum StructFlags
{
    /// <summary>
    /// The fields of the struct cannot be assigned after it was created, which is marked by an
    /// <c>IsReadOnlyAttribute</c>.
    /// </summary>
    ReadOnly  = 1 << 0,

    /// <summary>
    /// The struct can only live on the stack, which is marked by an <c>IsByRefLikeAttribute</c> and by an
    /// <c>ObsoleteAttribute</c> which keeps it out of the fields of another type.
    /// </summary>
    Ref       = 1 << 1,

    /// <summary>
    /// The struct is visible to the types of every assembly.
    /// </summary>
    Public    = 1 << 2,

    /// <summary>
    /// The struct is visible to the types which derive from the type which declares it.
    /// </summary>
    Protected = 1 << 3,

    /// <summary>
    /// The struct is visible to the types of the assembly which declares it alone.
    /// </summary>
    Internal  = 1 << 4,

    /// <summary>
    /// The struct is visible to the type which declares it alone.
    /// </summary>
    Private   = 1 << 5
}

/// <summary>
/// Extensions for <see cref="StructFlags"/> for type conversion.
/// </summary>
internal static class StructFlagExtensions
{
    /// <summary>
    /// Default struct type attributes. It always includes Sealed, SequentialLayout, AnsiClass and BeforeFieldInit. The visibility attributes are determined by the struct flags.
    /// </summary>
    private const TypeAttributes DEFAULT_ATTRIBUTE = TypeAttributes.Sealed | TypeAttributes.SequentialLayout | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit;

    /// <param name="structFlags">Struct flags.</param>
    extension(StructFlags structFlags)
    {
        /// <summary>
        /// Converts <see cref="StructFlags"/> to <see cref="TypeAttributes"/>.
        /// </summary>
        /// <returns><see cref="TypeAttributes"/> corresponding to <see cref="StructFlags"/>.</returns>
        internal TypeAttributes ToTypeAttributes()
        {
            var typeAttributes = DEFAULT_ATTRIBUTE;
            typeAttributes |= (structFlags & StructFlags.Internal) != 0 ? TypeAttributes.NotPublic : TypeAttributes.Public;
            return typeAttributes;
        }

        /// <summary>
        /// Converts <see cref="StructFlags"/> to nested <see cref="TypeAttributes"/>.
        /// </summary>
        /// <returns>Nested <see cref="TypeAttributes"/> corresponding to <see cref="StructFlags"/>.</returns>
        internal TypeAttributes ToNestedTypeAttributes()
        {
            var typeAttributes = DEFAULT_ATTRIBUTE;

            // Check access level.
            typeAttributes |= structFlags switch
            {
                _ when (structFlags & StructFlags.Public) != 0    => TypeAttributes.NestedPublic,
                _ when (structFlags & StructFlags.Internal) != 0  => TypeAttributes.NestedAssembly,
                _ when (structFlags & StructFlags.Protected) != 0 => TypeAttributes.NestedFamily,
                _ when (structFlags & StructFlags.Private) != 0   => TypeAttributes.NestedPrivate,
                _                                                 => 0 // No access modifier flag is set
            };
            return typeAttributes;
        }
    }
}