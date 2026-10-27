using System.IO;
using System.Runtime.InteropServices;

namespace Lucent;

/// <summary>Moves files to the Recycle Bin, so a removed duplicate can still be put back.</summary>
public static class Trash
{
    /// <summary>The Recycle Bin, as the duplicates window names it.</summary>
    public const string Name = "Recycle Bin";

    /// <summary>Moves each file to the Recycle Bin, one at a time. Returns the ones that went.</summary>
    public static List<string> MoveToTrash(IEnumerable<string> paths)
    {
        var moved = new List<string>();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var operation = new ShFileOperation
            {
                Function = FoDelete,
                From = path + "\0\0",
                // Windows asks first if a file can't be recycled (on a network drive, say) and would be deleted for good.
                Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning,
            };

            if (SHFileOperation(ref operation) == 0 && !operation.AnyOperationsAborted && !File.Exists(path))
            {
                moved.Add(path);
            }
        }

        return moved;
    }

    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x4;
    private const ushort FofNoConfirmation = 0x10;
    private const ushort FofAllowUndo = 0x40;
    private const ushort FofNoErrorUi = 0x400;
    private const ushort FofWantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOperation operation);
}
