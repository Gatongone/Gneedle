namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a property.
/// </summary>
[Flags]
public enum PropertyFlags
{
    Public    = 1 << 1,
    Internal  = 1 << 2,
    Protected = 1 << 3,
    Private   = 1 << 4,
    Static    = 1 << 5,
    Virtual   = 1 << 6,
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