/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/13-01:47:29
 * Github: https://github.com/Gatongone
 */
    
namespace Gneedle.Inject;

/// <summary>
/// String resources for exceptions message.
/// </summary>
internal static class ErrorResources
{
    // Dirty operation exceptions:
    internal const string DIRTY_ASSEMBLY_OPERATION = "The assembly still dirty.";
    
    // Not supported exceptions:
    internal const string ARCHITECTURE_NOT_SUPPORTED = "Not supported architecture.";
    
    // Invalid operation exceptions:
    internal const string INVALID_CONSTRAINT = "The type of constraint is invalid. Type: {0}.";
    
    // Invalid arguments:
    internal const string IS_NOT_NON_GENERIC_PARAMETER_TYPE = "The type is should contains any generic parameters or arguments. Type: {0}";
    internal const string IS_NOT_PARAMETERIZED_GENERIC_TYPE = "The type is not parameterized generic type. Type: {0}";
    internal const string INVALID_TYPE_NAME = "Invalid type name.";
}