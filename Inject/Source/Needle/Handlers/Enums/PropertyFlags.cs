namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a property.
/// </summary>
[Flags]
public enum PropertyFlags
{
    /// <summary>
    /// The accessors of the property are visible to the types of every assembly.
    /// </summary>
    Public    = 1 << 1,

    /// <summary>
    /// The accessors of the property are visible to the types of the assembly which declares it alone.
    /// </summary>
    Internal  = 1 << 2,

    /// <summary>
    /// The accessors of the property are visible to the types which derive from the type which declares it.
    /// </summary>
    Protected = 1 << 3,

    /// <summary>
    /// The accessors of the property are visible to the type which declares it alone.
    /// </summary>
    Private   = 1 << 4,

    /// <summary>
    /// The property belongs to the type rather than to an instance of it.
    /// </summary>
    Static    = 1 << 5,

    /// <summary>
    /// The accessors of the property can be overridden by a type which derives from the declaring one.
    /// </summary>
    Virtual   = 1 << 6,

    /// <summary>
    /// The accessors of the property have no body, and a type which derives from the declaring one implements them.
    /// </summary>
    Abstract  = 1 << 7
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

            // Process property modifiers.
            if ((propertyFlags & PropertyFlags.Static) != 0)   attributes |= MethodAttributes.Static;
            if ((propertyFlags & PropertyFlags.Virtual) != 0)  attributes |= MethodAttributes.Virtual | MethodAttributes.NewSlot;
            if ((propertyFlags & PropertyFlags.Abstract) != 0) attributes |= MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot;

            return attributes;
        }
    }
}