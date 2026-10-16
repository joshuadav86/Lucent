// Name: Aurora Veil
// Ribbons of northern lights that sway with the mids and shimmer with the treble.

float3 Render(float2 uv, float2 p)
{
    // Old light drifts slowly downward and fades.
    float3 trail = previous(uv + float2(0.0, -0.0015)) * 0.9;

    float3 colour = 0.0;
    for (int ribbon = 0; ribbon < 4; ribbon++)
    {
        float index = ribbon;
        float level = spectrum(0.1 + index * 0.2);
        float centre = -0.35 + index * 0.18
            + sin(p.x * (1.2 + index * 0.3) + Time * (0.4 + index * 0.1)) * (0.15 + Mid * 0.25)
            + fbm(float2(p.x * 1.5 + index * 4.0, Time * 0.1)) * 0.3;

        // Bright along its lower edge, fading upward like a curtain.
        float below = p.y - centre;
        float curtain = exp(-max(below, 0.0) * 40.0) * exp(-max(-below, 0.0) * 9.0);
        float shimmer = 0.6 + 0.4 * noise(float2(p.x * 30.0 + index * 10.0, Time * 4.0)) * (0.5 + Treble);
        colour += hsv2rgb(0.33 + index * 0.12 + level * 0.1, 0.8, 1.0) * curtain * shimmer * (0.05 + level * 0.25);
    }

    float2 starCell = floor(uv * Resolution / 2.0);
    float twinkle = 0.5 + 0.5 * sin(Time * 3.0 + hash(starCell + 1.7) * 20.0);
    float stars = step(0.997, hash(starCell)) * twinkle * 0.03;

    return trail + colour + stars;
}
