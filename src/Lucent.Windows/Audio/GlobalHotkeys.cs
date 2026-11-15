using System.Runtime.InteropServices;
using System.Windows.Interop;
using Lucent.Settings;

namespace Lucent.Audio;

/// <summary>
/// The global shortcuts set in Options > Sound, registered with Windows (RegisterHotKey) so they work while another
/// app is in front. A shortcut another app already has can't be registered: <see cref="Set"/> says which.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1;
    private const uint MOD_CONTROL = 0x2;
    private const uint MOD_SHIFT = 0x4;
    private const uint MOD_WIN = 0x8;
    private const uint MOD_NOREPEAT = 0x4000;

    // Well clear of the media keys' ids (their virtual-key codes, see MediaControls).
    private const int FirstId = 0xB100;

    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _registered = new();

    public GlobalHotkeys(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
    }

    /// <summary>Raised on the UI thread when one of the shortcuts is pressed, wherever the focus is.</summary>
    public event Action<HotkeyAction>? Pressed;

    /// <summary>Registers these shortcuts in place of the ones before. Returns the actions whose keys another app already has.</summary>
    public IReadOnlyList<HotkeyAction> Set(IReadOnlyDictionary<HotkeyAction, Hotkey> shortcuts)
    {
        Clear();
        var taken = new List<HotkeyAction>();
        var id = FirstId;
        foreach (var (action, hotkey) in shortcuts)
        {
            if (hotkey.Info is not { } key)
            {
                continue;
            }

            var modifiers = MOD_NOREPEAT
                | (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt) ? MOD_ALT : 0)
                | (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control) ? MOD_CONTROL : 0)
                | (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift) ? MOD_SHIFT : 0)
                | (hotkey.Modifiers.HasFlag(HotkeyModifiers.Command) ? MOD_WIN : 0);
            if (RegisterHotKey(_source.Handle, id, modifiers, (uint)key.WindowsCode))
            {
                _registered[id] = action;
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
        foreach (var id in _registered.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _registered.Clear();
    }

    public void Dispose()
    {
        Clear();
        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
