using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Gneedle.Inject.Test;

[TestFixture]
public class PropertyTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static TypeHandler NewClass()
    {
        var handler = (AssemblyHandler) Assembly.Create("PropertyTestAssembly").Handler;
        return (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
    }

    private static PropertyHandler NewProperty(TypeHandler host, string name, bool withGetter, bool withSetter)
    {
        var module = host.Source.Module;
        var propertyType = module.TypeSystem.Int32;
        var property = new PropertyDefinition(name, PropertyAttributes.None, propertyType);

        if (withGetter)
        {
            var getter = new MethodDefinition(
                $"get_{name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                propertyType);
            getter.Body.GetILProcessor().Emit(OpCodes.Ret);
            getter.DeclaringType = host.Source;
            property.GetMethod = getter;
            host.Source.Methods.Add(getter);
        }

        if (withSetter)
        {
            var setter = new MethodDefinition(
                $"set_{name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, propertyType));
            setter.Body.GetILProcessor().Emit(OpCodes.Ret);
            setter.DeclaringType = host.Source;
            property.SetMethod = setter;
            host.Source.Methods.Add(setter);
        }

        host.Source.Properties.Add(property);
        return new PropertyHandler(property, host);
    }

    [Test]
    public void Name_And_FullName_Delegate_To_Source()
    {
        var host = NewClass();
        var property = NewProperty(host, "Value", withGetter: true, withSetter: true);

        Assert.That(property.Name, Is.EqualTo("Value"));
        Assert.That(property.FullName, Does.Contain("Value"));
    }

    [Test]
    public void GetGetter_Returns_Handler_When_Present()
    {
        var host = NewClass();
        var property = NewProperty(host, "Value", withGetter: true, withSetter: false);

        var getter = property.GetGetter();
        Assert.That(getter, Is.Not.Null);
        Assert.That(getter!.Name, Is.EqualTo("get_Value"));
    }

    [Test]
    public void GetSetter_Returns_Handler_When_Present()
    {
        var host = NewClass();
        var property = NewProperty(host, "Value", withGetter: false, withSetter: true);

        var setter = property.GetSetter();
        Assert.That(setter, Is.Not.Null);
        Assert.That(setter!.Name, Is.EqualTo("set_Value"));
    }

    [Test]
    public void GetGetter_Returns_Null_When_Absent()
    {
        var host = NewClass();
        var property = NewProperty(host, "Value", withGetter: false, withSetter: true);

        Assert.That(property.GetGetter(), Is.Null);
    }

    [Test]
    public void GetSetter_Returns_Null_When_Absent()
    {
        var host = NewClass();
        var property = NewProperty(host, "Value", withGetter: true, withSetter: false);

        Assert.That(property.GetSetter(), Is.Null);
    }

    [Test]
    public void GetGetter_Is_Cached()
    {
        var host = NewClass();
        var property = NewProperty(host, "Value", withGetter: true, withSetter: false);

        Assert.That(property.GetGetter(), Is.SameAs(property.GetGetter()));
    }
}
