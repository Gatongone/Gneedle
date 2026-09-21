namespace Gneedle.Inject.Test;

// The image which the runtime mapped into memory is measured on the runtimes which read it back out of that memory: the
// framework reads the bytes of an image it loaded through a method of its own, and measures nothing.
#if !NETFRAMEWORK

/// <summary>
/// Tests for measuring the image which the runtime mapped into memory, which is read back through the section table of
/// its header.<para/>
/// Every field of that header is a value of the image itself, so a malformed image names offsets and lengths which the
/// memory it was mapped into does not hold: the read of one of those is a fault of the process which cannot be caught,
/// so each of them is held to the memory which is mapped before it is used, and the image is refused rather than read.
/// </summary>
[TestFixture]
public class MappedImageTests
{
    /// <summary>
    /// Offset of the field of the DOS header which holds the offset of the PE header.
    /// </summary>
    private const int PE_HEADER_OFFSET = 0x3C;

    /// <summary>
    /// The sizes of the header of an image: the signature, the file header, the optional header of a 32-bit image and of
    /// a 64-bit one, and one entry of the section table.
    /// </summary>
    private const int SIGNATURE_SIZE = 4;

    private const int FILE_HEADER_SIZE    = 20;
    private const int OPTIONAL_HEADER32   = 224;

    [Test]
    public void An_Image_Is_Measured_To_The_End_Of_Its_Last_Section()
    {
        var image = ReadImage();
        var header = ReadHeader(image);
        Assert.Multiple(() =>
        {
            Assert.That(CachedAssemblyResolver.TryMeasureImage(header, image.Length, out var fileSize), Is.True, "the header of the assembly of these tests was not read as an image.");
            Assert.That(fileSize, Is.GreaterThan(0));
            Assert.That(fileSize, Is.LessThanOrEqualTo(image.Length), "the length which was measured stands past the file which holds the image.");
        });
    }

    [Test]
    public void A_Header_Which_Is_No_Image_Is_Refused()
    {
        var header = new byte[0x1000];

        Assert.That(CachedAssemblyResolver.TryMeasureImage(header, header.Length, out _), Is.False,
            "a header which names no image was measured as one.");
    }

    [Test]
    public void An_Image_Whose_Pe_Header_Stands_Past_What_Was_Read_In_Is_Refused()
    {
        var header = ReadHeader(ReadImage());
        PutInt(header, PE_HEADER_OFFSET, header.Length);

        Assert.That(CachedAssemblyResolver.TryMeasureImage(header, header.Length, out _), Is.False,
            "an image whose PE header stands past the window which was read in was measured.");
    }

    [Test]
    public void An_Image_Which_Names_More_Sections_Than_A_Loader_Accepts_Is_Refused()
    {
        var header = ReadHeader(ReadImage());
        PutShort(header, FileHeaderOf(header) + 2, unchecked((short)0xFFFF));

        Assert.That(CachedAssemblyResolver.TryMeasureImage(header, header.Length, out _), Is.False,
            "an image which names more sections than a loader accepts was measured.");
    }

    [Test]
    public void An_Image_Whose_Optional_Header_Is_No_Size_An_Image_Declares_Is_Refused()
    {
        var header = ReadHeader(ReadImage());
        PutShort(header, FileHeaderOf(header) + 16, 0x100);

        Assert.That(CachedAssemblyResolver.TryMeasureImage(header, header.Length, out _), Is.False,
            "an image whose optional header is of a size no image declares was measured.");
    }

    [Test]
    public void A_Section_Table_Which_Stands_Past_What_Was_Read_In_Is_Refused()
    {
        var header = ReadHeader(ReadImage());
        var fileHeader = FileHeaderOf(header);
        PutShort(header, fileHeader + 2, 96);
        PutShort(header, fileHeader + 16, OPTIONAL_HEADER32);
        var sectionTable = fileHeader + FILE_HEADER_SIZE + OPTIONAL_HEADER32;

        // The window holds the headers alone, so the table which the header names stands past it.
        Assert.That(CachedAssemblyResolver.TryMeasureImage(header, sectionTable, out _), Is.False,
            "a section table which stands past the window which was read in was measured.");
    }

    [Test]
    public void A_Section_Which_Names_Raw_Data_Past_The_Memory_Is_Refused()
    {
        // The length which the image names is the one the copy of it is made with, so an image which names more than
        // the memory it was mapped into holds is the one whose read faults the process. The length here is one which
        // stands past that memory rather than one which is not a length at all.
        var header = ReadHeader(ReadImage());
        PutInt(header, FirstSectionOf(header) + 16, header.Length * 4);

        Assert.That(CachedAssemblyResolver.TryMeasureImage(header, header.Length, out _), Is.False,
            "a section which names raw data past the memory which was mapped was measured.");
    }

    [Test]
    public void A_Section_Which_Names_A_Length_Which_Is_Not_One_Is_Refused()
    {
        var header = ReadHeader(ReadImage());
        PutInt(header, FirstSectionOf(header) + 20, -1);

        Assert.That(CachedAssemblyResolver.TryMeasureImage(header, header.Length, out _), Is.False,
            "a section whose raw data stands at a length which is not one was measured.");
    }

    /// <summary>
    /// The bytes of the assembly of these tests, which is an image a compiler wrote.
    /// </summary>
    /// <returns>The bytes of it.</returns>
    private static byte[] ReadImage() => File.ReadAllBytes(typeof(MappedImageTests).Assembly.Location);

    /// <summary>
    /// The beginning of the given image, which is as much of it as is read into memory before it is measured.
    /// </summary>
    /// <param name="image">The bytes of the image.</param>
    /// <returns>The window which is read out of it.</returns>
    private static byte[] ReadHeader(byte[] image) => image[..0x4000];

    /// <summary>
    /// Offset of the file header of the image which the given header describes, which stands after the PE signature.
    /// </summary>
    /// <param name="header">The beginning of the image.</param>
    /// <returns>The offset of the file header.</returns>
    private static int FileHeaderOf(byte[] header) => BitConverter.ToInt32(header, PE_HEADER_OFFSET) + SIGNATURE_SIZE;

    /// <summary>
    /// Offset of the first entry of the section table of the image which the given header describes.
    /// </summary>
    /// <param name="header">The beginning of the image.</param>
    /// <returns>The offset of the entry.</returns>
    private static int FirstSectionOf(byte[] header)
    {
        var fileHeader = FileHeaderOf(header);
        var sizeOfOptionalHeader = BitConverter.ToInt16(header, fileHeader + 16);
        return fileHeader + FILE_HEADER_SIZE + sizeOfOptionalHeader;
    }

    /// <summary>
    /// Write an integer into the header at the offset, the way the image itself writes one.
    /// </summary>
    /// <param name="header">The beginning of the image.</param>
    /// <param name="offset">Offset of the field.</param>
    /// <param name="value">Value of it.</param>
    private static void PutInt(byte[] header, int offset, int value) => BitConverter.GetBytes(value).CopyTo(header, offset);

    /// <summary>
    /// Write a short into the header at the offset, the way the image itself writes one.
    /// </summary>
    /// <param name="header">The beginning of the image.</param>
    /// <param name="offset">Offset of the field.</param>
    /// <param name="value">Value of it.</param>
    private static void PutShort(byte[] header, int offset, short value) => BitConverter.GetBytes(value).CopyTo(header, offset);
}

#endif