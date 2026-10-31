using System.Globalization;
using System.IO;
using System.Text;
using Lucent.Audio;
using Microsoft.Win32;

namespace Lucent.Audio;

/// <summary>
/// Shapes a streaming app's sound with the equalizer's curve through Equalizer APO (free and open source, installed
/// separately), since Windows has no way for Lucent to quieten another app and play its sound itself. While Lucent
/// controls an app, the curve goes into lucent.txt in Equalizer APO's settings folder, which its config.txt includes;
/// Equalizer APO picks the change up straight away. Once Lucent is back to its own music the file goes flat, so
/// Lucent's own music isn't equalized twice.
/// </summary>
/// <remarks>
/// Equalizer APO shapes everything playing on the outputs it was installed on, so a browser playing alongside the app
/// is shaped too. The Mac shapes just the app, through a muted tap (Audio/SystemAudio.cs).
/// </remarks>
public sealed class EqualizerApo : IDisposable
{
    /// <summary>Where people download Equalizer APO.</summary>
    public const string DownloadPage = "https://sourceforge.net/projects/equalizerapo/";

    private const string FileName = "lucent.txt";

    // Slider moves come many to the second; the file is written once they've settled for a moment.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

    private readonly Equalizer _equalizer;
    private readonly string _folder;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _disposed;

    private EqualizerApo(Equalizer equalizer, string folder)
    {
        _equalizer = equalizer;
        _folder = folder;
        _timer = new Timer(_ => Write());
        IncludeLucent(folder);
        Write();
        equalizer.Changed += OnChanged;
    }

    /// <summary>Equalizer APO's settings folder, or null when it isn't installed.</summary>
    public static string? ConfigFolder()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\EqualizerAPO");
                if (key?.GetValue("ConfigPath") is string folder && File.Exists(Path.Combine(folder, "config.txt")))
                {
                    return folder;
                }
            }
            catch (Exception)
            {
                // Not readable: try the usual place.
            }
        }

        var usual = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO", "config");
        return File.Exists(Path.Combine(usual, "config.txt")) ? usual : null;
    }

    /// <summary>
    /// Starts handing <paramref name="equalizer"/>'s curve to Equalizer APO, following every change, until disposed.
    /// Throws when Equalizer APO isn't installed or its settings can't be written.
    /// </summary>
    public static EqualizerApo Start(Equalizer equalizer)
    {
        var folder = ConfigFolder() ?? throw new InvalidOperationException("Equalizer APO isn't installed.");
        return new EqualizerApo(equalizer, folder);
    }

    /// <summary>
    /// Flattens a curve Lucent left behind (it closed while controlling an app, or crashed), so other apps don't stay
    /// equalized. Call as Lucent starts.
    /// </summary>
    public static void FlattenLeftover()
    {
        try
        {
            if (ConfigFolder() is { } folder && File.Exists(Path.Combine(folder, FileName)))
            {
                Save(folder, Flat);
            }
        }
        catch (Exception)
        {
            // Nothing Lucent can do about it now; the next start tries again.
        }
    }

    /// <summary>The file's lines for a curve: the preamp, the balance and a graphic EQ traced through the curve.</summary>
    public static string Describe(EqualizerDesign design, double balance)
    {
        if (design.IsBypass && balance == 0)
        {
            return Flat;
        }

        // Every sixth of an octave from 20 Hz to 20 kHz: fine enough to follow 32 bands.
        var points = Enumerable.Range(0, 61).Select(step => 20 * Math.Pow(2, step / 6.0)).ToArray();
        var gains = points.Select(hertz => design.IsBypass ? 0 : design.BandsResponseDb(hertz)).ToArray();
        // Equalizer APO has no limiter, so boosts are paid for up front, as Lucent's own headroom would.
        var preamp = (design.IsBypass ? 0 : design.PreampDb) - Math.Max(0, gains.Max());
        var text = new StringBuilder(Header);
        text.Append("Preamp: ").Append(Number(preamp)).AppendLine(" dB");
        text.Append("GraphicEQ: ")
            .AppendJoin("; ", points.Select((hertz, index) => $"{Number(hertz)} {Number(gains[index])}"))
            .AppendLine();
        if (balance != 0)
        {
            // The side it leans away from is turned down, as in Lucent's own playback.
            text.AppendLine("Channel: L").Append("Preamp: ").Append(Number(SideDb(1 - balance))).AppendLine(" dB");
            text.AppendLine("Channel: R").Append("Preamp: ").Append(Number(SideDb(1 + balance))).AppendLine(" dB");
            text.AppendLine("Channel: all");
        }

        return text.ToString();
    }

    private const string Header =
        "# Lucent's equalizer, for the streaming app it controls. Lucent rewrites this file, so changes here don't last.\r\n";

    private static string Flat => Header + "# Lucent isn't controlling an app, so this does nothing.\r\n";

    private static double SideDb(double gain) => 20 * Math.Log10(Math.Clamp(gain, 0.001, 1));

    // Two decimal places at most, and never "-0".
    private static string Number(double value)
    {
        var rounded = Math.Round(value, 2);
        return (rounded == 0 ? 0 : rounded).ToString("0.##", CultureInfo.InvariantCulture);
    }

    // config.txt has to include Lucent's file once. Added at the end, after anything people set up themselves.
    private static void IncludeLucent(string folder)
    {
        var config = Path.Combine(folder, "config.txt");
        var lines = File.ReadAllLines(config);
        if (lines.Any(line => line.Trim().Equals($"Include: {FileName}", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        File.AppendAllText(config, $"\r\n# Added by Lucent: its equalizer, while it controls a streaming app.\r\nInclude: {FileName}\r\n");
    }

    private void OnChanged(object? sender, EventArgs e) => _timer.Change(Settle, Timeout.InfiniteTimeSpan);

    private void Write()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                Save(_folder, Describe(_equalizer.Design, _equalizer.Balance));
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Equalizer APO", ex);
            }
        }
    }

    // Written whole and then swapped in, so Equalizer APO never reads half a file.
    private static void Save(string folder, string text)
    {
        var path = Path.Combine(folder, FileName);
        var temporary = path + ".new";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Stops following the equalizer and flattens the file, so other apps sound as they did.</summary>
    public void Dispose()
    {
        _equalizer.Changed -= OnChanged;
        _timer.Dispose();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                Save(_folder, Flat);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Equalizer APO", ex);
            }
        }
    }
}
