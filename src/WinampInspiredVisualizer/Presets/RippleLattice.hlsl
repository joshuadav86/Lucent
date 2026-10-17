// Name: Ripple Lattice
// A fine net of light points laid flat like a woven cloth: every beat drops a pebble somewhere on it, and the
// rings spread, cross and interfere, rippling the threads and catching the light as they pass.
// Family: Geometric
// Tags: Calm, Minimal, Geometric

float2 rlHash2(float n)
{
    return frac(sin(float2(n * 12.9898, n * 78.233 + 3.1)) * 43758.5453);
}

// Height of the surface: rings from the last few beats, plus two gentle ever-running ripples.
float rlHeight(float2 x)
{
    float h = 0.0;
    float settle = 1.0 - saturate(Beat);
    [unroll]
    for (int j = 0; j < 6; j++)
    {
        float index = BeatCount - j;
        float2 source = (rlHash2(index) - 0.5) * float2(2.6, 1.5);
        float age = j + settle;
        float radius = age * 0.42;
        float r = length(x - source);
        float packet = exp(-pow((r - radius) / 0.3, 2.0));
        float strength = exp(-age * 0.45) * (0.6 + 0.4 * frac(index * 0.618)) * 1.3;
        h += strength * packet * cos((r - radius) * 17.0);
    }
    float2 a = float2(sin(Time * 0.07) * 1.4, cos(Time * 0.05) * 0.8);
    float2 b = float2(cos(Time * 0.06 + 2.0) * 1.3, sin(Time * 0.08 + 1.0) * 0.7);
    h += 0.18 * (0.4 + Mid) * sin(length(x - a) * 16.0 - Time * 1.4);
    h += 0.18 * (0.4 + Treble) * sin(length(x - b) * 19.0 - Time * 1.7);
    return h;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float2 x = rotate(p, 0.12 + 0.05 * sin(Time * 0.02));

    // Slope of the surface, for both the sideways push of the threads and their lighting.
    const float e = 0.003;
    float h = rlHeight(x);
    float hx = rlHeight(x + float2(e, 0.0));
    float hy = rlHeight(x + float2(0.0, e));
    float2 slope = float2(hx - h, hy - h) / e;

    float amount = 0.0014 * (0.7 + Bass * 0.8);
    float2 woven = x + slope * amount;

    // Fine square lattice: faint threads and brighter knots where they cross.
    const float spacing = 1.0 / 19.0;
    float2 cellPos = (frac(woven / spacing + 0.5) - 0.5) * spacing;
    float stretch = 1.0 + length(slope) * amount * 0.5;
    float aa = px * stretch;
    float threadX = saturate(1.0 - abs(cellPos.x) / aa);
    float threadY = saturate(1.0 - abs(cellPos.y) / aa);
    float threads = max(threadX, threadY);
    float knotRadius = px * 1.6;
    float knot = saturate((knotRadius - length(cellPos)) / aa + 0.5);
    float knotGlow = exp(-dot(cellPos, cellPos) / (px * px * 6.0));

    // Light from the upper left: slopes facing it shine, crests glow warm, troughs fall cool and dim.
    float2 lightDir = normalize(float2(-0.6, -0.8));
    float facing = dot(slope, lightDir) * 0.018;
    float crest = saturate(h * 0.9);
    float trough = saturate(-h * 0.9);
    float3 cool = float3(0.35, 0.55, 1.0);
    float3 warm = float3(1.0, 0.8, 0.55);
    float3 tint = lerp(cool, warm, crest);
    float light = 0.55 + facing + crest * 1.4 - trough * 0.25 + Level * 0.25;
    light = clamp(light, 0.05, 1.5);

    float vignette = 1.0 - 0.25 * dot(p, p);
    float3 colour = float3(0.006, 0.008, 0.016);
    colour += tint * threads * (0.1 + crest * 0.35) * light * vignette;
    colour += tint * (knot * 0.85 + knotGlow * 0.25) * light * vignette;
    return colour;
}
