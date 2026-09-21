namespace Gneedle.Inject.Test;

/// <summary>
/// The files which a test writes to the temporary directory of the machine, which are held there until the run of the
/// test which holds one of them is over, and which no run removes for a run which comes after it unless it is asked of
/// it.
/// </summary>
[TestFixture]
public class TempFilesTests
{
    private const string PREFIX       = "gneedle-sweep-probe";
    private const string OTHER_PREFIX = "gneedle-sweep-other";
    private const string EXTENSION    = ".dll";
    private const string OTHER_SUFFIX = ".pdb";

    /// <summary>
    /// The name of a file is one which no other call names, which is what keeps a run from writing over the file of
    /// another run which is going at the same time.
    /// </summary>
    [Test]
    public void NewPath_Names_A_File_Which_No_Other_Call_Names()
    {
        var first = TempFiles.NewPath(PREFIX, EXTENSION);
        var second = TempFiles.NewPath(PREFIX, EXTENSION);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(Path.GetDirectoryName(first), Is.EqualTo(Path.GetDirectoryName(second)));
            Assert.That(Path.GetFileName(first), Does.StartWith(PREFIX).And.EndWith(EXTENSION));
            Assert.That(File.Exists(first), Is.False, "the name which was handed back is one of a file which is already written.");
            Assert.That(File.Exists(second), Is.False, "the name which was handed back is one of a file which is already written.");
        });
    }

    /// <summary>
    /// What a run before this one left is removed when the next file of that kind is named, which is what keeps the
    /// temporary directory of the machine from holding a file for every run which was ever made of a test.
    /// </summary>
    [Test]
    public void NewPath_Removes_The_File_Which_A_Run_Before_It_Left()
    {
        var left = Write(Path.GetTempPath(), PREFIX, EXTENSION);

        var path = TempFiles.NewPath(PREFIX, EXTENSION);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(left), Is.False, "the file which a run before this one left was not removed.");
            Assert.That(path, Is.Not.EqualTo(left));
        });
    }

    /// <summary>
    /// A file of another kind is not one which a run of this kind left, and a sweep which removed it would be removing
    /// the file of a test which is not this one.
    /// </summary>
    [Test]
    public void Sweep_Leaves_The_Files_Of_Another_Kind_Alone()
    {
        var otherName = Write(Path.GetTempPath(), OTHER_PREFIX, EXTENSION);
        var otherExtension = Write(Path.GetTempPath(), PREFIX, OTHER_SUFFIX);

        try
        {
            TempFiles.Sweep(PREFIX, EXTENSION);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(otherName), Is.True, "a file of another name was removed by the sweep.");
                Assert.That(File.Exists(otherExtension), Is.True, "a file of another extension was removed by the sweep.");
            });
        }
        finally
        {
            File.Delete(otherName);
            File.Delete(otherExtension);
        }
    }

    /// <summary>
    /// A file which a run holds is one which the system refuses to remove, and the sweep leaves it where it is rather
    /// than reporting it: a file which is held is one which a run that is not over is still to read.
    /// </summary>
    [Test]
    public void Sweep_Leaves_A_File_Which_Is_Held_Where_It_Is()
    {
        var path = Write(Path.GetTempPath(), PREFIX, EXTENSION);

        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.DoesNotThrow(() => TempFiles.Sweep(PREFIX, EXTENSION),
                "the sweep answered a file which a run holds with a report rather than with the file that it is.");

            // The file is read and written by the run which holds it, which a sweep of another run is not to disturb.
            held.WriteByte(0);
            held.Flush();
        }

        File.Delete(path);
    }

    private static string Write(string directory, string prefix, string extension)
    {
        var path = Path.Combine(directory, $"{prefix}-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, []);
        return path;
    }
}