// Copyright ©2023 Gatongone
// Author: Gatongone
// Email: gatongone@gmail.com
// Created On: 2024/01/19-21:35:57
// Github: https://github.com/Gatongone

namespace Gneedle.Psect;

[StructLayout(LayoutKind.Sequential)]
public struct PEFile
{
    public byte[]    Data;
    public DOSHeader DOSHeader;
    public NTHeader  NTHeader;
}

/// <summary>
/// IMAGE_DOS_HEADER
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DOSHeader
{
    public              ushort MZSignature;
    public              ushort UsedBytesInTheLastPage;
    public              ushort FileSizeInPages;
    public              ushort NumberOfRelocationItems;
    public              ushort HeaderSizeInParagraphs;
    public              ushort MinimumExtraParagraphs;
    public              ushort MaximumExtraParagraphs;
    public              ushort InitialRelativeSS;
    public              ushort InitialSP;
    public              ushort Checksum;
    public              ushort InitialIP;
    public              ushort InitialRelativeCS;
    public              ushort AddressOfRelocationTable;
    public              ushort OverlayNumber;
    public unsafe fixed ushort Reserved[4];
    public              ushort OEMid;
    public              ushort OEMinfo;
    public unsafe fixed ushort Reserved2[10];
    public              uint   AddressOfNewExeHeader;
}

/// <summary>
/// IMAGE_DOS_HEADER
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NTHeader
{
    uint           Signature;
    FileHeader     FileHeader;
    OptionalHeader OptionalHeader;
}

[StructLayout(LayoutKind.Sequential)]
public struct FileHeader
{
    public ushort Machine;
    public ushort NumberOfSections;
    public uint   TimeDateStamp;
    public uint   PointerToSymbolTable;
    public uint   NumberOfSymbols;
    public ushort SizeOfOptionalHeader;
    public ushort Characteristics;
}

[StructLayout(LayoutKind.Sequential)]
public struct OptionalHeader
{
    /// <summary>
    /// The state of the image file.
    /// </summary>
    public ushort Magic;

    /// <summary>
    /// The major version number of the linker.
    /// </summary>
    public byte MajorLinkerVersion;

    /// <summary>
    /// The minor version number of the linker.
    /// </summary>
    public byte MinorLinkerVersion;

    /// <summary>
    /// The size of the code section, in bytes, or the sum of all such sections if there are multiple code sections.
    /// </summary>
    public uint SizeOfCode;

    /// <summary>
    /// The size of the initialized data section, in bytes, or the sum of all such sections if there are multiple initialized data sections.
    /// </summary>
    public uint SizeOfInitializedData;

    /// <summary>
    /// The size of the uninitialized data section, in bytes, or the sum of all such sections if there are multiple uninitialized data sections.
    /// </summary>
    public uint SizeOfUninitializedData;

    /// <summary>
    /// A pointer to the entry point function, relative to the image base address. For executable files, this is the starting address.
    /// For device drivers, this is the address of the initialization function.
    /// The entry point function is optional for DLLs. When no entry point is present, this member is zero.
    /// </summary>
    public uint AddressOfEntryPoint;

    /// <summary>
    /// A pointer to the beginning of the code section, relative to the image base.
    /// </summary>
    public uint BaseOfCode;

    /// <summary>
    /// The preferred address of the first byte of the image when it is loaded in memory.
    /// This value is a multiple of 64K bytes.
    /// The default value for DLLs is 0x10000000.
    /// The default value for applications is 0x00400000, except on Windows CE where it is 0x00010000.
    /// </summary>
    public ulong ImageBase;

    /// <summary>
    /// The alignment of sections loaded in memory, in bytes. This value must be greater than or equal to the FileAlignment member. The default value is the page size for the system.
    /// </summary>
    public uint SectionAlignment;

    /// <summary>
    /// The alignment of the raw data of sections in the image file, in bytes.
    /// The value should be a power of 2 between 512 and 64K (inclusive).
    /// The default is 512. If the SectionAlignment member is less than the system page size, this member must be the same as <see cref="SectionAlignment"/>.
    /// </summary>
    public uint FileAlignment;

    /// <summary>
    /// The major version number of the required operating system.
    /// </summary>
    public ushort MajorOperatingSystemVersion;

