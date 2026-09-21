using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// The injectors which an assembly declares, applied to the assembly itself.<para/>
/// An injector is an attribute which implements one of the interfaces of this library, and which says what to do with
/// the member it is put on. Applying them is the last step of a weaver: the assembly is read as an image of bytes, its
/// members are looked up in that image, and each injector is called with the handler of the member it names.
/// </summary>
/// <remarks>
/// The assembly is given as the one which was loaded from the image, because how an assembly is loaded belongs to
/// whoever loads it: a build which reads an assembly from a file, a tool which holds it in memory, and an editor which
/// compiles one each resolve the assemblies they refer to in their own way.
/// </remarks>
public static class Injections
{
    /// <summary>
    /// Apply the injectors which <paramref name="assembly"/> declares to the image it was loaded from.
    /// </summary>
    /// <param name="assembly">The assembly whose attributes are the injectors, which is the image being woven.</param>
    /// <param name="image">The bytes of the image which the assembly was loaded from.</param>
    /// <param name="removesTheWeaver">Whether the attributes and the reference to this library are taken back out. They are removed by default, which leaves the woven assembly standing alone.</param>
    /// <param name="reportError">Where a member which an injector names and the assembly does not hold is reported, or null when nothing reports it.</param>
    /// <param name="searchDirectory">Directory which the assemblies the image refers to lie in, or null when the caller
    /// knows of none. An injector which another assembly declares is read through the assembly which declares it, and
    /// the templates which its attributes name are read the same way, so a weaver which is driven by a build hands over
    /// the folder of the assembly which it was asked to weave, which is where the build put the assemblies it refers to.
    /// </param>
    /// <returns>Whether the assembly was changed, and the image which holds the result. A run which reported anything hands back the image it was given rather than the one it wove, because a report leaves an assembly which is woven in part, which no caller could tell from one which was woven whole.</returns>
    public static (bool Changed, byte[] Image) Apply(System.Reflection.Assembly assembly, byte[] image,
                                                     bool removesTheWeaver = true, Action<string>? reportError = null,
                                                     string? searchDirectory = null)
    {
        var (changed, woven, _) = Apply(assembly, image, symbols: null, removesTheWeaver, reportError, searchDirectory);

        return (changed, woven);
    }

    /// <summary>
    /// Apply the injectors which <paramref name="assembly"/> declares to the image it was loaded from, and to the
    /// symbols which were compiled with it.
    /// </summary>
    /// <remarks>
    /// The symbols of an image which was woven are woven with it rather than carried over: the image which is written
    /// holds the instructions of the template where the image which was read held the stub of it, and the places which
    /// the symbols record are places of the instructions which were read. Reading them with the image is what keeps the
    /// two in step, because a symbol reader hands the debug information of a body over with the body, and what the
    /// writer of the symbols writes is what the module holds.
    /// </remarks>
    /// <param name="assembly">The assembly whose attributes are the injectors, which is the image being woven.</param>
    /// <param name="image">The bytes of the image which the assembly was loaded from.</param>
    /// <param name="symbols">The bytes of the portable program database which describes the image, or null when the
    /// image has none. The woven symbols are handed back, and null when none was read.</param>
    /// <param name="removesTheWeaver">Whether the attributes and the reference to this library are taken back out. They are removed by default, which leaves the woven assembly standing alone.</param>
    /// <param name="reportError">Where a member which an injector names and the assembly does not hold is reported, or null when nothing reports it.</param>
    /// <param name="searchDirectory">Directory which the assemblies the image refers to lie in, or null when the caller
    /// knows of none.</param>
    /// <returns>Whether the assembly was changed, the image which holds the result, and the symbols which describe it.
    /// A run which reported anything hands back what it was given rather than what it wove, because a report leaves an
    /// assembly which is woven in part, which no caller could tell from one which was woven whole.</returns>
    public static (bool Changed, byte[] Image, byte[]? Symbols) Apply(System.Reflection.Assembly assembly, byte[] image, byte[]? symbols,
                                                                     bool removesTheWeaver = true, Action<string>? reportError = null,
                                                                     string? searchDirectory = null)
        => new Injection(assembly, image, symbols, removesTheWeaver, reportError, searchDirectory).Run();

