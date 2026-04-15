using System.Reflection;

namespace Gneedle.Inject;

public interface IPropertyHandler
{
    string Name { get; }
    string FullName { get; }
    ITypeHandler DeclaringTypeHandler { get; }
    IMethodHandler? GetSetter();
    IMethodHandler? GetGetter();
    void SetSetter(MethodInfo body);
    void SetGetter(MethodInfo body);
}