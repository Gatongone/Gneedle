using System.Linq.Expressions;
using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// A context of the runtime which the image of an assembly is loaded into, so that the assemblies of the weaver which
/// the image refers to are read where the weaving which reads them lies.
/// </summary>
/// <remarks>
/// The weaver is loaded by whoever drives it, and a host which loads it into a context of its own - the post processor of
/// a Unity project, whose context Unity makes collectible - holds the only copy of it which a weaving of that host reads.
/// An image which is loaded from bytes beside that context refers to the weaver by its name, and the runtime refuses an
/// assembly of a collectible context which is handed over to a context it made for the image: reading the types of the
/// target fails whole, and an assembly which declares an injector is left unwoven.<para/>
/// The image is loaded into a context of its own instead, which answers the requests of the image itself: the copy of the
/// weaver which lies in the context of the weaving is the one which is answered, and every other assembly is left to the
/// runtime, which reads it from where the process loaded it. A context of its own is also what keeps two loads of one
/// image apart, which the context of the weaver itself would not: a context holds one assembly of a name, and an image
/// which is loaded again - which an editor compiling the same assembly over and over does - would be read as the one
/// which the earlier compilation loaded.<para/>
/// A runtime which holds no context at all, which .NET Framework and Mono are, loads the image the way it always did.
/// </remarks>
internal static class WeavingContext
{
    /// <summary>
    /// The type of a context of the runtime, or null where the runtime holds none.<para/>
    /// The type is asked for by name rather than imported, because the framework which this library is built for as the
    /// package Unity loads, netstandard2.1, holds no context at all, while the runtime which runs a post processor does.
    /// </summary>
    private static readonly Type? s_Type = FindTheType();

    /// <summary>
    /// The method which answers the context an assembly was loaded into.
    /// </summary>
    private static readonly MethodInfo? s_GetLoadContext = s_Type?.GetMethod(
        "GetLoadContext", BindingFlags.Public | BindingFlags.Static, null, [typeof(System.Reflection.Assembly)], null);

    /// <summary>
    /// The property which answers the assemblies which a context holds.
    /// </summary>
    private static readonly PropertyInfo? s_Assemblies = s_Type?.GetProperty("Assemblies");

    /// <summary>
    /// Load the image of <paramref name="rawBytes"/> into a context of its own, or answer null where the runtime holds
    /// no context to load it into.
    /// </summary>
    /// <param name="rawBytes">Bytes that is a COFF-based image containing a managed assembly.</param>
    /// <returns>The loaded assembly, or null where the runtime holds no context.</returns>
    public static System.Reflection.Assembly? Load(byte[] rawBytes)
    {
        if (s_Type is not { } type) return null;

        try
        {
            var resolving = type.GetEvent("Resolving");
            var constructor = type.GetConstructor([typeof(string), typeof(bool)]);
            var loadFromStream = type.GetMethod("LoadFromStream", [typeof(Stream)]);
            if (constructor == null || resolving?.AddMethod == null || loadFromStream == null) return null;

            var context = constructor.Invoke(["Gneedle", true]);
            resolving.AddMethod.Invoke(context, [CreateTheResolution(resolving.EventHandlerType!)]);

            using var stream = new MemoryStream(rawBytes);
            return (System.Reflection.Assembly) loadFromStream.Invoke(context, [stream])!;
        }
        catch (Exception)
        {
            // A runtime which names the type and not the context which it declares - a stub of a runtime which cannot
            // load an assembly of its own at all - loads the image the way it did before this was asked of it.
            return null;
        }
    }

    /// <summary>
    /// The type of a context of the runtime, or null where the runtime holds none.
    /// </summary>
    private static Type? FindTheType()
    {
        foreach (var name in new[] {"System.Runtime.Loader.AssemblyLoadContext, System.Runtime.Loader", "System.Runtime.Loader.AssemblyLoadContext"})
        {
            if (Type.GetType(name, false) is { } type) return type;
        }

        return null;
    }

    /// <summary>
    /// The resolution which the context of the image answers a request which it cannot answer itself with.
    /// </summary>
    /// <param name="handler">The type of the handler which the runtime declares the resolution by.</param>
    /// <returns>The handler of that type.</returns>
    private static Delegate CreateTheResolution(Type handler)
    {
        // The handler is built rather than written, because the type of the event is the one which the runtime declares
        // for it: the sender is a context, which this library cannot name where it is built, and neither can the method
        // of this class which answers the request read it.
        var parameters = handler.GetMethod("Invoke")!.GetParameters();
        var sender = Expression.Parameter(parameters[0].ParameterType, "sender");
        var name = Expression.Parameter(parameters[1].ParameterType, "name");
        var body = Expression.Call(typeof(WeavingContext).GetMethod(nameof(Resolve), BindingFlags.NonPublic | BindingFlags.Static)!, name);
        return Expression.Lambda(handler, body, sender, name).Compile();
    }

    /// <summary>
    /// The assembly which the request of <paramref name="name"/> asks for, taken from the context which the weaving
    /// which answers it lies in.
    /// </summary>
    /// <param name="name">The name of the assembly which is asked for.</param>
    /// <returns>The assembly, or null where the context of the weaving holds none of that name.</returns>
    private static System.Reflection.Assembly? Resolve(AssemblyName name)
    {
        // The copy of the weaver which the weaving holds is the only one which a type of the target can implement the
        // interfaces of, so it is answered from wherever it lies - the context of the host which loaded it, which is not
        // the one of this image. Everything else is left to the runtime, which reads it from where the process loaded
        // it, from the folder it lies in, or not at all.
        if (s_GetLoadContext?.Invoke(null, [typeof(WeavingContext).Assembly]) is not { } context) return null;
        if (s_Assemblies?.GetValue(context) is not IEnumerable<System.Reflection.Assembly> assemblies) return null;

        return assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
    }
}