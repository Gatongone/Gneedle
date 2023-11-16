/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/13-01:56:12
 * Github: https://github.com/Gatongone
 */

namespace Gneedle.Inject;

/// <summary>
/// Writing for <see cref="Mono.Cecil.AssemblyDefinition"/> to target file or memories.
/// </summary>
public abstract class AssemblyWriter
{
    /// <summary>
    /// Target assembly definition.
    /// </summary>
    public readonly AssemblyDefinition Source;
    
    /// <summary>
    /// Assembly dirty flags. 
    /// </summary>
    public AssemblyState State { get; private set; }
    
    /// <summary>
    /// Convert target file or memories to <see cref="System.Reflection.Assembly"/>.
    /// </summary>
    /// <returns>Reflection assembly.</returns>
    public abstract System.Reflection.Assembly ToReflectionAssembly();
    
    /// <param name="source">Source assembly definition which need to write.</param>
    protected AssemblyWriter(AssemblyDefinition source) => Source = source;

    /// <summary>
    /// Save assembly with default way.
    /// </summary>
    public void Save()
    {
        OnDefaultSave();
        SetFresh();
    }
    
    /// <summary>
    /// Save assembly with implementation way.
    /// </summary>
    protected abstract void OnDefaultSave();

    /// <summary>
    /// Create assembly at target path.
    /// </summary>
    /// <param name="path">Target assembly path.</param>
    public void SaveTo(string path) => Source.Write(path);
    
    /// <summary>
    ///  Write assembly to bytes.
    /// </summary>
    /// <param name="buffer">Assembly with bytes.</param>
    public void SaveTo(out byte[] buffer)
    {
        using var stream = new MemoryStream();
        Source.Write(stream);
        buffer = stream.GetBuffer();
        stream.Flush();
    }
    
    /// <summary>
    /// Set assembly state to dirty.
    /// </summary>
    internal void SetDirty() => State = AssemblyState.Dirty;
    
    /// <summary>
    /// Set assembly state to fresh.
    /// </summary>
    private void SetFresh() => State = AssemblyState.Fresh;
}