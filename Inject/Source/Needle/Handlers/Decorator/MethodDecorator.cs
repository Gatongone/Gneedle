using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing a method, following the chainable pattern of
/// <see cref="ClassDecorator"/>. Create via <c>ITypeHandler.AddMethod(name, flags)</c>.
/// </summary>
public class MethodDecorator : MethodDecorator.IGenericParameterDecorator
{
    private readonly TypeHandler m_TypeHandler;
    private readonly string m_MethodName;
    private readonly MethodFlags m_MethodFlags;
    private IType m_ReturnType = typeof(void).ToGneedleType();
    private readonly List<GenericParameterType> m_GenericParameters = [];
    private readonly List<Parameter> m_Parameters = [];
    private DefaultMethodBody? m_DefaultBody;
    private MethodInfo? m_BodyMethod;
    private MethodInfo? m_AroundBodyMethod;

    /// <summary>
    /// Create a decorator which describes a method before it is appended to the module.
    /// </summary>
    /// <param name="typeHandler">Handler of the type which the method is appended to.</param>
    /// <param name="methodName">Name of the method.</param>
    /// <param name="methodFlags">Flags of the method.</param>
    internal MethodDecorator(TypeHandler typeHandler, string methodName, MethodFlags methodFlags)
    {
        m_TypeHandler = typeHandler;
        m_MethodName  = methodName;
        m_MethodFlags = methodFlags;
    }

    /// <inheritdoc/>
    public IGenericParameterDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints)
    {
        m_GenericParameters.Add(new GenericParameterType(genericParameterName, constraints));
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithParameter(Parameter parameter)
    {
        m_Parameters.Add(parameter);
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithParameter(string name, IType parameterType)
    {
        m_Parameters.Add(new Parameter(name, parameterType));
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithParameter(string name, Type parameterType)
    {
        m_Parameters.Add(new Parameter(name, parameterType.ToGneedleType()));
        return this;
    }

    /// <inheritdoc/>
    public IBodyDecorator WithReturnType(IType returnType)
    {
        m_ReturnType = returnType;
        return this;
    }

    /// <inheritdoc/>
    public IBodyDecorator WithReturnType(Type returnType)
    {
        m_ReturnType = returnType.ToGneedleType();
        return this;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithBody(DefaultMethodBody body)
    {
        // The two ways of describing a body replace each other, so that the one which was asked for last is the one which
        // is applied. They are not replaced by the around body, which wraps whichever body the method holds by then.
        m_DefaultBody = body;
        m_BodyMethod  = null;
        return this;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithBody(MethodInfo method)
    {
        m_BodyMethod  = method;
        m_DefaultBody = null;
        return this;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithAroundBody(MethodInfo method)
    {
        m_AroundBodyMethod = method;
        return this;
    }

    /// <inheritdoc/>
    public IMethodHandler GetHandler()
    {
        var handler = m_TypeHandler.AddMethod(
            m_MethodName,
            m_ReturnType,
            m_GenericParameters.ToArray(),
            m_Parameters.ToArray(),
            m_MethodFlags);

        // The method is added with a body which throws, so that a method which is added without a body is still
        // loadable. The body which was asked for replaces it, and the around body is woven over whichever of the two the
        // method holds by then.
        if (m_BodyMethod is { } methodInfo) handler.SetBody(methodInfo);
        else if (m_DefaultBody is { } body) handler.SetBody(body);

        if (m_AroundBodyMethod is { } aroundMethodInfo) handler.AroundBody(aroundMethodInfo);

        return handler;
    }

    /// <summary>
    /// Decorator which completes the method. It is the end of the chain.
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
    /// Decorator for describing method body. A body could only be described for a complete signature, so it is the
    /// last part which could be described.
    /// </summary>
    public interface IBodyDecorator : ITypeDecorator
    {
        /// <summary>
        /// Set the body of the method to the default body behavior.
        /// </summary>
        /// <param name="body">Default method body.</param>
        /// <returns>Result for chains calling.</returns>
        ITypeDecorator WithBody(DefaultMethodBody body);

        /// <summary>
        /// Set the body of the method from the method which holds the IL to copy.
        /// </summary>
        /// <param name="method">Method which holds the body.</param>
        /// <returns>Result for chains calling.</returns>
        ITypeDecorator WithBody(MethodInfo method);

        /// <summary>
        /// Set the body of the method to run around the body which it holds, which the template reaches through
        /// <see cref="Proceed"/>.
        /// </summary>
        /// <param name="method">Method which holds the body to weave around.</param>
        /// <returns>Result for chains calling.</returns>
        ITypeDecorator WithAroundBody(MethodInfo method);
    }

    /// <summary>
    /// Decorator for describing method return type.
    /// </summary>
    public interface IReturnTypeDecorator : IBodyDecorator
    {
        /// <summary>
        /// Append return type to the method from <see cref="IType"/>.
        /// </summary>
        /// <param name="returnType">Return type of method.</param>
        /// <returns>Result for chains calling.</returns>
        IBodyDecorator WithReturnType(IType returnType);

        /// <summary>
        /// Append return type to the method from <see cref="System.Type"/>.
        /// </summary>
        /// <param name="returnType">Return type of method.</param>
        /// <returns>Result for chains calling.</returns>
        IBodyDecorator WithReturnType(Type returnType);
    }

    /// <summary>
    /// Decorator for describing method parameters.
    /// </summary>
    public interface IParameterDecorator : IReturnTypeDecorator
    {
        /// <summary>
        /// Append parameter to the method.
        /// </summary>
        /// <param name="parameter">Parameter name and type.</param>
        /// <returns>Result for chains calling.</returns>
        IParameterDecorator WithParameter(Parameter parameter);

        /// <summary>
        /// Append parameter to the method from <see cref="IType"/> under a name.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="parameterType">Parameter type.</param>
        /// <returns>Result for chains calling.</returns>
        IParameterDecorator WithParameter(string name, IType parameterType);

        /// <summary>
        /// Append parameter to the method from <see cref="System.Type"/> under a name.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="parameterType">Parameter type.</param>
        /// <returns>Result for chains calling.</returns>
        IParameterDecorator WithParameter(string name, Type parameterType);
    }

    /// <summary>
    /// Decorator for describing method generic parameters. It is the entry of the chain, which follows the order in
    /// which the parts of a method depend on each other: the generic parameters, the parameters, the return type and
    /// lastly the body. The flags are given to <c>ITypeHandler.AddMethod(name, flags)</c>.
    /// </summary>
    public interface IGenericParameterDecorator : IParameterDecorator
    {
        /// <summary>
        /// Append generic parameter to the method.
        /// </summary>
        /// <param name="genericParameterName">Generic parameter name.</param>
        /// <param name="constraints">Generic parameter constrains.</param>
        /// <returns>Result for chains calling.</returns>
        IGenericParameterDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints);
    }
}
