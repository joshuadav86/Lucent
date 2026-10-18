// Name: Horizon Line
// A single thread of light divides a dark sky from still water and trembles softly with the sound, while a distant
// sun takes minutes to rise and set, laying a glittering path across the water.
// Family: Minimal
// Tags: Minimal, Calm, Water
// Added: 2026-10-17 21:37

// The horizon's height at x: almost flat, carrying a softened copy of the waveform near the middle.
float hlHorizon(float x)
{
    float u = saturate(x / (2.0 * Aspect) + 0.5);
    float wave = (waveform(u - 0.01) + waveform(u) * 2.0 + waveform(u + 0.01)) * 0.25;
    float taper = exp(-x * x * 1.2);
    return 0.12 + wave * 0.018 * taper * (0.5 + Level);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // The sun follows a very long arc: about a minute and a half from rising to rising again.
    float cycle = Time * 2.0 * PI / 90.0;
    float sunHeight = 0.25 * sin(cycle) + 0.03;          // above the horizon when positive
    float2 sunPos = float2(0.35 * cos(cycle * 0.5) * 0.4, 0.12 - sunHeight);
    float sunRadius = 0.065;
    // Low suns are deep orange, high ones pale gold.
    float altitude = saturate(sunHeight / 0.23);
    float3 sunColour = lerp(float3(1.0, 0.38, 0.12), float3(1.0, 0.8, 0.55), altitude);
    float dayLight = smoothstep(-0.08, 0.2, sunHeight);

    float horizonY = hlHorizon(p.x);
    float3 colour = float3(0.0, 0.0, 0.0);

    if (p.y < horizonY)
    {
        // Sky: deep blue-black overhead, a faint warm band at the horizon that grows as the sun comes up.
        float up = horizonY - p.y;
        float3 zenith = float3(0.004, 0.006, 0.016);
        float3 band = lerp(float3(0.045, 0.05, 0.1), sunColour * 0.22, dayLight);
        colour = lerp(band, zenith, saturate(pow(up / 0.9, 0.55)));
        // The glow around the sun.
        float toSun = length(p - sunPos);
        colour += sunColour * exp(-toSun * 5.0) * 0.12 * (0.3 + dayLight);
        // The sun disc itself, clipped by the horizon.
        float disc = 1.0 - smoothstep(sunRadius - px, sunRadius + px, toSun);
        colour = lerp(colour, sunColour * (0.9 + Bass * 0.25), disc);
        // At night a few faint stars come out.
        float2 starCell = floor(p * 70.0);
        float star = step(0.993, hash(starCell)) * smoothstep(0.3, 0.0, length(frac(p * 70.0) - 0.5));
        colour += float3(0.6, 0.65, 0.8) * star * 0.35 * (1.0 - dayLight) * smoothstep(0.05, 0.4, up)
                * (0.6 + 0.4 * sin(Time * 0.8 + hash(starCell + 7.0) * 40.0));
    }
    else
    {
        // Water: a dark mirror of the sky, its reflection stretched and broken by slow swells.
        float depth = p.y - horizonY;
        float swell = noise(float2(p.x * 3.0, depth * 40.0 / (depth + 0.05) - Time * 0.25)) - 0.5;
        float2 mirror = float2(p.x + swell * depth * 0.25, horizonY - depth * 1.4);
        float up = depth * 1.4;
        float3 zenith = float3(0.004, 0.006, 0.016);
        float3 band = lerp(float3(0.03, 0.035, 0.07), sunColour * 0.22, dayLight);
        colour = lerp(band, zenith, saturate(pow(up / 0.9, 0.55))) * 0.55;

        // The sun's path: a column of glints that sparkle with the treble.
        float column = exp(-abs(mirror.x - sunPos.x) / (0.04 + depth * 0.5));
        float ripples = noise(float2(p.x * 22.0 / (depth * 3.0 + 0.1) + swell * 3.0, depth * 260.0 / (depth + 0.1) - Time * 1.2));
        float glint = smoothstep(0.6 - Treble * 0.12, 0.85, ripples);
        float sunUp = smoothstep(-0.02, 0.06, sunHeight);
        colour += sunColour * column * glint * sunUp * (0.5 + Treble * 0.5) * exp(-depth * 2.5);
        colour += sunColour * column * sunUp * 0.05 * exp(-depth * 3.0);
    }

    // The line: a fine luminous thread with a soft halo, brightest in the middle, breathing with the level.
    float gap = abs(p.y - horizonY);
    float lineLight = (1.0 - smoothstep(0.6 * px, 1.6 * px, gap)) * 0.9 + exp(-gap / (px * 6.0)) * 0.25 + exp(-gap / 0.05) * 0.04;
    float centreWeight = 0.35 + 0.65 * exp(-p.x * p.x * 0.8);
    float3 lineColour = lerp(float3(0.75, 0.82, 1.0), sunColour, 0.5 * dayLight);
    colour += lineColour * lineLight * centreWeight * (0.55 + Level * 0.6 + Beat * 0.15);
    return colour;
}
