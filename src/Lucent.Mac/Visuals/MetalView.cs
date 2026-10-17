using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Lucent.Mac.Interop;
using SharpMetal.Metal;

namespace Lucent.Mac.Visuals;

/// <summary>
/// The part of the window the visuals draw into: a native macOS view backed by a Core Animation
/// Metal layer, hosted in the Avalonia layout. It starts drawing as soon as macOS creates it.
/// </summary>
public sealed class MetalView : NativeControlHost
{
    private readonly VisualEngine _engine;
    private IntPtr _view;
    private IntPtr _layer;

    public MetalView(VisualEngine engine) => _engine = engine;

    /// <summary>Drawing the visuals, from when the view appears until it's taken down.</summary>
    public MetalRenderer? Renderer { get; private set; }

    /// <summary>Raised on the UI thread once the renderer exists, so the window can listen to it.</summary>
    public event EventHandler<MetalRenderer>? RendererStarted;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return base.CreateNativeControlCore(parent);
        }

        _view = ObjC.Send(ObjC.Send(ObjC.Class("NSView"), "alloc"), "initWithFrame:", new ObjC.CGRect { Width = 100, Height = 100 });
        _layer = ObjC.Send(ObjC.Send(ObjC.Class("CAMetalLayer"), "layer"), "retain");
        ObjC.SendVoid(_layer, "setDevice:", _engine.Device.NativePtr);
        ObjC.SendVoid(_layer, "setPixelFormat:", (ulong)MTLPixelFormat.BGRA8Unorm);
        ObjC.SendVoid(_layer, "setFramebufferOnly:", true);
        ObjC.SendVoid(_layer, "setOpaque:", true);
        // Follow the view's size; kCALayerWidthSizable | kCALayerHeightSizable.
        ObjC.SendVoid(_layer, "setAutoresizingMask:", 2UL | 16UL);
        ObjC.SendVoid(_view, "setLayer:", _layer);
        ObjC.SendVoid(_view, "setWantsLayer:", true);

        Renderer = new MetalRenderer(_layer, _engine);
        UpdateSize();
        Renderer.Start();
        RendererStarted?.Invoke(this, Renderer);
        return new PlatformHandle(_view, "NSView");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (!OperatingSystem.IsMacOS())
        {
            base.DestroyNativeControlCore(control);
            return;
        }

        Renderer?.Dispose();
        Renderer = null;
        ObjC.Release(_layer);
        ObjC.Release(_view);
        _layer = IntPtr.Zero;
        _view = IntPtr.Zero;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            UpdateSize();
        }
    }

    /// <summary>Tells the renderer the view's size in pixels, and the layer how sharp to be on Retina screens.</summary>
    private void UpdateSize()
    {
        if (Renderer is null || _layer == IntPtr.Zero)
        {
            return;
        }

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        ObjC.SendVoid(_layer, "setContentsScale:", scale);
        Renderer.SetSize((int)Math.Round(Bounds.Width * scale), (int)Math.Round(Bounds.Height * scale));
    }
}
