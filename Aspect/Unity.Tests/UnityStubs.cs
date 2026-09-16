using System.Collections.Generic;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Unity.CompilationPipeline.Common.Diagnostics
{
    /// <summary>
    /// The kind of a message which a post processor reports to the compilation, of which only the error is reported
    /// here.
    /// </summary>
    public enum DiagnosticType
    {
        Error,
        Warning,
        Info
    }

    /// <summary>
    /// A message which a post processor reports to the compilation.
    /// </summary>
    public class DiagnosticMessage
    {
        /// <summary>
        /// The kind of the message.
        /// </summary>
        public DiagnosticType DiagnosticType { get; set; }

        /// <summary>
        /// What the message says.
        /// </summary>
        public string MessageData { get; set; } = string.Empty;
    }
}

namespace Unity.CompilationPipeline.Common.ILPostProcessing
{
    /// <summary>
    /// The image of an assembly which is handed to a post processor and answered with, with the symbols it was compiled
    /// with.
    /// </summary>
    public class InMemoryAssembly
    {
        /// <summary>
        /// Hold the image of an assembly and its symbols.
        /// </summary>
        /// <param name="peData">The bytes of the image.</param>
        /// <param name="pdbData">The bytes of the symbols of the image.</param>
        public InMemoryAssembly(byte[] peData, byte[] pdbData)
        {
            PeData = peData;
            PdbData = pdbData;
        }

        /// <summary>
        /// The bytes of the image.
        /// </summary>
        public byte[] PeData { get; }

        /// <summary>
        /// The bytes of the symbols of the image.
        /// </summary>
        public byte[] PdbData { get; }
    }

    /// <summary>
    /// What a post processor answers with: the image which replaces the one it was given, and what it reports of the
    /// assembly.
    /// </summary>
    public class ILPostProcessResult
    {
        /// <summary>
        /// Hold the image which a post processor answers with and what it reported.
        /// </summary>
        /// <param name="inMemoryAssembly">The image which replaces the one which was given.</param>
        /// <param name="diagnostics">What the post processor reported.</param>
        public ILPostProcessResult(InMemoryAssembly inMemoryAssembly, List<DiagnosticMessage> diagnostics)
        {
            InMemoryAssembly = inMemoryAssembly;
            Diagnostics      = diagnostics;
        }

        /// <summary>
        /// The image which replaces the one which was given.
        /// </summary>
        public InMemoryAssembly InMemoryAssembly { get; }

        /// <summary>
        /// What the post processor reported.
        /// </summary>
        public List<DiagnosticMessage> Diagnostics { get; }
    }

    /// <summary>
    /// One assembly of a compilation, as the post processors of it are handed it.
    /// </summary>
    public interface ICompiledAssembly
    {
        /// <summary>
        /// The image which the assembly was compiled to, with its symbols.
        /// </summary>
        InMemoryAssembly InMemoryAssembly { get; }

        /// <summary>
        /// The name of the assembly.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The paths which the assembly refers to.
        /// </summary>
        string[] References { get; }
    }

    /// <summary>
    /// A step of a compilation which is handed the image of an assembly before it is written and loaded, and which
    /// answers with the image which replaces it.
    /// </summary>
    /// <remarks>
    /// The types of this file stand for the ones which the editor of Unity gives a post processor, and they hold what
    /// the post processor of this repository reads of them alone: the tests of the package are run where no editor is
    /// installed, and the editor compiles the sources against its own of these.
    /// </remarks>
    public abstract class ILPostProcessor
    {
        /// <summary>
        /// The processor which the compilation runs, which is one for each of them rather than one for all of them.
        /// </summary>
        /// <returns>The processor.</returns>
        public abstract ILPostProcessor GetInstance();

        /// <summary>
        /// Whether the assembly is one which this processor weaves.
        /// </summary>
        /// <param name="assembly">The assembly which was compiled.</param>
        /// <returns>Whether the assembly is woven by this processor.</returns>
        public abstract bool WillProcess(ICompiledAssembly assembly);

        /// <summary>
        /// Weave the assembly and answer with the image which replaces the one it was compiled to.
        /// </summary>
        /// <param name="assembly">The assembly which was compiled.</param>
        /// <returns>The image which replaces the one which was given, and what was reported of it.</returns>
        public abstract ILPostProcessResult Process(ICompiledAssembly assembly);
    }
}
