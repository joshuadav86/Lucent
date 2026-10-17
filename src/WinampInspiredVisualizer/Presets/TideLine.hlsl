// Name: Tide Line
// Two quiet colours meet along one soft, curving shoreline. The tide ebbs and floods with the bass, the treble
// sends small ripples running along its edge, and faint wet lines mark where each wave last reached.
// Family: Minimal
// Tags: Minimal, Calm, Water

// The shoreline's height across the screen at time t: long slow swells plus a ripple from the treble.
float tlShore(float x, float t, float bass, float treble)
{
    float swell = sin(x * 1.3 + t * 0.21) * 0.07 + sin(x * 2.9 - t * 0.33 + 1.7) * 0.035 + sin(x * 0.6 + t * 0.07) * 0.08;
    float ebb = -0.05 + 0.24 * bass;
    float ripple = sin(x * 17.0 - t * 2.4) * 0.008 * treble + sin(x * 31.0 + t * 3.1) * 0.004 * treble;
    return ebb + swell + ripple;
}

// Two calm colour pairs that drift between dusk and morning over a few minutes.
void tlColours(float t, out float3 sea, out float3 sand)
{
    float m = 0.5 + 0.5 * sin(t * 0.012);
    sea = lerp(float3(0.025, 0.11, 0.17), float3(0.07, 0.05, 0.16), m);
    sand = lerp(float3(0.62, 0.46, 0.34), float3(0.72, 0.42, 0.40), m);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float3 sea;
    float3 sand;
    tlColours(Time, sea, sand);

    // The composition is tilted very slightly, so the shore runs a little downhill across the screen.
    float2 q = rotate(p, 0.32 + sin(Time * 0.02) * 0.04);
    // Positive y is towards the bottom of the screen, where the sand lies.
    float shore = tlShore(q.x, Time, Bass, Treble);
    float d = q.y - shore;

    // Wet lines: where the tide reached a little while ago, drawn as fading hairlines on the sand.
    float wet = 0.0;
    [unroll]
    for (int i = 1; i <= 3; i++)
    {
        float lag = i * 0.9;
        float past = tlShore(q.x, Time - lag, Bass * (0.85 - 0.1 * i) + 0.12 * i, 0.0) + 0.055 * i;
        float lineD = abs(q.y - past);
        wet += saturate(1.0 - lineD / (px * 1.4)) * (0.55 - i * 0.12) * step(0.0, q.y - shore);
    }

    // The two fields, joined by a soft gradient and a crisp bright foam edge.
    float softness = 0.012 + 0.01 * (1.0 - Level);
    float wetBand = smoothstep(0.0, 0.16, d);
    float3 sandShade = lerp(sand * 0.72, sand, wetBand);
    sandShade *= 1.0 - 0.06 * fbm(q * float2(4.0, 22.0));
    // Shallow water near the shore is lighter and greener than the deep water beyond.
    float3 shallow = lerp(sea, float3(0.18, 0.42, 0.45), 0.55);
    float3 seaShade = lerp(sea * 0.8, shallow, smoothstep(-0.5, 0.0, d) * smoothstep(-0.5, 0.0, d));
    // Gentle bands of light moving across the water, slower than the ripples.
    seaShade *= 1.0 + 0.12 * sin(q.y * 14.0 + q.x * 2.0 - Time * 0.8 + fbm(q * 3.0) * 3.0) * smoothstep(-0.9, -0.05, d) * smoothstep(0.0, -0.1, d);

    float3 colour = lerp(seaShade, sandShade, smoothstep(-softness, softness, d));
    // The foam line glows a little brighter on the beat.
    float foam = exp(-abs(d) / (px * 1.5 + 0.002)) * (0.55 + 0.4 * Beat + 0.25 * Treble);
    colour += float3(0.95, 0.93, 0.88) * foam * 0.6;
    colour += float3(0.95, 0.93, 0.88) * exp(-max(-d, 0.0) * 30.0) * step(d, 0.0) * 0.06;
    colour = lerp(colour, sand * 0.6, wet);

    // Soft vignette.
    colour *= 1.0 - 0.25 * smoothstep(0.6, 1.9, length(p));
    return colour;
}
