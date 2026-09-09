namespace Gneedle.Inject;

public interface IFieldHandler
{
    string Name { get; }
    ITypeHandler DeclaringTypeHandler { get; }
}
