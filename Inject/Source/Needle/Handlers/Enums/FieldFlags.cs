namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a field.
/// </summary>
[Flags]
public enum FieldFlags
{
    /// <summary>
    /// The field belongs to an instance of the type rather than to the type itself.<para/>
    /// Every field belongs to one of the two, which are this flag and <see cref="Static"/>, and the two of them
    /// together name an instance one: the instance shape is the narrower of the two, so it is the one which a field
    /// that names both is written as and read back as. A field which names neither is one of an instance as well,
    /// because the static shape is the one which the metadata writes out.
    /// </summary>
    Instance = 1 << 0,

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

            // Process field modifiers. The instance shape is the narrower of the two shapes of belonging, so a field
            // which names both of them is written as one of an instance: the static shape is written only where it is
            // the one of the two which the field names.
            if ((fieldFlags & FieldFlags.Static) != 0 && (fieldFlags & FieldFlags.Instance) == 0) attributes |= FieldAttributes.Static;
            if ((fieldFlags & FieldFlags.ReadOnly) != 0) attributes                                    |= FieldAttributes.InitOnly;

            return attributes;
        }
    }

    /// <param name="fieldDefinition">The field definition which is read.</param>
    extension(FieldDefinition fieldDefinition)
    {
        /// <summary>
        /// Reads the flags of the field which the definition declares, which is the inverse of the conversion above:
        /// the two shapes of belonging are read back the same way, which is what makes the instance shape the one of
        /// the two which a field that names both is answered with.
        /// </summary>
        /// <returns><see cref="FieldFlags"/> corresponding to the definition.</returns>
        internal FieldFlags ToFieldFlags()
        {
            var attributes = fieldDefinition.Attributes;
            var fieldFlags = (attributes & FieldAttributes.FieldAccessMask) switch
            {
                FieldAttributes.Public      => FieldFlags.Public,
                FieldAttributes.Assembly    => FieldFlags.Internal,
                FieldAttributes.Family      => FieldFlags.Protected,
                FieldAttributes.Private     => FieldFlags.Private,
                FieldAttributes.FamORAssem  => FieldFlags.Protected | FieldFlags.Internal,
                FieldAttributes.FamANDAssem => FieldFlags.Private | FieldFlags.Protected,
                _                           => (FieldFlags) 0 // No access modifier flag is set
            };

            // Every field belongs to the type or to an instance of it, and the metadata writes the static shape out:
            // the one which belongs to the type is marked static, and every other one is read as the instance shape.
            if (fieldDefinition.IsStatic) fieldFlags |= FieldFlags.Static;
            else fieldFlags                         |= FieldFlags.Instance;

            if (fieldDefinition.IsInitOnly) fieldFlags |= FieldFlags.ReadOnly;

            return fieldFlags;
        }
    }
}