    /// <summary>
    /// One run of the injectors of one assembly.
    /// </summary>
    /// <param name="assembly">The assembly whose attributes are the injectors.</param>
    /// <param name="image">The bytes of the image which the assembly was loaded from.</param>
    /// <param name="symbols">The bytes of the portable program database which describes the image, or null when it has
    /// none.</param>
    /// <param name="removesTheWeaver">Whether the attributes and the reference to this library are taken back out.</param>
    /// <param name="reportError">Where a member which an injector names and the assembly does not hold is reported.</param>
    /// <param name="searchDirectory">Directory which the assemblies the image refers to lie in, or null when the caller
    /// knows of none.</param>
    private sealed class Injection(System.Reflection.Assembly assembly, byte[] image, byte[]? symbols, bool removesTheWeaver, Action<string>? reportError, string? searchDirectory)
    {
        /// <summary>
        /// The members which the injectors of a type are looked for on: every member which the type declares, whichever
        /// way a caller could reach it, because a member which no injector names is left alone either way.<para/>
        /// Only the members which the type declares itself are walked, because an injector is applied where its member
        /// is declared. A member which a base type declares is walked with that base type, which the assembly holds as
        /// well when the base type is one of its own, and a member of a base type which another assembly declares cannot
        /// be written to from here at all.
        /// </summary>
        private const BindingFlags INJECTED_MEMBERS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                                   | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>
        /// The interfaces which an attribute implements to be asked to inject into a type.<para/>
        /// Every one of them is looked for, which the kinds are told apart by afterwards: looking for the first alone
        /// leaves the attributes of the other three on a type, where they are passed over without a word because nothing
        /// asked for them.
        /// </summary>
        private static readonly Type[] s_TypeInjectors = [typeof(ITypeInjector), typeof(IClassInjector), typeof(IStructInjector), typeof(IEnumInjector)];

        /// <summary>
        /// Whether anything was reported of this run.<para/>
        /// A report is made of a member which could not be woven, and one which could not be woven leaves the assembly
        /// woven in part: the injectors which ran before it hold, and the ones which would have run after it do not.
        /// An image of that is one which no caller can tell from an image which was woven whole, so no image at all is
        /// handed back of a run which reported anything.
        /// </summary>
        private bool m_Reported;

        /// <summary>
        /// Full names of the attribute types which the injectors of this run were read from, as the metadata names them.
        /// <para/>
        /// The traces of the injectors are taken back out by the names which were read here, because an injector may be
        /// declared by another assembly and put on a member of this one: a project which declares the attributes for
        /// another project to weave with is that case, and the type of such an attribute is not this assembly's to
        /// remove, while the attribute which carries it is what this assembly applied and what it takes off.
        /// </summary>
        private readonly HashSet<string> m_InjectorAttributes = new(StringComparer.Ordinal);

