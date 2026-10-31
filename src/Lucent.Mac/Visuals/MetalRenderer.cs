using System.Diagnostics;
using Lucent.Mac.Interop;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;
using Lucent.Visualizer;

namespace Lucent.Mac.Visuals;

/// <summary>
/// Draws the visuals into a window's Core Animation Metal layer on its own thread, one frame per
/// screen refresh (the layer hands out a new picture to draw into each time the screen is ready), or
/// at about 60 fps on a faster screen with <see cref="FrameRateCap"/> on.
/// With HDR wanted and a screen that can show it, the layer switches to half-float pictures in
/// extended linear sRGB (EDR), the Mac's match for the scRGB swap chain the Windows renderer uses.
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
    private volatile bool _covered;
    private volatile bool _hdrWanted;
    private int _drawableWidth;
    private int _drawableHeight;
    private bool _hdrActive;

    public MetalRenderer(IntPtr layer, VisualEngine engine)
    {
        _layer = layer;
        _engine = engine;
        _thread = new Thread(Run) { IsBackground = true, Name = "Visuals renderer" };
    }

    /// <summary>Raised (on the render thread) if drawing stops because of an error.</summary>
    public event Action<string>? Failed;

    /// <summary>Raised (on the render thread) about once a second with the frame rate, drawing size and whether HDR is on.</summary>
    public event Action<FrameStats>? FrameRateMeasured;

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

    /// <summary>
    /// True while macOS says none of the window can be seen (fully covered by other windows, or on
    /// another Space): nothing is drawn then either, as there's nobody to see it.
    /// </summary>
    public bool Covered
    {
        get => _covered;
        set => _covered = value;
    }

    /// <summary>Draw in HDR: set when HDR is switched on in Lucent and the screen can show it.</summary>
    public bool Hdr
    {
        get => _hdrWanted;
        set => _hdrWanted = value;
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
        var keptUp = false;
        try
        {
            _engine.StartCompiling();
            while (_running)
            {
                Lucent.Mac.Visualizer.BackgroundFrameRate.KeepUp(ref keptUp);
                var now = clock.Elapsed.TotalSeconds;
                var deltaTime = (float)Math.Clamp(now - lastTime, 0.0, 0.1);
                lastTime = now;

                var width = _pixelWidth;
                var height = _pixelHeight;
                if (!_visible || _covered || width <= 1 || height <= 1)
                {
                    // Hidden, minimized or covered (the visuals window can be closed while music plays): nothing to draw.
                    Thread.Sleep(50);
                    continue;
                }

                using var pool = AutoreleasePool.Begin();
                if (_hdrWanted != _hdrActive)
                {
                    UseHdr(_hdrWanted);
                }

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
                // With the 60 fps cap on a faster screen, each frame stays up for whole refreshes (two
                // at 120 Hz), and nextDrawable waits for one to come free, so the loop runs half as often.
                var minimum = FrameRateCap.MinimumFrameSeconds(_engine.Display.RefreshRate);
                if (minimum > 0)
                {
                    ObjC.SendVoid(commands.NativePtr, "presentDrawable:afterMinimumDuration:", drawable, minimum);
                }
                else
                {
                    commands.PresentDrawable(new MTLDrawable(drawable));
                }

                commands.Commit();
                _engine.FramePresented(now);

                frames++;
                if (now - frameRateStart >= 1.0)
                {
                    FrameRateMeasured?.Invoke(new FrameStats(frames / (now - frameRateStart), _engine.RenderWidth, _engine.RenderHeight, _engine.HdrActive));
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

    /// <summary>
    /// Switches the layer between standard range (8-bit, as the screen takes it) and HDR (half-float
    /// pictures in linear light, where 1.0 is normal white and macOS shows brighter values brighter).
    /// </summary>
    private void UseHdr(bool hdr)
    {
        if (hdr)
        {
            ObjC.SendVoid(_layer, "setPixelFormat:", (ulong)MTLPixelFormat.RGBA16Float);
            ObjC.SendVoid(_layer, "setColorspace:", CoreGraphics.ExtendedLinearSrgb);
            ObjC.SendVoid(_layer, "setWantsExtendedDynamicRangeContent:", true);
        }
        else
        {
            ObjC.SendVoid(_layer, "setWantsExtendedDynamicRangeContent:", false);
            ObjC.SendVoid(_layer, "setColorspace:", IntPtr.Zero);
            ObjC.SendVoid(_layer, "setPixelFormat:", (ulong)MTLPixelFormat.BGRA8Unorm);
        }

        _hdrActive = hdr;
    }
}
