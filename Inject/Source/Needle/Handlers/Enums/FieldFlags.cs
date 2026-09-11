namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a field.
/// </summary>
[Flags]
public enum FieldFlags
{
    /// <summary>
    /// The field is visible to the types of every assembly.
    /// </summary>
    Public = 1 << 1,

    /// <summary>
    /// The field is visible to the types of the assembly which declares it alone.
    /// </summary>
    Internal = 1 << 2,

    /// <summary>
    /// The field is visible to the types which derive from the type which declares it.
    /// </summary>
    Protected = 1 << 3,

    /// <summary>
    /// The field is visible to the type which declares it alone.
    /// </summary>
    Private = 1 << 4,

    /// <summary>
    /// The field belongs to the type rather than to an instance of it.
    /// </summary>
    Static = 1 << 5,

    /// <summary>
    /// The field can only be assigned by a constructor of the type which declares it.
    /// </summary>
    ReadOnly = 1 << 6
}

/// <summary>
/// Extensions for <see cref="FieldFlags"/> for type conversion.
/// </summary>
internal static class FieldFlagExtensions
{
    /// <param name="fieldFlags">Field flags.</param>
    extension(FieldFlags fieldFlags)
    {
        /// <summary>
        /// Converts <see cref="FieldFlags"/> to <see cref="FieldAttributes"/>.
        /// </summary>
        /// <returns><see cref="FieldAttributes"/> corresponding to <see cref="FieldFlags"/>.</returns>
        internal FieldAttributes ToFieldAttributes()
        {
            var attributes = (FieldAttributes) 0;

            // Check access level.
            attributes |= fieldFlags switch
            {
                _ when (fieldFlags & FieldFlags.Public) != 0    => FieldAttributes.Public,
                _ when (fieldFlags & FieldFlags.Internal) != 0  => FieldAttributes.Assembly,
                _ when (fieldFlags & FieldFlags.Protected) != 0 => FieldAttributes.Family,
                _ when (fieldFlags & FieldFlags.Private) != 0   => FieldAttributes.Private,
                _                                               => FieldAttributes.Public // Default
            };

            // Process field modifiers.
            if ((fieldFlags & FieldFlags.Static) != 0) attributes   |= FieldAttributes.Static;
            if ((fieldFlags & FieldFlags.ReadOnly) != 0) attributes |= FieldAttributes.InitOnly;

            return attributes;
        }
    }
}