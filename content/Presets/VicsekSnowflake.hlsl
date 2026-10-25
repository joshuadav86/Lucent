// Name: Vicsek Snowflake
// An endless fall into a cross of ice built from five smaller crosses, each built from five smaller again. Crisp frosted outlines etch every level, glowing softly with the music as beats ripple inward.
// Family: Fractal
// Tags: Calm, Geometric
// Added: 2026-10-17 21:50

static const float VicsekPeriod = 9.0;   // seconds per threefold zoom

// Signed distance to a plus shape filling the square -1..1: a centre square and its four neighbours.
float vicsekPlus(float2 x)
{
    float2 a = abs(x);
    float horizontal = max(a.x - 1.0, a.y - 1.0 / 3.0);
    float vertical = max(a.x - 1.0 / 3.0, a.y - 1.0);
    return min(horizontal, vertical);
}

float3 vicsekIce(float t)
{
    // Ice hues only: pale cyan, glacier blue, a touch of violet.
    return hsv2rgb(0.53 + 0.1 * sin(2.0 * PI * t), 0.45 + 0.15 * sin(2.0 * PI * t * 0.5 + 1.0), 1.0);
}

float3 Render(float2 uv, float2 p)
{
    float u = frac(Time / VicsekPeriod);
    // Zoom threefold per period: the centre cross becomes the whole, so the dive never ends.
    // Begin four levels up so even the corners of the screen hold structure.
    float2 view = rotate(float2(p.x, -p.y), Time * 0.02 + 0.3 * sin(Time * 0.031));
    float size = 1.3 * 81.0 * exp2(-u * 1.5849625);   // half-width of the outermost square, in screen units
    float2 x = view / size;
    float px = pixelSize();
    float hue = Time * 0.01 + (BeatCount - Beat) * 0.02;

    float3 colour = 0.0;
    float generation = -4.0 - u;               // continuous level index relative to the screen
    float pulse = (1.0 - Beat) * 8.0 - 4.0;   // each beat ripples inward through the levels
    bool inside = true;
    float exitDistance = 0.0;
    float frost = 0.0;
    float3 lines = 0.0;
    int levels = qualitySteps(10) + 4;
    [loop]
    for (int level = 0; level < levels; level++)
    {
        float sizePx = size / px;              // half-width of this level's square, in pixels
        if (sizePx < 1.5)
        {
            break;
        }
        float d = vicsekPlus(x) * size;        // world distance to this level's cross
        float dpx = d / px;
        float visible = saturate((sizePx - 4.0) / 20.0);
        float prominence = exp(-abs(log2(size) + 0.5) * 0.45);   // the level nearest screen size is brightest
        float ripple = saturate(1.0 - abs(generation - pulse) * 0.6);
        float loud = spectrum(saturate(0.1 + (1.0 - prominence) * 0.6 + 0.05 * generation));

        // A crisp etched outline of this level's cross, with a soft frosty halo.
        float3 tint = vicsekIce(hue + generation * 0.07);
        float etch = saturate(1.2 - abs(dpx) * 0.9);
        float halo = exp(-abs(dpx) * (0.18 - Bass * 0.06));
        // Outlines shared by several levels take the brightest, rather than piling up.
        lines = max(lines, tint * visible * etch * (0.25 + 0.65 * prominence + 0.6 * ripple + 0.4 * loud));
        colour += tint * visible * halo * (0.03 + 0.05 * prominence + 0.08 * loud);
        frost = max(frost, etch * visible);

        if (d > 0.0)
        {
            inside = false;
            exitDistance = dpx;
            break;
        }
        // Step into whichever of the five squares holds this point.
        float2 cell = clamp(floor((x + 1.0) * 1.5), 0.0, 2.0);
        x = ((x + 1.0) * 1.5 - cell) * 2.0 - 1.0;
        size /= 3.0;
        generation += 1.0;
    }

    colour += lines;

    // Inside the finest crosses: packed ice, faintly lit.
    if (inside)
    {
        colour += vicsekIce(hue + 0.3) * (0.18 + 0.15 * Level);
    }
    else
    {
        // Out in the dark, frost thins with distance from the nearest cross.
        colour += vicsekIce(hue + 0.5) * 0.02 * exp(-exitDistance * 0.02);
    }

    // Glints of frost on the outlines, catching the treble.
    float glint = step(0.93, hash(floor(p * 0.4 / px) + floor(Time * 8.0)));
    colour += float3(0.85, 0.95, 1.0) * glint * frost * Treble * 1.2;

    // A deep winter-blue backdrop.
    colour += float3(0.004, 0.008, 0.02) * (1.2 - length(p) * 0.3);
    return colour + previous(uv) * 0.1;
}
