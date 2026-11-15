// Name: Marbling
// Paper marbling the way it's done on water: every beat lets a drop of ink fall and spread, pushing everything
// already floating outward into rings, and now and then a comb is drawn through the lot, pulling the rings into
// feathers. Jewel-coloured inks on indigo, each shimmering with its own band of the spectrum.
// Family: Organic
// Tags: Colourful, Calm, Water
// Added: 2026-11-15 17:45

// The mathematics of marbling: a drop of radius r let fall at C moves every point p of the surface out to
// C + (p - C) sqrt(1 + r^2 / |p - C|^2), which keeps every patch of ink the same area, and a comb drawn along
// a line slides each point along it by an amount that falls off with its distance from the line. Both can be
// undone exactly, so a pixel is traced back through the last strokes, newest first, until it lands inside a drop.
static const float Stations = 7.0;            // drops fall at a few places, so they build rings
static const float CombEvery = 7.0;           // every seventh stroke is a comb

float fmRandom(float n, float salt)
{
    // (Stroke numbers start again every 1024, so the numbers stay small however long the visual has been open.)
    n = wrap(n, 1024.0);
    return hash(float2(n * 0.617 + salt * 7.31, salt * 3.17 - n * 0.283));
}

float3 fmInk(float n)
{
    float pick = floor(fmRandom(n, 5.0) * 6.0);
    float3 ink = float3(0.02, 0.42, 0.45);                           // teal
    ink = pick > 0.5 ? float3(0.62, 0.05, 0.12) : ink;              // crimson
    ink = pick > 1.5 ? float3(0.85, 0.55, 0.1) : ink;               // ochre gold
    ink = pick > 2.5 ? float3(0.86, 0.8, 0.66) : ink;               // ivory
    ink = pick > 3.5 ? float3(0.16, 0.1, 0.5) : ink;                // violet
    ink = pick > 4.5 ? float3(0.03, 0.4, 0.2) : ink;                // emerald
    return ink;
}

float3 Render(float2 uv, float2 p)
{
    // The surface never quite rests.
    float2 at = p + 0.018 * float2(sin(p.y * 3.1 + Time * 0.21), cos(p.x * 2.7 + Time * 0.17));
    int strokes = qualitySteps(40);
    float count = (float)strokes;
    float3 colour = 0.0;
    bool found = false;

    [loop]
    for (int i = 0; i < strokes; i++)
    {
        float n = BeatCount - (float)i;
        // A stroke grows as the beat's glide carries it in, and the oldest one is undone as it's forgotten.
        float grown = saturate(BeatGlide - n + 1.0) * saturate(n + count - 2.0 - BeatGlide);
        grown = grown * grown * (3.0 - 2.0 * grown);
        if (wrap(n, CombEvery) > CombEvery - 1.5)
        {
            // A comb: parallel tines drawn across the bath.
            float angle = fmRandom(n, 1.0) * PI;
            float2 heading = float2(cos(angle), sin(angle));
            float2 across = float2(-heading.y, heading.x);
            float spacing = 0.42;
            float distance0 = abs(wrap(dot(at, across) + fmRandom(n, 2.0) * spacing, spacing) - spacing * 0.5);
            float way = fmRandom(n, 3.0) < 0.5 ? -1.0 : 1.0;
            at -= heading * way * 0.3 * grown * 0.05 / (distance0 + 0.05);
        }
        else
        {
            float station = floor(fmRandom(n, 1.0) * Stations);
            float2 centre = (float2(fmRandom(station, 11.0), fmRandom(station, 12.0)) - 0.5) * float2(Aspect * 1.5, 1.5);
            centre += (float2(fmRandom(n, 2.0), fmRandom(n, 3.0)) - 0.5) * 0.07;
            float radius = (0.1 + 0.15 * fmRandom(n, 4.0)) * grown;
            float2 away = at - centre;
            float far2 = dot(away, away);
            if (far2 < radius * radius)
            {
                // Inside this drop: its ink, a little denser towards its edge.
                float edge = far2 / max(radius * radius, 1e-6);
                float band = spectrum(0.03 + fmRandom(n, 6.0) * 0.7);
                colour = fmInk(n) * (1.0 - edge * edge * 0.35) * (0.75 + band * 0.6);
                found = true;
                break;
            }

            at = centre + away * sqrt(max(1.0 - radius * radius / far2, 0.0));
        }
    }

    if (!found)
    {
        // The bath itself: indigo size, clouded with older ink.
        float cloud = fbm(at * 1.7 + 3.0);
        float veins = abs(frac(cloud * 6.0) - 0.5) * 2.0;
        colour = lerp(float3(0.025, 0.035, 0.16), float3(0.05, 0.16, 0.3), cloud) * (0.7 + 0.5 * veins);
    }

    // Paper grain, and a little more light where the music is loud.
    float grain = 0.93 + 0.14 * noise(p * 190.0);
    float vignette = 1.0 - dot(uv - 0.5, uv - 0.5) * 0.9;
    return colour * grain * vignette * (0.8 + Level * 0.35 + BeatSwell * 0.08 + DropPulse * 0.15);
}
