using Mono.Cecil;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;

namespace Gneedle.Inject.Test;

using static TestFixtures;

/// <summary>
/// Tests for the member of another type which the template names through `Static`, which are the tests of <see cref="PointerTests"/> for that one placeholder.
/// </summary>
public partial class PointerTests
{
    /// <summary>
    /// The kinds of static member which the placeholder of a type reaches, which the fixture of a case builds on the
    /// type it names.
    /// </summary>
    public enum StaticMember
    {
        /// <summary>A member of a type of the framework, which the template names in its own source.</summary>
        MethodOfAFramework,

        /// <summary>A member of a type which the fixture declares.</summary>
        MethodOfTheType,
        FieldGet,
        FieldSet,
        PropertyGet,
        PropertySet
    }

    [TestCase(StaticMember.MethodOfAFramework, nameof(InstanceStaticTemplates.StaticMethod_BCL), "get_CommandLine", typeof(string))]
    [TestCase(StaticMember.MethodOfTheType, nameof(InstanceStaticTemplates.StaticMethod_Local), "GetValue", typeof(int))]
    [TestCase(StaticMember.FieldGet, nameof(InstanceStaticTemplates.StaticField_Get), "StaticField", typeof(int))]
    [TestCase(StaticMember.FieldSet, nameof(InstanceStaticTemplates.StaticField_Set), "StaticField", typeof(int))]
    [TestCase(StaticMember.PropertyGet, nameof(InstanceStaticTemplates.StaticProperty_Get), "get_StaticProperty", typeof(int))]
    [TestCase(StaticMember.PropertySet, nameof(InstanceStaticTemplates.StaticProperty_Set), "set_StaticProperty", typeof(int))]
    public void A_Static_Member_Is_Reached_Through_The_Placeholder_Of_A_Type(StaticMember member, string template, string name, Type returns)
    {
        // The placeholder Static names a type by a string, and the member which the template reaches on it stands in the
        // woven body as the member itself rather than as a call of the placeholder: a member of every kind is written
        // that way, and no operand of the body names the placeholder afterwards.
        var asm = Assembly.Create("StaticMemberAssembly");
        var handler = (AssemblyHandler) asm.Handler;
        var host = AddAHost(handler);

        BuildTheMember(member, asm, handler, name);

        var method = member is StaticMember.FieldSet or StaticMember.PropertySet
            ? host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(int).ToGneedleType())], MethodFlags.Public | MethodFlags.Static)
            : host.AddMethod("Run", returns.ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
        method.SetBody(Template(typeof(InstanceStaticTemplates), template));

        var ins = ((MethodHandler) method).Source.Body.Instructions.ToArray();
        var opcode = member switch
        {
            StaticMember.FieldGet => OpCodes.Ldsfld,
            StaticMember.FieldSet => OpCodes.Stsfld,
            _                     => OpCodes.Call
        };
        Assert.Multiple(() =>
        {
            Assert.That(ins.Any(i => i.OpCode == opcode && i.Operand is MemberReference reference && reference.Name == name), Is.True,
                $"the member which the placeholder names was not reached by a {opcode.Name}.");
            Assert.That(ins.Any(i => i.Operand is MemberReference reference && reference.DeclaringType.FullName == Static.TYPE_NAME), Is.False,
                "an instruction of the body still names the placeholder.");
        });
        if (member is StaticMember.PropertyGet)
        {
            // The property holds a getter and no setter, and the member which is woven is static: no receiver is
            // written, because the property being reached is of a static member and the accessor of it is static as
            // well. A receiver written here is the load of a `this` which the member does not have.
            Assert.That(ins.Any(i => i.OpCode == OpCodes.Ldarg_0), Is.False, "a receiver was written for a static member.");
        }
    }

    /// <summary>
    /// Declare on the type which the template names the member which a case reaches, which is the fixture the case
    /// needs and no more.
    /// </summary>
    /// <param name="member">The kind of member the case reaches.</param>
    /// <param name="asm">The assembly which is built.</param>
    /// <param name="handler">The handler of the assembly.</param>
    /// <param name="name">The name of the member.</param>
    private static void BuildTheMember(StaticMember member, Assembly asm, AssemblyHandler handler, string name)
    {
        if (member is StaticMember.MethodOfAFramework) return;

        var module = asm.Source.MainModule;
        var staticClass = AddAHost(handler, "LocalStatic");

        switch (member)
        {
            case StaticMember.MethodOfTheType:
                var staticMethod = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Int32);
                staticMethod.Body.GetILProcessor().Emit(OpCodes.Ret);
                staticClass.Source.Methods.Add(staticMethod);
                break;

            case StaticMember.FieldGet or StaticMember.FieldSet:
                staticClass.Source.Fields.Add(new FieldDefinition(name, FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32));
                break;

            case StaticMember.PropertyGet or StaticMember.PropertySet:
                var property = new PropertyDefinition(name.Substring(name.IndexOf('_') + 1), PropertyAttributes.None, module.TypeSystem.Int32);
                var accessor = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    member is StaticMember.PropertySet ? module.TypeSystem.Void : module.TypeSystem.Int32)
                {
                    DeclaringType = staticClass.Source,
                };
                if (member is StaticMember.PropertySet)
                {
                    accessor.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
                    property.SetMethod = accessor;
                }
                else
                {
                    property.GetMethod = accessor;
                }

                accessor.Body.GetILProcessor().Emit(OpCodes.Ret);
                staticClass.Source.Methods.Add(accessor);
                staticClass.Source.Properties.Add(property);
                break;
        }
    }
}