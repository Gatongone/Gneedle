namespace Gneedle.Inject;

public interface IBaseTypeContainer
{
    IClassHandler BaseType { get; }
}

public interface IInterfaceContainer
{
    bool ContainsInterface(IType interfaceType);
}

public interface ITypeHandler : IAttributeContainer
{
    IAssemblyHandler AssemblyHandler { get; }
    string Name { get; set; }
    string Namespace { get; set; }
    bool TryGetRuntimeType(out Type? type);
    IMethodHandler AddMethod(string methodName, IType returnType, IType[] parameterTypes, MethodFlags methodFlags = MethodFlags.Public);
    IMethodHandler AddMethod(string methodName, IType returnType, Constraint[] genericArguments, IType[] parameterTypes, MethodFlags methodFlags = MethodFlags.Public);
}

public interface IClassHandler : ITypeHandler, IBaseTypeContainer, IInterfaceContainer;
public interface IStructHandler : ITypeHandler, IInterfaceContainer;
public interface IEnumHandler : ITypeHandler;

public static class TypeHandlerExtensions
{
    extension(IInterfaceContainer container)
    {
        public bool ContainsInterface(Type interfaceType) => container.ContainsInterface(interfaceType.ToGneedleType());
        public bool ContainsInterface<TInterface>() => container.ContainsInterface(typeof(TInterface));
    }
}