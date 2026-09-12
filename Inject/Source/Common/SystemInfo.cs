namespace Gneedle.Inject;

/// <summary>
/// Provide information about executing System.
/// </summary>
internal static class SystemInfo
{
    /// <summary>
    /// Current CPU architecture definition.
    /// </summary>
    /// <exception cref="NotSupportException">No supported architecture.</exception>
    internal static readonly TargetArchitecture Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X86   => TargetArchitecture.I386,
        System.Runtime.InteropServices.Architecture.X64   => TargetArchitecture.AMD64,
        System.Runtime.InteropServices.Architecture.Arm   => TargetArchitecture.ARM,
        System.Runtime.InteropServices.Architecture.Arm64 => TargetArchitecture.ARM64,
        _                                                 => throw new NotSupportException(ErrorMessages.ARCHITECTURE_NOT_SUPPORTED)
    };
}