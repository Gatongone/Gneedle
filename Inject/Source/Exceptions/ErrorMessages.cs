namespace Gneedle.Inject;

/// <summary>
/// The messages which the exceptions of this library carry.<para/>
/// A message names the member which could not be woven wherever one is known, because what a build reports is what a
/// caller has left to look at. A message which is a format string is filled in at the throw, and what each of its
/// placeholders stands for is written out here rather than left to be read off the call.
/// </summary>
internal static class ErrorMessages
{
    // Invalid operation exceptions:

    /// <summary>
    /// A member which stands for a member of the type which is woven was called where it is written rather than from a
    /// body which the weaving wrote, so nothing replaced it with the member it stands for.
    /// </summary>
    internal const string INJECTION_NOT_EFFECTIVE = "The member stands for a member of the type which is woven, and it was called where it is written rather than from a member which was woven. Nothing replaced it.";

    /// <summary>
    /// The instructions which a template compiles a symbol of it into are not the shape which the weaving reads. The
    /// placeholder is the name of the member which the symbol names.
    /// </summary>
    internal const string INVALID_IL = "The instructions which the template compiles the member into are not the shape which the weaving reads. Member: {0}.";

    // Not supported exceptions:

    /// <summary>
    /// The process which weaves runs on an architecture which this library cannot read the memory of an image on.
    /// </summary>
    internal const string ARCHITECTURE_NOT_SUPPORTED = "The process which weaves runs on an architecture which is not supported.";

    // Invalid arguments exceptions:

    /// <summary>
    /// A reference to an assembly would leave the two assemblies referring to each other, which metadata cannot hold.
    /// The placeholders are the full name of the assembly which refers and that of the assembly which it would refer
    /// to.
    /// </summary>
    internal const string ASSEMBLY_CYCLE_REFERENCE = "The reference cannot be written, because the two assemblies would refer to each other. Assembly: {0}, Referred to: {1}.";

    /// <summary>
    /// A type which carries generic parameters or generic arguments was given where a plain type is asked for. The
    /// placeholder is the name of the type.
    /// </summary>
    internal const string IS_NOT_NON_GENERIC_PARAMETER_TYPE = "A type which is generic was given where a type without generic arguments is asked for. Type: {0}.";

    /// <summary>
    /// A type which holds no generic parameters was given where a generic type is asked for. The placeholder is the
    /// name of the type.
    /// </summary>
    internal const string IS_NOT_PARAMETERIZED_GENERIC_TYPE = "A type which holds no generic parameters was given where a generic type is asked for. Type: {0}.";

    /// <summary>
    /// An instruction of a template is not one which the weaving reads a value off the stack for. The placeholders are
    /// the instruction, the type whose member it belongs to, and the member itself.
    /// </summary>
    internal const string INVALID_INSTRUCTION_METHOD = "The instruction is not one which a value is read off the stack for. Instruction: {0}. Type: {1}, Method: {2}.";

    /// <summary>
    /// A name does not name a type which the assembly holds.
    /// </summary>
    internal const string INVALID_TYPE_NAME = "The name does not name a type which the assembly holds.";

    /// <summary>
    /// No constructor of an attribute takes the arguments which were given for it.
    /// </summary>
    internal const string INVALID_PARAMETERS = "No constructor of the attribute takes the arguments which were given.";

    /// <summary>
    /// The field which a template names cannot be resolved. The placeholder is that name.
    /// </summary>
    internal const string INVALID_FIELD = "The field cannot be resolved in the assembly which is woven. Field: {0}.";

    /// <summary>
    /// The property which a template names cannot be resolved. The placeholder is that name.
    /// </summary>
    internal const string INVALID_PROPERTY = "The property cannot be resolved in the assembly which is woven. Property: {0}.";

    /// <summary>
    /// The method which a template names cannot be resolved. The placeholder is that name.
    /// </summary>
    internal const string INVALID_METHOD = "The method cannot be resolved in the assembly which is woven. Method: {0}.";

    /// <summary>
    /// A template reads the parameter at a position which the member being woven does not hold. The placeholders are
    /// the position and the member which was being woven.
    /// </summary>
    internal const string INVALID_TEMPLATE_PARAMETER = "A template reads the parameter at the position which the member being woven does not hold. Position: {0}, Method: {1}.";

    /// <summary>
    /// A template names a generic parameter which neither the type being woven nor the member declares. The placeholder
    /// is the name which was given.
    /// </summary>
    internal const string INVALID_GENERIC_PARAMETER = "The generic parameter named {0} is declared by neither the type nor the member which is woven.";

    /// <summary>
    /// The getter of a property which holds no getter was asked for. The placeholder is the name of the property.
    /// </summary>
    internal const string NON_GET_METHOD = "The property holds no getter. Property: {0}.";

    /// <summary>
    /// The setter of a property which holds no setter was asked for. The placeholder is the name of the property.
    /// </summary>
    internal const string NON_SET_METHOD = "The property holds no setter. Property: {0}.";

