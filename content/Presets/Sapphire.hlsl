// Name: Sapphire
// Looking down into a cut sapphire: hundreds of flat facets in deep blue, electric blue, and ice white,
// folded into an eight-sided star. A lamp circles overhead, so facets flash as it finds them. The bass
// recuts the stone, the lamp swings round with the beat, and the brightest facets follow the treble.
// Family: Kaleidoscope
// Tags: Geometric, Colourful, Intense
// Added: 2026-11-15 21:10

// The height of the stone's surface. It is made only of folds, so it is flat between creases: facets.
float saCut(float2 p, float recut)
{
    float2 q = kaleidoscope(p, 8.0);
    float h = 0.0;
    float size = 1.0;

    [unroll]
    for (int k = 0; k < 5; k++)
    {
        float f = (float)k;
        q = abs(q) - float2(0.34 + 0.1 * sin(recut + f * 1.7), 0.21 + 0.08 * cos(recut * 0.8 + f * 2.3));
        q = rotate(q, 0.7 + 0.5 * f + 0.25 * sin(recut * 0.6 + f));
        h += size * max(abs(q.x), abs(q.y) * 0.8);
        q *= 1.55;
        size /= 1.55;
    }

    return h;
}

float3 Render(float2 uv, float2 p)
{
    float recut = Time * 0.11 + BeatGlide * 0.16 + 0.5 * Bass;
    float2 at = rotate(p, Time * 0.03) * 0.7;
    float e = 0.0015;
    float h = saCut(at, recut);
    float2 slope = float2(saCut(at + float2(e, 0.0), recut) - h, saCut(at + float2(0.0, e), recut) - h) / e;
    float3 n = normalize(float3(-slope * 0.75, 1.0));

    float swing = Time * 0.3 + BeatGlide * 0.8;
    float3 lamp = normalize(float3(0.75 * cos(swing), 0.75 * sin(swing), 0.7));
    float3 eye = float3(0.0, 0.0, 1.0);
    float lit = saturate(dot(n, lamp));
    float flash = pow(saturate(dot(n, normalize(lamp + eye))), 40.0);

    // Deeper cuts hold darker blue; the light from below comes up through the shallow ones.
    float depth = saturate(h * 0.75);
    float3 stone = lerp(float3(0.02, 0.07, 0.5), float3(0.08, 0.5, 1.6), lit);
    stone = lerp(stone, float3(0.3, 1.1, 1.5), saturate(lit * 1.4 - 0.5) * (1.0 - depth));
    stone *= 0.55 + 0.6 * (1.0 - depth);

    float3 colour = stone + float3(1.15, 1.2, 1.25) * flash * (0.7 + 0.9 * Treble + 0.3 * BeatSwell);
    return colour * (0.92 + 0.1 * Level) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.5);
}
