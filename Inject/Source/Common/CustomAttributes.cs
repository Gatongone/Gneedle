namespace Gneedle.Inject;

/// <summary>
/// The custom attribute which a type definition and the arguments of a constructor build.
/// </summary>
internal static class CustomAttributes
{
    /// <param name="attributeDefinition">Type definition of attribute.</param>
    extension(TypeDefinition attributeDefinition)
    {
        /// <summary>
        /// Create a custom attribute from type definition and constructor arguments.
        /// </summary>
        /// <param name="module">The provider of importing argument type references.</param>
        /// <param name="arguments">Arguments of the attribute constructor calling.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <c>arguments</c> is null, which is a caller who left the arguments out rather than one who gave
        /// none of them.
        /// </exception>
        /// <exception cref="WeavingException">
        /// Thrown when one of the arguments is null, which names no constructor, or when the attribute doesn't contain
        /// any constructor which has argument types match with <c>arguments</c>.
        /// </exception>
        /// <returns>
        /// The custom attribute from <c>attributeDefinition</c> with parameters.
        /// </returns>
        internal CustomAttribute CreateCustomAttribute(ModuleDefinition module, params object[] arguments)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));

            // Get argument types. The type of an argument is what the constructor is looked up by, so a null names no
            // constructor at all and is refused where it stands rather than read as a type of its own.
            var argTypes = new Type[arguments.Length];
            for (var index = 0; index < arguments.Length; index++)
            {
                if (arguments[index] == null)
                {
                    throw new WeavingException(string.Format(ErrorMessages.NULL_ATTRIBUTE_ARGUMENT, attributeDefinition.FullName, index));
                }

                argTypes[index] = arguments[index].GetType();
            }

            // Get the constructor matches with argTypes.
            var method = attributeDefinition.Methods.FirstOrDefault(method => method.IsConstructor && method.Parameters.SameWith(argTypes));
            if (method == null) throw new WeavingException(string.Format(ErrorMessages.INVALID_PARAMETERS, attributeDefinition.FullName));

            // Create custom attribute and append arguments. The constructor is imported rather than used as it is, because
            // the attribute is declared by another assembly whenever the type which carries it is not the one which
            // declares the attribute: Cecil writes a member of another module only through a reference to it.
            var attribute = new CustomAttribute(ModuleLock.Import(module, method));
            for (var index = 0; index < argTypes.Length; index++)
            {
                var argument = new CustomAttributeArgument(ModuleLock.Import(module, argTypes[index]), arguments[index]);
                attribute.ConstructorArguments.Add(argument);
            }

            return attribute;
        }
    }
}