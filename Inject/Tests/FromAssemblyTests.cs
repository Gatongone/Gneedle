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
        public const string ASSEMBLY_NAME = "FromAssemblyTarget";
    }

    /// <summary>
    /// Stubs which stand for the types of <see cref="StubTarget.ASSEMBLY_NAME"/>. They carry the same full names as the
    /// real types, which is what ties them together, so they are declared in the namespace of the generated types. They
    /// live top level in the test assembly so that Cecil can resolve them from disk.
    /// </summary>
    [FromAssembly(StubTarget.ASSEMBLY_NAME)]
    public class Stub
    {
        public static int Field;
        public static int Read() => 0;
        public static int Property { get; set; }
    }

    [FromAssembly(StubTarget.ASSEMBLY_NAME)]
    public class GenericStub<T>
    {
        public static int Count;
    }

    [FromAssembly(StubTarget.ASSEMBLY_NAME)]
    public interface IStub { }

    public class OuterStub
    {
        [FromAssembly(StubTarget.ASSEMBLY_NAME)]
        public class Inner
        {
            public static int Field;
        }
    }

    /// <summary>
    /// A stub which the target assembly holds no real type for.
    /// </summary>
    [FromAssembly(StubTarget.ASSEMBLY_NAME)]
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
    using static TestFixtures;

    /// <summary>
    /// Tests for <see cref="FromAssemblyAttribute"/>, which makes a stub declared in the template assembly stand for the
    /// type of the same full name which another assembly declares.
    /// </summary>
    [TestFixture]
    public class FromAssemblyTests
    {
        /// <summary>
        /// Template bodies live in the test assembly so Cecil can resolve them from disk.
        /// </summary>
        public static class Templates
        {
            public static int ReadStubField() => Stub.Field;
            public static int CallStubMethod() => Stub.Read();
            public static object CreateStub() => new Stub();
            public static int ReadStubProperty() => Stub.Property;
            public static List<Stub> StubList() => [];

            public static bool StubListLocal()
            {
                List<Stub> local = [];
                GC.KeepAlive(local);
                return true;
            }

            public static int ReadGenericStubCount() => GenericStub<int>.Count;

            public static Stub[] StubArray() => [];
            public static int ReadNestedStubField() => OuterStub.Inner.Field;

            public static int InstanceMethod_StubReceiver(Stub instance) => new Instance(instance).Method<ReadGetter>("Read")();
            public static int InstanceField_StubReceiver(Stub instance) => new Instance(instance).Field<int>(nameof(Stub.Field)).Get();
            public static int InstanceProperty_StubReceiver(Stub instance) => new Instance(instance).Property<int>(nameof(Stub.Property)).Get();

            public static int ReadAbsentStubField() => AbsentStub.Field;
            public static int ReadUnresolvableStubField() => UnresolvableStub.Field;
        }

        /// <summary>
        /// The delegate which names the member <c>Read</c> of the stub, which hands back the value that member hands
        /// back: the signature of a call is what the member is looked up by, so the value is read there as well.
        /// </summary>
        public delegate int ReadGetter();

        #region Target fixture

        /// <summary>
        /// Create the target assembly and the real types which the stubs stand for. The real types carry the same full
        /// names as the stubs, so nothing but the module they are declared by tells them apart.
        /// </summary>
        private static Assembly NewTarget()
        {
            var assembly = Assembly.Create(StubTarget.ASSEMBLY_NAME);
            var module = assembly.Source.MainModule;

            var stub = new TypeDefinition(NS, nameof(Stub), TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
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
            stub.Properties.Add(new PropertyDefinition(nameof(Stub.Property), PropertyAttributes.None, module.TypeSystem.Int32) {GetMethod = getter, SetMethod = setter});

            AddAnInstanceConstructor(stub, module);

            module.Types.Add(stub);

            // The field of the generic stub is typed int rather than by the generic parameter, so that the emitted
            // reference does not carry a generic parameter of a foreign module (Cecil issue #954).
            var generic = new TypeDefinition(NS, "GenericStub`1", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            generic.GenericParameters.Add(new GenericParameter("T", generic));
            generic.Fields.Add(new FieldDefinition(nameof(GenericStub<int>.Count), FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32));
            module.Types.Add(generic);

            module.Types.Add(new TypeDefinition(NS, nameof(IStub), TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract));

            // The real type of a nested stub is nested in the very same way, so that both carry the same full name. A
            // nested type which the compiler emits holds no namespace of its own, so this one holds none either: the
            // last segment of the name of `Namespace.Outer/Inner` is the simple name.
            var outer = new TypeDefinition(NS, nameof(OuterStub), TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            var inner = new TypeDefinition(string.Empty, nameof(OuterStub.Inner), TypeAttributes.NestedPublic | TypeAttributes.Class, module.TypeSystem.Object) {DeclaringType = outer};
            inner.Fields.Add(new FieldDefinition(nameof(OuterStub.Inner.Field), FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32));
            outer.NestedTypes.Add(inner);
            module.Types.Add(outer);

            return assembly;
        }

        /// <summary>
        /// Weave the template which the name stands for, and hand back the member which the woven body names, which is
        /// what tells that the stub was replaced by the real type: a reference which stands in the module which was
        /// woven is one of the type the module declares rather than of the stub which shares its name.<para/>
        /// The member is looked for by name, so the test names the one its template reads.
        /// </summary>
        /// <typeparam name="T">The kind of the reference which the template reads the member through.</typeparam>
        /// <param name="templateName">Name of the template which is woven.</param>
        /// <param name="memberName">Name of the member which the woven body is expected to name.</param>
        /// <param name="parameterTypes">The parameters of the member which is woven, or null for none.</param>
        /// <returns>The member which the woven body names.</returns>
        private static T TheMemberWhichWasWoven<T>(string templateName, string memberName, Parameter[]? parameterTypes = null)
            where T : MemberReference
        {
            var method = Weave(templateName, parameterTypes);
            var member = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<T>()
                               .FirstOrDefault(reference => reference.Name == memberName);

            Assert.That(member, Is.Not.Null, $"the woven body names no {typeof(T).Name} of the name '{memberName}'.");
            Assert.That(member!.DeclaringType.Module, Is.SameAs(method.Source.Module),
                $"the {typeof(T).Name} which the woven body names is not of the module which was woven.");

            return member;
        }

        private static MethodHandler Weave(string templateName, Parameter[]? parameterTypes = null)
        {
            var assembly = NewTarget();
            var host = AddAHost(assembly);
            var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToGneedleType(), [], parameterTypes ?? [],
                MethodFlags.Public | MethodFlags.Static);
            method.SetBody(typeof(Templates).GetMethod(templateName)!);
            return method;
        }

        #endregion

        #region Member access

        [Test]
        public void SetBody_Replaces_The_Stub_Field()
            => TheMemberWhichWasWoven<FieldReference>(nameof(Templates.ReadStubField), nameof(Stub.Field));

        [Test]
        public void SetBody_Replaces_The_Stub_Method_Call()
            => TheMemberWhichWasWoven<MethodReference>(nameof(Templates.CallStubMethod), nameof(Stub.Read));

        [Test]
        public void SetBody_Replaces_The_Stub_Constructor()
            => TheMemberWhichWasWoven<MethodReference>(nameof(Templates.CreateStub), ".ctor");

        [Test]
        public void SetBody_Replaces_The_Stub_Property_Access()
            => TheMemberWhichWasWoven<MethodReference>(nameof(Templates.ReadStubProperty), "get_Property");

        [Test]
        public void SetBody_Replaces_The_Stub_Receiver_Of_Object_Method()
        {
            var method = Weave(nameof(Templates.InstanceMethod_StubReceiver), [new Parameter(typeof(Stub).ToGneedleType())]);
            var call = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                             .FirstOrDefault(reference => reference.Name == nameof(Stub.Read));

            Assert.That(call, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(call!.DeclaringType.Module, Is.SameAs(method.Source.Module));

                // The marker itself is rewritten away, so none of its members survives.
                Assert.That(method.Source.Body.Instructions.Any(instruction => instruction.Operand is MethodReference reference
                    && reference.DeclaringType.FullName == Instance.TYPE_NAME), Is.False);
            });
        }

        #endregion

        #region Nested in a type shape

        [Test]
        public void SetBody_Replaces_The_Stub_Nested_In_A_Generic_Argument()
        {
            var method = Weave(nameof(Templates.StubList));
            var type = method.Source.ReturnType;

            Assert.That(type, Is.InstanceOf<GenericInstanceType>());
            var argument = ((GenericInstanceType) type).GenericArguments[0];
            Assert.Multiple(() =>
            {
                Assert.That(argument.FullName, Is.EqualTo($"{NS}.{nameof(Stub)}"));
                Assert.That(argument.Module, Is.SameAs(method.Source.Module));
            });
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Element_Of_A_Generic_Instance()
        {
            var method = Weave(nameof(Templates.ReadGenericStubCount));
            var field = method.Source.Body.Instructions.Select(instruction => instruction.Operand).OfType<FieldReference>()
                              .FirstOrDefault(reference => reference.Name == nameof(GenericStub<int>.Count));

            Assert.That(field, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(field!.DeclaringType, Is.InstanceOf<GenericInstanceType>());
                Assert.That(((GenericInstanceType) field.DeclaringType).ElementType.Module, Is.SameAs(method.Source.Module));
            });
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Of_A_Local_Variable()
        {
            var method = Weave(nameof(Templates.StubListLocal));
            var variable = method.Source.Body.Variables.First();
            Assert.Multiple(() =>
            {
                Assert.That(variable.VariableType, Is.InstanceOf<GenericInstanceType>());
                Assert.That(((GenericInstanceType) variable.VariableType).GenericArguments[0].Module, Is.SameAs(method.Source.Module));
            });
        }

        #endregion

        #region The produced assembly

        [Test]
        public void SetBody_Does_Not_Leak_The_Stub_Into_The_Produced_Assembly()
        {
            // The real type is declared by the module which is produced, so a reference to it needs no type reference at
            // all. One for the name which the stub shares with it therefore means that the stub itself survived, which is
            // the leak the whole attribute exists to prevent. Asserting on the assembly reference instead would not tell
            // them apart: importing the template method references the template assembly either way.
            var assembly = NewTarget();
            var host = AddAHost(assembly);
            var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
            method.SetBody(typeof(Templates).GetMethod(nameof(Templates.CallStubMethod))!);

            using var stream = new MemoryStream();
            assembly.SaveTo(stream);
            stream.Position = 0;

            var reread = AssemblyDefinition.ReadAssembly(stream);
            Assert.That(reread.MainModule.GetTypeReferences().Any(reference => reference.FullName == $"{NS}.{nameof(Stub)}"), Is.False);
        }

        [Test]
        public void Load_Executes_A_Method_Which_Reads_The_Real_Type()
        {
            // The real Read returns 41 where the stub returns 0, so the value tells the two apart even though they share
            // a full name. Executing the produced method is the strongest form of that check.
            var assembly = NewTarget();
            var host = AddAHost(assembly);
            var method = (MethodHandler) host.AddMethod("Run", typeof(int).ToGneedleType(), [], [], MethodFlags.Public | MethodFlags.Static);
            method.SetBody(typeof(Templates).GetMethod(nameof(Templates.CallStubMethod))!);

            var loaded = assembly.Load();
            var run = loaded.GetType($"{NS}.Host")!.GetMethod("Run")!;

            Assert.That(run.Invoke(null, null), Is.EqualTo(41));
        }

        [Test]
        public void SetBody_Replaces_The_Stub_Of_An_Array()
        {
            var method = Weave(nameof(Templates.StubArray));
            var type = method.Source.ReturnType;
            Assert.Multiple(() =>
            {
                Assert.That(type, Is.InstanceOf<ArrayType>());
                Assert.That(((ArrayType) type).ElementType.Module, Is.SameAs(method.Source.Module));
            });
        }

        [Test]
        public void SetBody_Replaces_The_Nested_Stub()
            => TheMemberWhichWasWoven<FieldReference>(nameof(Templates.ReadNestedStubField), nameof(OuterStub.Inner.Field));

        [Test]
        public void SetBody_Replaces_The_Stub_Receiver_Of_Object_Field()
            => TheMemberWhichWasWoven<FieldReference>(nameof(Templates.InstanceField_StubReceiver), nameof(Stub.Field),
                [new Parameter(typeof(Stub).ToGneedleType())]);

        [Test]
        public void SetBody_Replaces_The_Stub_Receiver_Of_Object_Property()
            => TheMemberWhichWasWoven<MethodReference>(nameof(Templates.InstanceProperty_StubReceiver), "get_Property",
                [new Parameter(typeof(Stub).ToGneedleType())]);

        #endregion

        #region Public API

        [Test]
        public void WithBaseType_Replaces_The_Stub()
        {
            var assembly = NewTarget();
            var host = (ClassHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", NS, ClassFlags.Public)
                                                                          .WithBaseType(typeof(Stub))
                                                                          .GetHandler();

            Assert.That(host.Source.BaseType!.Module, Is.SameAs(host.Source.Module));
        }

        [Test]
        public void WithInterface_Replaces_The_Stub()
        {
            var assembly = NewTarget();
            var host = (ClassHandler) ((AssemblyHandler) assembly.Handler).AddClass("Host", NS, ClassFlags.Public)
                                                                          .WithInterface(typeof(IStub))
                                                                          .GetHandler();

            var implementation = host.Source.Interfaces.Single();
            Assert.That(implementation.InterfaceType.Module, Is.SameAs(host.Source.Module));
        }

        [Test]
        public void AddMethod_Replaces_The_Stub_Parameter_Type()
        {
            var assembly = NewTarget();
            var host = AddAHost(assembly);
            var method = (MethodHandler) host.AddMethod("Run", typeof(void).ToGneedleType(), [], [new Parameter(typeof(Stub).ToGneedleType())],
                MethodFlags.Public | MethodFlags.Static);

            var parameter = method.Source.Parameters[0].ParameterType;
            Assert.Multiple(() =>
            {
                Assert.That(parameter.FullName, Is.EqualTo($"{NS}.{nameof(Stub)}"));
                Assert.That(parameter.Module, Is.SameAs(method.Source.Module));
            });
        }

        #endregion

        #region The resolver

        [Test]
        public void ResolveTypeFromAssembly_Resolves_A_Type_Of_The_Target_Assembly()
        {
            var module = NewTarget().Source.MainModule;

            var definition = FromAssembly.ResolveTypeFromAssembly(module, StubTarget.ASSEMBLY_NAME, $"{NS}.{nameof(Stub)}");
            Assert.Multiple(() =>
            {
                Assert.That(definition.FullName, Is.EqualTo($"{NS}.{nameof(Stub)}"));
                Assert.That(definition.Module, Is.SameAs(module));
            });
        }

        [Test]
        public void ResolveTypeFromAssembly_Resolves_A_Type_Of_A_Third_Assembly()
        {
            // The weaver itself is loaded by the process which runs the tests, and it is resolvable by the same resolver
            // which already resolves Gneedle.Inject.This while a marker is parsed.
            var module = NewTarget().Source.MainModule;

            var definition = FromAssembly.ResolveTypeFromAssembly(module, "Gneedle.Inject", "Gneedle.Inject.TypeName");

            Assert.That(definition.FullName, Is.EqualTo("Gneedle.Inject.TypeName"));
        }

        [Test]
        public void ResolveTypeFromAssembly_Resolves_An_Assembly_Which_Only_Exists_In_Memory()
        {
            // The dependency is never written to the file system, so the resolver of the module could not find it: that
            // one searches the file system alone. It is reachable because the handler read it.
            var dependency = Assembly.Create("InMemoryDependencyAssembly");
            var dependencyType = AddAHost(dependency, "Dependency");

            var assembly = NewTarget();
            ((AssemblyHandler) assembly.Handler).GetCecilType(dependencyType.Source);

            var definition = FromAssembly.ResolveTypeFromAssembly(assembly.Source.MainModule, "InMemoryDependencyAssembly", $"{NS}.Dependency");

            Assert.That(definition.FullName, Is.EqualTo($"{NS}.Dependency"));
        }

        [Test]
        public void ResolveTypeFromAssembly_Resolves_An_Assembly_Loaded_From_A_File_Outside_The_Search_Paths()
        {
            // The dependency sits in a directory which no search path of the resolver of the module holds, so the resolver
            // of the file system cannot find it. It is found through the assembly which is loaded in the process, which
            // knows the file it was loaded from.
            //
            // The case this does not cover is an assembly which was loaded from bytes: nothing hands the image of an
            // assembly over on .NET 5 and later, so such an assembly cannot be read back at all.
            var path = TempFiles.NewPath("gneedle-file", ".dll");
            var dependency = Assembly.Create("FileDependencyAssembly");
            ((AssemblyHandler) dependency.Handler).AddClass("Dependency", NS, ClassFlags.Public).GetHandler();
            dependency.SaveTo(path);

            try
            {
                System.Reflection.Assembly.LoadFrom(path);

                var assembly = NewTarget();
                var definition = FromAssembly.ResolveTypeFromAssembly(assembly.Source.MainModule, "FileDependencyAssembly", $"{NS}.Dependency");

                Assert.That(definition.FullName, Is.EqualTo($"{NS}.Dependency"));
            }
            finally
            {
                // The image of the assembly is mapped for as long as the process which loaded it lives, and a system
                // which mapped an image refuses to remove the file of it: what this run leaves is the one file of this
                // kind which the next run sweeps. The removal is asked for all the same, a runtime which answers it
                // being one which leaves nothing at all.
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }

        [Test]
        public void ResolveTypeFromAssembly_Resolves_An_Assembly_Which_Was_Loaded_From_Bytes()
        {
            // The dependency is loaded from its bytes and its file is removed, so nothing of it is reachable from the
            // file system: the resolver finds it through the image of the assembly which is loaded in the process.
            var path = TempFiles.NewPath("gneedle-bytes", ".dll");
            var dependency = Assembly.Create("BytesDependencyAssembly");
            ((AssemblyHandler) dependency.Handler).AddClass("Dependency", NS, ClassFlags.Public).GetHandler();
            dependency.SaveTo(path);

            var bytes = File.ReadAllBytes(path);
            File.Delete(path);
            System.Reflection.Assembly.Load(bytes);

            var assembly = NewTarget();
            var definition = FromAssembly.ResolveTypeFromAssembly(assembly.Source.MainModule, "BytesDependencyAssembly", $"{NS}.Dependency");

            Assert.That(definition.FullName, Is.EqualTo($"{NS}.Dependency"));
        }

        [Test]
        public void AssemblyLoader_Remembers_The_Bytes_It_Loaded()
        {
            var path = TempFiles.NewPath("gneedle-loader", ".dll");
            var produced = Assembly.Create("LoaderProbeAssembly");
            ((AssemblyHandler) produced.Handler).AddClass("Dependency", NS, ClassFlags.Public).GetHandler();
            produced.SaveTo(path);

            var expected = File.ReadAllBytes(path);
            File.Delete(path);

            var loaded = AssemblyLoader.LoadFromBytes(expected);
            Assert.Multiple(() =>
            {
                Assert.That(AssemblyLoader.TryGetSource(loaded, out var remembered), Is.True);
                Assert.That(remembered, Is.EqualTo(expected));
            });
        }

        [Test]
        public void AssemblyLoader_Remembers_The_Bytes_Which_It_Was_Told_Of()
        {
            // The assembly is loaded by the test rather than by the loader, which is the case Remember is for.
            var path = TempFiles.NewPath("gneedle-remember", ".dll");
            var produced = Assembly.Create("RememberProbeAssembly");
            ((AssemblyHandler) produced.Handler).AddClass("Dependency", NS, ClassFlags.Public).GetHandler();
            produced.SaveTo(path);

            var expected = File.ReadAllBytes(path);
            File.Delete(path);

            var loaded = System.Reflection.Assembly.Load(expected);
            Assert.That(AssemblyLoader.TryGetSource(loaded, out _), Is.False, "the bytes were remembered before they were told");

            AssemblyLoader.Remember(loaded, expected);
            Assert.Multiple(() =>
            {
                Assert.That(AssemblyLoader.TryGetSource(loaded, out var remembered), Is.True);
                Assert.That(remembered, Is.EqualTo(expected));
            });
        }

        [Test]
        public void ResolveTypeFromAssembly_Resolves_An_Assembly_Loaded_Through_AssemblyLoader()
        {
            // Reading the memory which an image was mapped to is a Windows layout, so the bytes which the loader
            // remembered are the way this is covered on another runtime. The two paths cannot be told apart on Windows,
            // where both would find the image.
            var path = TempFiles.NewPath("gneedle-loader", ".dll");
            var dependency = Assembly.Create("LoaderDependencyAssembly");
            ((AssemblyHandler) dependency.Handler).AddClass("Dependency", NS, ClassFlags.Public).GetHandler();
            dependency.SaveTo(path);

            var bytes = File.ReadAllBytes(path);
            File.Delete(path);
            AssemblyLoader.LoadFromBytes(bytes);

            var assembly = NewTarget();
            var definition = FromAssembly.ResolveTypeFromAssembly(assembly.Source.MainModule, "LoaderDependencyAssembly", $"{NS}.Dependency");

            Assert.That(definition.FullName, Is.EqualTo($"{NS}.Dependency"));
        }

        [Test]
        public void ResolveTypeFromAssembly_With_An_Unknown_Assembly_Throws()
            => Assert.Throws<WeavingException>(() => FromAssembly.ResolveTypeFromAssembly(NewTarget().Source.MainModule, "No.Such.Assembly", $"{NS}.{nameof(Stub)}"));

        #endregion

        #region Failure

        [Test]
        public void SetBody_With_A_Stub_Which_The_Target_Does_Not_Declare_Throws()
        {
            var exception = Assert.Throws<WeavingException>(() => Weave(nameof(Templates.ReadAbsentStubField)));

            Assert.That(exception!.Message, Does.Contain($"{NS}.{nameof(AbsentStub)}"));
        }

        [Test]
        public void SetBody_With_A_Stub_Of_An_Unknown_Assembly_Throws()
        {
            var exception = Assert.Throws<WeavingException>(() => Weave(nameof(Templates.ReadUnresolvableStubField)));

            Assert.That(exception!.Message, Does.Contain("No.Such.Assembly"));
        }

        #endregion
    }
}