        /// <summary>
        /// Apply every injector of the assembly.
        /// </summary>
        public (bool Changed, byte[] Image, byte[]? Symbols) Run()
        {
            // The assembly is read as an image of bytes and written back as one, so that the caller keeps the file to
            // itself: a reader which holds the file leaves the write which follows nowhere to go. The folder which the
            // assemblies it refers to lie in is handed over with it, because the resolution of the module holds no
            // folder of its own: the image came from bytes, and nothing beside a stream names where its references are.
            using var stream = new MemoryStream(image);
            using var target = Assembly.Read(stream, AssemblySymbol.None, searchDirectory, symbols);

            // The same folder answers the runtime as well, because an injector is an attribute which the runtime makes
            // out of the type the image names: the metadata is read through the folder of the module, and the type of
            // the attribute, and the body which it names, are read through the folder of the process.
            using var referenced = searchDirectory == null ? null : new ReferencedAssemblies(assembly, searchDirectory);

            var handler = new AssemblyHandler(target);
            var changed = ProcessAssembleInjector(handler);

            // The types which an injector stands on are read out of the metadata of the image before the types of the
            // runtime are asked for, because materializing every type of an assembly is the cost of this pass beside the
            // weaving of it: a type which no attribute of an injector stands on, and none of whose members carries one,
            // is one which no injector is applied to, and the metadata which is read here and the assembly which the
            // injectors are read from are the same image.
            foreach (var typeDefinition in InjectorInterfaces.AllTypes(target.Source.MainModule))
            {
                if (!HoldsAnInjector(typeDefinition)) continue;

                // The name of the type is written the way the runtime writes it, which separates the types a type is
                // nested in with a plus where the metadata writes a slash.
                if (assembly.GetType(new TypeName(typeDefinition).ToString()) is not { } type) continue;

                // One type which cannot be woven does not take the rest of the assembly with it. What went wrong is
                // reported, and whoever drives the library decides what a report is worth: a build reports it as an
                // error and fails while the assembly it wove is discarded, and a driver which has an assembly to answer
                // with keeps the one it was given.
                try
                {
                    changed |= ProcessType(handler, type);
                }
                catch (Exception exception)
                {
                    // A refusal which the weaver raised deliberately names what it refused in its own message, and the
                    // frame of it tells a reader nothing which that message does not. Every other exception is a shape
                    // which the weaver did not expect, which is the fault a reader has the least to find it by: the kind
                    // of the exception and the frame it stands at are written with it.
                    Report($"Type '{type.FullName}' could not be woven. {(exception is ArgumentException ? exception.Message : exception.ToString())}");
                }
            }

            // The injectors are read from attributes which the assembly declares, and those attributes name the weaver,
            // so the weaver is removed from the assembly once they have been applied to it. An assembly which declares
            // them for another one to weave with keeps them, and keeps the weaver which they name. The attributes which
            // were read are named to the removal, because an injector may be declared by another assembly: the trace of
            // one of those is an attribute of this assembly all the same, and it is taken off the member which has it.
            if (removesTheWeaver) changed |= handler.RemoveTheWeaver(m_InjectorAttributes);

            // The assembly is written back only when an injector changed it and nothing was reported of the run, which
            // is what keeps an assembly which is woven in part from being taken for one which was woven whole. The
            // caller which was given no image of a run which reported anything holds the one it gave, which is the
            // assembly it built.
            if (!changed || m_Reported) return (false, image, symbols);

            // The symbols are written beside the image when they were read with it, and the two are written from the
            // same module: what the writer of the symbols writes is the debug information which the reader attached to
            // the bodies, so the two describe one image rather than two.
            using var result = new MemoryStream();
            if (symbols == null)
            {
                target.SaveTo(result);
                return (true, result.ToArray(), null);
            }

            using var written = new MemoryStream();
            target.SaveTo(result, written);
            return (true, result.ToArray(), written.ToArray());
        }

        /// <summary>
        /// Report a member which an injector named and the assembly does not hold.
        /// </summary>
        /// <param name="message">What is reported.</param>
        private void Report(string message)
        {
            m_Reported = true;
            reportError?.Invoke(message);
        }

        /// <summary>
        /// Report a type which an injector of a kind was asked to inject into and which is not of that kind.<para/>
        /// What is reported is the report which the injector of a kind is answered with by every one of the kinds: the
        /// type, the injector which named the kind, and the kind it named.
        /// </summary>
        /// <param name="type">The type which the injector was asked of.</param>
        /// <param name="injector">The attribute which is the injector.</param>
        /// <param name="kind">The word for the kind which the injector names.</param>
        private void ReportTheKindOf(Type type, Attribute injector, string kind)
            => Report($"Type '{type.FullName}' is not a {kind}, which '{injector.GetType().FullName}' injects into.");

