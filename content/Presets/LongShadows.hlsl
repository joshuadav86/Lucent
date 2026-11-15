// Name: Long Shadows
// Rings within rings standing on a sheet of grey paper, lit by one low lamp that swings slowly overhead, so every
// ring throws a long shadow away from it. The rings are a slice through a fractal that turns with the music, so
// they swell, split and close; their rims glow with the spectrum and each beat sends a wash of light out from the lamp.
// Family: Fractal
// Tags: Geometric, Calm, Minimal
// Added: 2026-11-15 19:43

// The rings are where the page cuts through a fractal made of bubbles: space is tiled into cubes, each cube
// is turned inside out through a sphere, and that is done again and again, which bends the flat walls between
// the cubes into bubbles inside bubbles. The page is first wrapped onto a ball, so that even a wall which
// stayed flat is cut in a circle. Turning the ball slowly makes the rings grow, merge and vanish. Shadows
// are found by walking from each point of the page towards the lamp and noting how nearly the walk grazes a ring.
static const float Wall = 0.0045;              // half the thickness of a ring's wall

float lsRings(float2 p, float2 lean, float fold)
{
    float3 off = float3(p - float2(0.2, -0.1), 1.15);
    float bend = 3.0 / dot(off, off);
    float3 q = off * bend;
    q.xz = rotate(q.xz, lean.x);
    q.yz = rotate(q.yz, lean.y);
    q += float3(0.37, 0.21, 0.55);
    float grown = bend;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        q = 2.0 * frac(0.5 * q + 0.5) - 1.0;
        float k = fold / max(dot(q, q), 1e-4);
        q *= k;
        grown *= k;
    }

    float3 a = abs(q);
    return min(a.x, min(a.y, a.z)) / grown - Wall;
}

float3 Render(float2 uv, float2 p)
{
    float2 lean = float2(Time * 0.043 + BeatGlide * 0.021, 0.6 + Time * 0.031);
    float fold = 1.36 + 0.07 * sin(Time * 0.083) + Bass * 0.03;
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;

    // The lamp swings across the top of the page.
    float swing = sin(Time * 0.11) * 1.05;
    float2 lamp = float2(sin(swing) * Aspect * 0.8, -cos(swing) * 0.86 + 0.12 * sin(Time * 0.17));
    float2 toLamp = lamp - p;
    float far = length(toLamp);
    float2 way = toLamp / max(far, 1e-4);

    float here = lsRings(p, lean, fold);

    // Walk towards the lamp. The rings are low walls and the lamp hangs a little above the page, so a wall
    // shades the page behind it for a way and then the light clears its top.
    float lightReach = 1.0;
    float longest = far * 0.62;
    float t = 0.008;
    int steps = qualitySteps(40);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        if (t >= longest)
        {
            break;
        }

        float d = lsRings(p + way * t, lean, fold);
        float soft = 0.004 + t * 0.07;
        float cover = saturate(1.0 - d / soft);
        float low = 1.0 - smoothstep(0.3, 1.0, t / longest);
        lightReach = min(lightReach, 1.0 - cover * low);
        t += clamp(d * 0.7, 0.006, 0.07);
    }

    float falls = 1.0 / (1.0 + far * far * 0.6);
    float lampPower = 0.7 + Level * 0.12 + BeatSwell * 0.06 + DropPulse * 0.2;

    // The page: cool grey paper with a little tooth.
    float tooth = 0.94 + 0.06 * noise(p * 190.0) + 0.03 * (fbm(p * 7.0) - 0.5);
    float3 paper = float3(0.74, 0.74, 0.78) * tooth;
    float3 colour = paper * (0.12 + falls * lampPower * 1.15 * (0.06 + 0.94 * lightReach));

    // A wash of light spreading from the lamp on each beat.
    float wash = far - BeatPhase * 2.8;
    float washed = exp(-wash * wash * 7.0) * beatLight * (1.0 - BeatPhase);

    // The rings' rims glow, each distance from the middle of the page listening to its own band.
    float out0 = length(p);
    float band = spectrum(0.02 + saturate(out0 / (Aspect * 1.05)) * 0.7);
    float3 tint = lerp(float3(1.0, 0.72, 0.86), float3(0.86, 0.8, 1.0), 0.5 + 0.5 * sin(out0 * 2.2 - Time * 0.2));
    tint = lerp(tint, float3(1.0, 0.84, 0.76), 0.35 + 0.35 * sin(Time * 0.071));
    float rim = exp(-abs(here) * 260.0);
    float halo = exp(-max(here, 0.0) * 34.0);
    float body = smoothstep(pixelSize(), -pixelSize(), here);
    float glowing = 0.3 + band * 1.5 + washed * 0.9 + Treble * 0.25;
    colour = lerp(colour, tint * (0.5 + 0.5 * glowing), body * 0.9);
    colour += tint * rim * (0.35 + 0.55 * glowing);
    colour += tint * halo * 0.045 * glowing;

    // The lamp itself, and the streak it leaves across the lens.
    colour += float3(1.0, 0.97, 0.94) * exp(-max(far - 0.03, 0.0) * 7.0) * (0.3 + lampPower * 0.9);
    float2 across = rotate(toLamp, 0.5 + 0.2 * sin(Time * 0.05));
    float streak = exp(-abs(across.y) * 150.0) * exp(-abs(across.x) * 0.9);
    colour += lerp(float3(1.0, 0.8, 0.95), float3(1.0, 1.0, 1.0), exp(-abs(across.x) * 3.0)) * streak * (0.2 + lampPower * 0.7);

    return colour * (0.9 + Intensity * 0.2) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.6);
}
