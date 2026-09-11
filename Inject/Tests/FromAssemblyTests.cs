using Mono.Cecil;
using Mono.Cecil.Cil;
using Gneedle.Inject;
using Gneedle.Test.Generated;

namespace Gneedle.Test.Generated
{
    /// <summary>
    /// Name of the assembly which the stubs below stand for. The stubs live in the test assembly, so the target is built
    /// under this name to give the real types a home.
    /// </summary>
    public static class StubTarget
    {
        public const string AssemblyName = "FromAssemblyTarget";
    }

    /// <summary>
    /// Stubs which stand for the types of <see cref="StubTarget.AssemblyName"/>. They carry the same full names as the
    /// real types, which is what ties them together, so they are declared in the namespace of the generated types. They
    /// live top level in the test assembly so that Cecil can resolve them from disk.
    /// </summary>
    [FromAssembly(StubTarget.AssemblyName)]
    public class Stub
    {
        public static int Field;
        public static int Read() => 0;
        public static int Property { get; set; }
    }

    [FromAssembly(StubTarget.AssemblyName)]
    public class GenericStub<T>
    {
        public static int Count;
    }

    [FromAssembly(StubTarget.AssemblyName)]
    public interface IStub
    {
    }

    public class OuterStub
    {
        [FromAssembly(StubTarget.AssemblyName)]
        public class Inner
        {
            public static int Field;
        }
    }

    /// <summary>
    /// A stub which the target assembly holds no real type for.
    /// </summary>
    [FromAssembly(StubTarget.AssemblyName)]
    public class AbsentStub
    {
        public static int Field;
    }

    /// <summary>
    /// A stub which names an assembly which does not exist.
    /// </summary>
    [FromAssembly("No.Such.Assembly")]
    public class UnresolvableStub
    {
        public static int Field;
    }
}

namespace Gneedle.Inject.Test
{
    /// <summary>
    /// Tests for <see cref="FromAssemblyAttribute"/>, which makes a stub declared in the template assembly stand for the
    /// type of the same full name which another assembly declares.
    /// </summary>
    [TestFixture]
    public class FromAssemblyTests
    {
        private const string Ns = "Gneedle.Test.Generated";

        /// <summary>
        /// Template bodies live in the test assembly so Cecil can resolve them from disk.
        /// </summary>
        public static class Templates
        {
            public static int ReadStubField()    => Stub.Field;
            public static int CallStubMethod()   => Stub.Read();
            public static object CreateStub()    => new Stub();
            public static int ReadStubProperty() => Stub.Property;
            public static List<Stub> StubList()  => new List<Stub>();

            public static bool StubListLocal()
            {
                List<Stub> local = new();
                GC.KeepAlive(local);
                return true;
            }

            public static int ReadGenericStubCount() => GenericStub<int>.Count;

            public static Stub[] StubArray()          => new Stub[0];
            public static int    ReadNestedStubField() => OuterStub.Inner.Field;

            public static string ObjectMethod_StubReceiver(Stub instance)   => new Object(instance).Method<NameGetter>("Read")();
            public static int    ObjectField_StubReceiver(Stub instance)    => new Object(instance).Field<int>(nameof(Stub.Field)).Get();
            public static int    ObjectProperty_StubReceiver(Stub instance) => new Object(instance).Property<int>(nameof(Stub.Property)).Get();

            public static int ReadAbsentStubField()       => AbsentStub.Field;
            public static int ReadUnresolvableStubField() => UnresolvableStub.Field;
        }

        public delegate string NameGetter();

        // region Target fixture

        /// <summary>
        /// Create the target assembly and the real types which the stubs stand for. The real types carry the same full
        /// names as the stubs, so nothing but the module they are declared by tells them apart.
        /// </summary>
        private static Assembly NewTarget()
        {
            var assembly = Assembly.Create(StubTarget.AssemblyName);
            var module = assembly.Source.MainModule;

            var stub = new TypeDefinition(Ns, nameof(Stub), TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            stub.Fields.Add(new FieldDefinition(nameof(Stub.Field), FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32));

            var read = new MethodDefinition(nameof(Stub.Read), MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Int32);
            read.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 41));
            read.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            stub.Methods.Add(read);

