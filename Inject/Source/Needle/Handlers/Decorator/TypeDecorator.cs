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
        switch (type)
        {
            case null:                              throw new NullReferenceException(nameof(type));
            case {IsValueType              : true}: throw new ArgumentException(ErrorMessages.TYPE_IS_VALUE_TYPE);
            case {IsSealed                 : true}: throw new ArgumentException(ErrorMessages.TYPE_IS_SEALED);
            case {IsInterface              : true}: throw new ArgumentException(ErrorMessages.TYPE_IS_INTERFACE);
            case {ContainsGenericParameters: true}: throw new ArgumentException($"{ErrorMessages.TYPE_IS_GENERIC} Please use WithBaseType(IType) instead.");
        }

        // CecilType.Reference is owned by the target module already, so it can be appended as it is.
        m_BaseType = m_AssemblyHandler.GetCecilType(type).Reference;
        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithBaseType(IType type)
    {
        // Resolve and append to base type.
        m_BaseType = m_AssemblyHandler.ResolveParameterType(m_TypeDefinition, type);
        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithInterface(IType type)
    {
        // Resolve and append to collections.
        var implementation = new InterfaceImplementation(m_AssemblyHandler.ResolveParameterType(m_TypeDefinition, type));
        m_TypeDefinition.Interfaces.Add(implementation);
        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithInterface(Type type)
    {
        switch (type)
        {
            case null:                               throw new NullReferenceException(nameof(type));
            case {IsInterface              : false}: throw new ArgumentException(ErrorMessages.TYPE_IS_NOT_INTERFACE);
            case {ContainsGenericParameters: true}:  throw new ArgumentException($"{ErrorMessages.TYPE_IS_GENERIC} Please use WithInterface(IType) instead.");
        }

        // CecilType.Reference is owned by the target module already, so it can be appended as it is.
        var implementation = new InterfaceImplementation(m_AssemblyHandler.GetCecilType(type).Reference);
        m_TypeDefinition.Interfaces.Add(implementation);
        return this;
    }

    /// <inheritdoc/>
    public IClassHandler GetHandler()
    {
        // Default from object inheritance.
        if (m_BaseType == null)
        {
            WithBaseType(typeof(object));
        }

        // Build and append to module.
        return m_BuildCallback.Invoke(m_TypeDefinition, m_BaseType);
    }

    /// <summary>
    /// Decorator for create type definition to current module.
    /// </summary>
    public interface ITypeDecorator
    {
        /// <summary>
        /// Build type definition to module
        /// </summary>
        /// <returns>Handler for class.</returns>
        IClassHandler GetHandler();
    }

    /// <summary>
    /// Decorator for describing class interface.
    /// </summary>
    public interface IInterfaceDecorator : ITypeDecorator
    {
        /// <summary>
        /// Append interface to type.
        /// </summary>
        /// <param name="type">Interface type of type</param>
        /// <returns>Result for chains calling.</returns>
        IInterfaceDecorator WithInterface(Type type);

        /// <summary>
        /// Append interface to type.
        /// </summary>
        /// <param name="type">Interface type of type</param>
        /// <returns>Result for chains calling.</returns>
        IInterfaceDecorator WithInterface(IType type);
    }

    /// <summary>
    /// Decorator for describing class base type.
    /// </summary>
    public interface IBaseTypeDecorator : IInterfaceDecorator
    {
        /// <summary>
        /// Append base type to type.
        /// </summary>
        /// <param name="type">Base type of type.</param>
        /// <returns>Result for chains calling.</returns>
        IInterfaceDecorator WithBaseType(Type type);

        /// <summary>
        /// Append base type to type.
        /// </summary>
        /// <param name="type">Base type of type.</param>
        /// <returns>Result for chains calling.</returns>
        IInterfaceDecorator WithBaseType(IType type);
    }

    /// <summary>
    /// Decorator for describing class generic parameters.
    /// </summary>
    public interface IGenericParametersDecorator : IBaseTypeDecorator
    {
        /// <summary>
        ///  Append generic parameter to type.
        /// </summary>
        /// <param name="genericParameterName">Generic parameter name.</param>
        /// <param name="constraints">Generic parameter constrains.</param>
        /// <returns>Result for chains calling.</returns>
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
        // Resolve and append to collections.
        var implementation = new InterfaceImplementation(m_AssemblyHandler.ResolveParameterType(m_TypeDefinition, type));
        m_TypeDefinition.Interfaces.Add(implementation);
        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithInterface(Type type)
    {
        switch (type)
        {
            case null:                               throw new NullReferenceException(nameof(type));
            case {IsInterface              : false}: throw new ArgumentException(ErrorMessages.TYPE_IS_NOT_INTERFACE);
            case {ContainsGenericParameters: true}:  throw new ArgumentException($"{ErrorMessages.TYPE_IS_GENERIC} Please use WithInterface(IType) instead.");
        }

        // CecilType.Reference is owned by the target module already, so it can be appended as it is.
        var implementation = new InterfaceImplementation(m_AssemblyHandler.GetCecilType(type).Reference);
        m_TypeDefinition.Interfaces.Add(implementation);
        return this;
    }

    /// <inheritdoc/>
    public IStructHandler GetHandler()
    {
        // Default from object inheritance.
        if (m_BaseType == null || m_BaseType.FullName == typeof(ValueType).FullName)
        {
            // CecilType.Reference is owned by the target module already, so it can be appended as it is.
            m_BaseType = m_AssemblyHandler.GetCecilType(typeof(ValueType)).Reference;
        }

        // Build and append to module.
        return m_BuildCallback.Invoke(m_TypeDefinition, m_BaseType);
    }

    /// <summary>
    /// Decorator for create type definition to current module.
    /// </summary>
    public interface ITypeDecorator
    {
        /// <summary>
        /// Build struct definition to module.
        /// </summary>
        /// <returns>Handler for struct.</returns>
        IStructHandler GetHandler();
    }

    /// <summary>
    /// Decorator for describing class interface.
    /// </summary>
    public interface IInterfaceDecorator : ITypeDecorator
    {
        /// <summary>
        /// Append interface to type.
        /// </summary>
        /// <param name="type">Interface type of type</param>
        /// <returns>Result for chains calling.</returns>
        IInterfaceDecorator WithInterface(Type type);

        /// <summary>
        /// Append interface to type.
        /// </summary>
        /// <param name="type">Interface type of type</param>
        /// <returns>Result for chains calling.</returns>
        IInterfaceDecorator WithInterface(IType type);
    }

    /// <summary>
    /// Decorator for describing class generic parameters.
    /// </summary>
    public interface IGenericParametersDecorator : IInterfaceDecorator
    {
        /// <summary>
        ///  Append generic parameter to type.
        /// </summary>
        /// <param name="genericParameterName">Generic parameter name.</param>
        /// <param name="constraints">Generic parameter constrains.</param>
        /// <returns>Result for chains calling.</returns>
        IGenericParametersDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints);
    }
}