        /// <summary>
        /// The assemblies which the image of one run refers to, read into the process for as long as that run lasts.
        /// <para/>
        /// An injector is an attribute, which the runtime makes out of the type which the image names, so the assembly
        /// which declares one has to be one the process can read: the weaving reads the type of the attribute, and the
        /// template which it names, out of the assembly which declares them, and the process reads them through the
        /// same folder. A build weaves the assembly it has just written inside a node of itself, which knows nothing of
        /// the folders of the project which was built, so the assemblies which the image refers to are answered from
        /// the folder of the image, which is where that build put them.
        /// </summary>
        /// <remarks>
        /// A request is answered for the assemblies which this reading read as well as for the one which is woven,
        /// because an assembly which was read that way reaches for the ones it refers to itself, and none of them lie
        /// where the process looks. The reading is taken back out where the run ends, because the process outlives it:
        /// a node of a build weaves every assembly which the build asks it to.
        /// </remarks>
        private sealed class ReferencedAssemblies : IDisposable
        {
            /// <summary>
            /// The assemblies which the requests are answered for, which the one which is woven opens.
            /// </summary>
            private readonly List<System.Reflection.Assembly> m_Answered = new();

            /// <summary>
            /// Guard of <see cref="m_Answered"/>, which the requests of a weaving arrive on threads which are not this
            /// one, and which the assemblies of one project are woven on at the same time as each other.
            /// </summary>
            private readonly object m_Guard = new();

            /// <summary>
            /// Directory which the assemblies the image refers to lie in.
            /// </summary>
            private readonly string m_SearchDirectory;

            /// <summary>
            /// Answer the requests of <paramref name="woven"/> from <paramref name="searchDirectory"/> until this is
            /// disposed.
            /// </summary>
            /// <param name="woven">The assembly which is woven, which is the one whose requests are answered.</param>
            /// <param name="searchDirectory">Directory which the assemblies it refers to lie in.</param>
            public ReferencedAssemblies(System.Reflection.Assembly woven, string searchDirectory)
            {
                m_SearchDirectory = searchDirectory;
                m_Answered.Add(woven);
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            }

            /// <inheritdoc/>
            public void Dispose() => AppDomain.CurrentDomain.AssemblyResolve -= Resolve;

            /// <summary>
            /// Read the assembly of one request out of the folder of the image, or null when the request is not one of
            /// this run.
            /// </summary>
            /// <param name="sender">Whoever raised the request, which is not read.</param>
            /// <param name="args">The request of an assembly which could not be resolved.</param>
            /// <returns>The assembly which was asked for, or null when this run does not read it.</returns>
            private System.Reflection.Assembly? Resolve(object? sender, ResolveEventArgs args)
            {
                // Only the requests of the assemblies of this run are answered, because the handler is held by the
                // process, which outlives the run and holds assemblies of its own: a request of any other assembly is
                // left to the resolution which the runtime does by itself.
                if (args.RequestingAssembly is not { } requesting) return null;
                lock (m_Guard)
                {
                    if (!m_Answered.Contains(requesting)) return null;
                }

                if (new System.Reflection.AssemblyName(args.Name).Name is not { } name) return null;

                // An image which lies in the folder under the name which was asked for is the one which is read, which
                // is the one the request is made of: a version is not compared, because the assembly beside an image is
                // the one which the build which made that image wrote there.
                var path = Path.Combine(m_SearchDirectory, name + ".dll");
                if (!File.Exists(path)) return null;

                // An assembly which the process already holds is answered with the one it holds rather than with a
                // second copy of its image, because a type of the two would be one type in name alone: an image which
                // was read beside a weaving and one which the process read are the same assembly of the same project.
                var held = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate => candidate.GetName().Name == name);
                if (held != null) return held;

