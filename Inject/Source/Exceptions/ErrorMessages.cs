namespace Gneedle.Inject;

/// <summary>
/// The messages which the exceptions of this library carry.<para/>
/// A message names the member which could not be woven wherever one is known, because what a build reports is what a
/// caller has left to look at. A message which is a format string is filled in at the throw, and what each of its
/// placeholders stands for is written out here rather than left to be read off the call.
/// </summary>
internal static class ErrorMessages
{
    // Not supported exceptions:

    /// <summary>
    /// The architecture of the process which weaves is not one which this library reads the memory of an image on.
    /// </summary>
    internal const string ARCHITECTURE_NOT_SUPPORTED = "Not supported architecture.";

    // Invalid arguments exceptions:

    /// <summary>
    /// A reference to an assembly would leave the two assemblies referring to each other, which metadata cannot hold.
    /// The placeholders are the full name of the assembly which refers and that of the assembly which it would refer
    /// to.
    /// </summary>
    internal const string ASSEMBLY_CYCLE_REFERENCE          = "Assembly contains cycle reference. Assembly: \n{0}, \n{1}.";

    /// <summary>
    /// A type which has to carry generic parameters or generic arguments carries none. The placeholder is the name of
    /// the type.
    /// </summary>
    internal const string IS_NOT_NON_GENERIC_PARAMETER_TYPE = "The type is should contains any generic parameters or arguments. Type: {0}.";

    /// <summary>
    /// A type which has to be a generic type which was given its arguments is not one. The placeholder is the name of
    /// the type.
    /// </summary>
    internal const string IS_NOT_PARAMETERIZED_GENERIC_TYPE = "The type is not parameterized generic type. Type: {0}.";

    /// <summary>
    /// An instruction of a template is not one which the weaving reads a value off the stack for. The placeholders are
    /// the instruction, the type whose member it belongs to, and the member itself.
    /// </summary>
    internal const string INVALID_INSTRUCTION_METHOD        = "Invalid instruction: {0}. Type: {1}, Method: {2}";

    /// <summary>
    /// A name does not name a type which the assembly holds.
    /// </summary>
    internal const string INVALID_TYPE_NAME                 = "Invalid type name.";

    /// <summary>
    /// No constructor of an attribute matches the arguments which were given for it.
    /// </summary>
    internal const string INVALID_PARAMETERS                = "Invalid parameters.";

    /// <summary>
    /// A name does not name a field which the assembly holds. The placeholder is that name.
    /// </summary>
    internal const string INVALID_FIELD                     = "Invalid field: {0}.";

    /// <summary>
    /// A name does not name a property which the assembly holds. The placeholder is that name.
    /// </summary>
    internal const string INVALID_PROPERTY                  = "Invalid property: {0}.";

    /// <summary>
    /// A name does not name a method which the assembly holds. The placeholder is that name.
    /// </summary>
    internal const string INVALID_METHOD                    = "Invalid method: {0}.";

    /// <summary>
    /// A template reads the parameter at a position which the member being woven does not hold. The placeholders are
    /// the position and the member which was being woven.
    /// </summary>
    internal const string INVALID_TEMPLATE_PARAMETER        = "The template refers to the parameter of position {0}, which the method being woven does not hold. Method: {1}.";

    /// <summary>
    /// A template names a generic parameter which neither the type being woven nor the member declares. The placeholder
    /// is the name which was given.
    /// </summary>
    internal const string INVALID_GENERIC_PARAMETER         = "The generic parameter named {0} can't be found.";

    /// <summary>
    /// The getter of a property which holds no getter was asked for. The placeholder is the property.
    /// </summary>
    internal const string NON_GET_METHOD                    = "The property doesn't contain a get method. Property: {0}";

    /// <summary>
    /// The setter of a property which holds no setter was asked for. The placeholder is the property.
    /// </summary>
    internal const string NON_SET_METHOD                    = "The property doesn't contain a set method. Property: {0}";

    /// <summary>
    /// A body was described for an accessor which holds none, which an abstract accessor never does. The placeholder is
    /// the accessor.
    /// </summary>
    internal const string ABSTRACT_ACCESSOR_HOLDS_NO_BODY   = "An abstract accessor holds no body, so none can be described for it. Accessor: {0}.";

    /// <summary>
    /// A type was added to the assembly under a name which the assembly already declares a type by. The placeholder is
    /// that name.
    /// </summary>
    internal const string TYPE_HAS_DEFINED                  = "Type {0} has defined.";

