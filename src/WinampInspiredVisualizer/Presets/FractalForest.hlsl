// Name: Fractal Forest
// Gliding along a lake through a forest of glowing fractal trees: a thousand twigs sway with the music and every beat sends light racing up to their blossoms.

// One fractal tree, built by folding space: every level mirrors the plane so both child branches are drawn
// at once. Returns the colour and writes how much of the pixel the tree covers (to hide trees behind it).
float3 forestTree(float2 p, float seed, float pixel, float wind, float pulse, float3 trunkTint, float3 twigTint,
                  int levels, out float coverage)
{
    float spread = 0.3 + 0.38 * hash(float2(seed, 1.3)) + Mid * 0.05;
    float ratio = 0.66 + 0.1 * hash(float2(seed, 2.9));
    float lean = (hash(float2(seed, 4.7)) - 0.5) * 0.12 + wind;

    // The per-level rotations are the same at every level, so work them out once.
    float2 leanSC, turnSC;
    sincos(lean, leanSC.x, leanSC.y);
    sincos(spread, turnSC.x, turnSC.y);
    float invRatio = 1.0 / ratio;

    float scale = 1.0;
    float2 q = p;
    float3 colour = 0.0;
    coverage = 0.0;
    float levelCount = (float)levels;
    [loop]
    for (int i = 0; i < levels; i++)
    {
        float level = (float)i;
        float width = 0.055 * scale;                                   // tapers with the branches
        float d = (length(float2(q.x, q.y - clamp(q.y, 0.0, 1.0))) * scale - width) / pixel;   // pixels outside the branch
        float thin = saturate(width / pixel * 2.0);                   // sub-pixel twigs fade instead of shimmering
        float body = saturate(0.5 - d) * thin;
        float edge = saturate(1.0 - abs(d) * 0.8) * thin;
        float halo = 1.0 / (1.0 + max(d, 0.0) * 0.4);

        // Light climbs the tree after every beat.
        float climb = saturate(1.0 - abs(level - pulse) * 0.45);
        climb *= climb;
        float3 tint = lerp(trunkTint, twigTint, level / levelCount);
        colour += tint * (body * (0.05 + 0.5 * climb) + edge * (0.35 + 0.9 * climb) + halo * halo * 0.04 * thin);
        coverage = max(coverage, body);

        q.y -= 1.0;
        q = float2(leanSC.y * q.x - leanSC.x * q.y, leanSC.x * q.x + leanSC.y * q.y);
        q.x = abs(q.x);
        q = float2(turnSC.y * q.x - turnSC.x * q.y, turnSC.x * q.x + turnSC.y * q.y) * invRatio;
        scale *= ratio;
    }

    // Blossoms on every tip, twinkling with the treble.
    float tip = (length(q) * scale - 0.03 * scale - 0.004) / pixel;
    float bloom = saturate(1.0 - tip * 0.7) + 0.15 / (1.0 + max(tip, 0.0) * 0.3);
    float twinkle = 0.6 + 0.4 * sin(Time * 6.0 + dot(q, float2(13.1, 7.7)) + seed * 5.0);
    colour += lerp(twigTint, float3(1.0, 1.0, 1.0), 0.45) * bloom * (0.5 + Treble * 1.3 * twinkle + Beat * 0.3);
    coverage = max(coverage, saturate(1.0 - tip));
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    const float horizon = 0.5;      // the lake shore, below the centre of the screen
    bool reflection = p.y > horizon;
    float2 view = p;
    float rippleShade = 1.0;
    if (reflection)
    {
        // Mirror the forest in the lake, with ripples spreading from the beat.
        float depth = p.y - horizon;
        float ripple = sin(depth * 90.0 / (depth + 0.08) - Time * 3.0) * (0.004 + 0.012 * Bass) * (0.2 + depth * 3.0);
        view = float2(p.x + ripple, horizon - depth * 1.3);
        rippleShade = 0.6 * exp(-depth * 2.0);
    }

    float hueShift = Time * 0.012 + (BeatCount - Beat) * 0.03;
    float pulse = (1.0 - Beat) * 11.0 - 1.5;                  // the beat's light, travelling up the levels
    float travel = Time * 0.22;

    // Night air: dark, with faint drifting mist low between the trees.
    float mist = fbm(float2(view.x * 1.5 + travel * 0.6, view.y * 3.0 - Time * 0.05));
    float low = saturate(1.0 - (horizon - view.y) * 1.6);
    float3 colour = float3(0.012, 0.008, 0.03) + hsv2rgb(hueShift + 0.6, 0.6, 1.0) * mist * mist * low * low * 0.07;
    float hidden = 1.0;                                        // how much the nearer layers let through

    [unroll]
    for (int layer = 0; layer < 3; layer++)
    {
        // Near layer first so it can hide what is behind it.
        float depth = (float)(2 - layer);
        float layerScale = 0.4 / (1.0 + depth * 0.8);
        float parallax = 1.0 / (1.0 + depth * 0.8);
        float spacing = 3.3 - depth * 0.5;
        float2 lp = float2(view.x / layerScale + travel * parallax * 2.6 + depth * 7.3, (horizon - view.y) / layerScale);
        float pixel = pixelSize() / layerScale;
        float index = floor(lp.x / spacing + 0.5);
        float wind = 0.05 * sin(Time * 0.7 + depth) + 0.06 * Bass * sin(Time * 2.1 + depth);
        float fade = 1.0 / (1.0 + depth * depth * 1.5);
        float layerHue = hueShift + depth * 0.12;
        float3 trunkTint = hsv2rgb(layerHue, 0.85, 1.0);
        float3 twigTint = hsv2rgb(layerHue + 0.45, 0.6, 1.0);
        int levels = clamp(qualitySteps(9), 8, 11) - (int)depth;   // even Low keeps the trees full

        float3 layerColour = 0.0;
        float layerCover = 0.0;
        [unroll]
        for (int k = -1; k <= 1; k++)
        {
            float seed = index + (float)k + depth * 31.0;
            float size = 0.8 + 0.4 * hash(float2(seed, 11.3));
            float2 local = (lp - float2((index + (float)k) * spacing + (hash(float2(seed, 9.1)) - 0.5) * 0.8, 0.0)) / size;
            if (abs(local.x) < 2.8 && local.y > -0.1 && local.y < 4.4)
            {
                float cover;
                layerColour += forestTree(local, seed, pixel / size, wind, pulse - depth * 0.7, trunkTint, twigTint, levels, cover);
                layerCover = max(layerCover, cover);
            }
        }
        colour += layerColour * fade * hidden;
        hidden *= 1.0 - layerCover * 0.85;
    }

    colour *= rippleShade;

    // Fireflies drifting above the water, brighter with the treble.
    if (!reflection)
    {
        float2 cell = float2(p.x * 7.0 + Time * 0.15, p.y * 7.0 + sin(Time * 0.3 + p.x) * 0.3);
        float2 id = floor(cell);
        float2 offset = float2(hash(id + 1.7), hash(id + 5.3)) - 0.5;
        float firefly = glow(length(frac(cell) - 0.5 - offset * 0.6), 60.0) * step(0.85, hash(id + 2.2));
        colour += hsv2rgb(hueShift + 0.15, 0.6, 1.0) * firefly * (0.15 + Treble * 0.9) * (0.5 + 0.5 * sin(Time * 3.0 + id.x * 7.0));
    }
    return colour + previous(uv) * 0.1;
}