                try
                {
                    // The image is read into bytes rather than opened where it lies, so that the file is left to the
                    // build which writes it: an assembly which is read for the weaving of one of the assemblies beside
                    // it is one which the project of that assembly may build again while the node of this build runs.
                    var read = AssemblyLoader.LoadReference(File.ReadAllBytes(path));
                    lock (m_Guard)
                    {
                        m_Answered.Add(read);
                    }

                    return read;
                }
                catch (Exception)
                {
                    // An image which cannot be read is one which this cannot answer with, which leaves the request to
                    // the resolution of the runtime: what the caller is told of it is the failure of that resolution,
                    // which names the assembly which could not be read and the reason it could not be.
                    return null;
                }
            }
        }

        /// <summary>
        /// Whether a member carries an attribute whose type implements one of the interfaces which are named, which is
        /// recorded as it is read, because the attributes which were read are the ones which are taken back out.
        /// </summary>
        /// <remarks>
        /// The attributes of a member are read from the metadata of the assembly before they are read from the reflection
        /// of the member, because reading the reflection of a member loads the type of every attribute which it carries.
        /// An attribute of an assembly which the weaver cannot read would fail the member for it, and the whole type
        /// with the member, although the weaving has no use for an attribute which is not an injector: a method which
        /// carries an attribute of the editor of Unity is the case which this is here for.<para/>
        /// An attribute whose type cannot be resolved is answered as one which is not an injector, which is what leaves
        /// such a member alone rather than failing it. Such an attribute is one which the weaving does not apply either,
        /// which the reading here and the reading of the reflection agree on: the attribute of an injector is loaded by
        /// the reflection of the member which carries it, so a type which the weaving cannot read is a type which it
        /// could not apply.<para/>
        /// Every attribute of the kind is read rather than the first of them, because the attributes which were read are
        /// the ones which are taken back out, and one of them which was left behind would be read again by a weaving of
        /// the assembly which was woven.
        /// </remarks>
        /// <param name="attributes">The attributes which the member carries.</param>
        /// <param name="injectorInterfaces">Full names of the interfaces which an injector of the kind implements.</param>
        /// <returns>Whether the member carries an attribute which one of the interfaces is implemented by.</returns>
        private bool HoldsInjector(IEnumerable<CustomAttribute> attributes, string[] injectorInterfaces)
        {
            var holds = false;
            foreach (var attribute in attributes)
            {
                TypeDefinition? attributeType;
                try
                {
                    attributeType = attribute.AttributeType.Resolve();
                }
                catch (AssemblyResolutionException)
                {
                    // The attribute names a type of an assembly which the weaving cannot read, which is answered as an
                    // attribute which is not an injector rather than as a failure of the member which carries it.
                    continue;
                }

                if (attributeType == null || !InjectorInterfaces.IsAnInjector(attributeType, injectorInterfaces)) continue;

                // The name is the one which the metadata holds rather than the one which the runtime reads, because the
                // metadata is what the trace is taken out by: a nested type is named with a '+' by the one and with a
                // '/' by the other, and a name of one of those would not be found in the other.
                m_InjectorAttributes.Add(attribute.AttributeType.FullName);
                holds = true;
            }

            return holds;
        }

        /// <summary>
        /// Whether any injector stands on the type or on a member of it, which is read from the metadata of the image
        /// rather than from the types of the runtime: materializing every type of an assembly is the cost of a weaving
        /// beside the weaving of it, and a type which no injector stands on is one which nothing is applied to.
        /// </summary>
        /// <param name="type">The definition of the type which is read.</param>
        /// <returns>Whether the type declares an injector anywhere.</returns>
        private bool HoldsAnInjector(TypeDefinition type)
            => HoldsInjector(type.CustomAttributes, InjectorInterfaces.TypeInjectorNames)
            || type.Methods.Any(method => HoldsInjector(method.CustomAttributes, InjectorInterfaces.MethodInjectorNames))
            || type.Fields.Any(field => HoldsInjector(field.CustomAttributes, InjectorInterfaces.FieldInjectorNames))
            || type.Properties.Any(property => HoldsInjector(property.CustomAttributes, InjectorInterfaces.PropertyInjectorNames));

