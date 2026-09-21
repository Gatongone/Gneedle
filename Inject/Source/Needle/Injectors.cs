using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// What an injector may also be, whichever member it is put on: the order in which the injectors of one member are
/// applied is read off the priorities of those which declare one.<para/>
/// The order of a member's injectors is not the order in which they are written where the member is declared: the
/// specification of the language says that the attribute specifications of a member are equivalent in every order, so
/// what the runtime hands back is no order of the source at all. An injector which has to be applied before another -
/// which is the case of two which both write the body of a member, one of which proceeds into what the other wrote -
/// declares a priority rather than relying on where it stands.<para/>
/// Every injector is of this, and an injector which declares no priority of its own is one which is applied as though
/// its priority were nothing: the member cannot be handed a body by the interface, because a default member of an
/// interface is not supported by every framework the weaver is built for - net472 answers one with CS8701 - so each
/// injector which declares none says so itself.
/// </summary>
public interface IInjector
{
    /// <summary>
    /// The order in which this injector is applied among the injectors of the member which carries it: an injector of a
    /// greater priority is applied before one of a lesser priority, and two injectors of one priority are applied in
    /// the order of the names of their types.<para/>
    /// An injector which takes over the body of a member is applied before the one which proceeds into the body which
    /// the member holds, because what the one which proceeds reaches is what the one before it left: an around body
    /// wraps the body which is there when it is applied.
    /// </summary>
    int Priority { get; }
}

/// <summary>
/// Injects assemblies into a project using the provided assembly handler.
/// </summary>
public interface IAssemblyInjector : IInjector
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
public interface ITypeInjector : IInjector
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
public interface IClassInjector : IInjector
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
public interface IStructInjector : IInjector
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
public interface IEnumInjector : IInjector
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
public interface IMethodInjector : IInjector
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
public interface IPropertyInjector : IInjector
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
public interface IFieldInjector : IInjector
{
    /// <summary>
    /// Injects the specified field into the type using the provided field handler.
    /// </summary>
    /// <param name="field">The field to be injected.</param>
    /// <param name="handler">The field handler that defines how the field should be injected.</param>
    void Inject(FieldInfo field, IFieldHandler handler);
}