namespace WinampInspiredVisualizer.ClassicSkins;

/// <summary>
/// One of a classic skin's windows (the equalizer, the playlist's frame) as a picture: both apps show its
/// <see cref="Pixels"/> blown up with hard edges, and pass the mouse in, in skin pixels.
/// </summary>
public interface IClassicSurface
{
    /// <summary><see cref="Width"/> × <see cref="Height"/> pixels, 0xAARRGGBB, row by row.</summary>
    uint[] Pixels { get; }

    /// <summary>In skin pixels.</summary>
    int Width { get; }

    int Height { get; }

    /// <summary>Draws the picture as things are now.</summary>
    void Render();

    /// <summary>A mouse button went down at (x, y). True when it's not on a control, so the window should be dragged.</summary>
    bool PointerDown(double x, double y, int clicks);

    void PointerMove(double x, double y);

    void PointerUp(double x, double y);
}
