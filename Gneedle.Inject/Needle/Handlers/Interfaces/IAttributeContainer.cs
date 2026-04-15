namespace Gneedle.Inject;

public interface IAttributeContainer
{
    bool ContainsAttribute(IType attributeType);
}

public static class AttributeExtensions
{
    extension(IAttributeContainer container)
    {
        public bool ContainsAttribute(Type attributeType) => container.ContainsAttribute(attributeType.ToGneedleType());
        public bool ContainsAttribute<TAttribute>() where TAttribute : Attribute => container.ContainsAttribute(typeof(TAttribute));
    }
}