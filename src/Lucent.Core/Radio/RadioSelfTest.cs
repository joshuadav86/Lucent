using System.Net;
using System.Net.Http;

namespace Lucent.Radio;

/// <summary>
/// For the self-test: plays tools/test-media/hls-fmp4 (a second of a 440 Hz tone, as an HLS stream in MP4 pieces, the
/// way SomaFM sends its 320k AAC) through Lucent's own radio player's HLS reader and AAC decoder, served from the folder
/// instead of the network, and checks it comes out as that tone, whole, with the song its event box names. The same
/// pieces are read on Windows and the Mac.
/// </summary>
public static class RadioSelfTest
{
    private const double Seconds = 1;
    private const double ToneHz = 440;
    private const string Title = "Lucent - Self-test";

    /// <summary>Checks the stream in <paramref name="folder"/>. Returns the number of problems.</summary>
    public static int Run(Action<string> log, string? folder)
    {
        if (folder is null)
        {
            return 0;
        }

        if (!File.Exists(Path.Combine(folder, "program.m3u8")))
        {
            log($"::error::No HLS test stream at {folder}.");
            return 1;
        }

        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            using var http = new HttpClient(new FolderHandler(folder));
            var address = new Uri("http://self-test.invalid/program.m3u8");
            var playlist = File.ReadAllText(Path.Combine(folder, "program.m3u8"));
            using var hls = HlsStream.OpenAsync(http, address, playlist, "Lucent self-test", CancellationToken.None).GetAwaiter().GetResult();
            using var decoder = RadioDecoder.Open(new IcyStream(hls, 0), hls.Codec);
            var (seconds, hz) = Measure(decoder);
            var line = $"Radio HLS in MP4 pieces: {hls.Codec}, {hls.Bitrate} kbps, {decoder.Format.SampleRate} Hz, {seconds:0.000} s, "
                + $"{hz:0} Hz, \"{hls.Title}\", in {timer.ElapsedMilliseconds} ms";
            // AAC's encoder adds a little priming, and the decoder's first frame is read only to check it: a frame either way.
            if (hls.Codec != RadioCodec.Aac || seconds < Seconds - 0.05 || seconds > Seconds + 0.1 || Math.Abs(hz - ToneHz) > ToneHz * 0.05
                || hls.Title != Title)
            {
                log($"::error::{line}: not the second of 440 Hz tone, titled \"{Title}\", it holds.");
                return 1;
            }

            log(line);
            return 0;
        }
        catch (Exception ex)
        {
            log($"::error::Radio HLS in MP4 pieces couldn't be played: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>How long the first channel lasts, and the tone's pitch from its zero crossings.</summary>
    private static (double Seconds, double Hz) Measure(RadioDecoder decoder)
    {
        var pcm = new byte[RadioDecoder.MaxFrameBytes];
        var channels = decoder.Format.Channels;
        long frames = 0, loud = 0, cycles = 0;
        var high = false;
        int bytes;
        while ((bytes = decoder.Next(pcm)) >= 0)
        {
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(pcm.AsSpan(0, bytes));
            for (var i = 0; i < samples.Length; i += channels)
            {
                var sample = samples[i];
                frames++;
                // Counted with hysteresis, so the sweep and codec noise near zero don't add crossings.
                if (!high && sample > 0.2f)
                {
                    high = true;
                    cycles++;
                }
                else if (high && sample < -0.2f)
                {
                    high = false;
                }

                if (Math.Abs(sample) > 0.2f)
                {
                    loud++;
                }
            }
        }

        var seconds = (double)frames / decoder.Format.SampleRate;
        var sounding = loud > 0 ? Math.Min(seconds, Seconds) : 0;
        return (seconds, sounding > 0 ? cycles / sounding : 0);
    }

    /// <summary>Answers the stream's requests from the folder, as a station's server would.</summary>
    private sealed class FolderHandler : HttpMessageHandler
    {
        private readonly string _folder;

        public FolderHandler(string folder) => _folder = folder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Path.Combine(_folder, Path.GetFileName(request.RequestUri!.AbsolutePath));
            var response = File.Exists(path)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(path)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
