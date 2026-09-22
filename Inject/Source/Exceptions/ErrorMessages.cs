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

    /// <summary>
    /// The chain which describes a member was asked for a part of it after the member was built, which that member
    /// holds nothing of: the chain is read where it builds the member, and what is described after that point reaches
    /// nothing. The placeholder is the member.
    /// </summary>
    internal const string MEMBER_IS_ALREADY_BUILT = "The member which the decorator describes was built, so a part which is described after that is described to nothing. Member: {0}.";

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
    /// A reference names a type which the assembly it was asked of does not hold, so no definition of that type can be
    /// read. The placeholder is the type which the reference names.
    /// </summary>
    internal const string TYPE_CANNOT_BE_READ = "The type is not one which the assembly it was asked of holds, so nothing of the type can be read. Type: {0}.";

    /// <summary>
    /// An enum declares no field which holds the value of one of its members, so there is no type which its values are
    /// read as. The placeholder is the enum.
    /// </summary>
    internal const string ENUM_DECLARES_NO_VALUE_FIELD = "The enum declares no field which holds the value of a member of it, so the type which its values are read as cannot be told. Type: {0}.";

    /// <summary>
    /// No constructor of an attribute takes the arguments which were given for it. The placeholder is the attribute
    /// which the arguments were given for.
    /// </summary>
    internal const string INVALID_PARAMETERS = "No constructor of the attribute takes the arguments which were given. Attribute: {0}.";

    /// <summary>
    /// A null was given as one of the arguments of an attribute, which names no constructor: the type of an argument is
    /// what a constructor is looked up by. The placeholders are the attribute which the arguments were given for and the
    /// position of the null among them.
    /// </summary>
    internal const string NULL_ATTRIBUTE_ARGUMENT = "A null names no constructor, because the type of an argument is what one is looked up by. Attribute: {0}, Position: {1}.";

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
    /// A call which the template wrote names a member whose declaring type cannot be read, so the walk of the argument
    /// stack cannot tell whether the call is made on an instance — which is what the count of the values it takes the
    /// receiver off the stack with is read from. The placeholders are the member which is called and the member which is
    /// woven.
    /// </summary>
    internal const string INVALID_CALLED_MEMBER = "A member which the template calls is declared by a type which cannot be read, so whether the call belongs to an instance cannot be told, and the values which stand above it in the body cannot be counted. Member: {0}, Method: {1}.";

    /// <summary>
    /// The handle of a field or a property was held in a local and read for something other than the member which the
    /// handle stands for, which is a value which the weaving has no way to write. The placeholder is the name of the
    /// member.
    /// </summary>
    internal const string INVALID_HELD_HANDLE = "The value member which the template holds in a local is read for something other than the member itself, which the weaving has no way to write. Member: {0}.";

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
    /// A template named a member which declares a generic parameter of its own which stands for no parameter of the
    /// member being woven, so the call of it cannot be written: a parameter of the member is named by the token of the
    /// template which stands for it, which is the parameter of the body or of the type which declares it bearing its
    /// name, or the parameter of the body at the position of it where no name ties it to one, and a parameter which
    /// stands for none of those is left open by the call, which is one the runtime refuses to run. The placeholders are
    /// the member and the member which is woven.
    /// </summary>
    internal const string INVALID_GENERIC_MEMBER_CALL = "A member which the template names declares a generic parameter of its own which no parameter of the member being woven stands for, so the call of it cannot be written: a parameter of the member is named by the token of the template which stands for it, which is the parameter of the member being woven or of the type which declares it bearing its name, or the parameter of the member being woven which stands at the position of it where no name ties it to one, and a call which leaves the parameter of the member open is one the runtime refuses to run. Member: {0}, Method: {1}.";

    /// <summary>
    /// The delegate of the template describes a member which declares a generic parameter of its own with a signature
    /// which hands back a type that the instantiation which its parameters bind does not hand back, so the delegate
    /// names no member: what a member hands back is what tells one instantiation of it from another. The placeholders
    /// are the member and the member which is woven.
    /// </summary>
    internal const string INVALID_GENERIC_MEMBER_SIGNATURE = "A member which the template names declares a generic parameter of its own, and the signature which the delegate describes it with hands back a type which the instantiation which the parameters of that signature bind does not hand back, so the delegate names no member rather than another instantiation of one: a member which stands open is one the runtime refuses to run, and a call of it which names the parameters of the member being woven instead is a call of another instantiation than the one the delegate described. Member: {0}, Method: {1}.";

    /// <summary>
    /// The delegate of the template hands back a value where the member hands back none, or a value of another type
    /// than the member hands back, so the delegate describes no member: the value which the call leaves where the symbol
    /// stood is the one the body hands back, and a body which hands back another value than the member, or hands one
    /// back where the member hands none, is one the runtime refuses to run. The placeholders are the member and the
    /// member which is woven.
    /// </summary>
    internal const string INVALID_MEMBER_RETURN_TYPE = "The value which the delegate of the template hands back is not the one which the member hands back, so the delegate describes no member: the call of the member leaves the value which the body hands back, and a body which hands back another value than the member does, or a value where the member hands back none, is one the runtime refuses to run. Member: {0}, Method: {1}.";

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
    /// A template calls <see cref="Proceed.Invoke{TResult}"/> for a type which the member being woven does not hand
    /// back. The placeholders are the type which the call names and the member.
    /// </summary>
    internal const string PROCEED_INVOKE_RETURN_TYPE_MISMATCH = "The type which the call to Proceed.Invoke hands back is not the type which the member being woven hands back. Type: {0}, Method: {1}.";

    /// <summary>
    /// A placeholder is given a name which is not written where the call is, so there is no instruction ahead of it for
    /// the weaving to read the name out of. The placeholders are the member which the call names and the member being
    /// woven.
    /// </summary>
    internal const string NAME_IS_NOT_WRITTEN = "The name which this placeholder is given is not written where the call is. The weaving reads the name of a member out of the instruction ahead of the call, so a name is a literal, a nameof, or a constant of the template, rather than one which the template computes. Member: {0}, Method: {1}.";

    /// <summary>
    /// The template names a type which the compiler wrote for a body of the template's own — a lambda, a local
    /// function, an async body or an iterator — which holds that body as a method of its own rather than as
    /// instructions of the template. The placeholders are the reference which names it and the member.
    /// </summary>
    internal const string TEMPLATE_HOLDS_A_METHOD_OF_ITS_OWN = "The template names a type which the compiler wrote for a body of the template's own, which is a lambda, a local function, an async body or an iterator: what such a type holds is a method of its own rather than instructions of the template, so the weaving cannot carry it. Reference: {0}, Method: {1}.";

    /// <summary>
    /// The template calls a member which the compiler wrote as the body of a lambda or of a local function of the
    /// template's own, which is written on the type which declares the template rather than as a type of its own. The
    /// placeholders are the reference which names it and the member.
    /// </summary>
    internal const string TEMPLATE_HOLDS_A_BODY_OF_ITS_OWN = "The template calls a member which the compiler wrote for a body of the template's own, which is the body of a lambda or of a local function written inside the template: what such a member holds is a body of its own rather than instructions of the template, so the weaving cannot carry it. Reference: {0}, Method: {1}.";

    /// <summary>
    /// The template belongs to an instance and reads it, which is the template's own receiver rather than an argument
    /// of the member being woven. The placeholder is the member.
    /// </summary>
    internal const string TEMPLATE_READS_ITS_OWN_INSTANCE = "The template reads the instance which it belongs to, which is no argument of the member being woven. A template is a static method, and a lambda which captures a variable is an instance method of the type which holds the capture. Method: {0}.";

    /// <summary>
    /// The template reaches a member of an instance through the instance which the member being woven belongs to, and
    /// that member is static, so it belongs to none. The placeholder is the member of the member being woven.
    /// </summary>
    internal const string STATIC_MEMBER_REACHES_AN_INSTANCE_MEMBER = "The member which the template reaches belongs to an instance, and the member being woven is static and belongs to none: the first argument of it stands where that instance would be loaded from, which the template was handed for something else. An instance which a static member reaches a member of is one it was handed, which is what Instance names. Method: {0}.";

    /// <summary>
    /// A body which the compiler wrote for a body of the template's own proceeds into the body which was taken over
    /// from the member, which it cannot be handed the arguments of. The placeholders are the reference which names the
    /// call and the member being woven.
    /// </summary>
    internal const string PROCEED_IN_A_BODY_OF_ITS_OWN = "The template proceeds into the body which was taken over from a body of its own, which is a lambda, a local function, an iterator or an async body: the arguments which the call hands over are the arguments of the template, which the member being woven was given, and such a body is written with arguments of its own rather than with them. Reference: {0}, Method: {1}.";

    /// <summary>
    /// A body which the compiler wrote for a body of the template's own reaches a member of an instance, and the field
    /// which that body holds its instance in is not one of the member being woven: either the body captured no instance
    /// and holds no such field, or the instance it holds is one of another type, which is the case of a template
    /// declared in a type other than the one it is woven into. The placeholder is the member being woven.
    /// </summary>
    internal const string A_BODY_OF_ITS_OWN_REACHES_NO_INSTANCE = "The template reaches a member of an instance from a body of its own, which is a lambda, a local function, an iterator or an async body, and that body can reach no instance of the member being woven: the compiler writes the instance a body belongs to into a field of the type it writes for that body, and that field is either not there, because the body captured no instance, or holds an instance of the type the template was declared in rather than one of the type being woven. Method: {0}.";

    /// <summary>
    /// A body which the compiler wrote for a body of the template's own, and which holds no body to copy. The
    /// placeholder is the member which the compiler wrote.
    /// </summary>
    internal const string A_BODY_OF_ITS_OWN_HOLDS_NO_BODY = "The template reaches a member which the compiler wrote for a body of its own, and that member holds no body to copy: there are no instructions of it to carry. Member: {0}.";

    /// <summary>
    /// A body which the compiler wrote for a body of the template's own holds an instruction whose operand is of a kind
    /// the carrying does not write. The placeholders are the instruction and the member which the compiler wrote it in.
    /// </summary>
    internal const string A_BODY_OF_ITS_OWN_HOLDS_AN_OPERAND = "The body which the compiler wrote for a body of the template's own holds an instruction whose operand the carrying cannot write. Instruction: {0}, Member: {1}.";

    /// <summary>
    /// The template holds no body to copy, which a member which is abstract, or which is a pinvoke, or which an
    /// interface declares does. The placeholder is the template.
    /// </summary>
    internal const string TEMPLATE_HAS_NO_BODY = "The template holds no body, which a member which is abstract, a pinvoke, or a method which an interface declares is: there are no instructions of it to weave with. Template: {0}.";

    /// <summary>
    /// A body which a delegate describes was given to a decorator which this library does not build, which holds
    /// nothing to write what the template captured into: the delegate is given rather than the method alone because the
    /// value of a capture is read out of it, and a body which is described without that value is not the body which the
    /// template says. The placeholder is the decorator.
    /// </summary>
    internal const string DECORATOR_HOLDS_NO_CAPTURE = "The decorator is not one which this library builds, so the delegate cannot be read for what the template captured. Decorator: {0}.";

    /// <summary>
    /// A template which a delegate holds was given to a handler which this library does not build, which holds nothing
    /// to write what the template captured into. The placeholder is the handler.
    /// </summary>
    internal const string HANDLER_HOLDS_NO_CAPTURE = "The handler is not one which this library builds, so the delegate cannot be read for what the template captured. Handler: {0}.";

    /// <summary>
    /// The template reads a variable which it captured, and a value of that type cannot be written into the member
    /// being woven, where the value would have to be held. The placeholders are the name of the field which holds it,
    /// the name of the type, and the member.
    /// </summary>
    internal const string TEMPLATE_CAPTURE_CANNOT_BE_WRITTEN = "The template captures a variable whose value cannot be written into the member being woven, where a type, a string, a number, a character, a boolean, an enumeration, a null of a reference type can. Field: {0}, Type: {1}, Method: {2}.";

    /// <summary>
    /// The template captured a type which the weaver itself declares, which the assembly being woven must not name: the
    /// attributes which the injectors are read from, and the reference to the weaver which they name, are taken out of
    /// the assembly once they have been applied, and the token of one of the types of the weaver would name it again.
    /// The placeholders are the name of the field which holds the value, the name of the type which was captured, and
    /// the member.
    /// </summary>
    internal const string TEMPLATE_CAPTURE_NAMES_THE_WEAVER = "The template captured a type of the weaver itself, which the assembly being woven does not name: the weaving takes the reference to the weaver out of the member, and a token of one of its types would name it again. Field: {0}, Type: {1}, Method: {2}.";

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