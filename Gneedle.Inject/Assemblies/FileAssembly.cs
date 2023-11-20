/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/13-01:55:28
 * Github: https://github.com/Gatongone
 */

using System.Reflection;

namespace Gneedle.Inject;

/// <summary>
/// Assembly which reading and writing from local file.
/// </summary>
/// <param name="path">Assembly path.</param>
public sealed class FileAssembly(string path) : Assembly(AssemblyDefinition.ReadAssembly(path))
{
    /// <summary>
    /// Assembly path.
    /// </summary>
    private readonly string m_Path = path;
    
    /// <inheritdoc cref="Assembly.ToReflectionAssembly()"/>
    public override System.Reflection.Assembly ToReflectionAssembly() => System.Reflection.Assembly.LoadFile(m_Path);
    
    /// <summary>
    /// Save assembly to path.
    /// </summary>
    protected override void OnDefaultSave() => Source.Write();
}