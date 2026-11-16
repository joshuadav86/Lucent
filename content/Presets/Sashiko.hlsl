// Name: Sashiko
// White running stitches on indigo cloth, sewn in the old hemp-leaf pattern of six-pointed stars. The cloth
// sways in slow folds, every beat sends a ring of brighter thread out from the middle, and the star around
// each crossing is restitched in coral, gold, or sea green while its part of the spectrum is loud.
// Family: Geometric
// Tags: Geometric, Calm, Retro
// Added: 2026-11-15 20:40

// The pattern is a grid of triangles with a line from the middle of each triangle to its three corners.
static const float Side = 0.46;                 // length of one triangle side
static const float Rise = Side * 0.8660254;     // height of one triangle
static const float Spoke = Side * 0.5773503;    // from a corner to the middle of a triangle

// Distance to a row of stitches lying along `along`, one stitch every `pitch` with a gap between.
float skStitch(float along, float across, float pitch)
{
    float fromMiddle = abs(wrap(along, pitch) - pitch * 0.5);
    return length(float2(max(fromMiddle - pitch * 0.3, 0.0), across));
}

// The stitches that run level and the ones that run straight up, in a grid turned so one side is level.
float skLines(float2 q)
{
    float row = floor(q.y / Rise + 0.5);
    float level = skStitch(q.x, q.y - row * Rise, Side / 6.0);

    // Upright threads pass through every corner and stop at the middle of the triangle above and below it.
    float column = floor(q.x / (Side * 0.5) + 0.5);
    float up = q.y - wrap(column, 2.0) * Rise;
    up -= 2.0 * Rise * floor(up / (2.0 * Rise) + 0.5);
    float upright = abs(up) < Spoke ? skStitch(up, q.x - column * Side * 0.5, Spoke / 3.0) : 1.0;
    return min(level, upright);
}

// The corner of the triangle grid nearest to g.
float2 skCorner(float2 g)
{
    float row = floor(g.y / Rise + 0.5);
    float2 best = float2(0.0, 0.0);
    float nearest = 1e9;
    [unroll]
    for (int k = -1; k <= 1; k++)
    {
        float r = row + (float)k;
        float shift = wrap(r, 2.0) * Side * 0.5;
        float2 corner = float2(floor((g.x - shift) / Side + 0.5) * Side + shift, r * Rise);
        float away = dot(g - corner, g - corner);
        if (away < nearest)
        {
            nearest = away;
            best = corner;
        }
    }

    return best;
}

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float skLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

float3 Render(float2 uv, float2 p)
{
    // Slow folds in the cloth: they shift the pattern a little and catch the light on one side.
    float foldA = p.x * 1.7 + p.y * 0.9 + Time * 0.21;
    float foldB = p.y * 2.3 - p.x * 0.6 - Time * 0.17;
    float fold = 0.03 * sin(foldA) + 0.022 * sin(foldB);
    float slope = 0.03 * 1.7 * cos(foldA) - 0.022 * 0.6 * cos(foldB);

    float2 g = rotate(p, 0.12 + 0.05 * sin(Time * 0.05)) * (1.0 - 0.015 * BeatSwell);
    g += float2(fold, fold * 0.6) + float2(Time * 0.012, Time * 0.007);

    float thread = min(skLines(g), min(skLines(rotate(g, PI / 3.0)), skLines(rotate(g, -PI / 3.0))));
    float width = 0.0052 + 0.0015 * (skLoud(0.08) + skLoud(0.14)) + 0.001 * BeatSwell;
    float edge = pixelSize();

    // The cloth: indigo, with its weave, uneven dye, and the shade of the folds.
    float weave = 0.5 + 0.5 * sin(g.x * 210.0) * sin(g.y * 216.0);
    float dye = 0.8 + 0.4 * fbm(g * 2.6 + 3.0);
    float3 cloth = float3(0.03, 0.065, 0.3) * (0.78 + 0.3 * weave) * dye * (1.0 + slope * 4.0);
    cloth *= 0.72 + 0.28 * smoothstep(width, width * 3.2, thread);    // each stitch sinks into the cloth a little

    // The thread: cream, or a colour while the star it belongs to is loud.
    float2 corner = skCorner(g);
    float pick = hash(corner * 7.3 + 1.9);
    float heard = skLoud(0.07 + 0.6 * hash(corner * 3.1 + 5.7));
    float dyed = smoothstep(0.25, 0.8, heard + Intensity * 0.12);
    float3 accent = pick < 0.34 ? float3(1.6, 0.42, 0.28) : (pick < 0.67 ? float3(1.6, 1.08, 0.22) : float3(0.3, 1.3, 0.85));
    float3 yarn = lerp(float3(1.02, 0.98, 0.88), accent, dyed);

    // Each beat, a ring of brighter thread spreads from the middle.
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;
    float ring = length(p) - BeatPhase * 2.3;
    float lift = exp(-ring * ring * 22.0) * smoothstep(0.0, 0.12, BeatPhase) * (1.0 - BeatPhase) * beatLight;
    float round2 = sqrt(saturate(1.0 - (thread / width) * (thread / width)));
    yarn *= (0.72 + 0.28 * round2) * (0.92 + 0.5 * lift + 0.1 * Level + 0.12 * DropPulse) * (1.0 + slope * 2.0);

    float3 colour = lerp(cloth, yarn, smoothstep(width + edge, width - edge, thread));
    return colour * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.7);
}
