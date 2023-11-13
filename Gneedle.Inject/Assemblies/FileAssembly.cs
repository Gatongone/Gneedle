/*
 * Copyright ©2023 Gatongone
 * Author: Gatongone
 * Email: gatongone@gmail.com
 * Created On: 2023/11/13-01:55:28
 * Github: https://github.com/Gatongone
 */

using System.Reflection;

namespace Gneedle.Inject;

public class FileAssembly : AssemblyWriter
{
    /// <summary>
    /// Assembly path.
    /// </summary>
    private readonly string m_Path;

    public FileAssembly(string path) : base(AssemblyDefinition.ReadAssembly(path)) => m_Path = path;

    public override Assembly ToReflectionAssembly() => Assembly.LoadFile(m_Path);

    protected override void OnDefaultSave() => Source.Write();
}