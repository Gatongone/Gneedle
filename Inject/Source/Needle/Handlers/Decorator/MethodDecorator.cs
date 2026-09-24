using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// Decorator for describing a method, following the chainable pattern of
/// <see cref="ClassDecorator"/>. Create via <c>ITypeHandler.AddMethod(name, flags)</c>.
/// </summary>
public class MethodDecorator : MethodDecorator.IGenericParameterDecorator
{
    /// <summary>
    /// Handler of the type which the method is appended to when the chain ends.
    /// </summary>
    private readonly TypeHandler m_TypeHandler;

    /// <summary>
    /// Name of the method which the chain describes.
    /// </summary>
    private readonly string m_MethodName;

    /// <summary>
    /// Flags of the method which the chain describes.
    /// </summary>
    private readonly MethodFlags m_MethodFlags;

    /// <summary>
    /// Type of the value which the method hands back, which is <see cref="void"/> until another is asked for.
    /// </summary>
    private IType m_ReturnType = typeof(void).ToGneedleType();

    /// <summary>
    /// The generic parameters of the method, in the order they were asked for, which is the order their tokens are
    /// numbered in.
    /// </summary>
    private readonly List<GenericParameterType> m_GenericParameters = [];

    /// <summary>
    /// The parameters of the method, in the order they were asked for, which is the order a template reads them by.
    /// </summary>
    private readonly List<Parameter> m_Parameters = [];

    /// <summary>
    /// The body of a kind which can be written from the member alone, which the last <see cref="IBodyDecorator.WithBody(DefaultMethodBody)"/> left.
    /// </summary>
    /// <remarks>
    /// The two ways of describing a body replace each other, so at most one of this and <see cref="m_BodyMethod"/> is
    /// set: the one which was asked for last is the one which is applied.
    /// </remarks>
    private DefaultMethodBody? m_DefaultBody;

    /// <summary>
    /// The member whose body is copied, which the last <see cref="IBodyDecorator.WithBody(MethodInfo)"/> left.
    /// </summary>
    private MethodInfo? m_BodyMethod;

    /// <summary>
    /// The instance which the delegate of <see cref="m_BodyMethod"/> was made from, which holds what the template
    /// captured, or null when the body was given as a method alone, or when the template captured nothing.
    /// </summary>
    private object? m_BodyClosure;

