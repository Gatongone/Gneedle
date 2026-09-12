using System.Reflection;
using Gneedle.Inject;

namespace Gneedle.Aspect.Test;

/// <summary>
/// An attribute which replaces the body of the method it is put on with one which throws, so that the injection is
/// visible in the assembly which the task wrote.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ThrowBodyAttribute : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler) => handler.SetBody(DefaultMethodBody.ThrowException);
}

/// <summary>
/// An attribute which marks the field it is put on as obsolete, so that the injection is visible in the assembly which
/// the task wrote.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class MarkFieldAttribute : Attribute, IFieldInjector
{
    /// <inheritdoc/>
    public void Inject(FieldInfo field, IFieldHandler handler) => handler.AddAttribute(typeof(ObsoleteAttribute).ToGneedleType(), "marked");
}

/// <summary>
/// An attribute which replaces the body of the getter of the property it is put on with one which throws.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ThrowGetterBodyAttribute : Attribute, IPropertyInjector
{
    /// <inheritdoc/>
    public void Inject(PropertyInfo property, IPropertyHandler handler) => handler.SetGetter(DefaultPropertyBody.ThrowException);
}

/// <summary>
/// An attribute which records the member it was asked to inject into, so that a member which is walked more than once
/// is told apart from one which is walked once.<para/>
/// It records into a file rather than into a field, because the assembly is read from its bytes and the injectors which
/// run are the ones of that copy: a field which it wrote would be a field of a copy of this assembly, which the test
/// cannot read. The path of the file is handed over in an environment variable, which the copy shares.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RecordingAttribute : Attribute, IMethodInjector
{
    /// <summary>
    /// The environment variable which names the file the members are recorded into.
    /// </summary>
    public const string LogVariable = "GneedleAspectInjected";

    /// <inheritdoc/>
    public void Inject(MethodInfo method, IMethodHandler handler)
    {
        if (Environment.GetEnvironmentVariable(LogVariable) is not { } path) return;
        File.AppendAllText(path, $"{method.DeclaringType!.Name}.{method.Name}{Environment.NewLine}");
    }
}

/// <summary>
/// An attribute which applies to a class alone, so that it is refused on a type of another kind.
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public sealed class ClassOnlyAttribute : Attribute, IClassInjector
{
    /// <inheritdoc/>
    public void Inject(Type type, IClassHandler handler) => handler.AddAttribute(typeof(ObsoleteAttribute).ToGneedleType(), "class");
}
