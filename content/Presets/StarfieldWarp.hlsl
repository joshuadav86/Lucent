// Name: Starfield Warp
// Flying through a field of stars that streak into hyperspace when the bass hits.
// Family: Cosmic
// Tags: Intense, Space
// Added: 2026-10-16 20:08

float3 starLayer(float2 p, float depth, float seed)
{
    float scale = lerp(18.0, 1.5, depth);
    float2 grid = p * scale + seed * 37.0;
    float2 cell = floor(grid);
    float2 local = frac(grid) - 0.5;
    float chance = hash(cell + seed);
    float2 jitter = float2(hash(cell + 3.1 + seed), hash(cell + 7.7 + seed)) - 0.5;
    float starDistance = length(local - jitter * 0.7);
    float size = 0.04 + 0.06 * chance;

    // Fade stars in from far away and out as they pass the camera.
    float visible = smoothstep(0.0, 0.2, depth) * smoothstep(1.0, 0.8, depth) * step(0.6, chance);
    float brightness = smoothstep(size, 0.0, starDistance) * visible;
    return hsv2rgb(0.55 + chance * 0.2, 0.3 + Treble * 0.4, brightness * 2.0);
}

float3 Render(float2 uv, float2 p)
{
    // Zooming the last frame outward turns each star into a streak; more bass, longer streaks.
    float speed = 0.06 + Bass * 0.25 + Beat * 0.2;
    float3 trail = previous(toUv(p * (1.0 - speed * 0.12))) * (0.86 + Bass * 0.06);

    float3 stars = 0.0;
    for (int layer = 0; layer < 4; layer++)
    {
        float depth = frac(layer * 0.25 + Time * 0.05 + (BeatCount - Beat) * 0.03);
        stars += starLayer(p, depth, layer);
    }

    float haze = fbm(p * 2.0 - Time * 0.03) * 0.04 * (0.3 + Mid);
    float3 nebula = hsv2rgb(0.65 + fbm(p * 1.5 + Time * 0.02) * 0.3, 0.6, haze);
    return trail + stars * 0.5 + nebula;
}