    /// <summary>
    /// A value type was given where a class was to be added, which the caller asks for with the other overload.
    /// </summary>
    internal const string TYPE_IS_VALUE_TYPE                = "Type cannot be value type.";

    /// <summary>
    /// A sealed type was given as the base type of a type which is to be added, which nothing can be derived from.
    /// </summary>
    internal const string TYPE_IS_SEALED                    = "Type cannot be sealed type.";

    /// <summary>
    /// A type which carries generic parameters was given as a base type or as an interface of a type which is to be
    /// added. The parameters of such a type are given through an <see cref="IType"/> rather than by the type itself.
    /// </summary>
    internal const string TYPE_IS_GENERIC                   = "Type cannot be generic.";

    /// <summary>
    /// A type which holds no generic parameters was given where a generic type was asked for.
    /// </summary>
    internal const string TYPE_IS_NOT_GENERIC               = "Type must be generic.";

    /// <summary>
    /// An interface was given as the base type of a type which is to be added, which a base type cannot be.
    /// </summary>
    internal const string TYPE_IS_INTERFACE                 = "Type cannot be interface.";

    /// <summary>
    /// A type which is not an interface was given as an interface of a type which is to be added.
    /// </summary>
    internal const string TYPE_IS_NOT_INTERFACE             = "Type must be interface.";

    /// <summary>
    /// A type which is not derived from the type which was asked for was given. The placeholder is the name of the type
    /// which the given one has to be derived from.
    /// </summary>
    internal const string TYPE_CANNOT_ASSIGN_TO_TARGET_TYPE = "Type must be {0}.";

    /// <summary>
    /// The assembly which a <see cref="FromAssemblyAttribute"/> names could not be resolved. The placeholder is the
    /// name of that assembly.
    /// </summary>
    internal const string INVALID_FROM_ASSEMBLY_ASSEMBLY    = "The assembly {0} which the FromAssemblyAttribute names can't be resolved.";

    /// <summary>
    /// The type which a <see cref="FromAssemblyAttribute"/> marks was not found in the assembly which the attribute
    /// names. The placeholders are the name of the type and the name of that assembly.
    /// </summary>
    internal const string INVALID_FROM_ASSEMBLY_TYPE        = "The type {0} can't be found in the assembly {1} which the FromAssemblyAttribute names.";

    /// <summary>
    /// A template loads the first argument of a method which is static and is woven into a member which is not, or the
    /// other way round, so there is no receiver for the first argument to be loaded as.
    /// </summary>
    internal const string LDARG0_CONVERT_FAILED             = "Invalid operation code: ldarg.0";

    // Around body exceptions:

    /// <summary>
    /// The body of a member which holds none was to be woven around, which a member without a body has not. The
    /// placeholder is the member.
    /// </summary>
    internal const string AROUND_BODY_TARGET_HAS_NO_BODY      = "The method holds no body to weave around. Method: {0}.";

    /// <summary>
    /// A constructor was to be woven around, whose body no template may take over. The placeholder is the constructor.
    /// </summary>
    internal const string AROUND_BODY_TARGET_IS_CONSTRUCTOR   = "A constructor cannot be woven around. Method: {0}.";

    /// <summary>
    /// A member which is woven around already was. The placeholder is the member.
    /// </summary>
    internal const string AROUND_BODY_ALREADY_SET             = "The method is already woven around. Method: {0}.";

    /// <summary>
    /// The parameters of the template which is woven around a member are not the parameters of that member, so the call
    /// which proceeds into the body which was taken over could not be written. The placeholders are the member and the
    /// template.
    /// </summary>
    internal const string AROUND_BODY_PARAMETERS_MISMATCH     = "The parameters of the template do not match the method. Method: {0}, Template: {1}.";

    /// <summary>
    /// The return type of the template which is woven around a member is not the return type of that member. The
    /// placeholders are the member and the template.
    /// </summary>
    internal const string AROUND_BODY_RETURN_TYPE_MISMATCH    = "The return type of the template does not match the method. Method: {0}, Template: {1}.";

    /// <summary>
    /// The assembly already declares a member of the name which is given to the method that holds the body which was
    /// taken over. The placeholder is that name.
    /// </summary>
    internal const string AROUND_BODY_GENERATED_NAME_OCCUPIED = "The name of the generated method is taken. Name: {0}.";

    /// <summary>
    /// A template calls <see cref="Proceed"/> from a member which is not woven around, so there is no body for the call
    /// to proceed into. The placeholder is the member, which is the template itself.
    /// </summary>
    internal const string PROCEED_WITHOUT_AROUND_BODY         = "Proceed is used by a method which is not woven around. Method: {0}.";
}
