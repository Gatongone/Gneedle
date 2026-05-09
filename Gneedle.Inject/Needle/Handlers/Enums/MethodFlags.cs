namespace Gneedle.Inject;

[Flags]
public enum MethodFlags
{
    Public    = 1 << 1,
    Internal  = 1 << 2,
    Protected = 1 << 3,
    Private   = 1 << 4,
    Static    = 1 << 5,
    Abstract  = 1 << 6,
    Virtual   = 1 << 7
}

/// <summary>
/// Extensions for <see cref="MethodFlags"/> for MethodAttributes conversion.
/// </summary>
internal static class MethodFlagExtensions
{
    /// <summary>
    /// Default type attributes for a method.
    /// </summary>
    private const MethodAttributes DEFAULT_ATTRIBUTE = MethodAttributes.HideBySig;

    /// <param name="methodFlags">Class flags.</param>
    extension(MethodFlags methodFlags)
    {
        /// <summary>
        /// Converts <see cref="MethodFlags"/> to <see cref="MethodAttributes"/>.
        /// </summary>
        /// <returns><see cref="MethodAttributes"/> corresponding to <see cref="MethodFlags"/>.</returns>
        internal MethodAttributes ToMethodAttributes()
        {
            var methodAttributes = DEFAULT_ATTRIBUTE;

            // Check access level.
            methodAttributes |= methodFlags switch
            {
                _ when methodFlags.HasFlag(MethodFlags.Public)                                                 => MethodAttributes.Public,
                _ when methodFlags.HasFlag(MethodFlags.Protected) && methodFlags.HasFlag(MethodFlags.Internal) => MethodAttributes.FamORAssem,
                _ when methodFlags.HasFlag(MethodFlags.Private) && methodFlags.HasFlag(MethodFlags.Protected)  => MethodAttributes.FamANDAssem,
                _ when methodFlags.HasFlag(MethodFlags.Internal)                                               => MethodAttributes.Assembly,
                _ when methodFlags.HasFlag(MethodFlags.Protected)                                              => MethodAttributes.Family,
                _ when methodFlags.HasFlag(MethodFlags.Private)                                                => MethodAttributes.Private,
                _                                                                                              => 0 // No access modifier flag is set
            };

            // Process method type.
            methodAttributes |= methodFlags switch
            {
                _ when methodFlags.HasFlag(MethodFlags.Virtual)  => MethodAttributes.Virtual | MethodAttributes.NewSlot,
                _ when methodFlags.HasFlag(MethodFlags.Abstract) => MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot,
                _ when methodFlags.HasFlag(MethodFlags.Static)   => MethodAttributes.Static,
                _                                                => 0 // No method type flag is set
            };

            return methodAttributes;
        }
    }
}