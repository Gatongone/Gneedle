using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
    public sealed class GneedleILPostProcessor : ILPostProcessor
    {
        /// <summary>
        /// The interfaces which a type implements to be one of the attributes which an injector is read from.
        /// </summary>
        private static readonly string[] InjectorInterfaces =
        {
            typeof(IAssemblyInjector).FullName!, typeof(ITypeInjector).FullName!, typeof(IClassInjector).FullName!,
            typeof(IStructInjector).FullName!,   typeof(IEnumInjector).FullName!, typeof(IMethodInjector).FullName!,
            typeof(IFieldInjector).FullName!,    typeof(IPropertyInjector).FullName!
        };

        /// <summary>
        /// The path which every assembly an assembly of the compilation refers to was named by, by its name alone.
        /// </summary>
        /// <remarks>
        /// The assemblies of a compilation are processed on threads of their own, so what one of them remembers is
        /// there for the rest: a name stands for the one assembly which the compilation refers to by it.
        /// </remarks>
        private static readonly ConcurrentDictionary<string, string> s_References = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the assemblies which an injector names are looked for along the paths of the compilation.
        /// </summary>
        private static int s_ResolutionRegistered;

        /// <summary>
        /// The assemblies which were asked for while the injectors of an assembly were applied and which the resolution
        /// could not answer, each with what was known about it.<para/>
        /// An assembly which the weaving needs and cannot read fails the member which names it, and the report of that
        /// failure names the assembly alone: what was tried for it is added to the diagnostics here, because the
        /// resolution is the one part of it which happens away from the weaving.
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> s_Unresolved = new(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public override ILPostProcessor GetInstance() => new GneedleILPostProcessor();

        /// <summary>
        /// Whether the compiled assembly declares an injector, which is the one question worth asking of it.<para/>
        /// The weaver of a project is a plugin of it, so every assembly of the project refers to it whether it declares
        /// an injector or not, and the reference alone would have every assembly of Unity and of a package woven as
        /// well.
        /// </summary>
        /// <param name="assembly">The assembly which was compiled.</param>
        public override bool WillProcess(ICompiledAssembly assembly)
        {
            using var stream = new MemoryStream(assembly.InMemoryAssembly.PeData);
            using var image = Mono.Cecil.AssemblyDefinition.ReadAssembly(stream);
            return image.MainModule.Types.Any(DeclaresAnInjector);
        }

        /// <summary>
        /// Whether the type declares an injector of its own, or inherits one, the nested types included.
        /// </summary>
        /// <param name="type">The type which is read.</param>
        private static bool DeclaresAnInjector(Mono.Cecil.TypeDefinition type)
            => type.Interfaces.Any(implementation => Array.IndexOf(InjectorInterfaces, implementation.InterfaceType.FullName) >= 0)
            || type.NestedTypes.Any(DeclaresAnInjector);

        /// <inheritdoc/>
        public override ILPostProcessResult Process(ICompiledAssembly assembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            try
            {
                // The assembly which is woven is loaded with the types of the assemblies which it refers to, so every
                // path which the compilation named is remembered before anything is loaded.
                foreach (var reference in assembly.References)
                {
                    if (Path.GetFileNameWithoutExtension(reference) is { } name) s_References[name] = reference;
                }

                RegisterResolution();

                var image = assembly.InMemoryAssembly.PeData;
                LoadWhatTheWeavingReads(image);

                var loaded = AssemblyLoader.LoadFromBytes(image);
                var (changed, result) = Injections.Apply(loaded, image, removesTheWeaver: true,
                                                         reportError: message => diagnostics.Add(Error(message)));

                // What could not be read is reported with what was tried for it, because a member which names an
                // assembly the weaving cannot read is failed by the runtime rather than by the weaving, which says
                // which assembly it was and nothing more.
                if (diagnostics.Count > 0)
                {
                    foreach (var unresolved in s_Unresolved) diagnostics.Add(Error($"The assembly '{unresolved.Key}' was asked for and could not be resolved: {unresolved.Value}."));
                }

                // The symbols are handed back with the image, so that what was woven is still read where it was written.
                return changed
                    ? new ILPostProcessResult(new InMemoryAssembly(result, assembly.InMemoryAssembly.PdbData), diagnostics)
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
        /// Load every assembly which the image being woven names a type of, before the weaving reads any of them.
        /// </summary>
        /// <remarks>
        /// The runtime loads an assembly which a type of another one is declared by when that type is read, which the
        /// weaving does with the reflection of the members of the assembly. The runner of the post processors answers
        /// such a request from the folders it was given, one folder per <c>-assemblyFolders</c> of the compilation,
        /// and it looks a folder up for the simple name of the assembly alone. That list does not hold the folder which
        /// the modules of Unity lie in, which is a folder inside the one it does hold, so an assembly of the editor is
        /// not found by it - and the runner throws where it finds nothing rather than leaving the request to whoever
        /// answers after it, which is why the weaving cannot answer it either. A request which is never made is the
        /// only one which cannot fail: everything the image names is loaded here, and what the weaving reads afterwards
        /// is already read.
        /// </remarks>
        /// <param name="image">The bytes of the assembly which is woven.</param>
        private static void LoadWhatTheWeavingReads(byte[] image)
        {
            using var stream = new MemoryStream(image);
            using var definition = Mono.Cecil.AssemblyDefinition.ReadAssembly(stream);

            foreach (var reference in definition.MainModule.GetTypeReferences())
            {
                if (reference.Scope is not Mono.Cecil.AssemblyNameReference scope) continue;
                if (!s_References.TryGetValue(scope.Name, out var path)) continue;

                // An assembly which is loaded already is left where it is: a second copy of it would hold second types,
                // and the request for it would be answered with the copy which is there.
                if (AppDomain.CurrentDomain.GetAssemblies().Any(loaded => loaded.GetName().Name == scope.Name)) continue;

                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath)) Assembly.LoadFrom(fullPath);
                }
                catch (Exception exception)
                {
                    // An assembly which cannot be loaded here is left to the runtime, which fails where it is needed,
                    // and what was tried for it is reported with the failure.
                    s_Unresolved[scope.Name] = $"it could not be loaded from '{path}'. {exception.Message}";
                }
            }
        }

        /// <summary>
        /// Look the assemblies of the compilation up along the paths which the compilation named them by.
        /// </summary>
        private static void RegisterResolution()
        {
            if (Interlocked.Exchange(ref s_ResolutionRegistered, 1) != 0) return;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        /// <summary>
        /// Answer a request for an assembly which the runtime could not resolve by itself.
        /// </summary>
        /// <param name="sender">The sender of the request, which is not read.</param>
        /// <param name="args">The request of the assembly.</param>
        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            if (new AssemblyName(args.Name).Name is not { } name) return null;

            // An assembly which is already loaded is the one which is answered, because a second copy of it would hold
            // second types: a type of the assembly which is woven implements the interfaces of the weaver, and it has to
            // implement the very interfaces which the weaver which runs holds.
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == name);
            if (loaded != null) return loaded;

            if (!s_References.TryGetValue(name, out var path))
            {
                s_Unresolved[name] = "it is not one of the assemblies which the compilation refers to";
                return null;
            }

            // A path of a compilation is relative to the project which it was made for, which is where the process runs.
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                s_Unresolved[name] = $"the file it is named by, '{fullPath}', is not there";
                return null;
            }

            try
            {
                return Assembly.LoadFrom(fullPath);
            }
            catch (Exception exception)
            {
                s_Unresolved[name] = $"it could not be loaded from '{fullPath}'. {exception.Message}";
                return null;
            }
        }
    }
}
