using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace Gneedle.Inject.Test;

/// <summary>
/// Tests for <see cref="AssemblyHandler.RemoveTheWeaver"/>, which takes the weaver back out of the assembly which it
/// wove.
/// </summary>
[TestFixture]
public class WeaverTraceTests
{
    private const string Ns = "Gneedle.Test.Generated";

    /// <summary>
    /// Add a class which declares an injector to <paramref name="assembly"/>, which is what a project does when it
    /// writes an attribute to inject with.
    /// </summary>
    private static TypeHandler AddInjector(Assembly assembly)
    {
        var handler = (AssemblyHandler) assembly.Handler;
        var injector = (TypeHandler) handler.AddClass("Injector", Ns, ClassFlags.Public)
                                          .WithInterface(typeof(IMethodInjector).ToGneedleType())
                                          .GetHandler();

        var module = assembly.Source.MainModule;
        var method = new MethodDefinition("Inject", MethodAttributes.Public, module.TypeSystem.Void) { DeclaringType = injector.Source };
        method.Parameters.Add(new ParameterDefinition("method", ParameterAttributes.None, module.ImportReference(typeof(MethodInfo))));
        method.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.None, module.ImportReference(typeof(IMethodHandler))));
        method.Body.GetILProcessor().Emit(OpCodes.Ret);
        injector.Source.Methods.Add(method);

        return injector;
    }

    private static bool NamesTheWeaver(Assembly assembly)
        => assembly.Source.MainModule.AssemblyReferences.Any(reference => reference.Name == "Gneedle.Inject");

    /// <summary>
    /// Write the assembly to a stream, which an image whose metadata names what it does not hold cannot be.
    /// </summary>
    private static void VerifyWritable(Assembly assembly)
    {
        using var stream = new MemoryStream();
        assembly.SaveTo(stream);
    }

    [Test]
    public void RemoveTheWeaver_Removes_A_Type_Which_Nothing_Names_And_Drops_The_Reference()
    {
        var assembly = Assembly.Create("TraceOnlyInjectorAssembly");
        AddInjector(assembly);

        // The interface and the parameter of the method are what name the weaver, and the class is what holds them.
        Assert.That(NamesTheWeaver(assembly), Is.True);

        Assert.That(((AssemblyHandler) assembly.Handler).RemoveTheWeaver(), Is.True);

        Assert.That(assembly.Source.MainModule.Types.Any(type => type.Name == "Injector"), Is.False);
        Assert.That(NamesTheWeaver(assembly), Is.False);
        VerifyWritable(assembly);
    }

    [Test]
    public void RemoveTheWeaver_Keeps_A_Type_Which_The_Assembly_Names_And_Strips_It()
    {
        var assembly = Assembly.Create("TracedFieldAssembly");
        var injector = AddInjector(assembly);

        // A field which holds the attribute is what the assembly names it by, which is what a type cannot be removed
        // out from under without leaving the field naming what is not there.
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        host.Source.Fields.Add(new FieldDefinition("Injector", FieldAttributes.Public, injector.Source));

        ((AssemblyHandler) assembly.Handler).RemoveTheWeaver();

        var kept = assembly.Source.MainModule.GetType($"{Ns}.Injector");
        Assert.That(kept, Is.Not.Null, "the type which the field names was removed.");
        Assert.That(kept!.Interfaces.Any(implementation => implementation.InterfaceType.FullName == typeof(IMethodInjector).FullName), Is.False);
        Assert.That(kept.Methods.Any(method => method.Name == "Inject"), Is.False);

        // The interface and the method which reached for the weaver went with the strip, so the class which the field
        // holds names the weaver no longer, and the reference goes with them: the field keeps its type and the assembly
        // keeps no weaver.
        Assert.That(NamesTheWeaver(assembly), Is.False);
        VerifyWritable(assembly);
    }

    [Test]
    public void RemoveTheWeaver_Of_An_Assembly_Which_Declares_No_Injector_Changes_Nothing()
    {
        var assembly = Assembly.Create("UntracedAssembly");
        var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
        host.AddMethod("Ping", typeof(void).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);

        Assert.That(((AssemblyHandler) assembly.Handler).RemoveTheWeaver(), Is.False);
        Assert.That(assembly.Source.MainModule.Types.Any(type => type.Name == "Host"), Is.True);
    }
}
