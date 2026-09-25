using Mono.Cecil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// Tests for the property of the type which the template is woven into, which are the tests of <see cref="PointerTests"/> for that one placeholder.
/// </summary>
public partial class PointerTests
{
    /// <summary>
    /// The value which the getter of a property of a host hands back, which a weave of it is run to read.
    /// </summary>
    private const int PROPERTY_VALUE = 4242;

    /// <summary>
    /// Create a host which declares a property of the given name, with the accessors which are asked for, which are
    /// static when that is asked for.
    /// </summary>
    private static TypeHandler NewHostWithProperty(string propertyName, bool withGetter, bool withSetter, bool isVirtual, bool isStatic = false)
    {
        var handler = (AssemblyHandler) Assembly.Create("MemberInjectionPropAssembly").Handler;
        var host = AddAHost(handler);
        var module = host.Source.Module;
        var propertyType = module.TypeSystem.Int32;
        var property = new PropertyDefinition(propertyName, PropertyAttributes.None, propertyType);
        var methodAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig
            | (isStatic ? MethodAttributes.Static : 0)
            | (isVirtual ? MethodAttributes.Virtual | MethodAttributes.NewSlot : 0);

        if (withGetter)
        {
            var getter = new MethodDefinition($"get_{propertyName}", methodAttrs, propertyType) {DeclaringType = host.Source};
            // The getter hands back a value of its own rather than reading a field, so that a weave which calls it can
            // be run and not only read: a body which returns from a member which hands back an int without leaving one
            // on the stack is not IL which the runtime accepts.
            var il = getter.Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4, PROPERTY_VALUE);
            il.Emit(OpCodes.Ret);
            property.GetMethod = getter;
            host.Source.Methods.Add(getter);
        }

