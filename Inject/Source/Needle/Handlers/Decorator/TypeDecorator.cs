namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing class implementation and generic information.
/// </summary>
public class ClassDecorator : ClassDecorator.IGenericParametersDecorator
{
    /// <summary>
    /// Assembly handler for resolving and importing type.
    /// </summary>
    private readonly AssemblyHandler m_AssemblyHandler;

    /// <summary>
    /// Describing target.
    /// </summary>
    private readonly TypeDefinition m_TypeDefinition;

    /// <summary>
    /// Callback on <see cref="GetHandler"/> method.
    /// </summary>
    private readonly Func<TypeDefinition, TypeReference?, IClassHandler> m_BuildCallback;

    /// <summary>
    /// The base type which the class is described with, or null while none was described.
    /// </summary>
    private TypeReference? m_BaseType;

    /// <summary>
    /// The handler of the class which the chain built, or null while the class is still being described: the chain
    /// answers with it from the point where it ends, which is what makes a chain which is asked for the handler of the
    /// class twice append one class to the module rather than two of the same name.
    /// </summary>
    private IClassHandler? m_Handler;

    /// <summary>
    /// Create a decorator which describes a class before it is appended to the module.
    /// </summary>
    /// <param name="assemblyHandler">Handler of the assembly which the class is appended to.</param>
    /// <param name="typeDefinition">The class definition which is described.</param>
    /// <param name="baseType">The base type which the class is described with, or null when none is described yet.</param>
    /// <param name="buildCallback">Callback which appends the class to the module and returns its handler.</param>
    internal ClassDecorator(AssemblyHandler assemblyHandler, TypeDefinition typeDefinition, TypeReference? baseType, Func<TypeDefinition, TypeReference?, IClassHandler> buildCallback)
    {
        m_AssemblyHandler = assemblyHandler;
        m_TypeDefinition  = typeDefinition;
        m_BaseType        = baseType;
        m_BuildCallback   = buildCallback;
    }

