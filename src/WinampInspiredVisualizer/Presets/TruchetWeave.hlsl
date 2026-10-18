// Name: Truchet Weave
// Endless mirrored knotwork of fine neon threads woven over and under: tiles snap round on the beat to
// rewire the knots, spirals of light run along the strands, and the view falls forever through the weave.
// Family: Kaleidoscope
// Tags: Neon, Geometric, Retro
// Added: 2026-10-16 23:54

float twStroke(float d, float halfWidth, float px)
{
    float w = max(halfWidth, px * 0.5);
    return saturate((w - abs(d)) / px + 0.5) * (halfWidth / w);
}

float twHash(float2 p)
{
    p = frac(p * float2(233.34, 851.73));
    p += dot(p, p + 23.45);
    return frac(p.x * p.y);
}

// A ribbon of half-width w at distance d from its centreline, with flow parameter t along it: four fine
// parallel threads in a spread of hues over a dark band. Returns premultiplied colour and coverage.
float4 twRibbon(float d, float t, float w, float pxc, float hue, float flowPhase)
{
    float cover = saturate((w - abs(d)) / pxc + 0.5);
    float lanes = (d / w * 0.5 + 0.5) * 4.0;
    float lane = clamp(floor(lanes), 0.0, 3.0);
    float laneDistance = (lanes - lane - 0.5) * (w * 0.5);
    float thread = twStroke(laneDistance, max(w * 0.06, pxc * 0.5), pxc);
    float pulse = pow(0.5 + 0.5 * cos(2.0 * PI * (t - flowPhase - lane * 0.04)), 8.0);
    float3 threadColour = hsv2rgb(hue + lane * 0.06, 0.8, 1.0);
    float3 body = hsv2rgb(hue + 0.5, 0.7, 1.0) * 0.025 + threadColour * thread * (0.55 + pulse * 1.6);
    return float4(body * cover, cover);
}

// One layer of the weave in cell units. Returns premultiplied colour and coverage.
float4 twLayer(float2 q, float pxc, float seed, float hue, float flowPhase)
{
    float2 cell = floor(q);
    float2 f = q - cell;
    float w = 0.17;

    // Every tile turns a quarter every 32 beats, in two snaps on the beat, rewiring the knot (staggered
    // per tile). Both tile types look the same after half a turn, so counting quarter turns modulo two
    // keeps the motion seamless.
    float h = twHash(cell + seed * 17.31);
    float clock = (BeatCount - Beat + Time * 0.3) / 32.0 + h * 7.0;
    float quarter = wrap(floor(clock), 2.0) + smoothstep(0.9375, 1.0, frac(clock));
    float2 g = rotate(f - 0.5, quarter * PI * 0.5) + 0.5;
    float kind = twHash(cell * 1.31 + seed * 5.3);

    // Light flows outward through the weave as a spiral of pulses (a field over the whole layer, so it
    // never jumps when a tile turns).
    float t = length(q) * 0.22 + atan2(q.y, q.x) * 0.32;

    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    if (kind < 0.3)
    {
        // Crossing: one strand passes over the other, with a dark gap where it covers it.
        float dh = g.y - 0.5;
        float dv = g.x - 0.5;
        float4 under = twRibbon(dv, t, w, pxc, hue, flowPhase);
        float4 over = twRibbon(dh, t, w, pxc, hue, flowPhase);
        float gap = saturate((w * 1.45 - abs(dh)) / pxc + 0.5);
        result = under * (1.0 - gap);
        result = result * (1.0 - over.a) + over;
        result.a = max(result.a, gap * 0.85);
    }
    else
    {
        // Two quarter arcs joining neighbouring edge midpoints.
        float2 a = (kind < 0.65) ? g : float2(1.0 - g.x, g.y);
        float d0 = length(a) - 0.5;
        float d1 = length(a - 1.0) - 0.5;
        result = twRibbon((abs(d0) < abs(d1)) ? d0 : d1, t, w, pxc, hue, flowPhase);
    }
    return result;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // Mirror the plane into four quadrants so the weave forms symmetric knot mandalas.
    float2 m = abs(rotate(p, Time * 0.02));

    // Three layers at doubling sizes; the zoom carries each one forward into the next.
    float clock = Time * 0.05 + Bass * 0.02;
    float phase = frac(clock);
    float flowPhase = Time * 0.5 + (BeatCount - Beat) * 0.25;
    float3 colour = float3(0.006, 0.004, 0.012);

    [loop]
    for (int layer = 0; layer < 3; layer++)
    {
        float octave = layer + phase;                       // 0..3, small and far to large and near
        float cellSize = 0.11 * exp2(octave);
        float fadeIn = smoothstep(0.0, 1.0, octave);
        float fadeOut = 1.0 - smoothstep(2.2, 3.0, octave);
        float generation = floor(clock) - layer;
        // Each layer takes its own colour, kept to the cool-to-rose side of the wheel (no muddy greens).
        float hue = 0.665 + 0.215 * sin((generation * 0.21 + Time * 0.01) * 6.2831853);
        float pxc = px / cellSize;
        float4 weave = twLayer(m / cellSize, pxc, generation, hue, flowPhase);
        float alpha = weave.a * fadeIn * fadeOut;
        // Farther layers are dimmer, as if seen through depth.
        float depthShade = 0.35 + 0.65 * saturate(octave / 2.0);
        colour = colour * (1.0 - alpha) + weave.rgb * fadeIn * fadeOut * depthShade;
    }

    colour *= 0.8 + Level * 0.5;
    return colour + previous(uv) * 0.05;
}
