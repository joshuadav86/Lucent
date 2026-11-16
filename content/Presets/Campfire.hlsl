// Name: Campfire
// A cartoon campfire in sherbet colours on turquoise: seven tongues of flame, orange outside, pink within and
// cream at the heart. Each tongue leaps with its own part of the music, the bass in the middle and the treble
// at the edges, while the flames lick and sway, sparks fly up in time with the beat, and a drop makes it roar.
// Family: Music-shaped
// Tags: Fire, Colourful, Minimal
// Added: 2026-11-16 00:01

// Which part of the music each tongue listens to, left to right: bass in the middle.
static const float CfHears[7] = { 0.6, 0.34, 0.2, 0.08, 0.14, 0.46, 0.72 };

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float cfLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

// How far inside the fire this spot is (above zero is inside), for a fire `wide` times as wide and `tall` times
// as tall as the whole one. The three colours of the flame are the same fire at three sizes.
float cfInside(float x, float up, float wide, float tall)
{
    float inside = -1.0;

    [unroll]
    for (int k = 0; k < 7; k++)
    {
        float f = (float)k;
        float heard = cfLoud(CfHears[k]);
        float centre = (f - 3.0) * 0.37 + 0.04 * sin(Time * 0.8 + f * 2.3);
        float reach = (0.22 + 1.15 * heard + 0.3 * DropPulse) * tall * (1.0 - 0.04 * abs(f - 3.0));
        float across = 0.36 * wide;
        float u = saturate(1.0 - abs(x - centre) / across);
        float top = reach * pow(u, 1.35);
        float slope = reach * 1.35 * pow(max(u, 0.001), 0.35) / across;
        inside = max(inside, (top - up) / sqrt(1.0 + slope * slope));
    }

    return inside;
}

float cfLog(float2 x, float2 a, float2 b, float thick)
{
    float2 ab = b - a;
    float along = saturate(dot(x - a, ab) / dot(ab, ab));
    return length(x - a - ab * along) - thick;
}

float3 Render(float2 uv, float2 p)
{
    // Laid out for a wide screen and squeezed sideways to fit a narrow one.
    float2 q = float2(p.x * 1.6 / max(Aspect, 0.3), p.y);
    float e = pixelSize() * 1.5;

    float3 colour = float3(0.07, 0.8, 0.76) * (1.06 - 0.26 * uv.y);
    colour = lerp(colour, float3(0.03, 0.5, 0.52), smoothstep(-e, e, q.y - 0.8 - 0.02 * cos(q.x * 1.7)));

    // A warm glow on the air round the fire.
    float2 glowAt = (q - float2(0.0, 0.3)) * float2(0.55, 0.8);
    colour += float3(0.5, 0.3, 0.05) * exp(-dot(glowAt, glowAt) * 2.2) * (0.3 + 0.08 * Bass);

    float base = 0.68;
    float up = base - q.y;
    float rise = saturate(up * 1.5);
    float lick = 0.06 * sin(up * 7.0 - Time * 4.2 + q.x * 2.0) + 0.17 * (noise(float2(q.x * 2.4, up * 3.2 - Time * 2.6)) - 0.5);
    // The fire is narrow where it leaves the logs and fans out as it climbs.
    float x = q.x / (0.62 + 0.42 * saturate(up * 1.3)) + lick * rise;
    float floorMask = smoothstep(-0.02, 0.02, up);

    float outer = cfInside(x, up, 1.0, 1.0);
    float middle = cfInside(x + 0.4 * lick * rise, up + 0.02, 0.62, 0.7);
    float inner = cfInside(x, up + 0.03, 0.32, 0.42);
    colour = lerp(colour, float3(1.75, 0.46, 0.12), smoothstep(-e, e, outer) * floorMask);
    colour = lerp(colour, float3(1.6, 0.62, 0.74), smoothstep(-e, e, middle) * floorMask);
    colour = lerp(colour, float3(1.5, 1.36, 0.98), smoothstep(-e, e, inner) * floorMask);

    // Sparks: each one rises and dies over a few beats.
    [unroll]
    for (int k = 0; k < 12; k++)
    {
        float f = (float)k;
        float seed = hash(float2(f, 3.7));
        float side = hash(float2(f, 11.3));
        float life = frac(BeatGlide * (0.22 + 0.2 * seed) + seed * 7.0);
        float2 at = float2((side - 0.5) * 1.5 + 0.12 * sin(life * 7.0 + f), base - 0.25 - life * (0.9 + 0.5 * side));
        float size = 0.02 * (1.0 - life) * (0.6 + 0.8 * seed);
        float spark = smoothstep(e, -e, length(q - at) - size) * smoothstep(0.0, 0.08, life);
        colour = lerp(colour, float3(1.5, 1.3, 0.9), spark);
    }

    // Two crossed logs, each with a paler strip along its top and a cut end.
    float logA = cfLog(q, float2(-0.86, 0.83), float2(0.62, 0.64), 0.075);
    float logB = cfLog(q, float2(0.86, 0.83), float2(-0.62, 0.64), 0.075);
    float topA = cfLog(q + float2(0.0, 0.03), float2(-0.8, 0.83), float2(0.6, 0.65), 0.02);
    float topB = cfLog(q + float2(0.0, 0.03), float2(0.8, 0.83), float2(-0.6, 0.65), 0.02);
    float3 bark = float3(0.36, 0.1, 0.16);
    float3 barkLit = float3(0.8, 0.26, 0.24);
    colour = lerp(colour, bark * 0.8, smoothstep(e, -e, logB));
    colour = lerp(colour, barkLit * 0.8, smoothstep(e, -e, topB));
    colour = lerp(colour, bark, smoothstep(e, -e, logA));
    colour = lerp(colour, barkLit, smoothstep(e, -e, topA));
    float endA = length((q - float2(-0.86, 0.83)) * float2(1.7, 1.0)) - 0.075;
    float endB = length((q - float2(0.86, 0.83)) * float2(1.7, 1.0)) - 0.075;
    float ends = min(endA, endB);
    colour = lerp(colour, float3(1.25, 0.8, 0.55), smoothstep(e, -e, ends));
    colour = lerp(colour, float3(0.8, 0.4, 0.32), smoothstep(e, -e, abs(ends + 0.035) - 0.007));

    return colour * (0.95 + 0.06 * Level);
}
