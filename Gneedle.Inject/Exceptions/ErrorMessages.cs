namespace Gneedle.Inject;

/// <summary>
/// String resources for exceptions message.
/// </summary>
internal static class ErrorMessages
{
    // Dirty operation exceptions:
    internal const string DIRTY_ASSEMBLY_OPERATION = "The assembly still dirty.";

    // Not supported exceptions:
    internal const string ARCHITECTURE_NOT_SUPPORTED     = "Not supported architecture.";
    internal const string TARGET_FRAMEWORK_NOT_SUPPORTED = "Not supported target framework.";

    // Invalid operation exceptions:
    internal const string INVALID_CONSTRAINT = "The type of constraint is invalid. Type: {0}.";

    // Invalid arguments exceptions:
    internal const string ASSEMBLY_CYCLE_REFERENCE          = "Assembly contains cycle reference. Assembly: \n{0}, \n{1}.";
    internal const string IS_NOT_NON_GENERIC_PARAMETER_TYPE = "The type is should contains any generic parameters or arguments. Type: {0}.";
    internal const string IS_NOT_PARAMETERIZED_GENERIC_TYPE = "The type is not parameterized generic type. Type: {0}.";
    internal const string INVALID_INSTRUCTION_METHOD        = "Invalid instruction: {0}. Type: {1}, Method: {2}";
    internal const string INVALID_TYPE_NAME                 = "Invalid type name.";
    internal const string INVALID_PARAMETERS                = "Invalid parameters.";
    internal const string INVALID_FIELD                     = "Invalid field: {0}.";
    internal const string INVALID_PROPERTY                  = "Invalid property: {0}.";
    internal const string INVALID_METHOD                    = "Invalid method: {0}.";
    internal const string INVALID_GENERIC_PARAMETER         = "The generic parameter named {0} can't be found.";
    internal const string NON_GET_METHOD                    = "The property doesn't contain a get method. Property: {0}";
    internal const string NON_SET_METHOD                    = "The property doesn't contain a set method. Property: {0}";
    internal const string TYPE_HAS_DEFINED                  = "Type {0} has defined.";
    internal const string TYPE_IS_VALUE_TYPE                = "Type cannot be value type.";
    internal const string TYPE_IS_SEALED                    = "Type cannot be sealed type.";
    internal const string TYPE_IS_GENERIC                   = "Type cannot be generic.";
    internal const string TYPE_IS_NOT_GENERIC               = "Type must be generic.";
    internal const string TYPE_IS_INTERFACE                 = "Type cannot be interface.";
    internal const string TYPE_IS_NOT_INTERFACE             = "Type must be interface.";
    internal const string TYPE_CANNOT_ASSIGN_TO_TARGET_TYPE = "Type must be {0}.";
    internal const string LDARG0_CONVERT_FAILED             = "Invalid operation code: ldarg.0";
}