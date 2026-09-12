namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a method.
/// </summary>
[Flags]
public enum MethodFlags
{
    /// <summary>
    /// The method is visible to the types of every assembly.
    /// </summary>
    Public    = 1 << 1,

    /// <summary>
    /// The method is visible to the types of the assembly which declares it alone.
    /// </summary>
    Internal  = 1 << 2,

    /// <summary>
    /// The method is visible to the types which derive from the type which declares it.
    /// </summary>
    Protected = 1 << 3,

    /// <summary>
    /// The method is visible to the type which declares it alone.
    /// </summary>
    Private   = 1 << 4,

    /// <summary>
    /// The method belongs to the type rather than to an instance of it.
    /// </summary>
    Static    = 1 << 5,

    /// <summary>
    /// The method has no body, and a type which derives from the declaring one implements it.
    /// </summary>
    Abstract  = 1 << 6,

    /// <summary>
    /// The method can be overridden by a type which derives from the declaring one, and it is the implementation which
    /// that type inherits unless it overrides it.
    /// </summary>
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