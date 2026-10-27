using Lucent.Mac.Interop;

namespace Lucent.Mac;

/// <summary>Moves files to the Trash, so a removed duplicate can still be put back.</summary>
public static class Trash
{
    /// <summary>The Trash, as the duplicates window names it.</summary>
    public const string Name = "Trash";

    /// <summary>Moves each file to the Trash, one at a time. Returns the ones that went.</summary>
    public static List<string> MoveToTrash(IEnumerable<string> paths)
    {
        var moved = new List<string>();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            using var pool = AutoreleasePool.Begin();

            // [[NSFileManager defaultManager] trashItemAtURL:url resultingItemURL:nil error:nil]
            var url = ObjC.Send(ObjC.Class("NSURL"), "fileURLWithPath:", StringHelper.Create(path).NativePtr);
            var manager = ObjC.Send(ObjC.Class("NSFileManager"), "defaultManager");
            if (ObjC.SendBool(manager, "trashItemAtURL:resultingItemURL:error:", url, IntPtr.Zero, IntPtr.Zero) && !File.Exists(path))
            {
                moved.Add(path);
            }
        }

        return moved;
    }
}
