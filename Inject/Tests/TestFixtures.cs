using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace Gneedle.Inject.Test;

/// <summary>
/// The pieces which the fixtures of the tests are built from, which every file of tests held a copy of its own of.
/// </summary>
internal static class TestFixtures
{
    /// <summary>
    /// The name of the namespace which the types a test describes are declared in.
    /// </summary>
    internal const string Ns = "Gneedle.Test.Generated";

    /// <summary>
    /// Create an assembly of a class named <c>Host</c>, and hand back the handler of the type, the handler of the
    /// assembly which declares it, and the module of that assembly.
    /// </summary>
    /// <param name="assemblyName">The name of the assembly to build, which a test which loads its host gives one of its
    /// own because two assemblies of one name cannot be loaded into one run.</param>
    /// <param name="typeName">The name of the class to declare, which is <c>Host</c> for the tests which read the type
    /// they build rather than the one they name.</param>
    /// <returns>The handler of the assembly, the handler of the host, and the module which declares it.</returns>
    internal static (AssemblyHandler Handler, TypeHandler Host, ModuleDefinition Module) NewHost(string assemblyName, string typeName = "Host")
    {
        var assembly = Assembly.Create(assemblyName);
        var handler = (AssemblyHandler) assembly.Handler;
        var host = (TypeHandler) handler.AddClass(typeName, Ns, ClassFlags.Public).GetHandler();

        return (handler, host, assembly.Source.MainModule);
    }

    /// <summary>
    /// Create an assembly which declares a class named <c>Outer</c>, and hand back the handler of the assembly and the
    /// definition of that class.
    /// </summary>
    /// <param name="assemblyName">The name of the assembly to build.</param>
    /// <returns>The handler of the assembly, and the definition of the class it declares.</returns>
    internal static (AssemblyHandler Handler, TypeDefinition Outer) NewOuter(string assemblyName)
    {
        var assembly = Assembly.Create(assemblyName);
        var module = assembly.Source.MainModule;
        var outer = new TypeDefinition(Ns, "Outer", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(outer);

        return ((AssemblyHandler) assembly.Handler, outer);
    }

    /// <summary>
    /// The same, with a class named <c>Inner</c> declared inside the one which was built, which is the shape a nested
    /// type is read through.
    /// </summary>
    /// <param name="assemblyName">The name of the assembly to build.</param>
    /// <returns>The handler of the assembly, the class it declares, and the class declared inside it.</returns>
    internal static (AssemblyHandler Handler, TypeDefinition Outer, TypeDefinition Inner) NewOuterWithInner(string assemblyName)
    {
        var (handler, outer) = NewOuter(assemblyName);
        var inner = new TypeDefinition(Ns, "Inner", TypeAttributes.NestedPublic | TypeAttributes.Class, outer.Module.TypeSystem.Object) { DeclaringType = outer };
        outer.NestedTypes.Add(inner);

        return (handler, outer, inner);
    }

    /// <summary>
    /// The method of a holder which a template is, which is what a test hands over as the body to be woven.
    /// </summary>
    /// <param name="holder">The type which declares the template.</param>
    /// <param name="name">The name of the template.</param>
    /// <returns>The template.</returns>
    internal static MethodInfo Template(Type holder, string name) => holder.GetMethod(name)!;

    /// <summary>
    /// Give a host the constructor which an instance of it is made with, which Cecil writes for no type which it holds:
    /// it chains to the constructor of the type which the host derives from, which is the one of the values of the
    /// framework where the caller names none.
    /// </summary>
    /// <param name="host">The host which the constructor is added to.</param>
    /// <param name="baseType">The type which the host derives from, or null for the values of the framework.</param>
    internal static void AddAnInstanceConstructor(TypeHandler host, Type? baseType = null) => AddAnInstanceConstructor(host.Source, host.Source.Module, baseType);

    /// <summary>
    /// The same, of a definition which the module does not hold yet, which is why the module is handed over as well.
    /// </summary>
    /// <param name="type">The type definition which the constructor is added to.</param>
    /// <param name="module">The module which the definition is declared by.</param>
    /// <param name="baseType">The type which the definition derives from, or null for the values of the framework.</param>
    internal static void AddAnInstanceConstructor(TypeDefinition type, ModuleDefinition module, Type? baseType = null)
    {
        var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);

        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference((baseType ?? typeof(object)).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        type.Methods.Add(constructor);
    }

    /// <summary>
    /// Declare the class which a template is woven into in the assembly of a handler, and hand back the handler of it.
    /// </summary>
    /// <param name="handler">The handler of the assembly which the class is declared in.</param>
    /// <param name="typeName">The name of the class, which is <c>Host</c> for the files which call it that.</param>
    /// <returns>The handler of the class.</returns>
    internal static TypeHandler AddAHost(AssemblyHandler handler, string typeName = "Host")
        => (TypeHandler) handler.AddClass(typeName, Ns, ClassFlags.Public).GetHandler();

    /// <summary>
    /// The same, of the assembly a test built rather than of the handler it took out of it.
    /// </summary>
    /// <param name="assembly">The assembly which the class is declared in.</param>
    /// <param name="typeName">The name of the class.</param>
    /// <returns>The handler of the class.</returns>
    internal static TypeHandler AddAHost(Assembly assembly, string typeName = "Host") => AddAHost((AssemblyHandler) assembly.Handler, typeName);
}
