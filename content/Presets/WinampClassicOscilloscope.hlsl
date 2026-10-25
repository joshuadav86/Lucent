// Name: Winamp Classic Oscilloscope
// Winamp 2's little oscilloscope blown up to fill the screen: the waveform traced in big blocky pixels, green
// near the middle and turning yellow, orange and red as it swings out, over the faint dotted grid.
// Family: Music-shaped
// Tags: Retro, Colourful

// Winamp drew its scope 76 pixels wide and 16 tall. Here the pixels are square and there are 40 rows, which
// gives about 75 columns on a 16:9 screen, close to the original's.
static const float Rows = 40.0;

// The part of the screen the scope fills (in uv), with a thin black border like the player's display.
static const float2 AreaMin = float2(0.03, 0.05);
static const float2 AreaMax = float2(0.97, 0.95);

// The dark seam round each pixel, as a fraction of a pixel, so the trace reads as separate blocks.
static const float Seam = 0.14;

// Winamp's scope styles: 0 draws one dot per column, 1 joins each column to the next with a vertical line
// (Winamp's "lines" style, the one most people remember).
static const int ScopeStyle = 1;

// How much of the waveform is shown across the screen (1 = all ~46 ms of it). Winamp showed about 13 ms.
static const float WaveSpan = 0.3;
static const float Gain = 1.2;

// Which colours the trace uses: false for the analyser's green-to-red colours by distance from the middle,
// true for the stock skin's five scope colours (white in the middle fading to grey at the edges).
static const bool StockScopeColours = false;

// Winamp's default viscolor.txt, all 0..255 as the file wrote them: colours 17 (green) up to 2 (red),
// the five scope colours 18 to 22, and the background grid dots (colour 1).
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
static const float3 ScopeColours[5] =
{
    float3(255.0, 255.0, 255.0),
    float3(214.0, 214.0, 222.0),
    float3(181.0, 189.0, 189.0),
    float3(160.0, 170.0, 175.0),
    float3(148.0, 156.0, 165.0)
};
static const float3 GridColour = float3(24.0, 33.0, 41.0);

// The engine tone-maps and slightly saturates every visual on its way to the screen. This undoes both,
// so a 0..255 colour shows as Winamp's own rather than a punchier version of it. It aims a touch under full
// brightness so the brightest colours don't spill too much glow.
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

// The row the trace is on in a column, like Winamp: one sample per column, rounded to a whole row.
float traceRow(float column, float columns)
{
    float value = waveform((column + 0.5) / columns * WaveSpan + (1.0 - WaveSpan) * 0.5) * Gain;
    return clamp(floor(Rows * 0.5 + value * Rows * 0.5), 0.0, Rows - 1.0);
}

// How much of a pixel at this point in its cell (0..1 across) is lit, leaving a seam of the given width.
// cellSize is the cell's size in screen pixels, for a crisp but smooth edge.
float cellCover(float inCell, float seam, float cellSize)
{
    return saturate((0.5 - seam * 0.5 - abs(inCell - 0.5)) * cellSize + 0.5);
}

float3 Render(float2 uv, float2 p)
{
    float2 area = (uv - AreaMin) / (AreaMax - AreaMin);
    if (any(area < 0.0) || any(area >= 1.0))
    {
        return 0.0;
    }

    // The low-resolution grid Winamp drew on, with square pixels; rows counted up from the bottom.
    float2 areaPixels = (AreaMax - AreaMin) * Resolution;
    float columns = floor(Rows * areaPixels.x / areaPixels.y + 0.5);
    float2 grid = float2(area.x * columns, (1.0 - area.y) * Rows);
    float2 cell = floor(grid);
    float2 inCell = grid - cell;
    float2 cellSize = areaPixels / float2(columns, Rows);
    float cover = cellCover(inCell.x, Seam, cellSize.x) * cellCover(inCell.y, Seam, cellSize.y);

    // Is this pixel on the trace? In the lines style a column lights every row between where the trace
    // was in the column before and where it is now.
    float here = traceRow(cell.x, columns);
    float low = here;
    float high = here;
    if (ScopeStyle == 1 && cell.x > 0.5)
    {
        float before = traceRow(cell.x - 1.0, columns);
        low = min(here, before);
        high = max(here, before);
    }

    if (cell.y >= low && cell.y <= high)
    {
        // How far this pixel is from the middle, 0..1: the further out, the warmer the colour.
        float fromMiddle = saturate(abs(cell.y + 0.5 - Rows * 0.5) / (Rows * 0.5));
        float3 colour;
        if (StockScopeColours)
        {
            colour = ScopeColours[clamp((int)(fromMiddle * 5.0), 0, 4)];
        }
        else
        {
            colour = BarColours[clamp((int)(fromMiddle * 16.0), 0, 15)];
        }
        return asShown(colour) * cover;
    }

    // The background: Winamp's faint dots on every other pixel of every other row.
    float isDot = fmod(cell.x, 2.0) < 0.5 && fmod(cell.y, 2.0) > 0.5 ? 1.0 : 0.0;
    return asShown(GridColour) * isDot * cover;
}
