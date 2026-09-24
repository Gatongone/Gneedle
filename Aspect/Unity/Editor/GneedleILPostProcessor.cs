using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Gneedle.Inject;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using Assembly = System.Reflection.Assembly;

namespace Gneedle.Aspect
{
    /// <summary>
    /// Applies the injectors which the assemblies of a compilation declare, before those assemblies are written and
    /// loaded.<para/>
    /// A post processor runs as a part of the compilation rather than beside it: it is handed the image which an
    /// assembly was compiled to, it answers with the image which replaces it, and it is a part of the compilation which
    /// Unity runs for the editor and for a player alike.
    /// </summary>
    /// <remarks>
    /// The assemblies of a compilation are woven on threads of their own, and everything which one weaving of one
    /// assembly needs is held for that weaving alone: the paths which the compilation named its assemblies by, what could
    /// not be read of them, and what is reported of it. A compilation which failed therefore leaves nothing of itself in
    /// the one which is woven beside it or after it, and the resolution which answers for those paths is taken back out
    /// where the weaving which put it in ends - the process a compilation runs in is the editor, which outlives every
    /// compilation of it.
    /// </remarks>
    public sealed class GneedleILPostProcessor : ILPostProcessor
    {
        /// <inheritdoc/>
        public override ILPostProcessor GetInstance() => new GneedleILPostProcessor();

        /// <summary>
        /// Whether the compiled assembly declares an injector, which is the one question worth asking of it.<para/>
        /// The weaver of a project is a plugin of it, so every assembly of the project refers to it whether it declares
        /// an injector or not, and the reference alone would have every assembly of Unity and of a package woven as
        /// well.
        /// </summary>
        /// <remarks>
        /// An injector is read here the way the weaving reads one, through the types of the module rather than by
        /// resolving what they refer to, so that this processor and the weaving agree on which assemblies have anything
        /// to weave: a reading which found an injector the weaving does not would take an assembly through a compilation
        /// which leaves it as it was.<para/>
        /// The types of the module are the ones which are read of it, which is every injector a weaving here can read:
        /// the attributes are always taken back out of an assembly of a compilation, so a type which was woven gives up
        /// what makes it an injector, and an attribute which another assembly of the compilation declares is none by the
        /// time the assembly which carries it is woven. The attributes of a project which keeps them for another project
        /// to weave with are read by the build task, which reads the assemblies beside the image which it weaves.
        /// </remarks>
        /// <param name="assembly">The assembly which was compiled.</param>
        public override bool WillProcess(ICompiledAssembly assembly)
        {
            using var stream = new MemoryStream(assembly.InMemoryAssembly.PeData);
            using var image = Mono.Cecil.AssemblyDefinition.ReadAssembly(stream);
            return InjectorInterfaces.AllTypes(image.MainModule).Any(type => InjectorInterfaces.IsAnInjector(type, InjectorInterfaces.AllNames));
        }

        /// <inheritdoc/>
        public override ILPostProcessResult Process(ICompiledAssembly assembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            try
            {
                // The assembly which is woven is loaded with the types of the assemblies which it refers to, so every
                // path which the compilation named is remembered before anything is loaded, and the resolution which
                // answers for them is put in place for as long as this weaving runs.
                using var weaving = new Weaving(assembly.References, diagnostics);

                var image = assembly.InMemoryAssembly.PeData;
                weaving.LoadWhatTheWeavingReads(image);

                // The symbols are read with the image and woven with it, because the two are one pair: the places which
                // a database records are places of the image it names, and the weaving writes an image of its own, so
                // the symbols of the image which was read describe an image which is not the one answered with below.
                var loaded = AssemblyLoader.LoadFromBytes(image);
                var (changed, result, symbols) = Injections.Apply(loaded, image, assembly.InMemoryAssembly.PdbData,
                                                                  removesTheWeaver: true, reportError: weaving.Report);

                // What could not be read is reported with what was tried for it, because a member which names an
                // assembly the weaving cannot read is failed by the runtime rather than by the weaving, which says
                // which assembly it was and nothing more.
                weaving.ReportUnresolved();

                // The symbols which were woven with the image are handed back with it, so that what was woven is still
                // read where it was written: the two describe one image, and the symbols of another one are a pair which
                // a reader of the two refuses. An assembly which was compiled without symbols is handed back without
                // them, which is what the weaving answers with where it was given none.
                return changed
                    ? new ILPostProcessResult(new InMemoryAssembly(result, symbols!), diagnostics)
                    : new ILPostProcessResult(assembly.InMemoryAssembly, diagnostics);
            }
            catch (Exception exception)
            {
                // An assembly which could not be woven is left as it was compiled, and what went wrong is reported as an
                // error of the compilation which produced it.
                diagnostics.Add(Error($"The injectors of '{assembly.Name}' were not applied. {exception}"));
                return new ILPostProcessResult(assembly.InMemoryAssembly, diagnostics);
            }
        }