        if (withSetter)
        {
            var setter = new MethodDefinition($"set_{propertyName}", methodAttrs, module.TypeSystem.Void) {DeclaringType = host.Source};
            setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, propertyType));
            setter.Body.GetILProcessor().Emit(OpCodes.Ret);
            property.SetMethod = setter;
            host.Source.Methods.Add(setter);
        }

        host.Source.Properties.Add(property);
        return host;
    }

    [Test]
    public void ReadInstanceProperty_Rewrites_To_Call_Getter()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: false);
        var method = host.AddMethod("Read", typeof(int).ToIType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceProperty)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "get_Prop"), Is.True);
    }

    [Test]
    public void WriteInstanceProperty_Rewrites_To_Call_Setter()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: false);
        var method = host.AddMethod("Write", typeof(void).ToIType(), [], [new Parameter(typeof(int).ToIType())], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteInstanceProperty)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "set_Prop"), Is.True);
    }

    [Test]
    public void A_Template_Which_Holds_A_Handle_Of_A_Property_Calls_Its_Accessors()
    {
        // A property is read and written through the accessors rather than the field, and the handle which the template
        // holds names the property rather than one of them: each accessor which a read of the local calls is written as
        // the accessor which the value member of that read stands for.
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: false);
        var ins = Rewrite(host, "Bump", typeof(int), [new Parameter(typeof(int).ToIType())], nameof(ThisMemberTemplates.BumpAHeldHandleOfAProperty), MethodFlags.Public);
        Assert.Multiple(() =>
        {
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "get_Prop"), Is.EqualTo(2), "the getter was not called exactly twice.");
            Assert.That(ins.Count(i => i.OpCode == OpCodes.Call && ((MethodReference) i.Operand).Name == "set_Prop"), Is.EqualTo(1), "the setter was not called exactly once.");
            Assert.That(ins.Any(i => i.Operand is MemberReference {DeclaringType.Namespace: "Gneedle.Inject"}), Is.False,
                "the handle which the template holds was left in the body.");
        });
        DoesNotReferToTheWeaver(host);
    }

    [Test]
    public void ReadVirtualProperty_Rewrites_To_Callvirt_Getter()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: true, isVirtual: true);
        var method = host.AddMethod("Read", typeof(int).ToIType(), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceProperty)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        Assert.That(ins.Any(i => i.OpCode == OpCodes.Callvirt && ((MethodReference) i.Operand).Name == "get_Prop"), Is.True);
    }

    [Test]
    public void ReadProperty_Without_Getter_Throws()
    {
        var host = NewHostWithProperty("Prop", withGetter: false, withSetter: true, isVirtual: false);
        var method = host.AddMethod("Read", typeof(int).ToIType(), [], [], MethodFlags.Public);

        Assert.Throws<WeavingException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadInstanceProperty))));
    }

    [Test]
    public void WriteProperty_Without_Setter_Throws()
    {
        var host = NewHostWithProperty("Prop", withGetter: true, withSetter: false, isVirtual: false);
        var method = host.AddMethod("Write", typeof(void).ToIType(), [], [new Parameter(typeof(int).ToIType())], MethodFlags.Public);

        Assert.Throws<WeavingException>(() => method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteInstanceProperty))));
    }

    /// <summary>
    /// Create a host which is generic in one parameter, and which declares a property of that type with the accessors
    /// which are asked for.
    /// </summary>
    private static TypeHandler NewGenericHostWithProperty(string propertyName, bool withGetter, bool withSetter)
    {
        var handler = (AssemblyHandler) Assembly.Create("MemberInjectionGenericPropAssembly").Handler;
        var host = (TypeHandler) handler.AddClass("Host", NS, ClassFlags.Public)
                                        .WithGenericParameter("T")
                                        .GetHandler();
        var gp = host.Source.GenericParameters[0];
        var module = host.Source.Module;
        var prop = new PropertyDefinition(propertyName, PropertyAttributes.None, gp);
        var methodAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        if (withGetter)
        {
            var getter = new MethodDefinition($"get_{propertyName}", methodAttrs, gp) {DeclaringType = host.Source};
            getter.Body.GetILProcessor().Emit(OpCodes.Ret);
            prop.GetMethod = getter;
            host.Source.Methods.Add(getter);
        }

        if (withSetter)
        {
            var setter = new MethodDefinition($"set_{propertyName}", methodAttrs, module.TypeSystem.Void) {DeclaringType = host.Source};
            setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, gp));
            setter.Body.GetILProcessor().Emit(OpCodes.Ret);
            prop.SetMethod = setter;
            host.Source.Methods.Add(setter);
        }

        host.Source.Properties.Add(prop);
        return host;
    }

    [Test]
    public void ReadGenericProp_Rewrites_To_Call_Getter_With_Correct_Signature()
    {
        var host = NewGenericHostWithProperty("Prop", withGetter: true, withSetter: true);
        var method = host.AddMethod("Get", new GenericParameterType("T"), [], [], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.ReadGenericProp)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var call = ins.FirstOrDefault(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            && ((MethodReference) i.Operand).Name == "get_Prop");
        Assert.Multiple(() =>
        {
            Assert.That(call, Is.Not.Null);
            // The getter's return type should be the generic parameter T.
            Assert.That(((MethodReference) call!.Operand).ReturnType, Is.InstanceOf<GenericParameter>());
        });
    }

    [Test]
    public void WriteGenericProp_Rewrites_To_Call_Setter_With_Correct_Signature()
    {
        var host = NewGenericHostWithProperty("Prop", withGetter: true, withSetter: true);
        var method = host.AddMethod("Set", typeof(void).ToIType(), [], [new Parameter(new GenericParameterType("T"))], MethodFlags.Public);
        method.SetBody(Template(typeof(ThisMemberTemplates), nameof(ThisMemberTemplates.WriteGenericProp)));
        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();

        var call = ins.FirstOrDefault(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            && ((MethodReference) i.Operand).Name == "set_Prop");
        Assert.That(call, Is.Not.Null);
        // The setter's parameter type should be the generic parameter T.
        var param = ((MethodReference) call!.Operand).Parameters[0];
        Assert.That(param.ParameterType, Is.InstanceOf<GenericParameter>());
    }
}