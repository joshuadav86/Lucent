// Name: Crystal Kaleidoscope
// Six-way crystal filigree: nested rings and facets drawn in fine glowing lines that fold
// into each other. The bass reshapes the crystal, the mids swell the rings, beats pulse it.

float3 Render(float2 uv, float2 p)
{
    // About one screen pixel, so lines stay razor sharp at any window size and resolution setting.
    float px = max(pixelSize(), 0.0019);
    float zoom = 1.25 - Beat * 0.05;
    float2 z = kaleidoscope(rotate(p, Time * 0.03), 6.0) * zoom;
    float2 c = float2(0.78 + 0.08 * sin(Time * 0.071), 0.52 + 0.08 * cos(Time * 0.053)) + Bass * 0.04;
    float ringRadius = 0.6 + Mid * 0.08;

    float stretch = zoom;  // how much the folds have magnified this spot so far
    float3 lines = 0.0;
    float3 haze = 0.0;
    [loop]
    for (int i = 0; i < 9; i++)
    {
        float r2 = max(dot(z, z), 1e-4);
        z = abs(z) / r2 - c;
        stretch /= r2;
        float level = i / 8.0;
        float3 hue = palette(0.55 + level * 0.55 + Time * 0.02);

        // Distances measured back on screen, so every line is about a pixel wide.
        float toLine = abs(z.y) / stretch;
        float toRing = abs(length(z) - ringRadius) / stretch;

        // Folds whose detail is finer than a pixel would only shimmer, so they fade into a haze.
        float resolvable = exp(-px * stretch * 14.0);
        float weight = (1.0 - level * 0.7) * (1.0 - level * 0.7) * resolvable;
        lines += hue * weight * (exp(-toLine / px) * 0.7 + exp(-toRing / (px * 1.3)) * 0.9);
        haze += hue * (1.0 - resolvable) * 0.012;
    }

    float3 colour = lines * (0.7 + Level * 1.1 + Beat * 0.7) + haze;
    return colour + previous(toUv(p * 0.985)) * 0.2;
}
