using System.Runtime.InteropServices;
using Lucent.Mac.Interop;

namespace Lucent.Mac.Streaming;

/// <summary>
/// What music apps announce to the whole Mac (distributed notifications): Spotify and Music each post one the moment a
/// song starts, plays or pauses, so Lucent reads them then rather than waiting for its next once-a-second read.
/// </summary>
internal static unsafe partial class AppAnnouncements
{
    /// <summary>Spotify's, as its song or play state changes.</summary>
    public const string Spotify = "com.spotify.client.PlaybackStateChanged";

    /// <summary>Music's, as its song or play state changes.</summary>
    public const string Music = "com.apple.Music.playerInfo";

    private const string Runtime = "/usr/lib/libobjc.A.dylib";

    private static readonly object Lock = new();
    private static readonly Dictionary<string, EventHandler?> Listeners = new();
    private static readonly HashSet<string> Observed = new();
    private static IntPtr _observer;

    /// <summary>Calls <paramref name="handler"/> (on the main thread) each time the announcement is posted.</summary>
    public static void Listen(string name, EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        lock (Lock)
        {
            Listeners[name] = Listeners.GetValueOrDefault(name) + handler;
            if (Observed.Add(name))
            {
                try
                {
                    Observe(name);
                }
                catch (Exception)
                {
                    // Not heard, then: the app is still read every second.
                }
            }
        }
    }

    public static void StopListening(string name, EventHandler? handler)
    {
        lock (Lock)
        {
            Listeners[name] = Listeners.GetValueOrDefault(name) - handler;
        }
    }

    // Asked once for each announcement, and kept for good: one small observer for the whole app.
    private static void Observe(string name)
    {
        if (_observer == IntPtr.Zero)
        {
            var type = WebKit.DefineClass("LucentAppAnnouncements",
                ("lucentAnnounced:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnAnnounced, "v@:@"));
            _observer = ObjC.Send(ObjC.Send(type, "alloc"), "init");
        }

        var center = ObjC.Send(ObjC.Class("NSDistributedNotificationCenter"), "defaultCenter");
        // -addObserver:selector:name:object:, the name copied by the center.
        void_objc_msgSend_IntPtr_IntPtr_IntPtr_IntPtr(center, ObjC.Selector("addObserver:selector:name:object:"),
            _observer, ObjC.Selector("lucentAnnounced:"), WebKit.String(name), IntPtr.Zero);
    }

    [UnmanagedCallersOnly]
    private static void OnAnnounced(IntPtr self, IntPtr selector, IntPtr notification)
    {
        try
        {
            var name = WebKit.ReadString(ObjC.Send(notification, "name"));
            EventHandler? listeners;
            lock (Lock)
            {
                listeners = Listeners.GetValueOrDefault(name);
            }

            listeners?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception)
        {
            // An exception mustn't cross back into Foundation.
        }
    }

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend_IntPtr_IntPtr_IntPtr_IntPtr(
        IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second, IntPtr third, IntPtr fourth);
}
