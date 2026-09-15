namespace Gneedle.Inject;

/// <summary>
/// Represents the modifier of a class.
/// </summary>
[Flags]
public enum ClassFlags
{
    /// <summary>
    /// The class cannot be inherited.
    /// </summary>
    Sealed = 1 << 0,

    /// <summary>
    /// The class is visible to the types of every assembly.
    /// </summary>
    Public = 1 << 1,

    /// <summary>
    /// The class is visible to the types of the assembly which declares it alone.
    /// </summary>
    Internal = 1 << 2,

    /// <summary>
    /// The class is visible to the types which derive from the type which declares it.
    /// </summary>
    Protected = 1 << 3,

    /// <summary>
    /// The class is visible to the type which declares it alone.
    /// </summary>
    Private = 1 << 4,

    /// <summary>
    /// The class cannot be instantiated, and it cannot be inherited either, which is the shape of a class whose members
    /// are all static.
    /// </summary>
    /// <remarks>
    /// It alone gives the abstract and sealed shape. It is shadowed when it is combined with <see cref="Abstract"/> or
    /// with <see cref="Sealed"/>, because those two are applied before it.
    /// </remarks>
    Static = 1 << 5,

    /// <summary>
    /// The class cannot be instantiated.
    /// </summary>
    Abstract = 1 << 6
}

/// <summary>
/// Extensions for <see cref="ClassFlags"/> for type conversion.
/// </summary>
internal static class ClassFlagExtensions
{
    /// <summary>
    /// Default type attributes for a class, including auto layout, ANSI character set, and before field init.
    /// </summary>
    private const TypeAttributes DEFAULT_ATTRIBUTE = TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit;

    /// <param name="classFlags">Class flags.</param>
    extension(ClassFlags classFlags)
    {
        /// <summary>
        /// Converts <see cref="ClassFlags"/> to <see cref="TypeAttributes"/>.
        /// </summary>
        /// <returns><see cref="TypeAttributes"/> corresponding to <see cref="ClassFlags"/>.</returns>
        internal TypeAttributes ToTypeAttributes()
        {
            var typeAttributes = DEFAULT_ATTRIBUTE;

            // Check access level.
            typeAttributes |= classFlags.HasFlag(ClassFlags.Internal) ? TypeAttributes.NotPublic : TypeAttributes.Public;

            // Process method type.
            typeAttributes |= classFlags switch
            {
                _ when classFlags.HasFlag(ClassFlags.Abstract) => TypeAttributes.Abstract,
                _ when classFlags.HasFlag(ClassFlags.Sealed)   => TypeAttributes.Sealed,
                _ when classFlags.HasFlag(ClassFlags.Static)   => TypeAttributes.Abstract | TypeAttributes.Sealed,
                _                                              => 0
            };

            return typeAttributes;
        }

        /// <summary>
        /// Converts <see cref="ClassFlags"/> to nested <see cref="TypeAttributes"/>.
        /// </summary>
        /// <returns>Nested <see cref="TypeAttributes"/> corresponding to <see cref="ClassFlags"/>.</returns>
        internal TypeAttributes ToNestedTypeAttributes()
        {
            var typeAttributes = DEFAULT_ATTRIBUTE;

            // Check access level.
            typeAttributes |= classFlags switch
            {
                _ when classFlags.HasFlag(ClassFlags.Public)                                               => TypeAttributes.NestedPublic,
                _ when classFlags.HasFlag(ClassFlags.Protected) && classFlags.HasFlag(ClassFlags.Internal) => TypeAttributes.NestedFamORAssem,
                _ when classFlags.HasFlag(ClassFlags.Private) && classFlags.HasFlag(ClassFlags.Protected)  => TypeAttributes.NestedFamANDAssem,
                _ when classFlags.HasFlag(ClassFlags.Internal)                                             => TypeAttributes.NestedAssembly,
                _ when classFlags.HasFlag(ClassFlags.Protected)                                            => TypeAttributes.NestedFamily,
                _ when classFlags.HasFlag(ClassFlags.Private)                                              => TypeAttributes.NestedPrivate,
                _                                                                                          => 0 // No access modifier flag is set
            };

            // Process method type.
            typeAttributes |= classFlags switch
            {
                _ when classFlags.HasFlag(ClassFlags.Abstract) => TypeAttributes.Abstract,
                _ when classFlags.HasFlag(ClassFlags.Sealed)   => TypeAttributes.Sealed,
                _ when classFlags.HasFlag(ClassFlags.Static)   => TypeAttributes.Abstract | TypeAttributes.Sealed,
                _                                              => 0 // No method type flag is set
            };

            return typeAttributes;
        }
    }

    /// <param name="typeDefinition">The class definition which is read.</param>
    extension(TypeDefinition typeDefinition)
    {
        /// <summary>
        /// Reads the flags of the class which the definition declares, which is the inverse of the two conversions
        /// above: the flags name what the class is, and the attributes are how that is written into the metadata, so
        /// the abstract and the sealed shape of a static class are read back as <see cref="ClassFlags.Static"/>.
        /// </summary>
        /// <returns><see cref="ClassFlags"/> corresponding to the definition.</returns>
        internal ClassFlags ToClassFlags()
        {
            var attributes = typeDefinition.Attributes;
            var classFlags = (ClassFlags) 0;

            // The visibility of a nested class is written in the nested shape of it, which is none of the two shapes a
            // class declared at the top of a module is written with, so the one which declares the definition tells the
            // two kinds apart.
            if (typeDefinition.IsNested)
            {
                classFlags |= (attributes & TypeAttributes.VisibilityMask) switch
                {
                    TypeAttributes.NestedPublic      => ClassFlags.Public,
                    TypeAttributes.NestedAssembly    => ClassFlags.Internal,
                    TypeAttributes.NestedFamily      => ClassFlags.Protected,
                    TypeAttributes.NestedPrivate     => ClassFlags.Private,
                    TypeAttributes.NestedFamORAssem  => ClassFlags.Protected | ClassFlags.Internal,
                    TypeAttributes.NestedFamANDAssem => ClassFlags.Private | ClassFlags.Protected,
                    _                                => 0 // No access modifier flag is set
                };
            }
            else
            {
                // A class declared at the top of a module is public unless it names no visibility, and the shape which
                // names none of the visibilities is the internal one.
                classFlags |= (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NotPublic
                    ? ClassFlags.Internal
                    : ClassFlags.Public;
            }

            // The abstract and the sealed shape together are how a static class is written, and the pair is read back as
            // the one flag which asks for it: the shapes are the only way in which the metadata says that, and none of
            // them names a class which was declared static on its own.
            if (typeDefinition is {IsAbstract: true, IsSealed: true}) classFlags |= ClassFlags.Static;
            else if (typeDefinition.IsAbstract) classFlags                       |= ClassFlags.Abstract;
            else if (typeDefinition.IsSealed) classFlags                         |= ClassFlags.Sealed;

            return classFlags;
        }
    }
}