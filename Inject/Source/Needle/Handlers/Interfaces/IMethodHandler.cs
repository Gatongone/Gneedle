using System.Reflection;

namespace Gneedle.Inject;

public interface IMethodHandler
{
    string Name { get; }
    ITypeHandler DeclaringTypeHandler { get; }
    void SetBody(MethodInfo method);
    void SetBody(DefaultMethodBody defaultMethodBody);
}

public static class MethodExtensions
{
    public static void SetBody(this IMethodHandler methodHandler, Delegate delegation) => methodHandler.SetBody(delegation.Method);

    /// <inheritdoc cref="MethodDecorator.IBodyDecorator.WithBody(MethodInfo)"/>
    public static MethodDecorator.ITypeDecorator WithBody(this MethodDecorator.IBodyDecorator decorator, Delegate delegation)
        => decorator.WithBody(delegation.Method);
}