    /// <summary>
    /// A body was described for an accessor which holds none, which an abstract accessor never does. The placeholder is
    /// the accessor.
    /// </summary>
    internal const string ABSTRACT_ACCESSOR_HOLDS_NO_BODY = "A body cannot be described for an accessor which is abstract, because an abstract accessor holds none. Accessor: {0}.";

    /// <summary>
    /// A type was added to the assembly under a name which the assembly already declares a type by. The placeholder is
    /// that name.
    /// </summary>
    internal const string TYPE_HAS_DEFINED = "The assembly already declares a type of that name. Type: {0}.";

    /// <summary>
    /// A value type was given as the base type of a type which is to be added, which a base type cannot be.
    /// </summary>
    internal const string TYPE_IS_VALUE_TYPE = "A value type cannot be the base type of a type which is added.";

    /// <summary>
    /// A sealed type was given as the base type of a type which is to be added, which nothing can be derived from.
    /// </summary>
    internal const string TYPE_IS_SEALED = "A sealed type cannot be the base type of a type which is added, because nothing can be derived from it.";

    /// <summary>
    /// A type which carries generic parameters was given as a base type or as an interface of a type which is to be
    /// added. The parameters of such a type are given through an <see cref="IType"/> rather than by the type itself,
    /// which the advice that the callers append to this message names.
    /// </summary>
    internal const string TYPE_IS_GENERIC = "A type which carries generic parameters cannot be named by the type itself.";

    /// <summary>
    /// A type which holds no generic parameters was given where a generic type was asked for.
    /// </summary>
    internal const string TYPE_IS_NOT_GENERIC = "A type which holds no generic parameters was given where a generic type is asked for.";

    /// <summary>
    /// An interface was given as the base type of a type which is to be added, which a base type cannot be.
    /// </summary>
    internal const string TYPE_IS_INTERFACE = "An interface cannot be the base type of a type which is added.";

    /// <summary>
    /// A type which is not an interface was given as an interface, whether of a type which is to be added or of one
    /// which is handled.
    /// </summary>
    internal const string TYPE_IS_NOT_INTERFACE = "A type which is not an interface was given as an interface.";

    /// <summary>
    /// A type which cannot be assigned to the type which was asked for was given. The placeholder is the name of the
    /// type which the given one has to be assignable to.
    /// </summary>
    internal const string TYPE_CANNOT_ASSIGN_TO_TARGET_TYPE = "The type has to be assignable to {0}.";

    /// <summary>
    /// The assembly which a <see cref="FromAssemblyAttribute"/> names could not be resolved. The placeholder is the
    /// name of that assembly.
    /// </summary>
    internal const string INVALID_FROM_ASSEMBLY_ASSEMBLY = "The assembly which the FromAssemblyAttribute names cannot be resolved. Assembly: {0}.";

    /// <summary>
    /// The type which a <see cref="FromAssemblyAttribute"/> names was not found in the assembly which the attribute
    /// names. The placeholders are the name of the type and the name of that assembly.
    /// </summary>
    internal const string INVALID_FROM_ASSEMBLY_TYPE = "The type which the FromAssemblyAttribute names cannot be found in the assembly it names. Type: {0}, Assembly: {1}.";

    /// <summary>
    /// The first argument of a template is loaded where the template and the member which is woven are of different
    /// types, so what the argument holds is not what the member is written against.
    /// </summary>
    internal const string LDARG0_CONVERT_FAILED = "The template and the member which is woven are of different types, so the first argument of the template cannot be loaded in it. Instruction: ldarg.0";

    /// <summary>
    /// The array which the image of an assembly was to be written to is smaller than the image. The placeholder is the
    /// number of bytes which the image takes.
    /// </summary>
    internal const string ARRAY_TOO_SMALL_FOR_ASSEMBLY = "The array is too small to hold the assembly. Required size: {0} bytes.";

    /// <summary>
    /// The array which the symbols of an assembly were to be written to is smaller than the symbols. The placeholder is
    /// the number of bytes which the symbols take.
    /// </summary>
    internal const string ARRAY_TOO_SMALL_FOR_SYMBOLS = "The array is too small to hold the symbols of the assembly. Required size: {0} bytes.";

    /// <summary>
    /// The member which was given to describe the getter of a property is not of the shape a getter has, which is one
    /// which takes no parameter and hands a value of the type of the property back. The placeholders are the property
    /// and the type of it.
    /// </summary>
    internal const string GETTER_MEMBER_DOES_NOT_MATCH = "A member which describes the getter takes no parameter and hands back a value of the type of the property. Property: {0}, Type: {1}.";

    /// <summary>
    /// The member which was given to describe the setter of a property is not of the shape a setter has, which is one
    /// which takes the value it sets as its last parameter, of the type of the property. The placeholders are the
    /// property and the type of it.
    /// </summary>
    internal const string SETTER_MEMBER_DOES_NOT_MATCH = "A member which describes the setter takes the value which is set as the last of its parameters, of the type of the property. Property: {0}, Type: {1}.";

