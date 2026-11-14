using System.Runtime.InteropServices;
using Avalonia.Threading;
using Lucent.Settings;

namespace Lucent.Mac.Audio;

/// <summary>
/// The global shortcuts set in Options > Sound, registered with macOS (Carbon's RegisterEventHotKey, which needs no
/// Accessibility permission) so they work while another app is in front. A shortcut another app or macOS already has
/// can't be registered: <see cref="Set"/> says which.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    private const uint EventClassKeyboard = 0x6B657962; // 'keyb'
    private const uint EventHotKeyPressed = 5;
    private const uint EventParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint TypeEventHotKeyId = 0x686B6964; // 'hkid'
    private const uint Signature = 0x4C636E74; // 'Lcnt'
    private const int EventNotHandled = -9874;

    private const uint CmdKey = 0x0100;
    private const uint ShiftKey = 0x0200;
    private const uint OptionKey = 0x0800;
    private const uint ControlKey = 0x1000;

    // The one registered with Carbon, which calls back without saying which object it was for.
    private static GlobalHotkeys? s_current;

    // Kept so the garbage collector doesn't take the callback away from Carbon.
    private static readonly EventHandler HandlerDelegate = OnHotKey;

    private readonly Dictionary<uint, (HotkeyAction Action, IntPtr Ref)> _registered = new();
    private IntPtr _handler;

    public GlobalHotkeys()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var types = new[] { new EventTypeSpec { EventClass = EventClassKeyboard, EventKind = EventHotKeyPressed } };
        if (InstallEventHandler(GetEventDispatcherTarget(), Marshal.GetFunctionPointerForDelegate(HandlerDelegate), 1, types,
                IntPtr.Zero, out _handler) == 0)
        {
            s_current = this;
        }
    }

    /// <summary>Raised on the UI thread when one of the shortcuts is pressed, wherever the focus is.</summary>
    public event Action<HotkeyAction>? Pressed;

    /// <summary>Registers these shortcuts in place of the ones before. Returns the actions whose keys another app already has.</summary>
    public IReadOnlyList<HotkeyAction> Set(IReadOnlyDictionary<HotkeyAction, Hotkey> shortcuts)
    {
        Clear();
        var taken = new List<HotkeyAction>();
        if (_handler == IntPtr.Zero)
        {
            return taken;
        }

        uint id = 1;
        foreach (var (action, hotkey) in shortcuts)
        {
            if (hotkey.Info is not { } key)
            {
                continue;
            }

            var modifiers = (hotkey.Modifiers.HasFlag(HotkeyModifiers.Command) ? CmdKey : 0)
                | (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift) ? ShiftKey : 0)
                | (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt) ? OptionKey : 0)
                | (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control) ? ControlKey : 0);
            var hotKeyId = new EventHotKeyId { Signature = Signature, Id = id };
            if (RegisterEventHotKey((uint)key.MacCode, modifiers, hotKeyId, GetEventDispatcherTarget(), 0, out var hotKeyRef) == 0)
            {
                _registered[id] = (action, hotKeyRef);
            }
            else
            {
                taken.Add(action);
            }

            id++;
        }

        return taken;
    }

    /// <summary>Lets go of every shortcut (while a new one is being typed in the options, say).</summary>
    public void Clear()
    {
        foreach (var (_, hotKeyRef) in _registered.Values)
        {
            UnregisterEventHotKey(hotKeyRef);
        }

        _registered.Clear();
    }

    public void Dispose()
    {
        Clear();
        if (_handler != IntPtr.Zero)
        {
            RemoveEventHandler(_handler);
            _handler = IntPtr.Zero;
        }

        if (s_current == this)
        {
            s_current = null;
        }
    }

    private static int OnHotKey(IntPtr next, IntPtr carbonEvent, IntPtr userData)
    {
        try
        {
            if (s_current is { } hotkeys
                && GetEventParameter(carbonEvent, EventParamDirectObject, TypeEventHotKeyId, IntPtr.Zero,
                    (uint)Marshal.SizeOf<EventHotKeyId>(), IntPtr.Zero, out var pressed) == 0
                && pressed.Signature == Signature && hotkeys._registered.TryGetValue(pressed.Id, out var registered))
            {
                Dispatcher.UIThread.Post(() => hotkeys.Pressed?.Invoke(registered.Action));
                return 0;
            }
        }
        catch (Exception)
        {
            // Nothing may escape into Carbon.
        }

        return EventNotHandled;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EventHandler(IntPtr next, IntPtr carbonEvent, IntPtr userData);

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [DllImport(Carbon)]
    private static extern IntPtr GetEventDispatcherTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(IntPtr target, IntPtr handler, uint typeCount, EventTypeSpec[] types,
        IntPtr userData, out IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options,
        out IntPtr hotKeyRef);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

    [DllImport(Carbon)]
    private static extern int GetEventParameter(IntPtr carbonEvent, uint name, uint desiredType, IntPtr actualType, uint bufferSize,
        IntPtr actualSize, out EventHotKeyId data);
}
