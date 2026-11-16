using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Mac.Visualizer;

namespace Lucent.Mac;

/// <summary>
/// The layout check's diary. The check writes its results only as it ends, so a run stopped for taking too long left
/// nothing to say where the time went. While the check runs, a thread of its own adds a line every five seconds to a
/// file beside the results (the results' path with ".diary" added): how long the check has been going, what it's
/// doing, the longest the window took to answer, which visual is on screen and how fast it's drawing, and how far the
/// visuals have got with compiling. tools/mac/launch-check.sh prints it after each run.
/// </summary>
internal static partial class LayoutCheck
{
    private const int DiaryEveryMilliseconds = 5000;

    private static readonly Stopwatch DiaryClock = new();
    private static volatile string _doing = "starting";
    private static volatile bool _diaryOpen;
    private static volatile VisualizerHost? _diaryHost;
    private static long _answeredAt = -1;
    private static long _longestWait;

    /// <summary>What the check is doing now, for the diary's next line.</summary>
    private static void Doing(string what) => _doing = what;

    /// <summary>How long the check has been going, and the longest the window took to answer, as a line for the results.</summary>
    private static string DiarySummary() =>
        string.Create(CultureInfo.InvariantCulture,
            $"the check took {DiaryClock.Elapsed.TotalSeconds:0} s, and the longest the window took to answer was {Interlocked.Read(ref _longestWait) / 1000.0:0.0} s");

    private static void StartDiary(Window main, Window visuals, string file)
    {
        DiaryClock.Restart();
        _diaryOpen = true;
        var thread = new Thread(() => KeepDiary(main, visuals, file + ".diary")) { IsBackground = true, Name = "Layout check diary" };
        thread.Start();
    }

    private static void EndDiary() => _diaryOpen = false;

    private static void KeepDiary(Window main, Window visuals, string file)
    {
        long askedAt = -1;
        long worst = 0;
        long lastLine = 0;
        long lastFrames = 0;
        while (_diaryOpen)
        {
            Thread.Sleep(250);
            var now = DiaryClock.ElapsedMilliseconds;

            // How long the window takes to answer: a note posted to it, and the time until it's read.
            if (askedAt < 0)
            {
                askedAt = now;
                Dispatcher.UIThread.Post(() =>
                {
                    _diaryHost ??= main.GetVisualDescendants().OfType<VisualizerHost>().FirstOrDefault()
                        ?? visuals.GetVisualDescendants().OfType<VisualizerHost>().FirstOrDefault();
                    Interlocked.Exchange(ref _answeredAt, DiaryClock.ElapsedMilliseconds);
                });
            }
            else
            {
                var answered = Interlocked.Read(ref _answeredAt);
                var wait = answered >= askedAt ? answered - askedAt : now - askedAt;
                worst = Math.Max(worst, wait);
                if (wait > Interlocked.Read(ref _longestWait))
                {
                    Interlocked.Exchange(ref _longestWait, wait);
                }

                if (answered >= askedAt)
                {
                    askedAt = -1;
                }
            }

            if (now - lastLine < DiaryEveryMilliseconds)
            {
                continue;
            }

            var line = string.Create(CultureInfo.InvariantCulture,
                $"{now / 1000.0,5:0} s: {_doing}; the window took up to {worst / 1000.0:0.0} s to answer");
            try
            {
                if (_diaryHost?.EngineProgress() is { } engine)
                {
                    var frameRate = (engine.Frames - lastFrames) * 1000.0 / Math.Max(1, now - lastLine);
                    lastFrames = engine.Frames;
                    line += string.Create(CultureInfo.InvariantCulture,
                        $"; showing {engine.Showing} at {frameRate:0.#} frames a second; {engine.Compiled} of {engine.Visuals} visuals compiled") +
                        (engine.Compilers > 0 ? $", {engine.Compilers} compilers on {engine.Compiling}" : string.Empty);
                }

                File.AppendAllText(file, line + "\n");
            }
            catch (Exception)
            {
                // The diary is only for reading afterwards: a line that can't be written is left out.
            }

            lastLine = now;
            worst = 0;
        }
    }
}