        /// <summary>
        /// Apply the injectors of one type of the assembly.
        /// </summary>
        /// <param name="handler">Handler of the assembly which the type belongs to.</param>
        /// <param name="type">The type which the injectors of it are applied to.</param>
        /// <returns>Whether the type was changed.</returns>
        private bool ProcessType(AssemblyHandler handler, Type type)
        {
            var changed = false;
            // A type which the assembly does not hold is refused where the handler of it is read, which the caller of
            // this reads as a type which could not be woven: a type is never answered with a handler of nothing, so
            // there is no such handler to check for.
            var typeHandler = handler.GetType(type);

            changed |= ProcessTypeInjector(handler, type);
            if (!type.IsClass && type is not {IsValueType: true, IsEnum: false}) return changed;

            // A member is looked up on the handler of the type rather than on the container of the methods of it,
            // because a method is looked up by the signature which the injector was put on beside its name, and the
            // handler of the type is what answers for a signature. The cast stands on every handler which the assembly
            // answers with for a type of its image being one of these.
            if (typeHandler is TypeHandler members)
            {
                foreach (var method in type.GetMethods(INJECTED_MEMBERS))
                {
                    changed |= ProcessMethodInjector(members, type, method);
                }
            }

            if (typeHandler is IFieldContainer fieldContainer)
            {
                foreach (var field in type.GetFields(INJECTED_MEMBERS))
                {
                    changed |= ProcessFieldInjector(fieldContainer, type, field);
                }
            }

            if (typeHandler is IPropertyContainer propertyContainer)
            {
                foreach (var property in type.GetProperties(INJECTED_MEMBERS))
                {
                    changed |= ProcessPropertyInjector(propertyContainer, type, property);
                }
            }

            return changed;
        }

