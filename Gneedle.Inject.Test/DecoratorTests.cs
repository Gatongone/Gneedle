using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

[TestFixture]
public class DecoratorTests
{
    private const string Ns = "Gneedle.Test.Generated";

    private static TypeHandler NewClass()
    {
        var handler = (AssemblyHandler) Assembly.Create("DecoratorTestsAssembly").Handler;
        return (TypeHandler) handler.AddClass("Host", Ns, ClassFlags.Public).GetHandler();
    }

    // region MethodDecorator

    [Test]
    public void MethodDecorator_Chain_Builds_Method()
    {
        var host = NewClass();
        var method = host.AddMethod("Compute")
                         .WithReturnType(typeof(int))
                         .WithParameter(typeof(int))
                         .WithParameter(typeof(string))
                         .WithFlags(MethodFlags.Public | MethodFlags.Static)
                         .GetHandler();

        Assert.That(method, Is.Not.Null);
        Assert.That(method.Name, Is.EqualTo("Compute"));
        Assert.That(((MethodHandler) method).Source.IsStatic, Is.True);
        Assert.That(((MethodHandler) method).Source.ReturnType.FullName, Is.EqualTo(typeof(int).FullName));
        Assert.That(((MethodHandler) method).Source.Parameters.Count, Is.EqualTo(2));
    }

    [Test]
    public void MethodDecorator_WithBody_Emits_Throw()
    {
        var host = NewClass();
        var method = host.AddMethod("Do")
                         .WithBody(DefaultMethodBody.ThrowException)
                         .GetHandler();

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        Assert.That(ins.Any(i => i.OpCode == OpCodes.Throw), Is.True);
    }

    // endregion

    // region FieldDecorator

    [Test]
    public void FieldDecorator_Chain_Builds_Field()
    {
        var host = NewClass();
        var field = host.AddField("Counter")
                        .WithType(typeof(int))
                        .AsStatic()
                        .GetHandler();

        Assert.That(field, Is.Not.Null);
        Assert.That(field.Name, Is.EqualTo("Counter"));
        var source = ((FieldHandler) field).Source;
        Assert.That(source.IsStatic, Is.True);
        Assert.That(source.FieldType.FullName, Is.EqualTo(typeof(int).FullName));
        Assert.That(host.Source.Fields.Contains(source), Is.True);
    }

    // endregion

    // region PropertyDecorator

    [Test]
    public void PropertyDecorator_Chain_Builds_Property()
    {
        var host = NewClass();
        var property = host.AddProperty("Value")
                           .WithType(typeof(int))
                           .WithGetter(DefaultPropertyBody.WithFieldOperation)
                           .WithSetter(DefaultPropertyBody.WithFieldOperation)
                           .GetHandler();

        Assert.That(property, Is.Not.Null);
        Assert.That(property.Name, Is.EqualTo("Value"));
        Assert.That(property.GetGetter(), Is.Not.Null);
        Assert.That(property.GetSetter(), Is.Not.Null);
        Assert.That(host.Source.Properties.Any(p => p.Name == "Value"), Is.True);
    }

    [Test]
    public void PropertyDecorator_WithFieldOperation_Creates_Backing_Field()
    {
        var host = NewClass();
        var property = host.AddProperty("Value")
                           .WithType(typeof(int))
                           .WithGetter(DefaultPropertyBody.WithFieldOperation)
                           .WithSetter(DefaultPropertyBody.WithFieldOperation)
                           .GetHandler();

        // Backing field should be created: <Value>k__BackingField
        var backingField = host.Source.Fields.FirstOrDefault(f => f.Name == "<Value>k__BackingField");
        Assert.That(backingField, Is.Not.Null);
        Assert.That(backingField!.IsPrivate, Is.True);
    }

    // endregion
}
