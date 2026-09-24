using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gneedle.Inject;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Gneedle.Unity.Test
{
    /// <summary>
    /// An attribute which marks the type it is put on as obsolete, so that the weaving of an assembly is visible in the
    /// image which was woven.
    /// </summary>
    [AttributeUsage(AttributeTargets.All)]
    public sealed class MarkTypeAttribute : Attribute, ITypeInjector
    {
        /// <inheritdoc/>
        public int Priority => 0;
        
        /// <inheritdoc/>
        public void Inject(Type type, ITypeHandler handler) => handler.AddAttribute(typeof(ObsoleteAttribute).ToGneedleType(), "marked");
    }

    /// <summary>
    /// The type which carries the marker above, whose injection is read back out of the image which was woven.
    /// </summary>
    [MarkType]
    public class MarkedFixture { }

    /// <summary>
    /// Tests for <see cref="Gneedle.Aspect.GneedleILPostProcessor"/>, the step of a compilation which applies the
    /// injectors of an assembly to it before it is written and loaded.<para/>
    /// The tests are a package of the editor which compiles the post processor, and are run by the test runner of that
    /// editor: the post processor is compiled into <c>Unity.Gneedle.CodeGen</c>, and the tests into the assembly beside
    /// it, which is named after it, because the name is what the compilation pipeline reads an assembly of its own by.
    /// </summary>
    [TestFixture]
    public class GneedleILPostProcessorTests
    {
        /// <summary>
        /// The namespace which the types of the images which the tests build are declared in.
        /// </summary>
        private const string Ns = "Gneedle.Unity.Test.Generated";

        /// <summary>
        /// Full name of the type which carries the marker of this assembly, which is read back out of the image which was
        /// woven.
        /// </summary>
        private const string MarkedType = "Gneedle.Unity.Test.MarkedFixture";

        /// <summary>
        /// The symbols which the assembly of these tests was compiled with, which are the program database beside it,
        /// and which are handed over with it as the symbols of its compilation.<para/>
        /// Whether the symbols of an image which was woven are handed back with it is a question which only an image
        /// which a weaving changes answers, and this one is not: the editor wove the assembly of these tests before they
        /// ran, and a weaving of it writes the image which it was given. The test which reads a woven pair builds its
        /// own image and hands it over with symbols of its own.
        /// </summary>
        private static readonly byte[] Symbols = File.ReadAllBytes(Path.ChangeExtension(System.Reflection.Assembly.GetExecutingAssembly().Location, ".pdb"));

        /// <summary>
        /// The processor which is asked of the assemblies of the tests, which the compilation asks for one of per assembly.
        /// </summary>
        private static Gneedle.Aspect.GneedleILPostProcessor Processor() => new Gneedle.Aspect.GneedleILPostProcessor();

        #region Which assemblies are woven

        [Test]
        public void WillProcess_Is_False_For_An_Assembly_Which_Declares_No_Injector()
        {
            var image = ImageOf("NoInjectorAssembly", module => NewType(module, "Host"));

            Assert.That(Processor().WillProcess(new StubCompiledAssembly("NoInjectorAssembly", image)), Is.False,
                        "an assembly which declares no injector was taken to be one to weave.");
        }

        [Test]
        public void WillProcess_Is_True_For_An_Injector_Which_Its_Type_Implements()
        {
            var image = ImageOf("DirectInjectorAssembly", module =>
            {
                NewType(module, "Injector").Interfaces.Add(new InterfaceImplementation(module.ImportReference(typeof(IMethodInjector))));
            });

            Assert.That(Processor().WillProcess(new StubCompiledAssembly("DirectInjectorAssembly", image)), Is.True,
                        "an assembly which declares an injector was left without its weaving.");
        }

        [Test]
        public void WillProcess_Is_True_For_An_Injector_Which_Its_Base_Type_Implements()
        {
            // The interface is implemented by a type which the injector derives from rather than by the injector itself,
            // which is a reading that follows the base types of the types of the module.
            var image = ImageOf("DerivedInjectorAssembly", module =>
            {
                var @base = NewType(module, "Base");
                @base.Interfaces.Add(new InterfaceImplementation(module.ImportReference(typeof(IMethodInjector))));

                NewType(module, "Derived", TypeAttributes.Public | TypeAttributes.Class, @base);
            });

            Assert.That(Processor().WillProcess(new StubCompiledAssembly("DerivedInjectorAssembly", image)), Is.True,
                        "an injector which reaches the interface through its base type was left without its weaving.");
        }

        [Test]
        public void WillProcess_Is_True_For_An_Injector_Which_An_Interface_Of_An_Interface_Implements()
        {
            // The same, one interface further out: the interface which the type implements is not one of the weaver, and it
            // is the interface of the weaver which that one extends.
            var image = ImageOf("InterfaceInjectorAssembly", module =>
            {
                var myInjector = new TypeDefinition(Ns, "IMyInjector", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract, null);
                myInjector.Interfaces.Add(new InterfaceImplementation(module.ImportReference(typeof(IMethodInjector))));
                module.Types.Add(myInjector);

                NewType(module, "Injector").Interfaces.Add(new InterfaceImplementation(myInjector));
            });

            Assert.That(Processor().WillProcess(new StubCompiledAssembly("InterfaceInjectorAssembly", image)), Is.True,
                        "an injector which reaches the interface through an interface was left without its weaving.");
        }

        [Test]
        public void WillProcess_Is_True_For_An_Injector_Which_Is_Nested()
        {
            var image = ImageOf("NestedInjectorAssembly", module =>
            {
                var outer = NewType(module, "Outer");
                var inner = new TypeDefinition(Ns, "Inner", TypeAttributes.NestedPublic | TypeAttributes.Class, module.TypeSystem.Object) { DeclaringType = outer };
                inner.Interfaces.Add(new InterfaceImplementation(module.ImportReference(typeof(IMethodInjector))));
                outer.NestedTypes.Add(inner);
            });

            Assert.That(Processor().WillProcess(new StubCompiledAssembly("NestedInjectorAssembly", image)), Is.True,
                        "an injector which is nested was left without its weaving.");
        }

        #endregion

        #region Weaving an assembly and answering with its image

        [Test]
        public void Process_Answers_With_The_Image_It_Was_Given_When_The_Assembly_Holds_No_Injector()
        {
            var image = ImageOf("UnwovenAssembly", module => NewType(module, "Host"));
            var assembly = new StubCompiledAssembly("UnwovenAssembly", image);

            var result = Processor().Process(assembly);

            Assert.That(Messages(result), Is.Empty, "an assembly which holds no injector was reported of.");
            Assert.That(result.InMemoryAssembly, Is.SameAs(assembly.InMemoryAssembly),
                        "an assembly which holds no injector was answered with an image of its own.");
        }

        [Test]
        public void Process_Answers_With_The_Weaving_Of_The_Assembly_Of_The_Compilation_And_Hands_The_Symbols_Back_With_It()
        {
            // The post processor is handed an assembly of a compilation, which is the assembly of these tests: the fixtures
            // of it carry the injectors which the weaving reads, and the image which is answered with is read back to see
            // what was woven.
            //
            // An assembly of a package is woven by the compilation which produces it, so where these tests are run by an
            // editor which weaves the package - which is the case of the package of a Unity project - the image which is
            // read here is the one which was woven then: the mark of the injector is on the fixture and the attribute it
            // was read from is gone, and a weaving of that image has nothing left to run. What is asserted of this
            // weaving is therefore the weaving of the assembly rather than one which it wrote itself, and the symbols
            // which are handed back with it belong to the image which was answered with either way.
            var image = File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var assembly = new StubCompiledAssembly("Unity.Gneedle.CodeGen.Tests", image, symbols: Symbols);

            var result = Processor().Process(assembly);

            Assert.That(Messages(result), Is.Empty, string.Join(Environment.NewLine, Messages(result)));

            // The image and the symbols are read together, because the pair is what the compilation reads back: what
            // tells a database from one which describes another image is the image it names, and a reader handed the
            // two is refused rather than left to read places of an image which is not the one it was given.
            using var stream = new MemoryStream(result.InMemoryAssembly.PeData);
            using (var read = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters
            {
                ReadSymbols          = true,
                SymbolReaderProvider = new SymbolsInBytes(result.InMemoryAssembly.PdbData)
            }))
            {
                var marked = read.MainModule.GetType(MarkedType);
                Assert.That(marked.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(ObsoleteAttribute).FullName), Is.True,
                            "the injector of the fixture did not run.");
                Assert.That(marked.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(MarkTypeAttribute).FullName), Is.False,
                            "the attribute which the injector was read from was left on the type.");
            }
        }

        [Test]
        public void Process_Answers_With_The_Symbols_Of_The_Image_Which_It_Wove()
        {
            // What a compilation hands a post processor is one image and the symbols which describe it, and what is
            // handed back is one image and the symbols which describe that one: the two are a pair, and what tells a
            // database from one which describes another image is the image it names.<para/>
            // The image here is one the test builds and one which a weaving writes, because the assembly of these tests
            // is the one which the editor wove before they ran: a weaving of that assembly answers with the image which
            // it was given, and the pair which is handed back is the pair which was handed in, which tells nothing of
            // either. What a weaving of this one writes is a pair of its own, and the two are read back together.
            var (image, symbols) = ImageAndSymbolsOf("WovenAssembly", module => NewType(module, "Host").CustomAttributes.Add(new CustomAttribute(AnInjectorOf(module))));
            var assembly = new StubCompiledAssembly("WovenAssembly", image, symbols: symbols);

            var result = Processor().Process(assembly);

            Assert.That(Messages(result), Is.Empty, string.Join(Environment.NewLine, Messages(result)));
            Assert.That(result.InMemoryAssembly.PeData, Is.Not.SameAs(image),
                        "the assembly was not woven, so what was handed back is what was handed in.");
            Assert.That(result.InMemoryAssembly.PdbData, Is.Not.SameAs(symbols),
                        "the symbols of the image which was read were handed back with the image which was woven.");

            // The pair which is handed back is read together: a database of another image is refused by the reader
            // rather than left to describe places of an image which is not the one it was handed.
            using var stream = new MemoryStream(result.InMemoryAssembly.PeData);
            using var read = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters
            {
                ReadSymbols          = true,
                SymbolReaderProvider = new SymbolsInBytes(result.InMemoryAssembly.PdbData)
            });

            Assert.That(read.MainModule.HasSymbols, Is.True, "the image which was woven was read without the symbols which were handed back with it.");
        }

        [Test]
        public void Process_Reports_Nothing_Of_A_Weaving_Which_Ran_Before_It()
        {
            // A weaving which could not read an assembly reports what was tried for it, and what one assembly could not read
            // says nothing about the assembly which is woven after it: the assemblies of a compilation are woven on threads
            // of their own. The first weaving records an assembly which is not there, and the second is asked for its own
            // alone.
            var first = Processor().Process(new StubCompiledAssembly("FirstAssembly", ImageNaming("FirstAssembly", "Gneedle.Missing.One")));
            var second = Processor().Process(new StubCompiledAssembly("SecondAssembly", ImageNaming("SecondAssembly", "Gneedle.Missing.Two")));

            Assert.That(Messages(first), Has.Some.Contains("Gneedle.Missing.One"), string.Join(Environment.NewLine, Messages(first)));
            Assert.That(Messages(second), Has.None.Contains("Gneedle.Missing.One"),
                        "what the weaving before it could not read was reported of this one.");
            Assert.That(Messages(second), Has.Some.Contains("Gneedle.Missing.Two"), string.Join(Environment.NewLine, Messages(second)));
        }

        [Test]
        public void Process_Stops_Answering_For_The_Assemblies_Of_The_Compilation_Where_It_Ends()
        {
            // The assemblies of a compilation are answered for while the assembly which names them is woven, and the
            // process which a compilation runs in is the editor, which outlives every compilation of it: what was put in
            // place for one weaving is taken back out where that weaving ends, so that a request which is made afterwards
            // is left to the resolution which the runtime does by itself.
            var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).FullName;
            try
            {
                var path = Path.Combine(directory, "Gneedle.Resolvable.dll");
                File.WriteAllBytes(path, ImageOf("Gneedle.Resolvable", module => NewType(module, "Thing")));

                // The image is one which cannot be read, which ends the weaving before it reads anything, while the paths of
                // the compilation are in place all the same: they are taken before the image is touched.
                Processor().Process(new StubCompiledAssembly("UnreadableAssembly", new byte[] {1, 2, 3}, new[] {path}));

                System.Reflection.Assembly resolved = null;
                try
                {
                    resolved = AppDomain.CurrentDomain.Load("Gneedle.Resolvable");
                }
                catch (Exception)
                {
                    // The file of the assembly is not one which the runtime looks in by itself, and nothing answers for it
                    // any more, which is what leaves the request unanswered. Which exception a request which nothing
                    // resolves is failed by belongs to the runtime rather than to the weaving.
                }

                Assert.That(resolved, Is.Null, "the assembly which the compilation named was answered for after the weaving which read it ended.");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        #endregion

        /// <summary>
        /// What a run of the post processor reported.
        /// </summary>
        /// <param name="result">What the run answered with.</param>
        private static IEnumerable<string> Messages(ILPostProcessResult result) => result.Diagnostics.Select(message => message.MessageData);

        /// <summary>
        /// The image of an assembly which holds the types which <paramref name="build"/> writes into it.<para/>
        /// The assembly is written with the reader of the post processor itself rather than through the weaver of the
        /// library, which these tests are not a part of: what is handed to the post processor is an image of bytes, and
        /// an image written by any compiler is one.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <param name="build">What writes the types of the module of the assembly.</param>
        private static byte[] ImageOf(string assemblyName, Action<ModuleDefinition> build)
        {
            var name = new AssemblyNameDefinition(assemblyName, new Version(1, 0));
            var assembly = AssemblyDefinition.CreateAssembly(name, assemblyName, ModuleKind.Dll);
            build(assembly.MainModule);

            using (var written = new MemoryStream())
            {
                assembly.Write(written);
                return written.ToArray();
            }
        }

        /// <summary>
        /// The image of an assembly which holds a type whose member names a type of an assembly which is not there, which is
        /// a member which the weaving cannot read and reports.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <param name="missingName">The name of the assembly which is named and is not there.</param>
        private static byte[] ImageNaming(string assemblyName, string missingName)
            => ImageOf(assemblyName, module =>
            {
                var missing = new AssemblyNameReference(missingName, new Version(1, 0));
                module.AssemblyReferences.Add(missing);

                var method = new MethodDefinition("Touch", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
                method.Parameters.Add(new ParameterDefinition("thing", ParameterAttributes.None, new TypeReference(missingName, "Thing", module, missing)));
                method.Body.GetILProcessor().Emit(OpCodes.Ret);

                var host = NewType(module, "Host");
                method.DeclaringType = host;
                host.Methods.Add(method);

                // The member carries an injector, which is what makes the weaving read it: a type which carries none is
                // one which nothing is applied to, and no member of it is read, so the assembly which the signature of
                // this one names would never be asked for and nothing would be reported of it.<para/>
                // The injector is declared by the image rather than by the assembly of these tests, which is the shape
                // the tests above read an injector by as well: the assembly of the tests is woven by the editor which
                // runs them, so what the weaving reads of a type of it is not what it reads of one which was compiled
                // and left alone.
                var injector = NewType(module, "Injector", TypeAttributes.Public | TypeAttributes.Class, module.ImportReference(typeof(Attribute)));
                injector.Interfaces.Add(new InterfaceImplementation(module.ImportReference(typeof(IMethodInjector))));

                var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void)
                {
                    DeclaringType = injector
                };
                constructor.Body.GetILProcessor().Emit(OpCodes.Ret);
                injector.Methods.Add(constructor);

                method.CustomAttributes.Add(new CustomAttribute(constructor));
            });

        /// <summary>
        /// The image of an assembly and the symbols which describe it, written together as a compilation writes them.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <param name="build">What the module of the image holds.</param>
        /// <returns>The bytes of the image and the bytes of the symbols.</returns>
        private static (byte[] Image, byte[] Symbols) ImageAndSymbolsOf(string assemblyName, Action<ModuleDefinition> build)
        {
            var name = new AssemblyNameDefinition(assemblyName, new Version(1, 0));
            var assembly = AssemblyDefinition.CreateAssembly(name, assemblyName, ModuleKind.Dll);
            build(assembly.MainModule);

            using var image = new MemoryStream();
            using var symbols = new MemoryStream();
            assembly.Write(image, new WriterParameters
            {
                WriteSymbols         = true,
                SymbolWriterProvider = new PortablePdbWriterProvider(),
                SymbolStream         = symbols
            });

            return (image.ToArray(), symbols.ToArray());
        }

        /// <summary>
        /// The constructor of a type which the image declares and which is an injector, which is what an attribute of
        /// the image is made of.<para/>
        /// The injector is declared by the image rather than by the assembly of these tests, because that assembly is
        /// woven by the editor which runs them: what a weaving reads of a type of it is not what it reads of one which
        /// was compiled and left alone. What it injects is nothing, which is enough for a weaving to be one which wrote
        /// an image, because an injector which ran is one which the image it stands on was changed by.
        /// </summary>
        /// <param name="module">The module which the injector is declared by.</param>
        /// <returns>The constructor of the injector, which the attribute names.</returns>
        private static MethodDefinition AnInjectorOf(ModuleDefinition module)
        {
            var injector = NewType(module, "Injector", TypeAttributes.Public | TypeAttributes.Class, module.ImportReference(typeof(Attribute)));
            injector.Interfaces.Add(new InterfaceImplementation(module.ImportReference(typeof(ITypeInjector))));

            var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void)
            {
                DeclaringType = injector
            };
            constructor.Body.GetILProcessor().Emit(OpCodes.Ret);
            injector.Methods.Add(constructor);

            // The order which the injector asks to be applied in, which the interface of every injector declares.
            var priority = new MethodDefinition("get_Priority", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.SpecialName, module.TypeSystem.Int32)
            {
                DeclaringType = injector
            };
            priority.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_0);
            priority.Body.GetILProcessor().Emit(OpCodes.Ret);
            injector.Methods.Add(priority);

            // The members of the interface are declared by the type rather than handed bodies by the interface itself,
            // which no framework the weaver is built for allows: what the weaving calls is the member, and a type which
            // does not hold one is one which the runtime refuses to load.
            var inject = new MethodDefinition("Inject", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final, module.TypeSystem.Void)
            {
                DeclaringType = injector
            };
            inject.Parameters.Add(new ParameterDefinition("type", ParameterAttributes.None, module.ImportReference(typeof(Type))));
            inject.Parameters.Add(new ParameterDefinition("handler", ParameterAttributes.None, module.ImportReference(typeof(ITypeHandler))));
            inject.Body.GetILProcessor().Emit(OpCodes.Ret);
            injector.Methods.Add(inject);

            return constructor;
        }

        /// <summary>
        /// Add a class to a module.
        /// </summary>
        /// <param name="module">The module which the class is declared by.</param>
        /// <param name="name">The name of the class.</param>
        /// <param name="attributes">The attributes of the class.</param>
        /// <param name="baseType">The type which the class derives from, or null when it derives from the one of the module.</param>
        /// <returns>The class which was added.</returns>
        private static TypeDefinition NewType(ModuleDefinition module, string name, TypeAttributes attributes = TypeAttributes.Public | TypeAttributes.Class,
                                              TypeReference baseType = null)
        {
            var type = new TypeDefinition(Ns, name, attributes, baseType ?? module.TypeSystem.Object);
            module.Types.Add(type);

            return type;
        }

        /// <summary>
        /// The symbols which are read out of the memory they were handed over in, which is the shape of what a post
        /// processor answers with: an image and its database are read together, and neither of the two is a file which
        /// a reader could find beside the image it was given the name of.
        /// </summary>
        private sealed class SymbolsInBytes : ISymbolReaderProvider
        {
            /// <summary>
            /// The bytes of the symbols, which are what the readers below read out of.
            /// </summary>
            private readonly byte[] m_Symbols;

            /// <summary>
            /// Hold the symbols of one image.
            /// </summary>
            /// <param name="symbols">The bytes of the symbols.</param>
            public SymbolsInBytes(byte[] symbols) => m_Symbols = symbols;

            /// <inheritdoc/>
            public ISymbolReader GetSymbolReader(ModuleDefinition module, string fileName) => GetSymbolReader(module, new MemoryStream(m_Symbols));

            /// <inheritdoc/>
            public ISymbolReader GetSymbolReader(ModuleDefinition module, Stream stream) => new PortablePdbReaderProvider().GetSymbolReader(module, stream);
        }

        /// <summary>
        /// One assembly of a compilation, as the post processor of the tests is handed one.<para/>
        /// The paths which the compilation named are the ones of the assembly which was joined to it, which is what tells
        /// that the resolution of the assemblies of one weaving is held by that weaving alone.
        /// </summary>
        private sealed class StubCompiledAssembly : ICompiledAssembly
        {
            /// <summary>
            /// Hold one assembly of a compilation.
            /// </summary>
            /// <param name="name">The name of the assembly.</param>
            /// <param name="image">The bytes of the image which the assembly was compiled to.</param>
            /// <param name="references">The paths which the assembly refers to.</param>
            /// <param name="symbols">The symbols which the image was compiled with, or null for the image which a test
            /// built itself, which was compiled with none.</param>
            public StubCompiledAssembly(string name, byte[] image, string[] references = null, byte[] symbols = null)
            {
                Name             = name;
                InMemoryAssembly = new InMemoryAssembly(image, symbols);
                References       = references ?? Array.Empty<string>();
            }

            /// <inheritdoc/>
            public InMemoryAssembly InMemoryAssembly { get; set; }

            /// <inheritdoc/>
            public string Name { get; set; }

            /// <inheritdoc/>
            public string[] References { get; set; }

            /// <inheritdoc/>
            /// <remarks>
            /// What a compilation defines is read by nothing here, and it is answered all the same because the interface
            /// of the editor asks for it: an assembly of a compilation is handed to a post processor as one whole.
            /// </remarks>
            public string[] Defines { get; set; } = Array.Empty<string>();
        }
    }
}
