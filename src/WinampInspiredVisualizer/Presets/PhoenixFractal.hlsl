// Name: Phoenix Fractal
// The Phoenix fractal's twin firebirds: their lace wings morph and coil into paisley spirals while plasma feathers stream from every edge.
// Family: Fractal
// Tags: Calm, Dark

float3 phoenixPlasma(float t)
{
    // Magenta -> violet -> electric blue -> gold, cycling.
    return 0.5 + 0.5 * cos(2.0 * PI * (t + float3(0.95, 0.15, 0.45)));
}

float3 Render(float2 uv, float2 p)
{
    // Parameters wander through the lacy part of the Phoenix family (z' = z^2 + c + k * z_prev).
    float2 c = float2(0.588 + 0.022 * sin(Time * 0.047), 0.012 * sin(Time * 0.061));
    float2 k = float2(-0.49 + 0.03 * sin(Time * 0.039 + 1.0), (0.045 + Mid * 0.02) * sin(Time * 0.053 + 2.0));

    // Breathe between both birds and a close-up of one wing.
    float breath = 0.5 - 0.5 * cos(Time * 0.071);
    float scale = 0.9 * exp2(-1.2 * breath);
    float2 focus = float2(0.0, 0.42) * breath;
    float2 q = rotate(p, 0.15 * sin(Time * 0.043));
    float2 z = float2(q.y, q.x) * scale + focus;
    float pixel = 2.0 * scale / Resolution.y;

    float2 last = 0.0;
    float2 dz = float2(1.0, 0.0);
    float2 dLast = 0.0;
    float n = 0.0;
    float stalk = 1.0e3;
    bool escaped = false;
    int iterations = qualitySteps(260);
    [loop]
    for (int i = 0; i < iterations; i++)
    {
        float2 zNext = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c
                     + float2(k.x * last.x - k.y * last.y, k.x * last.y + k.y * last.x);
        float2 dNext = 2.0 * float2(z.x * dz.x - z.y * dz.y, z.x * dz.y + z.y * dz.x)
                     + float2(k.x * dLast.x - k.y * dLast.y, k.x * dLast.y + k.y * dLast.x);
        last = z;
        dLast = dz;
        z = zNext;
        dz = dNext;
        if (i > 1)
        {
            stalk = min(stalk, abs(z.x * z.y));   // Pickover stalks: the orbit skimming the axes
        }
        if (dot(z, z) > 1.0e6)
        {
            escaped = true;
            break;
        }
        n += 1.0;
    }

    float hueShift = (BeatCount - Beat) * 0.03 + Time * 0.012;
    float plume = exp(-stalk * (75.0 - Bass * 25.0));
    float3 colour = 0.0;
    if (escaped)
    {
        float logR = 0.5 * log(dot(z, z));
        float smoothN = n + 1.0 - log2(logR);
        float de = length(z) * logR / max(length(dz), 1.0e-20);
        float dpx = de / pixel;
        float core = saturate(1.4 - dpx * 0.8);
        float halo = exp(-dpx * 0.5);

        float3 tint = phoenixPlasma(smoothN * 0.03 + hueShift);
        float pocket = saturate(smoothN / 30.0);
        float nearWings = saturate(smoothN / 12.0 - 0.25);
        colour = tint * (0.02 + plume * nearWings * (1.0 + Beat * 0.4) + pocket * pocket * 0.12)
               + lerp(tint, float3(0.85, 0.95, 1.0), 0.6) * (core * (0.9 + Treble * 0.6) + halo * 0.15);
    }
    else
    {
        colour = phoenixPlasma(0.55 + hueShift) * exp(-stalk * 12.0) * 0.35;
    }
    return colour + previous(uv) * 0.1;
}
