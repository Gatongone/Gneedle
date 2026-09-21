using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Assembly = Gneedle.Inject.Assembly;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using GenericParameterAttributes = Mono.Cecil.GenericParameterAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Gneedle.Inject.Test;

using static Gneedle.Inject.Test.TestFixtures;

/// <summary>
/// Tests for the name which the placeholder of the instance is declared under, which are the tests of <see cref="PointerTests"/> for that one placeholder.
/// </summary>
public partial class PointerTests
{
    /// <summary>
    /// The placeholder which wraps the instance a template holds is declared under a name which says what it holds,
    /// because a placeholder which was named after the type of the framework is the type which a template reads
    /// wherever it writes that name among the usings of this library, the keyword <c>object</c> and the full name of
    /// the type being all that is left of it: a template which declares a field, a parameter, a local or a return type
    /// of that name reads the placeholder, and the body which is written from it names a member of the type being woven
    /// where it meant to name a type of the framework.<para/>
    /// The name which the weaving answers a member reference by is the full name of the class, which is built from the
    /// name of the class where the class is declared: the class and the name are read together, so that a class which
    /// is renamed is a class which the weaving knows by the name which it holds now.
    /// </summary>
    [Test]
    public void The_Placeholder_Of_The_Instance_Is_Declared_Under_A_Name_Which_Shadows_Nothing()
    {
        var placeholder = typeof(This).Assembly.GetType("Gneedle.Inject.Instance");
        Assert.That(placeholder, Is.Not.Null, "the placeholder of the instance is not declared under a name which says what it holds.");

        var typeName = placeholder!.GetField("TYPE_NAME", BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue() as string;
        Assert.That(typeName, Is.EqualTo("Gneedle.Inject.Instance"), "the name which the weaving knows the placeholder by is not the full name of the class.");

        Assert.That(typeof(This).Assembly.GetType("Gneedle.Inject.Object"), Is.Null,
            "a class of this library is declared under a name which shadows the type of the framework.");
    }
}
