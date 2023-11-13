/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/13-02:22:53
 * Github: https://github.com/Gatongone
 */

namespace Gneedle.Inject;

/// <summary>
/// Exception with any not supported case.
/// </summary>
public class NotSupportException : Exception
{
    public NotSupportException(string message) : base(message) { }
}