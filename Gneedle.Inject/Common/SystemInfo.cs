/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/13-02:13:22
 * Github: https://github.com/Gatongone
 */

namespace Gneedle.Inject;

/// <summary>
/// Provide information about executing System.
/// </summary>
internal static class SystemInfo
{
    /// <summary>
    /// Current CPU architecture definition.
    /// </summary>
    /// <exception cref="NotSupportException">Not supported architecture.</exception>
    internal static readonly TargetArchitecture Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X86   => TargetArchitecture.I386,
        System.Runtime.InteropServices.Architecture.X64   => TargetArchitecture.IA64,
        System.Runtime.InteropServices.Architecture.Arm   => TargetArchitecture.ARM,
        System.Runtime.InteropServices.Architecture.Arm64 => TargetArchitecture.ARM64,
        _                                                 => throw new NotSupportException(ErrorMessages.ARCHITECTURE_NOT_SUPPORTED)
    };
}