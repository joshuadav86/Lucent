using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Lucent.Mac.Interop;
using SharpMetal.Metal;

namespace Lucent.Mac.Visuals;

/// <summary>
/// The part of the window the visuals draw into: a native macOS view backed by a Core Animation
/// Metal layer, hosted in the Avalonia layout. It starts drawing as soon as macOS creates it.
/// The view lets clicks through to the window beneath it, so they reach the Avalonia controls
/// around it (the visuals' clicks, double-clicks and menu are handled there).
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

    /// <summary>The NSView the visuals draw into, once macOS has made it.</summary>
    public IntPtr NativeView => _view;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return base.CreateNativeControlCore(parent);
        }

        _view = ObjC.Send(ObjC.Send(ViewClass(), "alloc"), "initWithFrame:", new ObjC.CGRect { Width = 100, Height = 100 });
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

    /// <summary>An NSView that's never the target of a click: hitTest: answers nil, so AppKit hands the click to the window's own view.</summary>
    private static unsafe IntPtr ViewClass() =>
        ObjC.Subclass("LucentVisualsView", "NSView", ("hitTest:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, ObjC.CGPoint, IntPtr>)&HitTest, "@@:{CGPoint=dd}"));

    [UnmanagedCallersOnly]
    private static IntPtr HitTest(IntPtr self, IntPtr selector, ObjC.CGPoint point) => IntPtr.Zero;

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

    /// <summary>
    /// Where the native view and its Metal layer are, against where the Avalonia layout put this
    /// control, as one line, and whether they agree (for the launch check in CI).
    /// </summary>
    internal (bool Matches, string Report) CheckGeometry()
    {
        if (_view == IntPtr.Zero || _layer == IntPtr.Zero)
        {
            return (false, "no native view yet");
        }

        var view = ObjC.ReadValue<ObjC.CGRect>(_view, "frame");
        var layer = ObjC.ReadValue<ObjC.CGRect>(_layer, "bounds");
        var drawable = ObjC.ReadValue<ObjC.CGSize>(_layer, "drawableSize");
        var superview = ObjC.Send(_view, "superview");
        var holder = superview == IntPtr.Zero ? default : ObjC.ReadValue<ObjC.CGRect>(superview, "bounds");
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        static bool Near(double a, double b) => Math.Abs(a - b) < 1.5;
        var matches = Near(view.Width, Bounds.Width) && Near(view.Height, Bounds.Height)
            && Near(layer.Width, Bounds.Width) && Near(layer.Height, Bounds.Height)
            && Near(drawable.Width, Bounds.Width * scale) && Near(drawable.Height, Bounds.Height * scale);
        return (matches,
            $"layout {Bounds.Width:0} x {Bounds.Height:0}, view {view.Width:0} x {view.Height:0} at {view.X:0},{view.Y:0}, " +
            $"layer {layer.Width:0} x {layer.Height:0}, holder {holder.Width:0} x {holder.Height:0}, " +
            $"drawable {drawable.Width:0} x {drawable.Height:0} (scale {scale:0.##})");
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
