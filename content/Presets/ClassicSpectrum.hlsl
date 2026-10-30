// Name: Classic Spectrum
// A classic desktop player's little spectrum analyser blown up to fill the screen: chunky bars built from big pixels in the
// classic green-to-red colours, grey peak dots that hang at the top and then drop back down, and the faint
// dotted grid behind it all.
// Family: Music-shaped
// Tags: Retro, Colourful

// Winamp drew its analyser 76 pixels wide and 16 tall: 19 bars, each 3 pixels wide with a 1-pixel gap.
// Here each bar is still 3 "pixels" and a gap, with about 20 bars on a 16:9 screen (more on wider ones),
// and twice the rows, so each of Winamp's 16 colours covers two of them.
static const float Rows = 32.0;
static const float BarsPerAspect = 11.0;   // 16:9 gives 20 bars, 4:3 gives 15
static const int MaxBars = 40;

// The part of the screen the analyser fills (in uv), with a thin black border like the player's display.
static const float2 AreaMin = float2(0.03, 0.05);
static const float2 AreaMax = float2(0.97, 0.95);

// The dark seam between rows, as a fraction of a row, so the bars read as stacks of pixels.
static const float RowSeam = 0.14;

// How the peak dots fall: still for a moment, then faster and faster (in bar heights per second squared).
static const float PeakGravity = 1.3;
static const float MaxPeakSpeed = 4.0;

// Winamp's default viscolor.txt, colours 17 (bottom of the bars) up to 2 (top), then the peak dots
// (colour 23) and the background grid dots (colour 1), all 0..255 as the file wrote them.
static const float3 BarColours[16] =
{
    float3(24.0, 132.0, 8.0),
    float3(41.0, 148.0, 0.0),
    float3(49.0, 156.0, 8.0),
    float3(57.0, 181.0, 16.0),
    float3(50.0, 190.0, 16.0),
    float3(41.0, 206.0, 16.0),
    float3(148.0, 222.0, 33.0),
    float3(189.0, 222.0, 41.0),
    float3(214.0, 181.0, 33.0),
    float3(222.0, 165.0, 24.0),
    float3(198.0, 123.0, 8.0),
    float3(214.0, 115.0, 0.0),
    float3(214.0, 102.0, 0.0),
    float3(214.0, 90.0, 0.0),
    float3(206.0, 41.0, 16.0),
    float3(239.0, 49.0, 16.0)
};
static const float3 PeakColour = float3(150.0, 150.0, 150.0);
static const float3 GridColour = float3(24.0, 33.0, 41.0);

// The engine tone-maps and slightly saturates every visual on its way to the screen. This undoes both,
// so a 0..255 colour shows as Winamp's own rather than a punchier version of it. It aims a touch under full
// brightness so the brightest reds don't spill too much glow.
float3 asShown(float3 colour255)
{
    float3 colour = colour255 / 255.0 * 0.88;
    float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
    colour = saturate(lerp(luminance.xxx, colour, 1.0 / 1.15));
    // Solve shown = x(2.51x + 0.03) / (x(2.43x + 0.59) + 0.14) for x.
    float3 a = 2.43 * colour - 2.51;
    float3 b = 0.59 * colour - 0.03;
    float3 c = 0.14 * colour;
    return (-b - sqrt(max(b * b - 4.0 * a * c, 0.0))) / (2.0 * a);
}

float barCount()
{
    return clamp(floor(Aspect * BarsPerAspect + 0.5), 12.0, (float)MaxBars);
}

// How tall a bar is, 0..1. Each bar covers an equal slice of the spectrum (spectrum(x) is already spread
// evenly over the octaves) and, like Winamp's, shows the loudest frequency in its slice.
float barLevel(float bar, float bars)
{
    float level = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        level = max(level, spectrum((bar + (i + 0.5) / 4.0) / bars));
    }
    // Quiet hiss stays at the bottom and loud music reaches the top, as it did in Winamp.
    return saturate((level - 0.1) * 1.3);
}