            var backing = new FieldDefinition("Backing", FieldAttributes.Private | FieldAttributes.Static, module.TypeSystem.Int32);
            stub.Fields.Add(backing);
            var getter = new MethodDefinition("get_Property", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, module.TypeSystem.Int32);
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, backing));
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            stub.Methods.Add(getter);
            var setter = new MethodDefinition("set_Property", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, module.TypeSystem.Void);
            setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Int32));
            setter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            setter.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, backing));
            setter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            stub.Methods.Add(setter);
            stub.Properties.Add(new PropertyDefinition(nameof(Stub.Property), PropertyAttributes.None, module.TypeSystem.Int32) { GetMethod = getter, SetMethod = setter });

            var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
            constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            stub.Methods.Add(constructor);

            module.Types.Add(stub);

            // The field of the generic stub is typed int rather than by the generic parameter, so that the emitted
            // reference does not carry a generic parameter of a foreign module (Cecil issue #954).
            var generic = new TypeDefinition(Ns, "GenericStub`1", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            generic.GenericParameters.Add(new GenericParameter("T", generic));
            generic.Fields.Add(new FieldDefinition(nameof(GenericStub<int>.Count), FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32));
            module.Types.Add(generic);

            module.Types.Add(new TypeDefinition(Ns, nameof(IStub), TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract));

            // The real type of a nested stub is nested in the very same way, so that both carry the same full name. A
            // nested type which the compiler emits holds no namespace of its own, so this one holds none either: the
            // last segment of the name of `Namespace.Outer/Inner` is the simple name.
            var outer = new TypeDefinition(Ns, nameof(OuterStub), TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            var inner = new TypeDefinition(string.Empty, nameof(OuterStub.Inner), TypeAttributes.NestedPublic | TypeAttributes.Class, module.TypeSystem.Object) { DeclaringType = outer };
            inner.Fields.Add(new FieldDefinition(nameof(OuterStub.Inner.Field), FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32));
            outer.NestedTypes.Add(inner);
            module.Types.Add(outer);

            return assembly;
        }

        private static MethodHandler Weave(string templateName, IType[]? parameterTypes = null)
        {
            var assembly = NewTarget();
            var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
            var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToGneedleType(), [], parameterTypes ?? [],
                                                        MethodFlags.Public | MethodFlags.Static);
            method.SetBody(typeof(Templates).GetMethod(templateName)!);
            return method;
        }

        // endregion

        // region Member access

        [Test]
        public void SetBody_Replaces_The_Stub_Field()
        {
            var method = Weave(nameof(Templates.ReadStubField));
            var field = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<FieldReference>()
                              .FirstOrDefault(reference => reference.Name == nameof(Stub.Field));

            Assert.That(field, Is.Not.Null);
            Assert.That(field!.DeclaringType.Module, Is.SameAs(method.Source.Module));
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Method_Call()
        {
            var method = Weave(nameof(Templates.CallStubMethod));
            var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                             .FirstOrDefault(reference => reference.Name == nameof(Stub.Read));

            Assert.That(call, Is.Not.Null);
            Assert.That(call!.DeclaringType.Module, Is.SameAs(method.Source.Module));
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Constructor()
        {
            var method = Weave(nameof(Templates.CreateStub));
            var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                             .FirstOrDefault(reference => reference.Name == ".ctor");

            Assert.That(call, Is.Not.Null);
            Assert.That(call!.DeclaringType.Module, Is.SameAs(method.Source.Module));
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Property_Access()
        {
            var method = Weave(nameof(Templates.ReadStubProperty));
            var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                             .FirstOrDefault(reference => reference.Name == "get_Property");

            Assert.That(call, Is.Not.Null);
            Assert.That(call!.DeclaringType.Module, Is.SameAs(method.Source.Module));
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Receiver_Of_Object_Method()
        {
            var method = Weave(nameof(Templates.ObjectMethod_StubReceiver), [typeof(Stub).ToGneedleType()]);
            var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                             .FirstOrDefault(reference => reference.Name == nameof(Stub.Read));

            Assert.That(call, Is.Not.Null);
            Assert.That(call!.DeclaringType.Module, Is.SameAs(method.Source.Module));

            // The marker itself is rewritten away, so none of its members survives.
            Assert.That(method.Source.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference
                                                                        && reference.DeclaringType.FullName == Object.TYPE_NAME), Is.False);
        }

        // endregion

        // region Nested in a type shape

        [Test]
        public void SetBody_Replaces_The_Stub_Nested_In_A_Generic_Argument()
        {
            var method = Weave(nameof(Templates.StubList));
            var type = method.Source.ReturnType;

            Assert.That(type, Is.InstanceOf<GenericInstanceType>());
            var argument = ((GenericInstanceType) type).GenericArguments[0];
            Assert.That(argument.FullName, Is.EqualTo($"{Ns}.{nameof(Stub)}"));
            Assert.That(argument.Module, Is.SameAs(method.Source.Module));
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Element_Of_A_Generic_Instance()
        {
            var method = Weave(nameof(Templates.ReadGenericStubCount));
            var field = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<FieldReference>()
                              .FirstOrDefault(reference => reference.Name == nameof(GenericStub<int>.Count));

            Assert.That(field, Is.Not.Null);
            Assert.That(field!.DeclaringType, Is.InstanceOf<GenericInstanceType>());
            Assert.That(((GenericInstanceType) field.DeclaringType).ElementType.Module, Is.SameAs(method.Source.Module));
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Of_A_Local_Variable()
        {
            var method = Weave(nameof(Templates.StubListLocal));
            var variable = method.Source.Body.Variables.First();

            Assert.That(variable.VariableType, Is.InstanceOf<GenericInstanceType>());
            Assert.That(((GenericInstanceType) variable.VariableType).GenericArguments[0].Module, Is.SameAs(method.Source.Module));
        }

        // endregion

        // region The produced assembly

        [Test]
        public void SetBody_Does_Not_Leak_The_Stub_Into_The_Produced_Assembly()
        {
            // The real type is declared by the module which is produced, so a reference to it needs no type reference at
            // all. One for the name which the stub shares with it therefore means that the stub itself survived, which is
            // the leak the whole attribute exists to prevent. Asserting on the assembly reference instead would not tell
            // them apart: importing the template method references the template assembly either way.
            var assembly = NewTarget();
            var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
            var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
            method.SetBody(typeof(Templates).GetMethod(nameof(Templates.CallStubMethod))!);

            using var stream = new MemoryStream();
            assembly.SaveTo(stream);
            stream.Position = 0;

            var reread = AssemblyDefinition.ReadAssembly(stream);
            Assert.That(reread.MainModule.GetTypeReferences().Any(reference => reference.FullName == $"{Ns}.{nameof(Stub)}"), Is.False);
        }

        [Test]
        public void Load_Executes_A_Method_Which_Reads_The_Real_Type()
        {
            // The real Read returns 41 where the stub returns 0, so the value tells the two apart even though they share
            // a full name. Executing the produced method is the strongest form of that check.
            var assembly = NewTarget();
            var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
            var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
            method.SetBody(typeof(Templates).GetMethod(nameof(Templates.CallStubMethod))!);

            var loaded = assembly.Load();
            var run = loaded.GetType($"{Ns}.Host")!.GetMethod("Run")!;

            Assert.That(run.Invoke(null, null), Is.EqualTo(41));
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Of_An_Array()
        {
            var method = Weave(nameof(Templates.StubArray));
            var type = method.Source.ReturnType;

            Assert.That(type, Is.InstanceOf<ArrayType>());
            Assert.That(((ArrayType) type).ElementType.Module, Is.SameAs(method.Source.Module));
        }

        [Test]
        public void SetBody_Replaces_The_Nested_Stub()
        {
            var method = Weave(nameof(Templates.ReadNestedStubField));
            var field = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<FieldReference>()
                              .FirstOrDefault(reference => reference.Name == nameof(OuterStub.Inner.Field));

            Assert.That(field, Is.Not.Null);
            Assert.That(field!.DeclaringType.Module, Is.SameAs(method.Source.Module));
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Receiver_Of_Object_Field()
        {
            var method = Weave(nameof(Templates.ObjectField_StubReceiver), [typeof(Stub).ToGneedleType()]);
            var field = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<FieldReference>()
                              .FirstOrDefault(reference => reference.Name == nameof(Stub.Field));

            Assert.That(field, Is.Not.Null);
            Assert.That(field!.DeclaringType.Module, Is.SameAs(method.Source.Module));
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Receiver_Of_Object_Property()
        {
            var method = Weave(nameof(Templates.ObjectProperty_StubReceiver), [typeof(Stub).ToGneedleType()]);
            var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                             .FirstOrDefault(reference => reference.Name == "get_Property");

            Assert.That(call, Is.Not.Null);
            Assert.That(call!.DeclaringType.Module, Is.SameAs(method.Source.Module));
        }

        // endregion

        // region Public API

        [Test]
        public void WithBaseType_Replaces_The_Stub()
        {
            var assembly = NewTarget();
            var host = (ClassHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public)
                                                                         .WithBaseType(typeof(Stub))
                                                                         .GetHandler();

            Assert.That(host.Source.BaseType!.Module, Is.SameAs(host.Source.Module));
        }

        [Test]
        public void WithInterface_Replaces_The_Stub()
        {
            var assembly = NewTarget();
            var host = (ClassHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public)
                                                                         .WithInterface(typeof(IStub))
                                                                         .GetHandler();

            var implementation = host.Source.Interfaces.Single();
            Assert.That(implementation.InterfaceType.Module, Is.SameAs(host.Source.Module));
        }

        [Test]
        public void AddMethod_Replaces_The_Stub_Parameter_Type()
        {
            var assembly = NewTarget();
            var host = (TypeHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", Ns, ClassFlags.Public).GetHandler();
            var method = (MethodHandler) host.AddMethod("Run", typeof(void).ToGneedleType(), [], [typeof(Stub).ToGneedleType()],
                                                        MethodFlags.Public | MethodFlags.Static);

            var parameter = method.Source.Parameters[0].ParameterType;
            Assert.That(parameter.FullName, Is.EqualTo($"{Ns}.{nameof(Stub)}"));
            Assert.That(parameter.Module, Is.SameAs(method.Source.Module));
        }

        // endregion

        // region The resolver

        [Test]
        public void ResolveTypeFromAssembly_Resolves_A_Type_Of_The_Target_Assembly()
        {
            var module = NewTarget().Source.MainModule;

            var definition = CecilExtensions.ResolveTypeFromAssembly(module, StubTarget.AssemblyName, $"{Ns}.{nameof(Stub)}");

            Assert.That(definition.FullName, Is.EqualTo($"{Ns}.{nameof(Stub)}"));
            Assert.That(definition.Module, Is.SameAs(module));
        }

        [Test]
        public void ResolveTypeFromAssembly_Resolves_A_Type_Of_A_Third_Assembly()
        {
            // The weaver itself is loaded by the process which runs the tests, and it is resolvable by the same resolver
            // which already resolves Gneedle.Inject.This while a marker is parsed.
            var module = NewTarget().Source.MainModule;

            var definition = CecilExtensions.ResolveTypeFromAssembly(module, "Gneedle.Inject", "Gneedle.Inject.TypeName");

            Assert.That(definition.FullName, Is.EqualTo("Gneedle.Inject.TypeName"));
        }

        [Test]
        public void ResolveTypeFromAssembly_Resolves_An_Assembly_Which_Only_Exists_In_Memory()
        {
            // The dependency is never written to the file system, so the resolver of the module could not find it: that
            // one searches the file system alone. It is reachable because the handler read it.
            var dependency = Assembly.Create("InMemoryDependencyAssembly");
            var dependencyType = (TypeHandler) ((AssemblyHandler) dependency.Handler).AddClass("Dependency", Ns, ClassFlags.Public).GetHandler();

            var assembly = NewTarget();
            ((AssemblyHandler) assembly.Handler).GetCecilType(dependencyType.Source);

            var definition = CecilExtensions.ResolveTypeFromAssembly(assembly.Source.MainModule, "InMemoryDependencyAssembly", $"{Ns}.Dependency");

            Assert.That(definition.FullName, Is.EqualTo($"{Ns}.Dependency"));
        }

        [Test]
        public void ResolveTypeFromAssembly_With_An_Unknown_Assembly_Throws()
            => Assert.Throws<ArgumentException>(() => CecilExtensions.ResolveTypeFromAssembly(NewTarget().Source.MainModule, "No.Such.Assembly", $"{Ns}.{nameof(Stub)}"));

        // endregion

        // region Failure

        [Test]
        public void SetBody_With_A_Stub_Which_The_Target_Does_Not_Declare_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(() => Weave(nameof(Templates.ReadAbsentStubField)));

            Assert.That(exception!.Message, Does.Contain($"{Ns}.{nameof(AbsentStub)}"));
        }

        [Test]
        public void SetBody_With_A_Stub_Of_An_Unknown_Assembly_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(() => Weave(nameof(Templates.ReadUnresolvableStubField)));

            Assert.That(exception!.Message, Does.Contain("No.Such.Assembly"));
        }

        // endregion
    }
}
