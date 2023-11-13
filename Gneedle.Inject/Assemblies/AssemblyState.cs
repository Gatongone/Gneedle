/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/13-01:59:28
 * Github: https://github.com/Gatongone
 */

namespace Gneedle.Inject;

/// <summary>
/// Assembly dirty flags.
/// </summary>
public enum AssemblyState
{
    /// <summary>
    /// Assembly has no changed.
    /// </summary>
    Fresh,
    
    /// <summary>
    /// Assembly never been saved.
    /// </summary>
    Dirty,
}