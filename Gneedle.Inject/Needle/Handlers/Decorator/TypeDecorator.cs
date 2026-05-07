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
    private readonly Func<TypeDefinition, Implementation, IClassHandler> m_BuildCallback;

    /// <summary>
    /// Describing information.
    /// </summary>
    private Implementation m_Implementation;

    internal ClassDecorator(AssemblyHandler assemblyHandler, TypeDefinition typeDefinition, Implementation implementation, Func<TypeDefinition, Implementation, IClassHandler> buildCallback)
    {
        m_AssemblyHandler = assemblyHandler;
        m_TypeDefinition  = typeDefinition;
        m_Implementation  = implementation;
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

        // Import and append to base type.
        var module = m_AssemblyHandler.Assembly.Source.MainModule;
        m_Implementation.BaseType = module.ImportReference(m_AssemblyHandler.GetCecilType(type).Reference);
        return this;
    }

    /// <inheritdoc/>
    public IInterfaceDecorator WithBaseType(IType type)
    {
        // Resolve and append to base type.
        m_Implementation.BaseType = m_AssemblyHandler.ResolveParameterType(m_TypeDefinition, type);
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

        // Import and append to collections.
        var module = m_AssemblyHandler.Assembly.Source.MainModule;
        var implementation = new InterfaceImplementation(module.ImportReference(m_AssemblyHandler.GetCecilType(type).Reference));
        m_TypeDefinition.Interfaces.Add(implementation);
        return this;
    }

    /// <inheritdoc/>
    public IClassHandler GetHandler()
    {
        // Default from object inheritance.
        if (m_Implementation.BaseType == null)
        {
            WithBaseType(typeof(object));
        }

        // Build and append to module.
        return m_BuildCallback.Invoke(m_TypeDefinition, m_Implementation);
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
    private readonly Func<TypeDefinition, Implementation, IStructHandler> m_BuildCallback;

    /// <summary>
    /// Describing information.
    /// </summary>
    private Implementation m_Implementation;

    internal StructDecorator(AssemblyHandler assemblyHandler, TypeDefinition typeDefinition, Implementation implementation, Func<TypeDefinition, Implementation, IStructHandler> buildCallback)
    {
        m_AssemblyHandler = assemblyHandler;
        m_TypeDefinition  = typeDefinition;
        m_Implementation  = implementation;
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
            SetConstraintFromType(genericParameter, constraint);
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

        // Import and append to collections.
        var module = m_AssemblyHandler.Assembly.Source.MainModule;
        var implementation = new InterfaceImplementation(module.ImportReference(m_AssemblyHandler.GetCecilType(type).Reference));
        m_TypeDefinition.Interfaces.Add(implementation);
        return this;
    }

    /// <inheritdoc/>
    public IStructHandler GetHandler()
    {
        // Default from object inheritance.
        if (m_Implementation.BaseType == null || m_Implementation.BaseType.FullName == typeof(ValueType).FullName)
        {
            // Import and append to base type.
            var module = m_AssemblyHandler.Assembly.Source.MainModule;
            m_Implementation.BaseType = module.ImportReference(m_AssemblyHandler.GetCecilType(typeof(ValueType)).Reference);
        }

        // Build and append to module.
        return m_BuildCallback.Invoke(m_TypeDefinition, m_Implementation);
    }

    /// <summary>
    /// Set the generic parameter constraint which from type.
    /// </summary>
    /// <param name="genericParameter">Constraint provider.</param>
    /// <param name="constraint">Constraint witch from type.</param>
    private void SetConstraintFromType(GenericParameter genericParameter, Constraint constraint)
    {
        if (constraint.Type == null) return;

        // Process parameter constraint.
        TypeReference constraintType;
        if (constraint.Type is SelfType selfType)
        {
            // If self type is generic type, then we make generic instance type.
            if (selfType.GenericArguments.Length > 0)
            {
                var genericArguments = selfType.GenericArguments;
                var resolvedArguments = new TypeReference[genericArguments.Length];
                // Resolve every arguments.
                for (var index = 0; index < genericArguments.Length; index++)
                {
                    resolvedArguments[index] = m_AssemblyHandler.ResolveParameterType(m_TypeDefinition, genericArguments[index]);
                }

                constraintType = m_TypeDefinition.MakeGenericInstanceType(resolvedArguments);
            }
            // Or we just constraint to itself.
            else constraintType = m_TypeDefinition;
        }
        else
        {
            // Resolve constraint type.
            var resolvedType = m_AssemblyHandler.ResolveParameterType(m_TypeDefinition, constraint.Type);
            constraintType = m_AssemblyHandler.Assembly.Source.MainModule.ImportReference(resolvedType);
        }

        // Append to constraint collections.
        genericParameter.Constraints.Add(new GenericParameterConstraint(constraintType));
    }

    /// <summary>
    /// Decorator for create type definition to current module.
    /// </summary>
    public interface ITypeDecorator
    {
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