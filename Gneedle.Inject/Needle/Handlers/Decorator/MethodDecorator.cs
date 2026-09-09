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

    public IParameterDecorator WithReturnType(IType returnType)
    {
        m_ReturnType = returnType;
        return this;
    }

    public IParameterDecorator WithReturnType(Type returnType)
    {
        m_ReturnType = returnType.ToGneedleType();
        return this;
    }

    public IFlagsDecorator WithParameter(IType parameterType)
    {
        m_ParameterTypes.Add(parameterType);
        return this;
    }

    public IFlagsDecorator WithParameter(Type parameterType)
    {
        m_ParameterTypes.Add(parameterType.ToGneedleType());
        return this;
    }

    public IFlagsDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints)
    {
        m_GenericParameters.Add(new GenericParameterType(genericParameterName, constraints));
        return this;
    }

    public IFlagsDecorator WithBody(DefaultMethodBody body)
    {
        m_Body = body;
        return this;
    }

    public ITypeDecorator WithFlags(MethodFlags methodFlags)
    {
        m_MethodFlags = methodFlags;
        return this;
    }

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

    public interface ITypeDecorator
    {
        IMethodHandler GetHandler();
    }

    public interface IFlagsDecorator : ITypeDecorator
    {
        ITypeDecorator WithFlags(MethodFlags methodFlags);
        IFlagsDecorator WithParameter(IType parameterType);
        IFlagsDecorator WithParameter(Type parameterType);
        IFlagsDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints);
        IFlagsDecorator WithBody(DefaultMethodBody body);
    }

    public interface IParameterDecorator : IFlagsDecorator
    {
        IFlagsDecorator WithParameter(IType parameterType);
        IFlagsDecorator WithParameter(Type parameterType);
        IFlagsDecorator WithGenericParameter(string genericParameterName, params Constraint[] constraints);
        IFlagsDecorator WithBody(DefaultMethodBody body);
    }

    public interface IReturnTypeDecorator : IParameterDecorator
    {
        IParameterDecorator WithReturnType(IType returnType);
        IParameterDecorator WithReturnType(Type returnType);
    }
}