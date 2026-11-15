// Name: Windswept
// A field of fine golden strands combed by a wind you can't see, like long grass or hair in sunlight. The
// wind's pattern drifts and turns with the beat, the bass combs the strands out longer and smoother, and
// the light that catches them swings round and warms as the song builds.
// Family: Organic
// Tags: Calm, Colourful
// Added: 2026-11-15 20:47

// Which way the wind blows at a point.
float2 wsWind(float2 q, float flow)
{
    float turn = noise(q * 0.8 + float2(flow * 0.21, -flow * 0.13)) * 2.0 + noise(q * 1.7 - float2(flow * 0.1, flow * 0.16));
    float angle = turn * 4.2 + 0.6 * sin(q.x * 0.9 + flow * 0.3) + 0.5 * sin(q.y * 1.3 - flow * 0.2);
    return float2(cos(angle), sin(angle));
}

// The fine grain the strands are combed out of.
float wsGrain(float2 q, float flow)
{
    return noise(q * 85.0 + float2(flow * 1.1, flow * 0.7));
}

float3 Render(float2 uv, float2 p)
{
    float flow = Time * 0.22 + BeatGlide * 0.25;
    float2 q = p * 1.15;
    float stride = 0.011 + 0.008 * Bass + 0.004 * BeatSwell;

    // Follow the wind both ways from this point and average the grain along the path: that draws each
    // grain out into a strand.
    float sum = wsGrain(q, flow) * 1.0;
    float weights = 1.0;
    float2 ahead = q;
    float2 behind = q;

    [unroll]
    for (int i = 1; i <= 13; i++)
    {
        float weight = 1.0 - (float)i / 14.0;
        ahead += wsWind(ahead, flow) * stride;
        behind -= wsWind(behind, flow) * stride;
        sum += (wsGrain(ahead, flow) + wsGrain(behind, flow)) * weight;
        weights += 2.0 * weight;
    }

    float strand = saturate((sum / weights - 0.5) * 3.4 + 0.5);

    // The light lies across the strands: those that run across it shine, those that run along it stay dull.
    float2 wind = wsWind(q, flow);
    float around = Time * 0.07 + 0.6;
    float sheen = abs(wind.x * sin(around) - wind.y * cos(around));
    sheen = sheen * sheen;

    // Big soft locks of lighter and darker, as in fur.
    float lock = fbm(q * 0.9 + float2(-flow * 0.15, flow * 0.1));

    float3 dull = lerp(float3(0.16, 0.085, 0.035), float3(0.5, 0.42, 0.26), lock);
    float3 gold = lerp(float3(1.25, 0.8, 0.3), float3(1.3, 1.18, 0.95), saturate(sheen * 0.8 + lock * 0.5));
    float3 colour = lerp(dull, gold, strand * (0.3 + 0.7 * sheen));
    colour *= 0.55 + 0.75 * strand;
    colour *= float3(1.0 + 0.12 * Intensity, 1.0, 1.0 - 0.1 * Intensity) * (0.95 + 0.06 * Level + 0.04 * BeatSwell);
    return colour * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.6);
}
