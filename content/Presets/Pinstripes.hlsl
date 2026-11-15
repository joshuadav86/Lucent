// Name: Pinstripes
// A wall of fine coloured strings on dark grey cloth, strung from top to bottom like the inside of a piano.
// Each string listens to its own part of the spectrum, bass on the left: when that part sounds, the string
// brightens and swings in a standing wave, and each beat sends a pluck rippling across the wall.
// Family: Music-shaped
// Tags: Minimal, Colourful, Dark
// Added: 2026-11-15 20:45

static const float Strings = 64.0;

float3 psThread(float pick)
{
    if (pick < 0.3) return float3(0.2, 1.2, 1.05);
    if (pick < 0.55) return float3(1.5, 0.42, 0.32);
    if (pick < 0.8) return float3(1.5, 1.15, 0.28);
    return float3(1.12, 1.1, 1.05);
}

float3 Render(float2 uv, float2 p)
{
    float width = 2.0 * Aspect / Strings;           // room for one string
    float slot = (p.x + Aspect) / width;
    float home = floor(slot);
    float along = saturate(0.5 - p.y * 0.5);         // 0 at the bottom of the string, 1 at the top
    float edge = pixelSize();
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;

    float3 colour = float3(0.075, 0.075, 0.085) * (0.85 + 0.3 * uv.y);
    colour *= 0.94 + 0.06 * sin(p.y * 260.0) * sin(p.x * 260.0);

    // A swinging string can reach into its neighbours' room, so look at the few nearest.
    [unroll]
    for (int k = -3; k <= 3; k++)
    {
        float id = home + (float)k;
        if (id < 0.0 || id > Strings - 1.0) continue;

        float seed = hash(float2(id, 3.3));
        float heard = (spectrum((id + 0.2) / Strings * 0.8) + spectrum((id + 0.8) / Strings * 0.8)) * 0.5;
        float lively = smoothstep(0.08, 0.7, heard);

        // The beat's pluck crosses the wall from left to right.
        float pluck = (id + 0.5) / Strings - BeatPhase * 1.4 + 0.2;
        float kick = exp(-pluck * pluck * 60.0) * beatLight;

        // A standing wave: still at both ends, with one to three bellies.
        float bellies = 1.0 + floor(seed * 3.0);
        float swing = sin(along * PI * bellies) * cos(Time * (2.2 + 1.6 * seed) + seed * 40.0);
        float reach = width * (0.15 + 2.3 * lively + 1.2 * kick) * (0.6 + 0.4 * seed);
        float x = (id + 0.5) * width - Aspect + swing * reach;

        float d = abs(p.x - x);
        float core = smoothstep(edge * 1.2 + 0.0012, 0.0, d);
        float haze = exp(-d / (0.006 + 0.012 * lively)) * (0.1 + 0.5 * lively + 0.4 * kick);
        float3 thread = psThread(hash(float2(id, 8.1)));
        colour += thread * (core * (0.3 + 0.9 * lively + 0.6 * kick) + haze * 0.35);
    }

    return colour * (0.9 + 0.15 * Intensity) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.5);
}
