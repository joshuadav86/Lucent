using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Lucent.Editor.Plugins;

/// <summary>Something went wrong with a plugin. The message is written for the user.</summary>
public sealed class PluginException : Exception
{
    public PluginException(string message) : base(message)
    {
    }
}

/// <summary>
/// One VST3 effect, running in its own helper process (LucentPluginHost.exe, next to Lucent.exe) so that
/// a plugin that crashes or hangs takes only the helper down. Lucent talks to it over the helper's
/// standard input and output; the commands are described in src/PluginHost/source/protocol.h.
/// </summary>
/// <remarks>
/// Every method waits for the helper, so call them off the UI thread (the plugin's window, in particular,
/// may need Lucent's window to answer while it opens). Calls from different threads take turns.
/// </remarks>
public sealed class PluginHost : IDisposable
{
    private const int MaxFramesPerRequest = 16384;

    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly Stream _toHost;
    private readonly Stream _fromHost;
    private readonly object _lock = new();
    private readonly byte[] _buffer = new byte[8];
    private int _generation;
    private bool _gone;

    private PluginHost(Process process, PluginInfo plugin, int sampleRate, int channels)
    {
        _process = process;
        _toHost = process.StandardInput.BaseStream;
        _fromHost = process.StandardOutput.BaseStream;
        Plugin = plugin;
        Name = plugin.Name;
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>Where the helper program is: next to Lucent.exe.</summary>
    public static string ExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "LucentPluginHost.exe" : "LucentPluginHost");

    /// <summary>False if the helper is missing (in a build made without it).</summary>
    public static bool IsAvailable => File.Exists(ExecutablePath);

    public PluginInfo Plugin { get; }

    /// <summary>The effect's name, as the plugin gives it.</summary>
    public string Name { get; private set; }

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>Whether the plugin has its own window of settings.</summary>
    public bool HasWindow { get; private set; }

    /// <summary>The helper's process id, so Lucent can let its window come to the front.</summary>
    public int ProcessId => _process.Id;

    /// <summary>True once the helper has closed, crashed or been stopped.</summary>
    public bool IsGone => _gone || HasExited();

