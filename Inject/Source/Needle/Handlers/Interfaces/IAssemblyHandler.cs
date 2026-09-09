namespace Gneedle.Inject;

public interface IAssemblyHandler
{
    void AddReference(Assembly targetAssembly);
    ITypeHandler? GetType(string typeFullName);
    ITypeHandler GetType(Type type);
    ITypeHandler[] GetTypes();
    ClassDecorator AddClass(string typeName, string typeNamespace , ClassFlags flags = ClassFlags.Public);
    StructDecorator AddStruct(string className, string typeNamespace, StructFlags flags = StructFlags.Public);
    EnumDecorator AddEnum(string enumName, string typeNamespace, EnumFlags flags = EnumFlags.Public);
}