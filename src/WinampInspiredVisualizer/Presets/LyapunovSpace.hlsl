// Name: Lyapunov Space
// Polished gold shapes floating over a deep blue sea, where order meets chaos in a simple population model. Every bar of the music swaps the rhythm of the rule, and the gold melts and reforms into new shapes.
// Family: Fractal
// Tags: Colourful

// Each pattern is twelve steps of A and B (bit = 1 means B), read from the lowest bit up.
uint lyapunovPattern(float index)
{
    float k = wrap(index, 8.0);
    if (k < 0.5) return 0xFC0u;   // AAAAAABBBBBB: the classic "Zircon City"
    if (k < 1.5) return 0xAAAu;   // ABABABABABAB
    if (k < 2.5) return 0xDB6u;   // ABBABBABBABB
    if (k < 3.5) return 0xE38u;   // AAABBBAAABBB
    if (k < 4.5) return 0x924u;   // AABAABAABAAB
    if (k < 5.5) return 0xF0Fu;   // BBBBAAAABBBB
    if (k < 6.5) return 0xCCCu;   // AABBAABBAABB
    return 0xB6Du;                // BABBABBABBAB
}

// The Lyapunov exponent of the logistic map x -> r x (1 - x), with r stepping between a and b
// in the pattern. Negative where the population settles (order), positive where it is chaotic.
// The pattern can be part-way between two patterns, so shapes melt from one rhythm to the next.
float lyapunovExponent(float2 ab, uint fromPattern, uint toPattern, float morph)
{
    float x = 0.5;
    int warmup = 36;
    [loop]
    for (int i = 0; i < warmup; i++)
    {
        uint bit = (uint)i % 12u;
        float wa = (float)((fromPattern >> bit) & 1u);
        float wb = (float)((toPattern >> bit) & 1u);
        float r = lerp(ab.x, ab.y, lerp(wa, wb, morph));
        x = r * x * (1.0 - x);
    }
    float total = 0.0;
    float product = 1.0;
    int count = qualitySteps(120);
    [loop]
    for (int j = 0; j < count; j++)
    {
        uint bit = (uint)j % 12u;
        float wa = (float)((fromPattern >> bit) & 1u);
        float wb = (float)((toPattern >> bit) & 1u);
        float r = lerp(ab.x, ab.y, lerp(wa, wb, morph));
        x = r * x * (1.0 - x);
        product *= max(abs(r * (1.0 - 2.0 * x)), 1.0e-6);
        // Take the logarithm every few steps; multiplying in between saves time.
        if ((j & 3) == 3)
        {
            total += log(product);
            product = 1.0;
        }
    }
    total += log(product);
    return total / (float)count;
}

float3 Render(float2 uv, float2 p)
{
    // One pattern per bar of four beats; the first beat of each bar melts from the old pattern to the new.
    float bar = floor(BeatCount / 4.0);
    float beatInBar = BeatCount - bar * 4.0;
    float morph = beatInBar < 0.5 ? smoothstep(0.0, 1.0, saturate(1.0 - Beat)) : 1.0;
    uint fromPattern = lyapunovPattern(bar - 1.0);
    uint toPattern = lyapunovPattern(bar);

    // Slow pan around the richest part of the plane.
    // Across the screen is b, up the screen is a (the classic Zircon City framing).
    float halfHeight = 0.25 + 0.03 * sin(Time * 0.017);
    float2 centre = float2(3.15 + 0.17 * sin(Time * 0.031), 3.66 + 0.02 * sin(Time * 0.023 + 1.0));
    float2 view = rotate(float2(p.x, -p.y), 0.08 * sin(Time * 0.013));
    float2 ba = centre + view * halfHeight;
    float2 ab = clamp(float2(ba.y, ba.x), 2.0, 4.0);
    float lambda = lyapunovExponent(ab, fromPattern, toPattern, morph);

    // Relief: the exponent's slope across the screen shades the gold like a polished surface.
    float2 slope = float2(ddx_fine(lambda), ddy_fine(lambda)) * Resolution.y * 0.012;
    slope = clamp(slope, -3.0, 3.0);
    float3 normal = normalize(float3(-slope.x, slope.y, 1.0));
    float3 lightDir = normalize(float3(0.5 * cos(Time * 0.1), 0.5 * sin(Time * 0.1) + 0.4, 0.75));
    float diffuse = saturate(dot(normal, lightDir));
    float specular = pow(saturate(dot(normal, normalize(lightDir + float3(0.0, 0.0, 1.0)))), 40.0);

    float3 colour = 0.0;
    if (lambda < 0.0)
    {
        // Order: gold, brightest deep inside the stable regions, darkening to bronze at their edges.
        float depth = 1.0 - exp(lambda * 2.2);   // 0 at the edge, approaching 1 deep inside
        float3 gold = lerp(float3(0.28, 0.12, 0.02), float3(1.0, 0.72, 0.28), depth);
        gold = lerp(gold, float3(1.0, 0.93, 0.7), depth * depth * depth * 0.6);
        colour = gold * (0.2 + 0.85 * diffuse) * (0.75 + Bass * 0.45 + Beat * 0.1)
               + float3(1.0, 0.9, 0.7) * specular * depth * (0.35 + Treble * 0.5);
        // A thin dark seam right at the edge of each shape.
        colour *= smoothstep(0.0, 0.06, -lambda);
    }
    else
    {
        // Chaos: a deep blue sea, glowing brighter where the chaos is strongest and with the music in its band.
        float strength = 1.0 - exp(-lambda * 2.5);
        float loud = spectrum(0.25 + strength * 0.6);
        float3 sea = lerp(float3(0.0, 0.01, 0.05), float3(0.02, 0.09, 0.32), strength);
        sea += float3(0.2, 0.5, 1.0) * strength * strength * strength * (0.1 + loud * 0.8);
        colour = sea * smoothstep(0.0, 0.05, lambda) * (0.85 + 0.3 * diffuse);
    }

    // Soft temporal smoothing tames the speckle in the chaotic parts as the view drifts.
    return lerp(colour, previous(uv), 0.35);
}
