using System.Diagnostics;
using Lucent.Mac.Interop;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;

namespace Lucent.Mac.Visuals;

/// <summary>
/// Draws the visuals into a window's Core Animation Metal layer on its own thread, one frame per
/// screen refresh (the layer hands out a new picture to draw into each time the screen is ready).
/// </summary>
public sealed class MetalRenderer : IDisposable
{
    private readonly IntPtr _layer;
    private readonly VisualEngine _engine;
    private readonly Thread _thread;
    private volatile bool _running;
    private volatile int _pixelWidth;
    private volatile int _pixelHeight;
    private volatile bool _visible = true;
    private int _drawableWidth;
    private int _drawableHeight;

    public MetalRenderer(IntPtr layer, VisualEngine engine)
    {
        _layer = layer;
        _engine = engine;
        _thread = new Thread(Run) { IsBackground = true, Name = "Visuals renderer" };
    }

    /// <summary>Raised (on the render thread) if drawing stops because of an error.</summary>
    public event Action<string>? Failed;

    /// <summary>Raised (on the render thread) about once a second with the frame rate.</summary>
    public event Action<double>? FrameRateMeasured;

    /// <summary>The view's size in pixels (not points), from the UI thread.</summary>
    public void SetSize(int pixelWidth, int pixelHeight)
    {
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
    }

    /// <summary>False while the window is hidden or minimized: nothing is drawn then.</summary>
    public bool Visible
    {
        get => _visible;
        set => _visible = value;
    }

    public void Start()
    {
        _running = true;
        _thread.Start();
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        var lastTime = 0.0;
        var frames = 0;
        var frameRateStart = 0.0;
        try
        {
            _engine.StartCompiling();
            while (_running)
            {
                var now = clock.Elapsed.TotalSeconds;
                var deltaTime = (float)Math.Clamp(now - lastTime, 0.0, 0.1);
                lastTime = now;

                var width = _pixelWidth;
                var height = _pixelHeight;
                if (!_visible || width <= 1 || height <= 1)
                {
                    Thread.Sleep(50);
                    continue;
                }

                using var pool = AutoreleasePool.Begin();
                if (width != _drawableWidth || height != _drawableHeight)
                {
                    ObjC.SendVoid(_layer, "setDrawableSize:", new ObjC.CGSize { Width = width, Height = height });
                    _drawableWidth = width;
                    _drawableHeight = height;
                }

                // Waits for the screen to be ready for another frame, or returns nothing after a
                // second if it isn't (the window is off screen, say).
                var drawable = ObjC.Send(_layer, "nextDrawable");
                if (drawable == IntPtr.Zero)
                {
                    continue;
                }

                var commands = _engine.EncodeFrame(new CAMetalDrawable(drawable).Texture, now, deltaTime);
                commands.PresentDrawable(new MTLDrawable(drawable));
                commands.Commit();

                frames++;
                if (now - frameRateStart >= 1.0)
                {
                    FrameRateMeasured?.Invoke(frames / (now - frameRateStart));
                    frames = 0;
                    frameRateStart = now;
                }
            }
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex.Message);
        }
    }
}
