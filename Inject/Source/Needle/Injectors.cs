using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// Injects assemblies into a project using the provided assembly handler.
/// </summary>
public interface IAssemblyInjector
{
    /// <summary>
    /// Injects the specified assembly into the project using the provided assembly handler.
    /// </summary>
    /// <param name="assembly">The assembly to be injected.</param>
    /// <param name="handler">The assembly handler that defines how the assembly should be injected.</param>
    void Inject(System.Reflection.Assembly assembly,IAssemblyHandler handler);
}

/// <summary>
/// Injects types into an assembly using the provided type handler.
/// </summary>
public interface ITypeInjector
{
    /// <summary>
    /// Injects the specified type into the assembly using the provided type handler.
    /// </summary>
    /// <param name="type">The type to be injected.</param>
    /// <param name="handler">The type handler that defines how the type should be injected.</param>
    void Inject(Type type, ITypeHandler handler);
}

/// <summary>
/// Injects classes into a type using the provided class handler.
/// </summary>
public interface IClassInjector
{
    /// <summary>
    /// Injects the specified class into the type using the provided class handler.
    /// </summary>
    /// <param name="type">The class type to be injected.</param>
    /// <param name="handler">The class handler that defines how the class should be injected.</param>
    void Inject(Type type, IClassHandler handler);
}

/// <summary>
/// Injects structs into a type using the provided struct handler.
/// </summary>
public interface IStructInjector
{
    /// <summary>
    /// Injects the specified struct into the type using the provided struct handler.
    /// </summary>
    /// <param name="type">The struct type to be injected.</param>
    /// <param name="handler">The struct handler that defines how the struct should be injected.</param>
    void Inject(Type type, IStructHandler handler);
}

/// <summary>
/// Injects enums into a type using the provided enum handler.
/// </summary>
public interface IEnumInjector
{
    /// <summary>
    /// Injects the specified enum into the type using the provided enum handler.
    /// </summary>
    /// <param name="type">The enum type to be injected.</param>
    /// <param name="handler">The enum handler that defines how the enum should be injected.</param>
    void Inject(Type type, IEnumHandler handler);
}

/// <summary>
/// Injects methods into a type using the provided method handler.
/// </summary>
public interface IMethodInjector
{
    /// <summary>
    /// Injects the specified method into the type using the provided method handler.
    /// </summary>
    /// <param name="method">The method to be injected.</param>
    /// <param name="handler">The method handler that defines how the method should be injected.</param>
    void Inject(MethodInfo method, IMethodHandler handler);
}

/// <summary>
/// Injects properties into a type using the provided property handler.
/// </summary>
public interface IPropertyInjector
{
    /// <summary>
    /// Injects the specified property into the type using the provided property handler.
    /// </summary>
    /// <param name="property">The property to be injected.</param>
    /// <param name="handler">The property handler that defines how the property should be injected.</param>
    void Inject(PropertyInfo property, IPropertyHandler handler);
}

/// <summary>
/// Injects fields into a type using the provided field handler.
/// </summary>
public interface IFieldInjector
{
    /// <summary>
    /// Injects the specified field into the type using the provided field handler.
    /// </summary>
    /// <param name="field">The field to be injected.</param>
    /// <param name="handler">The field handler that defines how the field should be injected.</param>
    void Inject(FieldInfo field, IFieldHandler handler);
}