    /// <summary>Starts a helper and loads the effect into it, ready to process audio of this format.</summary>
    public static PluginHost Open(PluginInfo plugin, int sampleRate, int channels)
    {
        if (!IsAvailable)
        {
            throw new PluginException("The plugin helper (LucentPluginHost.exe) is missing from Lucent's folder. Reinstalling Lucent puts it back.");
        }

        var process = StartHelper("--session");
        var host = new PluginHost(process, plugin, sampleRate, channels);
        try
        {
            var payload = new PayloadWriter();
            payload.WriteString(plugin.Path);
            payload.WriteString(plugin.ClassId);
            payload.Write(sampleRate);
            payload.Write(channels);
            var answer = host.Request(PluginCommand.Load, payload.ToArray(), LoadTimeout);
            var reader = new PayloadReader(answer);
            var name = reader.ReadString();
            reader.ReadInt32();
            reader.ReadInt64();
            host.HasWindow = reader.ReadByte() != 0;
            if (!string.IsNullOrWhiteSpace(name))
            {
                host.Name = name;
            }

            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the plugin ready to process from silence: in real time for previews, or offline (which lets
    /// some plugins use better quality) for rendering. Returns its delay and how long it rings on, in frames
    /// (-1 for endlessly), and a number that later <see cref="Process"/> calls can check against.
    /// </summary>
    public (int Latency, long Tail, int Generation) Start(bool offline)
    {
        var payload = new PayloadWriter();
        payload.Write(offline ? 1 : 0);
        lock (_lock)
        {
            var reader = new PayloadReader(Request(PluginCommand.Start, payload.ToArray(), CommandTimeout));
            var latency = reader.ReadInt32();
            var tail = reader.ReadInt64();
            _generation++;
            return (latency, tail, _generation);
        }
    }

    /// <summary>
    /// Processes <paramref name="frames"/> frames of planar audio, starting at <paramref name="offset"/>, in
    /// place. With a <paramref name="generation"/> from <see cref="Start"/>, does nothing and returns false if
    /// the plugin has been started again since (so an old preview can't run into a render).
    /// </summary>
    public bool Process(float[][] audio, int offset, int frames, int? generation = null)
    {
        if (audio.Length != Channels)
        {
            throw new ArgumentException("The audio has the wrong number of channels.", nameof(audio));
        }

        lock (_lock)
        {
            if (generation is { } expected && expected != _generation)
            {
                return false;
            }

            for (var done = 0; done < frames; done += MaxFramesPerRequest)
            {
                var count = Math.Min(MaxFramesPerRequest, frames - done);
                var bytesPerChannel = count * sizeof(float);
                var payload = new byte[sizeof(int) + bytesPerChannel * Channels];
                BitConverter.TryWriteBytes(payload, count);
                for (var c = 0; c < Channels; c++)
                {
                    MemoryMarshal.AsBytes(audio[c].AsSpan(offset + done, count))
                        .CopyTo(payload.AsSpan(sizeof(int) + c * bytesPerChannel));
                }

                var answer = Request(PluginCommand.Process, payload, CommandTimeout);
                if (answer.Length != bytesPerChannel * Channels)
                {
                    throw Gone("sent back the wrong amount of audio");
                }

                for (var c = 0; c < Channels; c++)
                {
                    MemoryMarshal.Cast<byte, float>(answer.AsSpan(c * bytesPerChannel, bytesPerChannel))
                        .CopyTo(audio[c].AsSpan(offset + done, count));
                }
            }

            return true;
        }
    }

    /// <summary>Opens the plugin's own window above <paramref name="owner"/>, or brings it to the front.</summary>
    public void ShowWindow(IntPtr owner, string title)
    {
        var payload = new PayloadWriter();
        payload.Write((long)owner);
        payload.WriteString(title);
        Request(PluginCommand.ShowEditor, payload.ToArray(), CommandTimeout);
    }

    public void HideWindow() => Request(PluginCommand.HideEditor, Array.Empty<byte>(), CommandTimeout);

    /// <summary>The plugin's settings, to give back to <see cref="SetSettings"/> another time.</summary>
    public byte[] GetSettings() => Request(PluginCommand.GetState, Array.Empty<byte>(), CommandTimeout);

    public void SetSettings(byte[] settings) => Request(PluginCommand.SetState, settings, CommandTimeout);

    /// <summary>Closes the plugin and its helper (forcing it if it doesn't close by itself).</summary>
    public void Dispose()
    {
        if (!_gone && !HasExited())
        {
            try
            {
                // Taking the lock waits for a call in progress to finish first.
                if (Monitor.TryEnter(_lock, TimeSpan.FromSeconds(5)))
                {
                    try
                    {
                        if (!_gone)
                        {
                            WriteRequest(PluginCommand.Quit, Array.Empty<byte>());
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                }

                _process.WaitForExit(3000);
            }
            catch (Exception)
            {
                // It's being stopped either way.
            }
        }

        _gone = true;
        Kill();
        _process.Dispose();
    }

    /// <summary>
    /// Lists the effects in one .vst3 file, in a helper of its own so a plugin that crashes while it's
    /// being looked at doesn't stop the search.
    /// </summary>
    public static async Task<ScanResult> ScanAsync(string path, CancellationToken cancellation)
    {
        using var process = StartHelper("--scan", path);
        process.StandardInput.Close();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(ScanTimeout);
            var text = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return ParseScan(text);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new ScanResult(Array.Empty<ScannedEffect>(), "It took too long to load, so it was skipped.");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // Already gone.
            }
        }
    }

    internal static ScanResult ParseScan(string text)
    {
        var effects = new List<ScannedEffect>();
        string? error = null;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('\t');
            if (fields[0] == "PLUGIN" && fields.Length >= 6)
            {
                effects.Add(new ScannedEffect(fields[1], fields[2], fields[3], fields[4], fields[5]));
            }
            else if (fields[0] == "ERROR")
            {
                error = fields.Length > 1 ? fields[1] : "It couldn't be loaded.";
            }
        }

        // Nothing at all usually means the helper crashed on it.
        if (effects.Count == 0 && error is null && !text.Contains("PLUGIN", StringComparison.Ordinal))
        {
            error = "It has no effects in it, or it crashed while loading.";
        }

        return new ScanResult(effects, error);
    }

    private static Process StartHelper(params string[] arguments)
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            return System.Diagnostics.Process.Start(info) ?? throw new PluginException("The plugin helper didn't start.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new PluginException($"The plugin helper didn't start ({ex.Message}).");
        }
    }

    // ---- Talking to the helper ----

    private byte[] Request(PluginCommand command, byte[] payload, TimeSpan timeout)
    {
        lock (_lock)
        {
            if (_gone)
            {
                throw new PluginException($"{Name} has stopped. Open it again from the Plugins menu.");
            }

            try
            {
                WriteRequest(command, payload);
                var deadline = DateTime.UtcNow + timeout;
                ReadExactly(_buffer, 8, deadline);
                var status = BitConverter.ToUInt32(_buffer, 0);
                var length = BitConverter.ToInt32(_buffer, 4);
                if (length < 0 || length > 256 << 20)
                {
                    throw Gone("sent something Lucent didn't understand");
                }

                var answer = new byte[length];
                ReadExactly(answer, length, deadline);
                if (status != 0)
                {
                    throw new PluginException(Encoding.UTF8.GetString(answer));
                }

                return answer;
            }
            catch (IOException)
            {
                throw Gone(null);
            }
            catch (ObjectDisposedException)
            {
                throw Gone(null);
            }
        }
    }

    private void WriteRequest(PluginCommand command, byte[] payload)
    {
        Span<byte> header = stackalloc byte[8];
        BitConverter.TryWriteBytes(header, (uint)command);
        BitConverter.TryWriteBytes(header[4..], payload.Length);
        _toHost.Write(header);
        _toHost.Write(payload);
        _toHost.Flush();
    }

    private void ReadExactly(byte[] target, int count, DateTime deadline)
    {
        var done = 0;
        while (done < count)
        {
            var wait = deadline - DateTime.UtcNow;
            var read = _fromHost.ReadAsync(target, done, count - done);
            if (wait <= TimeSpan.Zero || !read.Wait(wait))
            {
                // Stopping the helper ends the read too.
                _gone = true;
                Kill();
                throw new PluginException($"{Name} stopped responding, so Lucent closed it. Your sound wasn't changed.");
            }

            if (read.Result == 0)
            {
                throw Gone(null);
            }

            done += read.Result;
        }
    }

    private PluginException Gone(string? what)
    {
        _gone = true;
        Kill();
        return new PluginException(what is null
            ? $"{Name} crashed, so Lucent closed it. Your sound wasn't changed."
            : $"{Name} {what}, so Lucent closed it. Your sound wasn't changed.");
    }

    private bool HasExited()
    {
        try
        {
            return _process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }
        catch (Exception)
        {
            // Already gone.
        }
    }

    private enum PluginCommand : uint
    {
        Load = 1,
        Start = 2,
        Process = 3,
        ShowEditor = 4,
        HideEditor = 5,
        GetState = 6,
        SetState = 7,
        Quit = 8,
    }

    private sealed class PayloadWriter
    {
        private readonly MemoryStream _stream = new();
        private readonly BinaryWriter _writer;

        public PayloadWriter() => _writer = new BinaryWriter(_stream);

        public void Write(int value) => _writer.Write(value);

        public void Write(long value) => _writer.Write(value);

        public void WriteString(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            _writer.Write(bytes.Length);
            _writer.Write(bytes);
        }

        public byte[] ToArray()
        {
            _writer.Flush();
            return _stream.ToArray();
        }
    }

    private sealed class PayloadReader
    {
        private readonly BinaryReader _reader;

        public PayloadReader(byte[] bytes) => _reader = new BinaryReader(new MemoryStream(bytes));

        public int ReadInt32() => _reader.ReadInt32();

        public long ReadInt64() => _reader.ReadInt64();

        public byte ReadByte() => _reader.ReadByte();

        public string ReadString() => Encoding.UTF8.GetString(_reader.ReadBytes(_reader.ReadInt32()));
    }
}

/// <summary>One effect found in a .vst3 file.</summary>
public sealed record ScannedEffect(string ClassId, string Name, string Vendor, string Version, string Categories);

/// <summary>What looking inside a .vst3 file found: its effects, or why it couldn't be loaded.</summary>
public sealed record ScanResult(IReadOnlyList<ScannedEffect> Effects, string? Error);