        /// <summary>
        /// Report what the weaving of an assembly found, which is a member which an injector named and the assembly does
        /// not hold.
        /// </summary>
        /// <param name="message">What is reported.</param>
        private static DiagnosticMessage Error(string message) => new()
        {
            DiagnosticType = DiagnosticType.Error,
            MessageData    = message
        };

        /// <summary>
        /// One weaving of one assembly of a compilation, with the assemblies which that compilation named.
        /// </summary>
        /// <remarks>
        /// The state of a weaving is held by the weaving itself rather than by the processor, because the assemblies of
        /// a compilation are woven on threads of their own and the compiler which drives them holds one processor for
        /// all of them: what one assembly could not read is a failure of that assembly alone, and the paths of one
        /// compilation do not describe the assemblies of another. The resolution is registered with the weaving and
        /// taken back out where it ends, so that the process which outlives it does not answer for assemblies it has
        /// nothing to do with.
        /// </remarks>
        private sealed class Weaving : IDisposable
        {
            /// <summary>
            /// The path which every assembly which the compilation refers to was named by, by its name alone.
            /// </summary>
            private readonly ConcurrentDictionary<string, string> m_References = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// The assemblies which were asked for while this assembly was woven and which the resolution could not
            /// answer, each with what was known about it.<para/>
            /// An assembly which the weaving needs and cannot read fails the member which names it, and the report of
            /// that failure names the assembly alone: what was tried for it is added to the diagnostics here, because
            /// the resolution is the one part of it which happens away from the weaving.
            /// </summary>
            private readonly ConcurrentDictionary<string, string> m_Unresolved = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// What is reported of the assembly which is woven.<para/>
            /// The two collections above are written by the resolution, which the runtime reaches on threads of its own
            /// while a weaving runs, and that is what makes them concurrent. This one is not: what is reported of a
            /// weaving is reported by the weaving, and the reads which the runtime makes beside it say nothing of what
            /// could not be woven - they answer with an assembly or with nothing, and the assembly they cannot answer
            /// for is written where they stand rather than here. So the only thread which writes this is the one which
            /// weaves the assembly, and then the same one where the weaving reports what the resolution could not
            /// answer. The threads of a compilation are the threads of its assemblies, one assembly to each, and no two
            /// of them stand at the diagnostics of one.
            /// </summary>
            private readonly List<DiagnosticMessage> m_Diagnostics;

            /// <summary>
            /// Hold the paths which a compilation named its assemblies by, and wait for the resolution of the ones which
            /// are asked for while one of them is woven.
            /// </summary>
            /// <param name="references">The paths which the compilation named the assemblies it refers to by.</param>
            /// <param name="diagnostics">What is reported of the weaving.</param>
            public Weaving(IEnumerable<string> references, List<DiagnosticMessage> diagnostics)
            {
                m_Diagnostics = diagnostics;
                foreach (var reference in references)
                {
                    if (Path.GetFileNameWithoutExtension(reference) is { } name) m_References[name] = reference;
                }

                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            }

            /// <summary>
            /// Stop answering for the assemblies of the compilation, which is where the weaving which read them ends.
            /// </summary>
            public void Dispose() => AppDomain.CurrentDomain.AssemblyResolve -= Resolve;

            /// <summary>
            /// Report a member which the weaving could not write.
            /// </summary>
            /// <param name="message">What is reported.</param>
            public void Report(string message) => m_Diagnostics.Add(Error(message));

