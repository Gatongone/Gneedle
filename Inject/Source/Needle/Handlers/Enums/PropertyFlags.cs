namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a property.
/// </summary>
[Flags]
public enum PropertyFlags
{
    /// <summary>
    /// The property belongs to an instance of the type rather than to the type itself.<para/>
    /// Every property belongs to one of the two, which are this flag and <see cref="Static"/>, and the two of them
    /// together name an instance one: the instance shape is the narrower of the two, so it is the one which a property
    /// that names both is written as and read back as. A property which names neither is one of an instance as well,
    /// because the static shape is the one which the metadata writes out.
    /// </summary>
    Instance = 1 << 0,

    /// <summary>
    /// The accessors of the property are visible to the types of every assembly.
    /// </summary>
    Public = 1 << 1,

    /// <summary>
    /// The accessors of the property are visible to the types of the assembly which declares it alone.
    /// </summary>
    Internal = 1 << 2,

    /// <summary>
    /// The accessors of the property are visible to the types which derive from the type which declares it.
    /// </summary>
    Protected = 1 << 3,

    /// <summary>
    /// The accessors of the property are visible to the type which declares it alone.
    /// </summary>
    Private = 1 << 4,

    /// <summary>
    /// The property belongs to the type rather than to an instance of it.
    /// </summary>
    Static = 1 << 5,

    /// <summary>
    /// The accessors of the property can be overridden by a type which derives from the declaring one.
    /// </summary>
    Virtual = 1 << 6,

    /// <summary>
    /// The accessors of the property have no body, and a type which derives from the declaring one implements them.
    /// </summary>
    Abstract = 1 << 7
}

/// <summary>
/// Extensions for <see cref="PropertyFlags"/> for type conversion.
/// </summary>
internal static class PropertyFlagExtensions
{
    /// <param name="propertyFlags">Property flags.</param>
    extension(PropertyFlags propertyFlags)
    {
        /// <summary>
        /// Converts <see cref="PropertyFlags"/> to <see cref="MethodAttributes"/> for getter/setter methods.
        /// </summary>
        /// <returns><see cref="MethodAttributes"/> corresponding to <see cref="PropertyFlags"/>.</returns>
        internal MethodAttributes ToMethodAttributes()
        {
            var attributes = MethodAttributes.HideBySig | MethodAttributes.SpecialName;

            // Check access level.
            attributes |= propertyFlags switch
            {
                _ when (propertyFlags & PropertyFlags.Public) != 0    => MethodAttributes.Public,
                _ when (propertyFlags & PropertyFlags.Internal) != 0  => MethodAttributes.Assembly,
                _ when (propertyFlags & PropertyFlags.Protected) != 0 => MethodAttributes.Family,
                _ when (propertyFlags & PropertyFlags.Private) != 0   => MethodAttributes.Private,
                _                                                     => MethodAttributes.Public // Default
            };

            // Process property modifiers. The instance shape is the narrower of the two shapes of belonging, so a
            // property which names both of them is written as one of an instance: the static shape is written only
            // where it is the one of the two which the property names.
            if ((propertyFlags & PropertyFlags.Static) != 0 && (propertyFlags & PropertyFlags.Instance) == 0) attributes   |= MethodAttributes.Static;
            if ((propertyFlags & PropertyFlags.Virtual) != 0) attributes  |= MethodAttributes.Virtual | MethodAttributes.NewSlot;
            if ((propertyFlags & PropertyFlags.Abstract) != 0) attributes |= MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot;

            return attributes;
        }
    }

    /// <param name="propertyDefinition">The property definition which is read.</param>
    extension(PropertyDefinition propertyDefinition)
    {
        /// <summary>
        /// Reads the flags of the property which the definition declares, which is the inverse of the conversion above:
        /// a property holds no attributes of its own, so the flags of it are the ones of the accessor which it holds,
        /// which is the getter, or the setter when the property holds no getter. A property which holds no accessor at
        /// all declares no flag, and is answered with none. The two shapes of belonging are read off that accessor the
        /// same way, which is what makes the instance shape the one of the two which a property that names both is
        /// answered with.
        /// </summary>
        /// <returns><see cref="PropertyFlags"/> corresponding to the definition.</returns>
        internal PropertyFlags ToPropertyFlags()
        {
            var accessor = propertyDefinition.GetMethod ?? propertyDefinition.SetMethod;
            if (accessor == null) return 0; // The property holds no accessor, which is the shape of a property which declares nothing

            var attributes = accessor.Attributes;
            var propertyFlags = (attributes & MethodAttributes.MemberAccessMask) switch
            {
                MethodAttributes.Public      => PropertyFlags.Public,
                MethodAttributes.Assembly    => PropertyFlags.Internal,
                MethodAttributes.Family      => PropertyFlags.Protected,
                MethodAttributes.Private     => PropertyFlags.Private,
                MethodAttributes.FamORAssem  => PropertyFlags.Protected | PropertyFlags.Internal,
                MethodAttributes.FamANDAssem => PropertyFlags.Private | PropertyFlags.Protected,
                _                            => (PropertyFlags) 0 // No access modifier flag is set
            };

            // The abstract shape carries the virtual one with it, and it is read back as the abstract flag alone, as it
            // is for a method.
            if (accessor.IsAbstract) propertyFlags     |= PropertyFlags.Abstract;
            else if (accessor.IsVirtual) propertyFlags |= PropertyFlags.Virtual;

            // Every property belongs to the type or to an instance of it, and the metadata writes the static shape out
            // on the accessor: the one which belongs to the type is marked static, and every other one is read as the
            // instance shape.
            if (accessor.IsStatic) propertyFlags |= PropertyFlags.Static;
            else propertyFlags                   |= PropertyFlags.Instance;

            return propertyFlags;
        }
    }
}