        /// <summary>
        /// Apply the injectors which the assembly itself declares, which are the attributes put on the assembly rather
        /// than on a member of it.
        /// </summary>
        /// <param name="assemblyHandler">Handler of the assembly which the injectors are applied to.</param>
        /// <returns>Whether the assembly declares an injector at all.</returns>
        private bool ProcessAssembleInjector(AssemblyHandler assemblyHandler)
        {
            if (!HoldsInjector(assemblyHandler.Assembly.Source.CustomAttributes, InjectorInterfaces.AssemblyInjectorNames)) return false;

            var injectors = InTheOrderTheyAreApplied(assembly.GetCustomAttributes(inherit: false)
                                                            .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IAssemblyInjector)))
                                                            .Cast<Attribute>())
                               .Cast<IAssemblyInjector>()
                               .ToArray();
            if (injectors.Length == 0) return false;
            foreach (var injector in injectors)
            {
                injector.Inject(assembly, assemblyHandler);
            }

            return true;
        }

        /// <summary>
        /// The injectors of a member, in the order in which they are applied: by the priority which each of those which
        /// declare one declares, the greatest first, and the rest of them by the name of the type of each.<para/>
        /// The name of a type is what tells two injectors of one priority apart because nothing else does: the order in
        /// which the runtime hands the attributes of a member back is not the order in which they are written there,
        /// which the specification of the language says is no order of the source at all, so a weaving which is left to
        /// it is not the same weaving twice. Nothing of the order of two attributes of one type is read off the source
        /// either, because the source holds none: the priority is what tells those two apart.
        /// </summary>
        /// <param name="injectors">The injectors which the member carries.</param>
        /// <returns>The same injectors, in the order in which they are applied.</returns>
        private static Attribute[] InTheOrderTheyAreApplied(IEnumerable<Attribute> injectors)
            => injectors.OrderByDescending(injector => ((IInjector) injector).Priority)
                        .ThenBy(injector => injector.GetType().FullName, StringComparer.Ordinal)
                        .ToArray();

        /// <summary>
        /// Apply the injectors which one type declares, each of them asked of the kind of handler it injects into, so
        /// that one which is put on a type of another kind is reported rather than passed over.
        /// </summary>
        /// <param name="assemblyHandler">Handler of the assembly which the type belongs to.</param>
        /// <param name="type">The type whose injectors are applied.</param>
        /// <returns>Whether an injector was applied.</returns>
        private bool ProcessTypeInjector(AssemblyHandler assemblyHandler, Type type)
        {
            if (!HoldsInjector(assemblyHandler.GetCecilType(type).Definition.CustomAttributes, InjectorInterfaces.TypeInjectorNames)) return false;

            var dirty = false;
            var typeAttributes = InTheOrderTheyAreApplied(type.GetCustomAttributes(inherit: false)
                                                             .Where(static item => item is Attribute attr && s_TypeInjectors.Any(injector => injector.IsInstanceOfType(attr)))
                                                             .Cast<Attribute>());

            foreach (var typeAttribute in typeAttributes)
            {
                var typeHandler = assemblyHandler.GetType(type);
                if (typeHandler == null!)
                {
                    continue;
                }

                if (typeAttribute is ITypeInjector typeInjector)
                {
                    typeInjector.Inject(type, typeHandler);
                    dirty = true;
                }

                // An injector which names the kind of type it applies to applies to a type of that kind alone, and one
                // which is asked of a type of another kind is reported rather than passed over, because the injection
                // it stands for does not happen and a build which carried on would say that it had. The three kinds are
                // one shape, and each of them is asked in turn rather than one of them being answered: an attribute
                // which implements more than one of the interfaces is applied through every one the type answers to.
                if (typeAttribute is IClassInjector classInjector)
                {
                    if (typeHandler is not IClassHandler classHandler)
                    {
                        ReportTheKindOf(type, typeAttribute, "class");
                        continue;
                    }

                    classInjector.Inject(type, classHandler);
                    dirty = true;
                }

                if (typeAttribute is IStructInjector structInjector)
                {
                    if (typeHandler is not IStructHandler structHandler)
                    {
                        ReportTheKindOf(type, typeAttribute, "struct");
                        continue;
                    }

                    structInjector.Inject(type, structHandler);
                    dirty = true;
                }

                if (typeAttribute is IEnumInjector enumInjector)
                {
                    if (typeHandler is not IEnumHandler enumHandler)
                    {
                        ReportTheKindOf(type, typeAttribute, "enum");
                        continue;
                    }

                    enumInjector.Inject(type, enumHandler);
                    dirty = true;
                }
            }

            return dirty;
        }

        /// <summary>
        /// Apply the injectors which one method carries, which are looked up in the assembly by the name and the
        /// parameters of the method rather than by the method itself.<para/>
        /// The parameters are the ones which tell two methods of one name apart, and the empty signature which a method
        /// that takes no parameter carries tells one of them apart from a member of the name which takes some: a lookup
        /// by the name alone answers with the first of them, which is another member than the one the injector names.
        /// </summary>
        /// <param name="typeHandler">Handler of the type which declares the method.</param>
        /// <param name="runtimeType">The type which the method belongs to, which what is reported names.</param>
        /// <param name="methodInfo">The method whose injectors are applied.</param>
        /// <returns>Whether an injector was applied.</returns>
        private bool ProcessMethodInjector(TypeHandler typeHandler, Type runtimeType, MethodInfo methodInfo)
        {
            var methodHandler = typeHandler.GetMethodBySignature(methodInfo.Name, methodInfo.GetParameters().GetITypes());

            // The attributes are asked of the metadata first, so that a member which carries no injector is left without
            // its reflection being read. A member which the assembly does not hold is left to the reflection, so that
            // the injector which was put on it is reported as naming a member which is not there rather than passed
            // over in silence.
            if (methodHandler is MethodHandler {Source: { } methodDefinition} && !HoldsInjector(methodDefinition.CustomAttributes, InjectorInterfaces.MethodInjectorNames)) return false;

            if (InTheOrderTheyAreApplied(methodInfo.GetCustomAttributes(inherit: false)
                                                    .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IMethodInjector)))
                                                    .Cast<Attribute>())
                   .Cast<IMethodInjector>()
                   .ToArray() is not {Length: > 0} injectors) return false;

            // The assembly is written back only when something was injected into it, so an injector which found nothing
            // to inject into is not counted as a change: the member it names was reported instead.
            var injected = false;
            foreach (var injector in injectors)
            {
                if (methodHandler == null)
                {
                    Report($"Method '{methodInfo.Name}' not found in type '{runtimeType.FullName}'.");
                    continue;
                }

                injector.Inject(methodInfo, methodHandler);
                injected = true;
            }

            return injected;
        }

        /// <summary>
        /// Apply the injectors which one field carries, which are looked up in the assembly by the name of the field
        /// rather than by the field itself.
        /// </summary>
        /// <param name="typeHandler">Handler of the type which declares the field.</param>
        /// <param name="runtimeType">The type which the field belongs to, which what is reported names.</param>
        /// <param name="fieldInfo">The field whose injectors are applied.</param>
        /// <returns>Whether an injector was applied.</returns>
        private bool ProcessFieldInjector(IFieldContainer typeHandler, Type runtimeType, FieldInfo fieldInfo)
        {
            var fieldHandler = typeHandler.GetField(fieldInfo.Name);
            if (fieldHandler is FieldHandler {Source: { } fieldDefinition} && !HoldsInjector(fieldDefinition.CustomAttributes, InjectorInterfaces.FieldInjectorNames)) return false;

            if (InTheOrderTheyAreApplied(fieldInfo.GetCustomAttributes(inherit: false)
                                                   .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IFieldInjector)))
                                                   .Cast<Attribute>())
                   .Cast<IFieldInjector>()
                   .ToArray() is not {Length: > 0} injectors) return false;

            var injected = false;
            foreach (var injector in injectors)
            {
                if (fieldHandler == null)
                {
                    Report($"Field '{fieldInfo.Name}' not found in type '{runtimeType.FullName}'.");
                    continue;
                }

                injector.Inject(fieldInfo, fieldHandler);
                injected = true;
            }

            return injected;
        }

        /// <summary>
        /// Apply the injectors which one property carries, which are looked up in the assembly by the name of the
        /// property rather than by the property itself.
        /// </summary>
        /// <param name="typeHandler">Handler of the type which declares the property.</param>
        /// <param name="runtimeType">The type which the property belongs to, which what is reported names.</param>
        /// <param name="propertyInfo">The property whose injectors are applied.</param>
        /// <returns>Whether an injector was applied.</returns>
        private bool ProcessPropertyInjector(IPropertyContainer typeHandler, Type runtimeType, PropertyInfo propertyInfo)
        {
            var propertyHandler = typeHandler.GetProperty(propertyInfo.Name);
            if (propertyHandler is PropertyHandler {Source: { } propertyDefinition} && !HoldsInjector(propertyDefinition.CustomAttributes, InjectorInterfaces.PropertyInjectorNames)) return false;

            if (InTheOrderTheyAreApplied(propertyInfo.GetCustomAttributes(inherit: false)
                                                      .Where(static item => item is Attribute attr && attr.GetType().GetInterfaces().Contains(typeof(IPropertyInjector)))
                                                      .Cast<Attribute>())
                   .Cast<IPropertyInjector>()
                   .ToArray() is not {Length: > 0} injectors) return false;

            var injected = false;
            foreach (var injector in injectors)
            {
                if (propertyHandler == null)
                {
                    Report($"Property '{propertyInfo.Name}' not found in type '{runtimeType.FullName}'.");
                    continue;
                }

                injector.Inject(propertyInfo, propertyHandler);
                injected = true;
            }

            return injected;
        }
    }
}