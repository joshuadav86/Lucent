// Name: Sacred Geometry
// Nested polygons and a flower of circles, each spinning its own way and
// breathing with a different part of the spectrum, echoing back into a tunnel.
// Family: Kaleidoscope
// Tags: Neon, Geometric
// Added: 2026-10-16 20:19

float3 Render(float2 uv, float2 p)
{
    float3 colour = 0.0;

    [loop]
    for (int layer = 0; layer < 8; layer++)
    {
        float index = layer;
        float sides = 3.0 + wrap(index, 4.0);
        float level = spectrum(index / 8.0);
        float direction = (layer % 2 == 0) ? 1.0 : -1.0;
        float2 q = rotate(p, direction * Time * (0.08 + index * 0.025) + Beat * 0.1 * direction);
        float radius = 0.12 + index * 0.11 + level * 0.08;
        float outline = glow(polygon(q, sides, radius), 420.0);
        colour += palette(index * 0.12 + Time * 0.04) * outline * (0.35 + level * 1.2);
    }

    // Flower of life: six circles around a centre one.
    float2 flowerSpace = rotate(p, -Time * 0.05);
    float petalRadius = 0.22 + Bass * 0.06;
    float petals = glow(length(flowerSpace) - petalRadius, 500.0);
    [unroll]
    for (int k = 0; k < 6; k++)
    {
        float angle = k * (PI / 3.0);
        float2 centre = float2(cos(angle), sin(angle)) * petalRadius;
        petals += glow(length(flowerSpace - centre) - petalRadius, 500.0);
    }
    colour += neon(Time * 0.05) * petals * (0.25 + Mid);

    // Echo the last frame slightly smaller, so the pattern recedes into a tunnel.
    float3 echo = previous(toUv(rotate(p, 0.01) * 1.04)) * 0.82;
    return colour + echo;
}
