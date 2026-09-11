namespace Gneedle.Inject;

/// <summary>
/// Assembly symbol file type.
/// </summary>
public enum AssemblySymbol
{
    /// <summary>
    /// No symbol file is read or written, so the assembly is written without one.
    /// </summary>
    None,

    /// <summary>
    /// The symbol file of the portable and of the Windows program database.
    /// </summary>
    Pdb,

    /// <summary>
    /// The symbol file of the Mono debug format.
    /// </summary>
    Mdb
}