    /// <inheritdoc/>
    public IGenericParametersDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_TypeDefinition.Name);

        var genericParameterType = new GenericParameterType(genericParameterName, constraints);
        var genericParameter = new GenericParameter(genericParameterName, m_TypeDefinition)
        {
            // Append generic parameter attributes to constraint.
            Attributes = genericParameterType.GetGenericParameterAttributes()
        };
        m_TypeDefinition.GenericParameters.Add(genericParameter);

        // Process every constraint from type.
        foreach (var constraint in constraints)
        {
            genericParameter.SetConstraintFromType(m_AssemblyHandler, m_TypeDefinition, constraint);
        }

        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithBaseType(Type type)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_TypeDefinition.Name);

        switch (type)
        {
            case null:                              throw new WeavingException(string.Format(ErrorMessages.TYPE_IS_NULL, nameof(type)));
            case {IsValueType              : true}: throw new WeavingException(ErrorMessages.TYPE_IS_VALUE_TYPE);
            case {IsSealed                 : true}: throw new WeavingException(ErrorMessages.TYPE_IS_SEALED);
            case {IsInterface              : true}: throw new WeavingException(ErrorMessages.TYPE_IS_INTERFACE);
            case {ContainsGenericParameters: true}: throw new WeavingException($"{ErrorMessages.TYPE_IS_GENERIC} Please use WithBaseType(IType) instead.");
        }

        // CecilType.Reference is owned by the target module already, so it can be appended as it is.
        m_BaseType = m_AssemblyHandler.GetCecilType(type).Reference;
        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithBaseType(IType type)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_TypeDefinition.Name);

        // Resolve and append to base type.
        m_BaseType = m_AssemblyHandler.ResolveParameterType(m_TypeDefinition, type);
        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithInterface(IType type)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_TypeDefinition.Name);

        // Resolve and append to collections.
        var implementation = new InterfaceImplementation(m_AssemblyHandler.ResolveParameterType(m_TypeDefinition, type));
        m_TypeDefinition.Interfaces.Add(implementation);
        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithInterface(Type type)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_TypeDefinition.Name);

        switch (type)
        {
            case null:                               throw new WeavingException(string.Format(ErrorMessages.TYPE_IS_NULL, nameof(type)));
            case {IsInterface              : false}: throw new WeavingException(ErrorMessages.TYPE_IS_NOT_INTERFACE);
            case {ContainsGenericParameters: true}:  throw new WeavingException($"{ErrorMessages.TYPE_IS_GENERIC} Please use WithInterface(IType) instead.");
        }

        // CecilType.Reference is owned by the target module already, so it can be appended as it is.
        var implementation = new InterfaceImplementation(m_AssemblyHandler.GetCecilType(type).Reference);
        m_TypeDefinition.Interfaces.Add(implementation);
        return this;
    }

    /// <inheritdoc/>
    public IClassHandler GetHandler()
    {
        // The class is appended to the module once, and the chain answers with the handler of it from then on: a chain
        // which is asked for the handler of the class twice appends one class rather than two of the same name.
        if (m_Handler is { } built) return built;

        // Default from object inheritance.
        if (m_BaseType == null)
        {
            WithBaseType(typeof(object));
        }

        // Build and append to module.
        m_Handler = m_BuildCallback.Invoke(m_TypeDefinition, m_BaseType);
        return m_Handler;
    }

    /// <summary>
    /// Decorator which completes the class. It is the end of the chain, which asks for the handler of the class which
    /// the chain built and for nothing else.
    /// </summary>
    public interface ITypeDecorator
    {
        /// <summary>
        /// Build the class into the module and answer with the handler of the class which was built.<para/>
        /// The class is appended once: the chain answers with the handler of it from then on, and a part which is
        /// described after that point is refused.
        /// </summary>
        /// <returns>Handler for class.</returns>
        IClassHandler GetHandler();
    }

    /// <summary>
    /// Decorator for describing class interface. It is the level of the interfaces, which are the parts the base type
    /// is made up with, so either of them may be described before the other and either may be left out.
    /// </summary>
    public interface IInterfaceDecorator : ITypeDecorator
    {
        /// <summary>
        /// Append interface to type.
        /// </summary>
        /// <param name="type">Interface type of type</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the class was already built.</exception>
        IInterfaceDecorator WithInterface(Type type);

        /// <summary>
        /// Append interface to type.
        /// </summary>
        /// <param name="type">Interface type of type</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the class was already built.</exception>
        IInterfaceDecorator WithInterface(IType type);
    }

    /// <summary>
    /// Decorator for describing class base type. It is the level of the base type, which asks for the interfaces as
    /// well, because they are the parts which come after it.
    /// </summary>
    public interface IBaseTypeDecorator : IInterfaceDecorator
    {
        /// <summary>
        /// Append base type to type.
        /// </summary>
        /// <param name="type">Base type of type.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the class was already built.</exception>
        IInterfaceDecorator WithBaseType(Type type);

        /// <summary>
        /// Append base type to type.
        /// </summary>
        /// <param name="type">Base type of type.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the class was already built.</exception>
        IInterfaceDecorator WithBaseType(IType type);
    }

    /// <summary>
    /// Decorator for describing class generic parameters. It is the entry of the chain, which holds a level for each
    /// part of a class in the order the parts depend on each other: the generic parameters, the base type and the
    /// interfaces. A level asks for the parts of itself and of the levels after it, so a part which the class does not
    /// hold is passed by rather than described, and a level which was passed by is not asked for again.<para/>
    /// The chain describes the class until the class is appended to the module, which is where it ends: a part which is
    /// described after that is refused, because what the chain holds is read where the class is built and nothing reads
    /// it afterwards.
    /// </summary>
    public interface IGenericParametersDecorator : IBaseTypeDecorator
    {
        /// <summary>
        ///  Append generic parameter to type.
        /// </summary>
        /// <param name="genericParameterName">Generic parameter name.</param>
        /// <param name="constraints">Generic parameter constrains.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the class was already built.</exception>
        IGenericParametersDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints);
    }
}

/// <summary>
/// Decorator for describing class implementation and generic information.
/// </summary>
public class StructDecorator : StructDecorator.IGenericParametersDecorator
{
    /// <summary>
    /// Assembly handler for resolving and importing type.
    /// </summary>
    private readonly AssemblyHandler m_AssemblyHandler;

    /// <summary>
    /// Describing target.
    /// </summary>
    private readonly TypeDefinition m_TypeDefinition;

    /// <summary>
    /// Callback on <see cref="GetHandler"/> method.
    /// </summary>
    private readonly Func<TypeDefinition, TypeReference?, IStructHandler> m_BuildCallback;

    /// <summary>
    /// The base type which the struct is described with, or null while none was described.
    /// </summary>
    private TypeReference? m_BaseType;

    /// <summary>
    /// The handler of the struct which the chain built, or null while the struct is still being described: the chain
    /// answers with it from the point where it ends, which is what makes a chain which is asked for the handler of the
    /// struct twice append one struct to the module rather than two of the same name.
    /// </summary>
    private IStructHandler? m_Handler;

    /// <summary>
    /// Create a decorator which describes a struct before it is appended to the module.
    /// </summary>
    /// <param name="assemblyHandler">Handler of the assembly which the struct is appended to.</param>
    /// <param name="typeDefinition">The struct definition which is described.</param>
    /// <param name="baseType">The base type which the struct is described with, or null when none is described yet.</param>
    /// <param name="buildCallback">Callback which appends the struct to the module and returns its handler.</param>
    internal StructDecorator(AssemblyHandler assemblyHandler, TypeDefinition typeDefinition, TypeReference? baseType, Func<TypeDefinition, TypeReference?, IStructHandler> buildCallback)
    {
        m_AssemblyHandler = assemblyHandler;
        m_TypeDefinition  = typeDefinition;
        m_BaseType        = baseType;
        m_BuildCallback   = buildCallback;
    }

