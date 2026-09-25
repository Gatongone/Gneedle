namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a method.
/// </summary>
[Flags]
public enum MethodFlags
{
    /// <summary>
    /// The method belongs to an instance of the type rather than to the type itself.<para/>
    /// Every method belongs to one of the two, which are this flag and <see cref="Static"/>, and the two of them
    /// together name an instance one: the instance shape is the narrower of the two, so it is the one which a method
    /// that names both is written as and read back as. A method which names neither is one of an instance as well,
    /// because the static shape is the one which the metadata writes out.
    /// </summary>
    Instance = 1 << 0,

    /// <summary>
    /// The method is visible to the types of every assembly.
    /// </summary>
    Public = 1 << 1,

    /// <summary>
    /// The method is visible to the types of the assembly which declares it alone.
    /// </summary>
    Internal = 1 << 2,

    /// <summary>
    /// The method is visible to the types which derive from the type which declares it.
    /// </summary>
    Protected = 1 << 3,

    /// <summary>
    /// The method is visible to the type which declares it alone.
    /// </summary>
    Private = 1 << 4,

    /// <summary>
    /// The method belongs to the type rather than to an instance of it.
    /// </summary>
    Static = 1 << 5,

    /// <summary>
    /// The method has no body, and a type which derives from the declaring one implements it.
    /// </summary>
    Abstract = 1 << 6,

    /// <summary>
    /// The method can be overridden by a type which derives from the declaring one, and it is the implementation which
    /// that type inherits unless it overrides it.
    /// </summary>
    Virtual = 1 << 7
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

            // Process method type. The instance shape is the narrower of the two shapes of belonging, so a method which
            // names both of them is written as one of an instance: the static shape is written only where it is the one
            // of the two which the method names.
            var isStatic = methodFlags.HasFlag(MethodFlags.Static) && !methodFlags.HasFlag(MethodFlags.Instance);

            methodAttributes |= methodFlags switch
            {
                _ when methodFlags.HasFlag(MethodFlags.Virtual)  => MethodAttributes.Virtual | MethodAttributes.NewSlot,
                _ when methodFlags.HasFlag(MethodFlags.Abstract) => MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot,
                _ when isStatic                                  => MethodAttributes.Static,
                _                                                => 0 // No method type flag is set
            };

            return methodAttributes;
        }
    }

    /// <param name="methodDefinition">The method definition which is read.</param>
    extension(MethodDefinition methodDefinition)
    {
        /// <summary>
        /// Reads the flags of the method which the definition declares, which is the inverse of the conversion above:
        /// an abstract method is a virtual one which holds no body, and it is written with the two shapes at once, so it
        /// is read back as the narrower of them, which is <see cref="MethodFlags.Abstract"/>. The two shapes of
        /// belonging are read back the same way, which is what makes the instance shape the one of the two which a
        /// method that names both is answered with.
        /// </summary>
        /// <returns><see cref="MethodFlags"/> corresponding to the definition.</returns>
        internal MethodFlags ToMethodFlags()
        {
            var attributes = methodDefinition.Attributes;
            var methodFlags = (attributes & MethodAttributes.MemberAccessMask) switch
            {
                MethodAttributes.Public      => MethodFlags.Public,
                MethodAttributes.Assembly    => MethodFlags.Internal,
                MethodAttributes.Family      => MethodFlags.Protected,
                MethodAttributes.Private     => MethodFlags.Private,
                MethodAttributes.FamORAssem  => MethodFlags.Protected | MethodFlags.Internal,
                MethodAttributes.FamANDAssem => MethodFlags.Private | MethodFlags.Protected,
                _                            => (MethodFlags) 0 // No access modifier flag is set
            };

            // The abstract shape carries the virtual one with it, and it is read back as the abstract flag alone: a
            // method which is abstract is one which holds no body, where the one which is virtual holds the body which
            // its type gives it.
            if (methodDefinition.IsAbstract) methodFlags     |= MethodFlags.Abstract;
            else if (methodDefinition.IsVirtual) methodFlags |= MethodFlags.Virtual;

            // Every method belongs to the type or to an instance of it, and the metadata writes the static shape out:
            // the one which belongs to the type is marked static, and every other one is read as the instance shape.
            if (methodDefinition.IsStatic) methodFlags |= MethodFlags.Static;
            else methodFlags                           |= MethodFlags.Instance;

            return methodFlags;
        }
    }
}