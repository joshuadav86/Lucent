// Name: Star Polygons
// Four star polygons drawn in razor-thin light nest one inside another and turn against each other:
// their points sharpen and soften in a slow breath, they change shape every few bars, and their tips flare with the music.
// Family: Geometric
// Tags: Neon, Minimal, Geometric
// Added: 2026-10-17 21:44

// Point counts each ring cycles through.
static const float SPG_POINTS[8] = { 7.0, 9.0, 5.0, 11.0, 8.0, 12.0, 6.0, 10.0 };

// Distance to a star polygon with n points on a circle of radius r: n straight chords, each joining two
// points that lie k steps apart. Non-whole k still gives an even, symmetric star, which lets k glide smoothly.
// Also returns the distance to the nearest chord end, for the flares.
float spgStar(float2 q, float n, float k, float r, float spin, out float tipDist, out float tipIndex)
{
    float halfSpan = PI * k / n;
    float inset = r * cos(halfSpan);
    float halfLength = r * sin(halfSpan);
    float best = 1e3;
    tipDist = 1e3;
    tipIndex = 0.0;
    [loop]
    for (int i = 0; i < 16; i++)
    {
        if (i >= (int)n)
            break;
        float angle = spin + 2.0 * PI * i / n;
        float2 dir = float2(cos(angle), sin(angle));
        float2 local = float2(dot(q, dir), dir.x * q.y - dir.y * q.x);
        float d = length(float2(local.x - inset, max(abs(local.y) - halfLength, 0.0)));
        best = min(best, d);
        float tip = length(float2(local.x - inset, abs(local.y) - halfLength));
        if (tip < tipDist)
        {
            tipDist = tip;
            tipIndex = i;
        }
    }
    return best;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float2 q = p * (1.0 - Bass * 0.03);

    float beatPhase = BeatCount - saturate(Beat);
    float3 colour = float3(0.004, 0.004, 0.012) + float3(0.012, 0.01, 0.03) * exp(-dot(p, p) * 2.5);

    [loop]
    for (int layer = 0; layer < 4; layer++)
    {
        float fl = (float)layer;
        float radius = 0.88 * pow(0.68, fl);
        float spin = Time * (0.05 + fl * 0.025) * (layer % 2 == 0 ? 1.0 : -1.0) + fl * 0.4;
        float3 tint = hsv2rgb(0.55 + fl * 0.11 + 0.05 * sin(Time * 0.03 + fl), 0.6 - fl * 0.08, 1.0);

        // Each ring changes its point count every eight beats, a beat apart from its neighbour,
        // gliding from the old star to the new over one beat.
        float g = beatPhase - fl * 2.0 + 16.0;
        float section = floor(g / 8.0);
        float glide = smoothstep(7.0, 8.0, g - section * 8.0);
        float level = spectrum(0.06 + fl * 0.22);

        [unroll]
        for (int phase = 0; phase < 2; phase++)
        {
            float weight = phase == 0 ? 1.0 - glide : glide;
            if (weight <= 0.001)
                continue;
            float n = SPG_POINTS[(int)fmod(section + phase + fl * 3.0, 8.0)];
            // The star's sharpness breathes slowly between a plain polygon and its spikiest form. It rests on
            // whole steps, where the chords meet in clean points, and glides quickly between them.
            float sharp = 0.5 + 0.5 * sin(Time * (0.07 + fl * 0.015) + fl * 1.7 + n);
            float steps = lerp(1.0, floor((n - 1.0) * 0.5), sharp);
            float k = floor(steps) + smoothstep(0.3, 0.7, frac(steps));
            float growth = phase == 0 ? 1.0 + glide * 0.15 : 0.85 + glide * 0.15;
            float tipDist = 0.0;
            float tipIndex = 0.0;
            float d = spgStar(q, n, k, radius * growth, spin + phase * 0.3 * (1.0 - glide), tipDist, tipIndex);

            float core = saturate(1.0 - d / (px * 0.8));
            float halo = exp(-d / (px * 4.0)) * 0.15;
            colour += tint * (core * 0.85 + halo) * weight * (0.6 + level * 0.9);

            // Tips flare softly with their own slice of the spectrum.
            float flare = spectrum(frac(tipIndex / n * 0.8 + fl * 0.13));
            float tipGlow = exp(-tipDist * tipDist / (px * px * 20.0)) * 0.35 + exp(-tipDist / (px * 12.0)) * 0.08;
            colour += lerp(tint, float3(1.0, 1.0, 1.0), 0.4) * tipGlow * (flare * 1.2 + Beat * 0.4) * weight;
        }

        // A faint circle each star is inscribed in.
        float ring = abs(length(q) - radius);
        colour += tint * saturate(1.0 - ring / (px * 0.7)) * 0.06;
    }
    // Keep the brightest overlaps soft so they glow without hard sparkles.
    return min(colour, 1.5);
}
