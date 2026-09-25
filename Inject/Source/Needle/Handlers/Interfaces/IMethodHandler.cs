using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// Represents a handler for a method, which reads its name and its attributes and writes its body.
/// </summary>
public interface IMethodHandler : IAttributeContainer
{
    /// <summary>
    /// Flags of the method, which are the visibility and the modifiers which the definition declares, and the shape of
    /// belonging which is <see cref="MethodFlags.Static"/> or <see cref="MethodFlags.Instance"/>. An abstract method
    /// is a virtual one as well, and the flags name it by the narrower of the two shapes, which is
    /// <see cref="MethodFlags.Abstract"/>.
    /// </summary>
    MethodFlags Flags { get; }

    /// <summary>
    /// Name of the method.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Handler of the type which declares the method.
    /// </summary>
    ITypeHandler DeclaringTypeHandler { get; }

    /// <summary>
    /// Set the body of the method from the method which holds the IL to copy.<para/>
    /// The return type of the template becomes the return type of the method, which is a contract of the call rather
    /// than something which is checked against the method: a template which stands for the return type of the member
    /// being woven writes <see cref="T_0"/> or <see cref="M_0"/> where that type stands, and the weaving resolves the
    /// token to the type of the member.
    /// </summary>
    /// <param name="method">The method which holds the body.</param>
    /// <exception cref="WeavingException">Thrown when the template cannot be read, in which case the method is left as it was.</exception>
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
    /// <exception cref="WeavingException">Thrown when the method cannot be woven around, or when the template does not match it.</exception>
    void AroundBody(MethodInfo method);
}

/// <summary>
/// Extensions for <see cref="IMethodHandler"/>, and for the decorators which describe the body of a method.
/// </summary>
public static class MethodExtensions
{
    /// <param name="methodHandler">The handler of the method.</param>
    extension(IMethodHandler methodHandler)
    {
        /// <summary>
        /// Set the body of the method from the delegate which holds the IL to copy.<para/>
        /// A template may capture the variables which it is written among, and the delegate is what holds the values of
        /// them: it is given to the weaving rather than the method alone, so that what the template captured is written
        /// into the member being woven.
        /// </summary>
        /// <param name="delegation">The delegate which holds the body.</param>
        /// <exception cref="WeavingException">Thrown when the delegate captured a value and the handler is not one
        /// which this library builds, which holds nothing to write the value into.</exception>
        /// <remarks>
        /// A lambda written where a <see cref="Delegate"/> is asked for stands there from C# 10, which is the version of
        /// the language a lambda has a type of its own in: a caller compiled by an earlier one - which is what the
        /// compiler of a Unity project is - hands over the delegate the lambda would have been instead, which is
        /// <see cref="Action"/> where the template takes nothing and hands nothing back, and the <c>Func</c> which
        /// describes it otherwise.
        /// </remarks>
        public void SetBody(Delegate delegation) => MethodHandler.SetBody(methodHandler, delegation);

        /// <summary>
        /// Set the body of the method to run around the body which it holds, from the delegate which holds the template.
        /// </summary>
        /// <param name="delegation">The delegate which holds the body to weave around.</param>
        /// <exception cref="WeavingException">Thrown when the delegate captured a value and the handler is not one
        /// which this library builds, which holds nothing to write the value into.</exception>
        /// <remarks>
        /// A lambda written where a <see cref="Delegate"/> is asked for stands there from C# 10, which is the version of
        /// the language a lambda has a type of its own in: a caller compiled by an earlier one - which is what the
        /// compiler of a Unity project is - hands over the delegate the lambda would have been instead, which is
        /// <see cref="Action"/> where the template takes nothing and hands nothing back, and the <c>Func</c> which
        /// describes it otherwise.
        /// </remarks>
        public void AroundBody(Delegate delegation) => MethodHandler.AroundBody(methodHandler, delegation);

        /// <summary>
        /// Whether the method is the constructor of the type, which is the one which runs once for the type rather than
        /// once for each instance of it.
        /// </summary>
        public bool IsStaticCtor => methodHandler.Flags.HasFlag(MethodFlags.Static) && methodHandler.Name == ".cctor";

        /// <summary>
        /// Whether the method is the constructor of an instance of the type.
        /// </summary>
        public bool IsInstanceCtor => !methodHandler.Flags.HasFlag(MethodFlags.Static) && methodHandler.Name == ".ctor";
    }
}