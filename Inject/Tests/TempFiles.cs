namespace Gneedle.Inject.Test;

/// <summary>
/// The files which a test writes to the temporary directory of the machine for the length of the run of it.
/// </summary>
/// <remarks>
/// A file is named for the run which writes it rather than for the kind of file alone, so that two runs which go at
/// the same time write no file over the file of the other. What the runs before this one left is removed of itself:
/// the files of the kind are swept when the next one of them is named, because a test which removes its own file is
/// one whose file is left behind by a run which ended before it could, and the temporary directory of the machine is
/// not the place for what the tests of this suite left.
/// </remarks>
internal static class TempFiles
{
    /// <summary>
    /// Get the path of a file of the kind which <paramref name="prefix"/> and <paramref name="extension"/> name, which
    /// lies in the temporary directory of the machine and is not written yet.
    /// </summary>
    /// <remarks>
    /// The name which is handed back is one of a file which is not written yet, and no other call of this hands it
    /// back. The files of the kind which the runs before this one left are removed as the name is asked for, so a test
    /// which holds two files of one kind at the same time is a test which asks for the removal of the first of them.
    /// </remarks>
    /// <param name="prefix">Name which every file of the kind begins with.</param>
    /// <param name="extension">Extension which every file of the kind ends with.</param>
    /// <returns>The path of the file, which the caller writes.</returns>
    internal static string NewPath(string prefix, string extension)
    {
        Sweep(prefix, extension);
        return Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}{extension}");
    }

    /// <summary>
    /// Remove the files of the kind which <paramref name="prefix"/> and <paramref name="extension"/> name and which no
    /// run holds.
    /// </summary>
    /// <remarks>
    /// A file which an assembly was loaded from is held by the process which loaded it for as long as that process
    /// lives: the image of an assembly is mapped by the runtime, and a system which mapped an image refuses to remove
    /// the file of it. Such a file is left where it is rather than reported, because a run which is not over is one
    /// which the sweep of another run is not to fail, and because the sweep of the run which follows it is the one
    /// which removes it, the process of this one being gone by then.
    /// </remarks>
    /// <param name="prefix">Name which every file of the kind begins with.</param>
    /// <param name="extension">Extension which every file of the kind ends with.</param>
    internal static void Sweep(string prefix, string extension)
    {
        foreach (var path in Directory.EnumerateFiles(Path.GetTempPath(), $"{prefix}-*"))
        {
            // The name of a file ends with the extension of its kind alone, which the search of the file system does
            // not answer with on every system: a search for `.dll` answers a file of `.dllx` there as well.
            if (!Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The file is held by a run which is not over, or it is one which the caller may not remove. It is
                // left where it is, and the sweep of the run which follows is the one which removes it.
            }
        }
    }
}
