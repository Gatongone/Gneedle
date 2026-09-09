namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing a method, following the chainable pattern of
/// <see cref="ClassDecorator"/>. Create via <c>ITypeHandler.AddMethod(name)</c>.
/// </summary>
public class MethodDecorator : MethodDecorator.IReturnTypeDecorator
{
    private readonly TypeHandler m_TypeHandler;
    private readonly string m_MethodName;
    private IType m_ReturnType = typeof(void).ToGneedleType();
    private readonly List<GenericParameterType> m_GenericParameters = [];
    private readonly List<IType> m_ParameterTypes = [];
    private MethodFlags m_MethodFlags = MethodFlags.Public;
    private DefaultMethodBody? m_Body;

    internal MethodDecorator(TypeHandler typeHandler, string methodName)
    {
        m_TypeHandler = typeHandler;
        m_MethodName  = methodName;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithReturnType(IType returnType)
    {
        m_ReturnType = returnType;
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithReturnType(Type returnType)
    {
        m_ReturnType = returnType.ToGneedleType();
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithParameter(IType parameterType)
    {
        m_ParameterTypes.Add(parameterType);
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithParameter(Type parameterType)
    {
        m_ParameterTypes.Add(parameterType.ToGneedleType());
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints)
    {
        m_GenericParameters.Add(new GenericParameterType(genericParameterName, constraints));
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithBody(DefaultMethodBody body)
    {
        m_Body = body;
        return this;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithFlags(MethodFlags methodFlags)
    {
        m_MethodFlags = methodFlags;
        return this;
    }

    /// <inheritdoc/>
    public IMethodHandler GetHandler()
    {
        var handler = m_TypeHandler.AddMethod(
            m_MethodName,
            m_ReturnType,
            m_GenericParameters.ToArray(),
            m_ParameterTypes.ToArray(),
            m_MethodFlags);

        if (m_Body is { } body)
        {
            handler.SetBody(body);
        }

        return handler;
    }

    /// <summary>
    /// Decorator for create type definition to current module.
    /// </summary>
    public interface ITypeDecorator
    {
        /// <summary>
        /// Build method definition to module.
        /// </summary>
        /// <returns>Handler for method.</returns>
        IMethodHandler GetHandler();
    }

    /// <summary>
    /// Decorator for describing method flags.
    /// </summary>
    public interface IParameterDecorator : ITypeDecorator
    {
        /// <summary>
        /// Set method flags.
        /// </summary>
        /// <param name="methodFlags">Method flags.</param>
        /// <returns>Result for chains calling.</returns>
        ITypeDecorator WithFlags(MethodFlags methodFlags);

        /// <summary>
        /// Append parameter to the method.
        /// </summary>
        /// <param name="parameterType">Parameter type.</param>
        /// <returns>Result for chains calling.</returns>
        IParameterDecorator WithParameter(IType parameterType);

        /// <summary>
        /// Append parameter to the method from <see cref="System.Type"/>.
        /// </summary>
        /// <param name="parameterType">Parameter type.</param>
        /// <returns>Result for chains calling.</returns>
        IParameterDecorator WithParameter(Type parameterType);

        /// <summary>
        /// Append generic parameter to the method.
        /// </summary>
        /// <param name="genericParameterName">Generic parameter name.</param>
        /// <param name="constraints">Generic parameter constrains.</param>
        /// <returns>Result for chains calling.</returns>
        IParameterDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints);

        /// <summary>
        /// Append default body to the method.
        /// </summary>
        /// <param name="body">Default method body.</param>
        /// <returns>Result for chains calling.</returns>
        IParameterDecorator WithBody(DefaultMethodBody body);
    }

    /// <summary>
    /// Decorator for describing method return type.
    /// </summary>
    public interface IReturnTypeDecorator : IParameterDecorator
    {
        /// <summary>
        /// Append return type to the method from <see cref="IType"/>.
        /// </summary>
        /// <param name="returnType">Return type of method.</param>
        /// <returns>Result for chains calling.</returns>
        IParameterDecorator WithReturnType(IType returnType);

        /// <summary>
        /// Append return type to the method from <see cref="System.Type"/>.
        /// </summary>
        /// <param name="returnType">Return type of method.</param>
        /// <returns>Result for chains calling.</returns>
        IParameterDecorator WithReturnType(Type returnType);
    }
}