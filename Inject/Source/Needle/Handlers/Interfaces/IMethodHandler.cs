using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for a method, which reads its name and writes its body.
/// </summary>
public interface IMethodHandler
{
    /// <summary>
    /// Name of the method.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Handler of the type which declares the method.
    /// </summary>
    ITypeHandler DeclaringTypeHandler { get; }

    /// <summary>
    /// Set the body of the method from the method which holds the IL to copy.
    /// </summary>
    /// <param name="method">The method which holds the body.</param>
    void SetBody(MethodInfo method);

    /// <summary>
    /// Set the body of the method to the default body behavior.
    /// </summary>
    /// <param name="defaultMethodBody">The default body of the method.</param>
    void SetBody(DefaultMethodBody defaultMethodBody);

    /// <summary>
    /// Set the body of the method to run around the body which it holds, which the template reaches through
    /// <see cref="Proceed"/>.<para/>
    /// The template keeps the signature of the method, so its parameters and its return type have to match, and the body
    /// which the method holds is moved to a generated method of the declaring type which the template calls. For a method
    /// which belongs to an instance, the template has to place the instance before the call, as it does for
    /// <see cref="This.Method{TMethod}(string)"/>.
    /// </summary>
    /// <param name="method">The template which holds the body to weave around.</param>
    /// <exception cref="ArgumentException">Thrown when the method cannot be woven around, or when the template does not match it.</exception>
    void AroundBody(MethodInfo method);
}

/// <summary>
/// Extensions for <see cref="IMethodHandler"/>, and for the decorators which describe the body of a method.
/// </summary>
public static class MethodExtensions
{
    /// <summary>
    /// Set the body of the method from the delegate which holds the IL to copy.
    /// </summary>
    /// <param name="methodHandler">The handler of the method.</param>
    /// <param name="delegation">The delegate which holds the body.</param>
    public static void SetBody(this IMethodHandler methodHandler, Delegate delegation) => methodHandler.SetBody(delegation.Method);

    /// <summary>
    /// Set the body of the method from the delegate which holds the IL to copy.
    /// </summary>
    /// <param name="decorator">The decorator which describes the method.</param>
    /// <param name="delegation">The delegate which holds the body.</param>
    /// <returns>Result for chains calling.</returns>
    public static MethodDecorator.ITypeDecorator WithBody(this MethodDecorator.IBodyDecorator decorator, Delegate delegation)
        => decorator.WithBody(delegation.Method);

    /// <summary>
    /// Set the body of the method to run around the body which it holds, from the delegate which holds the template.
    /// </summary>
    /// <param name="methodHandler">The handler of the method.</param>
    /// <param name="delegation">The delegate which holds the body to weave around.</param>
    public static void AroundBody(this IMethodHandler methodHandler, Delegate delegation) => methodHandler.AroundBody(delegation.Method);
}