    /// <summary>
    /// A body which reads or writes a field was described for a property which is an indexer, which such a body has no
    /// form for: an indexer takes an index beside the value, and the field a body names is a single one. The
    /// placeholder is the property.
    /// </summary>
    internal const string INDEXER_TAKES_NO_FIELD_OPERATION = "A body which reads or writes a field cannot be described for an indexer. Property: {0}.";

    /// <summary>
    /// A constructor of an instance was declared with the flag a static member carries, which a constructor of an
    /// instance has not. The placeholder is the name of the constructor.
    /// </summary>
    internal const string INSTANCE_CONSTRUCTOR_IS_STATIC = "A constructor of an instance cannot be static. Method: {0}.";

    /// <summary>
    /// A constructor of an instance was declared abstract or virtual, which a constructor of an instance is neither.
    /// The placeholder is the name of the constructor.
    /// </summary>
    internal const string INSTANCE_CONSTRUCTOR_IS_ABSTRACT_OR_VIRTUAL = "A constructor of an instance cannot be abstract or virtual. Method: {0}.";

    /// <summary>
    /// A constructor of a type was declared with a visibility other than private, which the constructor of a type can
    /// only be. The placeholder is the name of the constructor.
    /// </summary>
    internal const string STATIC_CONSTRUCTOR_IS_NOT_PRIVATE = "A constructor of a type can only be private. Method: {0}.";

    /// <summary>
    /// A constructor of a type was declared without the flag a static member carries, which the constructor of a type
    /// has to carry. The placeholder is the name of the constructor.
    /// </summary>
    internal const string STATIC_CONSTRUCTOR_IS_NOT_STATIC = "A constructor of a type has to be static. Method: {0}.";

    /// <summary>
    /// A constructor of a type was declared abstract or virtual, which the constructor of a type is neither. The
    /// placeholder is the name of the constructor.
    /// </summary>
    internal const string STATIC_CONSTRUCTOR_IS_ABSTRACT_OR_VIRTUAL = "A constructor of a type cannot be abstract or virtual. Method: {0}.";

    /// <summary>
    /// The value which a member of an enum was given is of a type which the underlying type of an enum cannot hold. The
    /// placeholder is the name of the type of the value.
    /// </summary>
    internal const string INVALID_ENUM_UNDERLYING_TYPE = "The value which the member of the enum was given is of a type which an enum cannot hold. Type: {0}, which has to be one of sbyte, byte, short, ushort, int, uint, long or ulong.";

    /// <summary>
    /// A token names a generic parameter at a position which neither the type being woven nor the member declares. The
    /// placeholders are the token and the name of what its parameters were counted on.
    /// </summary>
    internal const string GENERIC_PARAMETER_OUT_OF_RANGE = "The token names a generic parameter at a position which is not declared. Token: {0}, Declared by: {1}.";

    // Around body exceptions:

    /// <summary>
    /// The body of a member which holds none was to be woven around, which a member without a body has not. The
    /// placeholder is the member.
    /// </summary>
    internal const string AROUND_BODY_TARGET_HAS_NO_BODY = "The member holds no body to weave around. Method: {0}.";

    /// <summary>
    /// A constructor was to be woven around, whose body no template may take over. The placeholder is the constructor.
    /// </summary>
    internal const string AROUND_BODY_TARGET_IS_CONSTRUCTOR = "A constructor cannot be woven around. Method: {0}.";

    /// <summary>
    /// A member which is woven around already was. The placeholder is the member.
    /// </summary>
    internal const string AROUND_BODY_ALREADY_SET = "The member is already woven around. Method: {0}.";

    /// <summary>
    /// The parameters of the template which is woven around a member are not the parameters of that member, so the call
    /// which proceeds into the body that was taken over could not be written. The placeholders are the member and the
    /// template.
    /// </summary>
    internal const string AROUND_BODY_PARAMETERS_MISMATCH = "The parameters of the template are not the parameters of the member. Method: {0}, Template: {1}.";

    /// <summary>
    /// The return type of the template which is woven around a member is not the return type of that member. The
    /// placeholders are the member and the template.
    /// </summary>
    internal const string AROUND_BODY_RETURN_TYPE_MISMATCH = "The return type of the template is not the return type of the member. Method: {0}, Template: {1}.";

    /// <summary>
    /// The assembly already declares a member of the name which is given to the method that holds the body which was
    /// taken over. The placeholder is that name.
    /// </summary>
    internal const string AROUND_BODY_GENERATED_NAME_OCCUPIED = "The assembly already declares a member of the name which the generated method takes. Name: {0}.";

    /// <summary>
    /// A template calls <see cref="Proceed"/> although no body was taken over for the call to proceed into. The
    /// placeholder is the template itself.
    /// </summary>
    internal const string PROCEED_WITHOUT_AROUND_BODY = "A template calls Proceed although no body was taken over for it to proceed into. Method: {0}.";
}
