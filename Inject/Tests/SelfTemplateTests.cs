using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Gneedle.Inject.Test;

/// <summary>
/// An injector which gives the member it stands on the body of another member of the type which declares it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WearTheTemplateOfMyOwnTypeAttribute : Attribute, IMethodInjector
{
    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public void Inject(System.Reflection.MethodInfo method, IMethodHandler handler)
    {
        var type = method.DeclaringType!;
        handler.SetBody(type.GetMethod(nameof(ASelfTemplatedType.Template))!);
    }
}

/// <summary>
/// The type whose member is woven with a template of the type itself, which reads the instance which the member being
/// woven belongs to.
/// </summary>
public class ASelfTemplatedType
{
    /// <summary>The instance which the template compares the receiver with.</summary>
    public static ASelfTemplatedType? s_Instance;

    /// <summary>The member which is woven.</summary>
    [WearTheTemplateOfMyOwnType]
    public virtual bool IsTheSame() => false;

    /// <summary>The template, which is a member of the type it is woven into and reads its own instance.</summary>
    public bool Template() => s_Instance == this;
}

/// <summary>
/// Tests for a template which is a member of the type it is woven into, which is the one shape in which a template
/// reads the instance which the member being woven belongs to: the instance a template of another type belongs to is
/// not that one, and a template which reads it is refused by name.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SelfTemplateTests
{
    [Test]
    public void A_Template_Of_The_Woven_Type_Reads_The_Instance_Of_The_Member_It_Is_Woven_Into()
    {
        var image = File.ReadAllBytes(typeof(SelfTemplateTests).Assembly.Location);
        var (changed, woven) = Injections.Apply(AssemblyLoader.LoadFromBytes(image), image);
        Assert.That(changed, Is.True, "the assembly of these tests was woven by none of its injectors.");

        using var stream = new MemoryStream(woven);
        using var read = AssemblyDefinition.ReadAssembly(stream);
        var body = read.MainModule.GetType(typeof(ASelfTemplatedType).FullName!)!
                        .Methods.Single(method => method.Name == nameof(ASelfTemplatedType.IsTheSame)).Body;

        Assert.Multiple(() =>
        {
            Assert.That(body.Instructions.Any(ins => ins.OpCode == OpCodes.Ldsfld), Is.True,
                "the woven body does not read the field the template compares the instance with.");
            Assert.That(body.Instructions.Any(ins => ins.OpCode == OpCodes.Ldarg_0), Is.True,
                "the woven body does not load the receiver which the template reads as its own instance.");
        });
    }
}
