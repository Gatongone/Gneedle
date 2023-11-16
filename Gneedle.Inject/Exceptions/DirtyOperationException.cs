/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/13-04:18:39
 * Github: https://github.com/Gatongone
 */

namespace Gneedle.Inject;

/// <summary>
/// Exception with any not supported case.
/// </summary>
public class DirtyOperationException(string message) : Exception(message);