            /// <summary>
            /// Report what the resolution could not answer, which is added to the diagnostics of a weaving that reported
            /// anything of its own.
            /// </summary>
            /// <remarks>
            /// A weaving which reported nothing is one whose assemblies were resolved or never needed, and a compilation
            /// which is told of an assembly which was asked for and found would fail over a weaving which went through.
            /// </remarks>
            public void ReportUnresolved()
            {
                if (m_Diagnostics.Count == 0) return;

                foreach (var unresolved in m_Unresolved) m_Diagnostics.Add(Error($"The assembly '{unresolved.Key}' was asked for and could not be resolved: {unresolved.Value}."));
            }

            /// <summary>
            /// Load every assembly which the image being woven names a type of, before the weaving reads any of them.
            /// </summary>
            /// <remarks>
            /// The runtime loads an assembly which a type of another one is declared by when that type is read, which
            /// the weaving does with the reflection of the members of the assembly. The runner of the post processors
            /// answers such a request from the folders it was given, one folder per <c>-assemblyFolders</c> of the
            /// compilation, and it looks a folder up for the simple name of the assembly alone. That list does not hold
            /// the folder which the modules of Unity lie in, which is a folder inside the one it does hold, so an
            /// assembly of the editor is not found by it - and the runner throws where it finds nothing rather than
            /// leaving the request to whoever answers after it, which is why the weaving cannot answer it either. A
            /// request which is never made is the only one which cannot fail: everything the image names is loaded here,
            /// and what the weaving reads afterwards is already read.
            /// </remarks>
            /// <param name="image">The bytes of the assembly which is woven.</param>
            public void LoadWhatTheWeavingReads(byte[] image)
            {
                using var stream = new MemoryStream(image);
                using var definition = Mono.Cecil.AssemblyDefinition.ReadAssembly(stream);

                foreach (var reference in definition.MainModule.GetTypeReferences())
                {
                    if (reference.Scope is not Mono.Cecil.AssemblyNameReference scope) continue;
                    if (!m_References.TryGetValue(scope.Name, out var path)) continue;

                    // An assembly which is loaded already is left where it is: a second copy of it would hold second
                    // types, and the request for it would be answered with the copy which is there.
                    if (AppDomain.CurrentDomain.GetAssemblies().Any(loaded => loaded.GetName().Name == scope.Name)) continue;

                    try
                    {
                        var fullPath = Path.GetFullPath(path);
                        if (File.Exists(fullPath)) Assembly.LoadFrom(fullPath);
                    }
                    catch (Exception exception)
                    {
                        // An assembly which cannot be loaded here is left to the runtime, which fails where it is
                        // needed, and what was tried for it is reported with the failure.
                        m_Unresolved[scope.Name] = $"it could not be loaded from '{path}'. {exception.Message}";
                    }
                }
            }

            /// <summary>
            /// Answer a request for an assembly which the runtime could not resolve by itself.
            /// </summary>
            /// <param name="sender">The sender of the request, which is not read.</param>
            /// <param name="args">The request of the assembly.</param>
            private Assembly Resolve(object sender, ResolveEventArgs args)
            {
                if (new AssemblyName(args.Name).Name is not { } name) return null;

                // An assembly which is already loaded is the one which is answered, because a second copy of it would
                // hold second types: a type of the assembly which is woven implements the interfaces of the weaver, and
                // it has to implement the very interfaces which the weaver which runs holds.
                var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == name);
                if (loaded != null) return loaded;

                if (!m_References.TryGetValue(name, out var path))
                {
                    m_Unresolved[name] = "it is not one of the assemblies which the compilation refers to";
                    return null;
                }

                // A path of a compilation is relative to the project which it was made for, which is where the process
                // runs.
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath))
                {
                    m_Unresolved[name] = $"the file it is named by, '{fullPath}', is not there";
                    return null;
                }

                try
                {
                    return Assembly.LoadFrom(fullPath);
                }
                catch (Exception exception)
                {
                    m_Unresolved[name] = $"it could not be loaded from '{fullPath}'. {exception.Message}";
                    return null;
                }
            }
        }
    }
}
