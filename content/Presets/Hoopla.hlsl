// Name: Hoopla
// Hoops of light in blue, magenta and orange, tiled at four sizes and laid over one another at a slant, so the
// big hoops are full of smaller ones. Every beat pumps a new hoop out of the middle of each tile, the big hoops
// thicken with the bass and the smallest with the treble, and the colours drift outwards from the centre.
// Family: Kaleidoscope
// Tags: Geometric, Colourful, Intense
// Added: 2026-11-16 00:04

// Blue, through magenta, to orange, and round again.
float3 hpColour(float t)
{
    float3 blue = float3(0.03, 0.22, 1.35);
    float3 magenta = float3(1.35, 0.03, 0.62);
    float3 orange = float3(1.45, 0.4, 0.02);
    float part = frac(t) * 3.0;
    float mix = smoothstep(0.0, 1.0, frac(part));
    if (part < 1.0) return lerp(blue, magenta, mix);
    if (part < 2.0) return lerp(magenta, orange, mix);
    return lerp(orange, blue, mix);
}

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float hpLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

float3 Render(float2 uv, float2 p)
{
    float far = length(p);
    float e = pixelSize();
    float3 colour = float3(0.03, 0.015, 0.11) * (1.3 - 0.5 * far);
    float2 x = p * (1.0 - 0.03 * BeatSwell - 0.1 * DropPulse);

    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float f = (float)k;
        float way = fmod(f, 2.0) < 0.5 ? 1.0 : -1.0;
        float heard = hpLoud(0.08 + 0.17 * f);
        float cell = 1.3 * pow(0.58, f);
        float2 g = rotate(x, f * 0.5236 + Time * 0.02 * way) / cell;
        float2 id = floor(g + 0.5);
        float d = length(g - id);                                   // 0 in the middle of a tile, 0.5 at its edge

        // Three hoops to a tile, all moving outwards by one place every two beats.
        float place = d * 6.0 - BeatGlide * 0.5;
        float toHoop = abs(frac(place) - 0.5) / 6.0 * cell;
        float thick = cell * (0.008 + 0.021 * heard);
        float core = smoothstep(thick + e, thick - e, toHoop);
        float halo = exp(-toHoop * toHoop / (thick * thick * 5.0 + 1e-5));
        float fade = smoothstep(0.52, 0.36, d) * smoothstep(0.0, 0.07, d);
        float3 tint = hpColour(far * 0.5 + f * 0.31 + hash(id + f * 17.0) * 0.14 - Time * 0.04);
        // Hoops lie over one another without adding up, so the picture never washes out.
        colour = max(colour, tint * core * fade * (0.86 - 0.08 * f) * (1.0 - 0.42 * heard));
        colour += tint * halo * fade * 0.07;
    }

    return min(colour, 1.9) * (0.92 + 0.08 * Level);
}
