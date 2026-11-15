// Name: Fable Topography
// A chart of an archipelago, drawn as a mapmaker would: blue depths, green lowlands, brown hills and white summits,
// ringed with contour lines and shaded by a low sun. Every island is one band of the spectrum and rises out of the
// sea as that band gets loud, so the coastlines and contours are forever redrawing themselves to the music.
// Family: Music-shaped
// Tags: Colourful, Calm, Water
// Added: 2026-11-15 17:45

// The land is one height at every point: a rolling sea floor, plus a hill for every island. Everything drawn
// comes from that height: the tint from how high it is, the contour lines from where it crosses each level,
// and the shading from which way it slopes.
static const float Spacing = 0.78;             // from one island to the next
static const float Rise = 0.05;                // the height between one contour and the next

float foHeight(float2 x)
{
    float height = -0.36 + fbm(x * 1.5) * 0.36;
    float2 cell = floor(x / Spacing);
    for (int j = -1; j <= 1; j++)
    {
        for (int i = -1; i <= 1; i++)
        {
            float2 id = cell + float2(i, j);
            float2 random = float2(hash(id * 0.173 + 1.3), hash(id * 0.291 + 7.9));
            float2 centre = (id + 0.2 + 0.6 * random) * Spacing;
            float2 off = x - centre;
            // Each island listens to its own band, and some are broad and some steep.
            float loud = spectrum(0.02 + hash(id * 0.417 + 4.1) * 0.7);
            float broad = 7.0 + 9.0 * random.x;
            height += (0.13 + loud * 0.9 + DropPulse * 0.1) * exp(-dot(off, off) * broad);
        }
    }

    return height;
}

float3 foTint(float height)
{
    // Sea: deep to shallow.
    float3 sea = lerp(float3(0.015, 0.06, 0.22), float3(0.1, 0.42, 0.55), smoothstep(-0.32, 0.0, height));
    // Land: sand, green, olive, brown, grey, snow.
    float3 land = lerp(float3(0.7, 0.64, 0.44), float3(0.26, 0.48, 0.2), smoothstep(0.0, 0.05, height));
    land = lerp(land, float3(0.56, 0.55, 0.26), smoothstep(0.1, 0.27, height));
    land = lerp(land, float3(0.5, 0.33, 0.19), smoothstep(0.27, 0.45, height));
    land = lerp(land, float3(0.55, 0.53, 0.52), smoothstep(0.45, 0.62, height));
    land = lerp(land, float3(0.9, 0.9, 0.93), smoothstep(0.62, 0.8, height));
    return height < 0.0 ? sea : land;
}

float3 Render(float2 uv, float2 p)
{
    // The chart drifts by slowly, and the coasts are roughened so no island is a plain round hill.
    float2 x = rotate(p, 0.15) * (1.0 + 0.05 * sin(Time * 0.04)) + float2(Time * 0.014, Time * 0.006) + 40.0;
    x += (float2(fbm(x * 2.3 + 2.0), fbm(x * 2.3 + float2(7.7, 3.1))) - 0.47) * 0.22;

    float e = 0.006;
    float height = foHeight(x);
    float2 slope = (float2(foHeight(x + float2(e, 0.0)), foHeight(x + float2(0.0, e))) - height) / e;
    float steep = max(length(slope), 1e-4);
    float pixel = pixelSize();

    float3 colour = foTint(height);
    // Shading from a low sun in the top left.
    float shade = dot(slope, float2(0.55, 0.65));
    colour *= clamp(1.0 + shade * 0.32, 0.45, 1.5);
    // Waves of light crossing the open sea, with the treble.
    float sea = step(height, 0.0);
    colour += sea * float3(0.1, 0.25, 0.3) * noise(x * 22.0 + float2(Time * 0.5, Time * 0.3)) * (0.08 + Treble * 0.3);

    // Contour lines: one every level, a bolder one every fifth, and the coast boldest of all.
    float levels = height / Rise;
    float fine = abs(frac(levels + 0.5) - 0.5) * Rise / steep;
    float bold = abs(frac(levels / 5.0 + 0.5) - 0.5) * Rise * 5.0 / steep;
    float coast = abs(height) / steep;
    float ink = smoothstep(pixel * 1.4, pixel * 0.4, fine) * 0.4;
    ink = max(ink, smoothstep(pixel * 2.4, pixel * 1.0, bold) * 0.62);
    ink = max(ink, smoothstep(pixel * 3.2, pixel * 1.6, coast) * 0.85);
    // Each beat, a ring crosses the chart and the contours it passes light up.
    float ring = length(p) - BeatPhase * 2.3;
    float lit = exp(-ring * ring * 10.0) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);
    float3 inkColour = lerp(sea > 0.5 ? float3(0.0, 0.03, 0.12) : float3(0.16, 0.08, 0.03), float3(1.0, 0.85, 0.5), lit * 0.8);
    colour = lerp(colour, inkColour, ink);

    // The chart's grid, and the grain of its paper.
    float2 grid = abs(frac(x / 0.5 + 0.5) - 0.5) * 0.5;
    colour *= 1.0 - 0.14 * smoothstep(pixel * 1.3, pixel * 0.3, min(grid.x, grid.y));
    colour *= 0.94 + 0.12 * noise(p * 170.0);
    return colour * (0.74 + Intensity * 0.2 + BeatSwell * 0.04) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.9);
}