    /// <summary>
    /// The minor version number of the required operating system.
    /// </summary>
    public ushort MinorOperatingSystemVersion;

    /// <summary>
    /// The major version number of the image.
    /// </summary>
    public ushort MajorImageVersion;

    /// <summary>
    /// The minor version number of the image.
    /// </summary>
    public ushort MinorImageVersion;

    /// <summary>
    /// The major version number of the subsystem.
    /// </summary>
    public ushort MajorSubsystemVersion;

    /// <summary>
    /// The minor version number of the subsystem.
    /// </summary>
    public ushort MinorSubsystemVersion;

    /// <summary>
    /// This member is reserved and must be 0.
    /// </summary>
    public uint Win32VersionValue;

    /// <summary>
    /// The size of the image, in bytes, including all headers. Must be a multiple of SectionAlignment.
    /// </summary>
    public uint SizeOfImage;

    /// <summary>
    /// The combined size of the following items, rounded to a multiple of the value specified in the FileAlignment member.
    /// <list type="bullet">
    /// <item>e_lfanew member of <see cref="DOSHeader">IMAGE_DOS_HEADER</see></item>
    /// <item>4 byte signature</item>
    /// <item>size of <see cref="FileHeader">IMAGE_FILE_HEADER</see></item>
    /// <item>size of <see cref="OptionalHeader">OPTIONAL_HEADER</see></item>
    /// <item>size of all section headers</item>
    /// </list>
    /// </summary>
    public uint SizeOfHeaders;

    /// <summary>
    /// The image file checksum. The following files are validated at load time: all drivers, any DLL loaded at boot time, and any DLL loaded into a critical system process.
    /// </summary>
    public uint CheckSum;

    /// <summary>
    /// he subsystem required to run this image. The following values are defined:
    /// <list type="bullet">
    /// <item><b>0</b>: Unknown subsystem.</item>
    /// <item><b>1</b>: No subsystem required (device drivers and native system processes).</item>
    /// <item><b>2</b>: Windows graphical user interface (GUI) subsystem.</item>
    /// <item><b>3</b>: Windows character-mode user interface (CUI) subsystem.</item>
    /// <item><b>5</b>: OS/2 CUI subsystem.</item>
    /// <item><b>7</b>: POSIX CUI subsystem.</item>
    /// <item><b>9</b>: Windows CE system.</item>
    /// <item><b>10</b>: Extensible Firmware Interface (EFI) application.</item>
    /// <item><b>11</b>: EFI driver with boot services.</item>
    /// <item><b>12</b>: EFI driver with run-time services.</item>
    /// <item><b>13</b>: EFI ROM image.</item>
    /// <item><b>14</b>: Xbox system.</item>
    /// <item><b>16</b>: Boot application.</item>
    /// </list>
    /// </summary>
    public ushort Subsystem;

    /// <summary>
    /// The DLL characteristics of the image. The following values are defined.
    /// </summary>
    public ushort DllCharacteristics;

    /// <summary>
    /// The number of bytes to reserve for the stack.
    /// Only the memory specified by the SizeOfStackCommit member is committed at load time;
    /// the rest is made available one page at a time until this reserve size is reached.
    /// </summary>
    public ulong SizeOfStackReserve;

    /// <summary>
    /// The number of bytes to commit for the stack.
    /// </summary>
    public ulong SizeOfStackCommit;

    /// <summary>
    /// The number of bytes to reserve for the local heap.
    /// Only the memory specified by the SizeOfHeapCommit member is committed at load time;
    /// the rest is made available one page at a time until this reserve size is reached.
    /// </summary>
    public ulong SizeOfHeapReserve;

    /// <summary>
    /// The number of bytes to commit for the local heap.
    /// </summary>
    public ulong SizeOfHeapCommit;

    /// <summary>
    /// This member is obsolete.
    /// </summary>
    public uint LoaderFlags;

    /// <summary>
    /// The number of directory entries in the remainder of the optional header. Each entry describes a location and size.
    /// </summary>
    public uint NumberOfRvaAndSizes;
}

/// <summary>
/// Represents the data directory.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DataDirectory
{
    /// <summary>
    /// The relative virtual address of the table.
    /// </summary>
    public uint VirtualAddress;

    /// <summary>
    /// The size of the table, in bytes.
    /// </summary>
    public uint Size;
}