// Memory: one pixel per bar along the top edge of the feedback buffer (in the black border), stored far too
// dim to see. Red holds the height of the bar's peak dot, green how fast it is falling.
static const float PeakStore = 0.004;

float3 encodePeak(float height, float speed)
{
    return float3(0.3 + 0.6 * saturate(float2(height, speed)), 0.5) * PeakStore;
}

// The peak dot's height and falling speed for a bar, or nothing at all before the first frame.
float2 loadPeak(float bar)
{
    float3 stored = previous(float2((bar + 0.5) / Resolution.x, 0.5 / Resolution.y)) / PeakStore;
    bool valid = all(stored > 0.27) && all(stored < 0.93) && abs(stored.b - 0.5) < 0.03;
    return valid ? (stored.rg - 0.3) / 0.6 : float2(0.0, 0.0);
}

// How much of a pixel at this point in its cell (0..1 across) is lit, leaving a seam of the given width.
// cellSize is the cell's size in screen pixels, for a crisp but smooth edge.
float cellCover(float inCell, float seam, float cellSize)
{
    return saturate((0.5 - seam * 0.5 - abs(inCell - 0.5)) * cellSize + 0.5);
}

float3 Render(float2 uv, float2 p)
{
    float bars = barCount();

    // ---- Memory pixels: move each bar's peak dot ----
    float2 pixel = floor(uv * Resolution);
    if (pixel.y < 0.5 && pixel.x < bars)
    {
        float level = barLevel(pixel.x, bars);
        float2 peak = loadPeak(pixel.x);
        float seconds = min(DeltaTime, 0.1);
        float speed = peak.y * MaxPeakSpeed + PeakGravity * seconds;
        float height = peak.x - speed * seconds;
        // A bar that reaches its peak dot pushes it up and holds it there.
        if (level >= height)
        {
            height = level;
            speed = 0.0;
        }
        return encodePeak(height, speed / MaxPeakSpeed);
    }

    // ---- The analyser ----
    float2 area = (uv - AreaMin) / (AreaMax - AreaMin);
    if (any(area < 0.0) || any(area >= 1.0))
    {
        return 0.0;
    }

    // The low-resolution grid Winamp drew on: 4 columns per bar (3 lit and a gap), rows counted up from the bottom.
    float columns = bars * 4.0;
    float2 grid = float2(area.x * columns, (1.0 - area.y) * Rows);
    float2 cell = floor(grid);
    float2 inCell = grid - cell;
    float2 cellSize = (AreaMax - AreaMin) * Resolution / float2(columns, Rows);

    float bar = floor(cell.x / 4.0);
    float barX = grid.x - bar * 4.0;   // 0..4 across this bar's columns
    float rowCover = cellCover(inCell.y, RowSeam, cellSize.y);

    // The bar: one solid block 3 columns wide, made of rows that each take the colour for their height.
    float barCover = saturate(min(barX, 3.0 - barX) * cellSize.x + 0.5) * rowCover;
    float litRows = floor(barLevel(bar, bars) * Rows + 0.5);
    float peakRows = floor(loadPeak(bar).x * Rows + 0.5);

    float3 colour = 0.0;
    if (cell.y < litRows)
    {
        int shade = clamp((int)(cell.y * 16.0 / Rows), 0, 15);
        colour = BarColours[shade];
    }
    // The peak dot sits on the highest row the bar has reached lately, over the bar when it's right there.
    if (peakRows >= 1.0 && cell.y == peakRows - 1.0)
    {
        colour = PeakColour;
    }
    if (any(colour > 0.0))
    {
        return asShown(colour) * barCover;
    }

    // The background: Winamp's faint dots on every other pixel of every other row.
    float isDot = fmod(cell.x, 2.0) < 0.5 && fmod(cell.y, 2.0) > 0.5 ? 1.0 : 0.0;
    float dotCover = cellCover(inCell.x, RowSeam, cellSize.x) * rowCover;
    return asShown(GridColour) * isDot * dotCover;
}
