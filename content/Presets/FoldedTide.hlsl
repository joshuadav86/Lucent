// Name: Folded Tide
// Water folded like silk: a tide that runs from white foam at the top through cyan and ultramarine to black in the
// deep, creased by wave upon wave. The bass rolls the big folds, the treble crinkles the fine ones, the height of
// the tide across the window follows the spectrum, and every beat lifts a swell up through it.
// Family: Organic
// Tags: Colourful, Calm, Water
// Added: 2026-11-15 19:42

// The folds come from pushing the picture's height about with waves of ever finer size, each one measured
// from where the last one left it, so they pile into creases the way cloth does.

// How loud the music is around the k-th of nine places across the spectrum.
float ftBand(float k)
{
    float at = 0.03 + k * 0.08;
    return (spectrum(at - 0.02) + spectrum(at - 0.01) + spectrum(at) + spectrum(at + 0.01) + spectrum(at + 0.02)) * 0.2;
}

// The folded height at a point: x across, h up.
float ftFold(float x, float h, float flow)
{
    float2 q = float2(x, h);
    [unroll]
    for (int i = 1; i < 8; i++)
    {
        float n = (float)i;
        // Big folds answer the bass, middling ones the mids, the finest the treble.
        float push = n < 2.5 ? 0.55 + Bass * 0.9 : (n < 4.5 ? 0.6 + Mid * 0.8 : 0.6 + Treble * 0.9);
        float lean = 0.31 * n;
        q.y += 0.1 * push * sin(q.x * n * n * 0.85 + flow * (0.6 + 0.08 * n) + lean) * sin(q.y * n * n * 0.8 + flow * 0.5 - lean * 2.0);
    }

    return q.y;
}

float3 ftWater(float h)
{
    // Black, ultramarine, azure, cyan, foam.
    float3 colour = lerp(float3(0.0, 0.0, 0.03), float3(0.0, 0.03, 0.55), smoothstep(-1.15, -0.45, h));
    colour = lerp(colour, float3(0.0, 0.3, 1.0), smoothstep(-0.5, 0.05, h));
    colour = lerp(colour, float3(0.25, 0.85, 1.0), smoothstep(0.0, 0.5, h));
    colour = lerp(colour, float3(1.0, 1.0, 1.0), smoothstep(0.42, 0.95, h));
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float flow = Time * 0.42 + BeatGlide * 0.35;
    float x = p.x + 0.37;
    float h = -p.y;

    // The tide's height across the window follows the spectrum, read at a few places and joined smoothly.
    float place = uv.x * 8.0;
    float knot = floor(place);
    float between = smoothstep(0.0, 1.0, place - knot);
    float tide = lerp(ftBand(knot), ftBand(knot + 1.0), between);
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;
    // Each beat lifts one swell from the deep to the foam.
    float rise = h + 1.2 - BeatPhase * 2.6;
    float swell = exp(-rise * rise * 5.0) * 0.07 * beatLight;
    float level = (tide - 0.25) * 0.55 + Level * 0.1 + swell + DropPulse * 0.12;

    float folded = ftFold(x, h, flow) - level;
    float3 colour = ftWater(folded);

    // A sheen along the creases, as if lit from above and a little to the left.
    float e = 0.004;
    float along = ftFold(x + e, h, flow) - folded - level;
    float upward = ftFold(x, h + e, flow) - folded - level;
    float crease = saturate(1.0 - upward / e * 0.55);
    float sheen = saturate(0.5 - along / e * 0.35) * crease;
    colour += float3(0.5, 0.8, 1.0) * sheen * sheen * 0.16;
    // Deep creases, where the cloth doubles back, go darker.
    colour *= 0.86 + 0.14 * smoothstep(-0.6, 0.9, upward / e);

    return colour * (0.88 + Intensity * 0.2) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.5);
}
