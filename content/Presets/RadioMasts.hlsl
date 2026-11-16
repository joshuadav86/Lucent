// Name: Radio Masts
// Lattice radio masts standing in teal fog at night, row behind row, each hung with red warning lamps. Every
// mast listens to its own part of the music and its lamps light from the ground up as that part gets louder,
// like a row of level meters. The beacons on top swell with the beat and send rings of signal out into the fog.
// Family: Cyberpunk
// Tags: Dark, Retro, Calm
// Added: 2026-11-16 00:05

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float rmLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

float3 Render(float2 uv, float2 p)
{
    float e = pixelSize();

    // Fog: dark overhead, pale and thick towards the ground, drifting slowly.
    float drift = fbm(p * float2(1.1, 2.6) + float2(Time * 0.03, 0.0));
    float low = smoothstep(-1.0, 0.75, p.y);
    float3 fog = lerp(float3(0.01, 0.07, 0.1), float3(0.16, 0.5, 0.5), low * low) * (0.85 + 0.3 * drift);
    float3 colour = fog;
    float3 lampRed = float3(1.8, 0.1, 0.06);

    [unroll]
    for (int layer = 0; layer < 3; layer++)
    {
        float f = (float)layer;
        float size = 0.6 + 0.4 * f;                                   // far masts are smaller
        float ground = 0.5 + 0.2 * f;
        float cell = 0.55 + 0.35 * f;
        float along = (p.x + Time * 0.02 * size + f * 3.7) / cell;
        float id = floor(along);
        float fx = (along - id - 0.5) * cell;
        float pick = hash(float2(wrap(id, 512.0), f * 7.0 + 1.0));
        float tall = size * (0.7 + 0.6 * pick);
        float hearAt = 0.07 + 0.6 * hash(float2(wrap(id, 512.0), f * 7.0 + 4.0));
        float heard = rmLoud(hearAt);

        float up = ground - p.y;
        float seg = size * 0.075;
        float rungs = floor(tall / seg);
        tall = rungs * seg;
        float wide = size * (0.006 + 0.034 * saturate(1.0 - up / tall));
        float t = frac(up / seg);
        float thin = size * 0.0035;
        float legs = abs(abs(fx) - wide);
        float braces = abs(abs(fx) - wide * abs(1.0 - 2.0 * t));
        float ties = max(min(t, 1.0 - t) * seg, abs(fx) - wide);
        float lattice = min(min(legs, braces), ties) - thin;
        float inMast = step(0.0, up) * step(up, tall);
        float spike = max(abs(fx) - thin, max(tall - up, up - tall - size * 0.07));
        float mast = min(lattice + (1.0 - inMast), spike);
        // The ground this row stands on.
        float earth = p.y - ground - 0.012 * sin(p.x * 3.0 + f * 2.0);

        float3 dark = lerp(float3(0.07, 0.26, 0.28), float3(0.005, 0.035, 0.045), f * 0.5);
        float solid = max(smoothstep(e, -e, mast), smoothstep(-e, e, earth));
        colour = lerp(colour, dark, solid);

        // Lamps: one at every rung, lit from the bottom up as this mast's part of the music gets louder.
        float rung = clamp(floor(up / seg + 0.5), 1.0, rungs);
        float2 toLamp = float2(fx, up - rung * seg);
        float on = saturate(heard * (rungs + 1.0) - (rung - 1.0));
        float lampSize = size * 0.011;
        float dl = length(toLamp);
        colour = lerp(colour, lerp(float3(0.22, 0.02, 0.02), lampRed, on), smoothstep(lampSize + e, lampSize - e, dl));
        colour += lampRed * on * exp(-dl * dl / (seg * seg * 0.15)) * 0.5;
        // The lit part of the mast reddens the fog beside it.
        float litTo = heard * (rungs + 1.0) * seg;
        float haze = exp(-fx * fx / (size * size * 0.005)) * smoothstep(litTo + seg, litTo - seg, up) * smoothstep(-0.02, 0.04, up);
        colour += lampRed * haze * 0.09;

        // The beacon on top, and the signal leaving it: two rings to a beat, fading as they spread.
        float2 toTip = float2(fx, up - tall - size * 0.07);
        float dt = length(toTip);
        float swell = 0.55 + 0.45 * BeatSwell;
        colour += lampRed * swell * exp(-dt * dt / (size * size * 0.0012));
        colour += float3(1.6, 0.9, 0.8) * smoothstep(size * 0.014 + e, size * 0.014 - e, dt);
        float spread = frac(BeatGlide * 0.5 + pick);
        float ring1 = abs(dt - spread * cell * 0.45);
        float ring2 = abs(dt - frac(spread + 0.5) * cell * 0.45);
        float signal = smoothstep(e * 1.6, 0.0, ring1 - 0.0015) * (1.0 - spread) + smoothstep(e * 1.6, 0.0, ring2 - 0.0015) * (1.0 - frac(spread + 0.5));
        colour += float3(0.35, 0.9, 0.85) * signal * (0.25 + 0.75 * heard) * step(0.0, toTip.y + size * 0.05) * 0.6;

        // Fog between this row and the next one nearer.
        colour = lerp(colour, fog, 0.3 - 0.12 * f);
    }

    return colour * (0.94 + 0.08 * Level);
}
