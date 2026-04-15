namespace Gneedle.Inject;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class FromAssemblyAttribute : Attribute
{
    public FromAssemblyAttribute(string fullName) { }
}