    /// <inheritdoc/>
    public IGenericParametersDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_TypeDefinition.Name);

        var genericParameterType = new GenericParameterType(genericParameterName, constraints);
        var genericParameter = new GenericParameter(genericParameterName, m_TypeDefinition)
        {
            // Append generic parameter attributes to constraint.
            Attributes = genericParameterType.GetGenericParameterAttributes()
        };
        m_TypeDefinition.GenericParameters.Add(genericParameter);

        // Process every constraint from type.
        foreach (var constraint in constraints)
        {
            genericParameter.SetConstraintFromType(m_AssemblyHandler, m_TypeDefinition, constraint);
        }

        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithInterface(IType type)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_TypeDefinition.Name);

        // Resolve and append to collections.
        var implementation = new InterfaceImplementation(m_AssemblyHandler.ResolveParameterType(m_TypeDefinition, type));
        m_TypeDefinition.Interfaces.Add(implementation);
        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithInterface(Type type)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_TypeDefinition.Name);

        switch (type)
        {
            case null:                               throw new WeavingException(string.Format(ErrorMessages.TYPE_IS_NULL, nameof(type)));
            case {IsInterface              : false}: throw new WeavingException(ErrorMessages.TYPE_IS_NOT_INTERFACE);
            case {ContainsGenericParameters: true}:  throw new WeavingException($"{ErrorMessages.TYPE_IS_GENERIC} Please use WithInterface(IType) instead.");
        }

        // CecilType.Reference is owned by the target module already, so it can be appended as it is.
        var implementation = new InterfaceImplementation(m_AssemblyHandler.GetCecilType(type).Reference);
        m_TypeDefinition.Interfaces.Add(implementation);
        return this;
    }

    /// <inheritdoc/>
    public IStructHandler GetHandler()
    {
        // The struct is appended to the module once, and the chain answers with the handler of it from then on: a chain
        // which is asked for the handler of the struct twice appends one struct rather than two of the same name.
        if (m_Handler is { } built) return built;

        // Default from value type inheritance.
        if (m_BaseType == null || m_BaseType.FullName == typeof(ValueType).FullName)
        {
            // CecilType.Reference is owned by the target module already, so it is appended as it is.
            m_BaseType = m_AssemblyHandler.GetCecilType(typeof(ValueType)).Reference;
        }

        // Build and append to module.
        m_Handler = m_BuildCallback.Invoke(m_TypeDefinition, m_BaseType);
        return m_Handler;
    }

    /// <summary>
    /// Decorator which completes the struct. It is the end of the chain, which asks for the handler of the struct which
    /// the chain built and for nothing else.
    /// </summary>
    public interface ITypeDecorator
    {
        /// <summary>
        /// Build the struct into the module and answer with the handler of the struct which was built.<para/>
        /// The struct is appended once: the chain answers with the handler of it from then on, and a part which is
        /// described after that point is refused.
        /// </summary>
        /// <returns>Handler for struct.</returns>
        IStructHandler GetHandler();
    }

    /// <summary>
    /// Decorator for describing struct interface.
    /// </summary>
    public interface IInterfaceDecorator : ITypeDecorator
    {
        /// <summary>
        /// Append interface to type.
        /// </summary>
        /// <param name="type">Interface type of type</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the struct was already built.</exception>
        IInterfaceDecorator WithInterface(Type type);

        /// <summary>
        /// Append interface to type.
        /// </summary>
        /// <param name="type">Interface type of type</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the struct was already built.</exception>
        IInterfaceDecorator WithInterface(IType type);
    }

    /// <summary>
    /// Decorator for describing struct generic parameters. It is the entry of the chain, which holds a level for each
    /// part of a struct in the order the parts depend on each other: the generic parameters, and the interfaces. A
    /// level asks for the parts of itself and of the level after it, so a part which the struct does not hold is passed
    /// by rather than described, and a level which was passed by is not asked for again.<para/>
    /// The chain describes the struct until the struct is appended to the module, which is where it ends: a part which
    /// is described after that is refused, because what the chain holds is read where the struct is built and nothing
    /// reads it afterwards.
    /// </summary>
    public interface IGenericParametersDecorator : IInterfaceDecorator
    {
        /// <summary>
        ///  Append generic parameter to type.
        /// </summary>
        /// <param name="genericParameterName">Generic parameter name.</param>
        /// <param name="constraints">Generic parameter constrains.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the struct was already built.</exception>
        IGenericParametersDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints);
    }
}