    /// <summary>
    /// The handler of the method which the chain built, or null while the method is still being described: the chain
    /// answers with it from the point where it ends, which is what makes a chain which is asked for the handler of the
    /// method twice build one method rather than two of the same name.
    /// </summary>
    private IMethodHandler? m_Handler;

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
        DecoratorChain.RefuseDescription(m_Handler, m_MethodName);
        m_GenericParameters.Add(new GenericParameterType(genericParameterName, constraints));
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithParameter(Parameter parameter)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_MethodName);
        m_Parameters.Add(parameter);
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithParameter(string name, IType parameterType)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_MethodName);
        m_Parameters.Add(new Parameter(name, parameterType));
        return this;
    }

    /// <inheritdoc/>
    public IParameterDecorator WithParameter(string name, Type parameterType)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_MethodName);
        m_Parameters.Add(new Parameter(name, parameterType.ToGneedleType()));
        return this;
    }

    /// <inheritdoc/>
    public IBodyDecorator WithReturnType(IType returnType)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_MethodName);
        m_ReturnType = returnType;
        return this;
    }

    /// <inheritdoc/>
    public IBodyDecorator WithReturnType(Type returnType)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_MethodName);
        m_ReturnType = returnType.ToGneedleType();
        return this;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithBody(DefaultMethodBody body)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_MethodName);

        // The two ways of describing a body replace each other, so that the one which was asked for last is the one which
        // is applied.
        m_DefaultBody = body;
        m_BodyMethod  = null;
        m_BodyClosure = null;
        return this;
    }

    /// <inheritdoc/>
    public ITypeDecorator WithBody(MethodInfo method)
    {
        DecoratorChain.RefuseDescription(m_Handler, m_MethodName);
        m_BodyMethod  = method;
        m_DefaultBody = null;
        m_BodyClosure = null;
        return this;
    }

    /// <inheritdoc cref="WithBody(MethodInfo)"/>
    /// <param name="delegation">The delegate which holds the body, and the instance which holds what it captured.</param>
    internal ITypeDecorator WithBody(Delegate delegation)
    {
        WithBody(delegation.Method);
        m_BodyClosure = delegation.Target;
        return this;
    }

    /// <inheritdoc/>
    public IMethodHandler GetHandler()
    {
        // The method is appended to the type once, and the chain answers with the handler of it from then on: a chain
        // which is asked for the handler of the method twice builds one method rather than two of the same name.
        if (m_Handler is { } built) return built;

        var handler = m_TypeHandler.AddMethod(
            m_MethodName,
            m_ReturnType,
            [.. m_GenericParameters],
            [.. m_Parameters],
            m_MethodFlags);

        // The method is added with a body which throws, so that a method which is added without a body is still
        // loadable. The body which was asked for replaces it.
        if (m_BodyMethod is { } methodInfo) MethodHandler.SetBody(handler, methodInfo, m_BodyClosure);
        else if (m_DefaultBody is { } body) handler.SetBody(body);

        m_Handler = handler;
        return handler;
    }

    /// <summary>
    /// Decorator which completes the method. It is the end of the chain, which asks for the handler of the method which
    /// the chain built and for nothing else.
    /// </summary>
    public interface ITypeDecorator
    {
        /// <summary>
        /// Build the method into the module and answer with the handler of the method which was built.<para/>
        /// The method is built once: the chain answers with the handler of it from then on, and a part which is
        /// described after that point is refused.
        /// </summary>
        /// <returns>Handler for method.</returns>
        IMethodHandler GetHandler();
    }

    /// <summary>
    /// Decorator for describing method body. It is the last part of the chain, which the method is described to an end
    /// by: the body is written for the signature which the levels before it made up, and a part which none of them
    /// asked for keeps what it holds, which is the default of a method. So a method which holds no generic parameter,
    /// no parameter and nothing to hand back is a method which this level alone describes.
    /// </summary>
    public interface IBodyDecorator : ITypeDecorator
    {
        /// <summary>
        /// Set the body of the method to the default body behavior.
        /// </summary>
        /// <param name="body">Default method body.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the method was already built.</exception>
        ITypeDecorator WithBody(DefaultMethodBody body);

        /// <summary>
        /// Set the body of the method from the method which holds the IL to copy.
        /// </summary>
        /// <param name="method">Method which holds the body.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the method was already built.</exception>
        ITypeDecorator WithBody(MethodInfo method);
    }

    /// <summary>
    /// Decorator for describing method return type. It is the level of the return type, which asks for the body as
    /// well, because a body is written for the signature which the return type is a part of.
    /// </summary>
    public interface IReturnTypeDecorator : IBodyDecorator
    {
        /// <summary>
        /// Append return type to the method from <see cref="IType"/>.
        /// </summary>
        /// <param name="returnType">Return type of method.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the method was already built.</exception>
        IBodyDecorator WithReturnType(IType returnType);

        /// <summary>
        /// Append return type to the method from <see cref="System.Type"/>.
        /// </summary>
        /// <param name="returnType">Return type of method.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the method was already built.</exception>
        IBodyDecorator WithReturnType(Type returnType);
    }

    /// <summary>
    /// Decorator for describing method parameters. It is the level of the parameters, which asks for the return type
    /// and the body as well, because both of those are the parts which come after the parameters in the order the parts
    /// of a method depend on each other.
    /// </summary>
    public interface IParameterDecorator : IReturnTypeDecorator
    {
        /// <summary>
        /// Append parameter to the method.
        /// </summary>
        /// <param name="parameter">Parameter name and type.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the method was already built.</exception>
        IParameterDecorator WithParameter(Parameter parameter);

        /// <summary>
        /// Append parameter to the method from <see cref="IType"/> under a name.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="parameterType">Parameter type.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the method was already built.</exception>
        IParameterDecorator WithParameter(string name, IType parameterType);

        /// <summary>
        /// Append parameter to the method from <see cref="System.Type"/> under a name.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="parameterType">Parameter type.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the method was already built.</exception>
        IParameterDecorator WithParameter(string name, Type parameterType);
    }

    /// <summary>
    /// Decorator for describing method generic parameters. It is the entry of the chain, which holds a level for each
    /// part of a method in the order the parts depend on each other: the generic parameters, the parameters, the return
    /// type and lastly the body. A level asks for the parts of itself and of the levels after it, so a part which the
    /// method does not hold is passed by rather than described, and a level which was passed by is not asked for
    /// again.<para/>
    /// The chain describes the method until the method is built, which is where it ends: a part which is described
    /// after that is refused, because what the chain holds is read where the method is built and nothing reads it
    /// afterwards. The flags are given to <c>ITypeHandler.AddMethod(name, flags)</c>.
    /// </summary>
    public interface IGenericParameterDecorator : IParameterDecorator
    {
        /// <summary>
        /// Append generic parameter to the method.
        /// </summary>
        /// <param name="genericParameterName">Generic parameter name.</param>
        /// <param name="constraints">Generic parameter constrains.</param>
        /// <returns>Result for chains calling.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the method was already built.</exception>
        IGenericParameterDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints);
    }
}

/// <summary>
/// Extensions for the decorator which describes a method, which name its body by a delegate.
/// </summary>
public static class MethodDecoratorExtensions
{
    /// <summary>
    /// Set the body of the method from the delegate which holds the IL to copy.<para/>
    /// A template may capture the variables which it is written among, and the delegate is what holds the values of
    /// them: it is given to the weaving rather than the method alone, so that what the template captured is written
    /// into the method being woven.
    /// </summary>
    /// <param name="decorator">The decorator which describes the method.</param>
    /// <param name="delegation">The delegate which holds the body.</param>
    /// <returns>Result for chains calling.</returns>
    /// <exception cref="WeavingException">Thrown when the decorator is not the one which this library builds, which
    /// holds nothing to write what the template captured into.</exception>
    /// <remarks>
    /// A lambda written where a <see cref="Delegate"/> is asked for stands there from C# 10: a caller compiled by an
    /// earlier one hands over the delegate the lambda would have been, as
    /// <see cref="MethodExtensions"/> describes.
    /// </remarks>
    public static MethodDecorator.ITypeDecorator WithBody(this MethodDecorator.IBodyDecorator decorator, Delegate delegation)
    {
        // The value of a capture is read out of the delegate where the body is woven, which only the decorator this
        // library builds does: another implementation holds nothing for it, so the delegate is refused rather than read
        // for the method alone, which would weave a body without the value which the template read.
        if (decorator is not MethodDecorator methodDecorator)
        {
            throw new WeavingException(string.Format(ErrorMessages.DECORATOR_HOLDS_NO_CAPTURE, decorator.GetType().FullName));
        }

        return methodDecorator.WithBody(delegation);
    }
}