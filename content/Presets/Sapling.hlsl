// Name: Sapling
// A tree in flat colour on lime green: one trunk that forks, and forks again, nine times over, down to
// twigs tipped with white blossom. The tree leans with the bass and its twigs shiver with the treble, each
// fork listening to its own part of the music, and the blossom swells on the beat.
// Family: Fractal
// Tags: Minimal, Calm, Colourful
// Added: 2026-11-15 21:06

static const int Forks = 9;

float spBranch(float2 q, float len)
{
    float along = clamp(q.y, 0.0, len);
    return length(q - float2(0.0, along));
}

float3 Render(float2 uv, float2 p)
{
    float3 lime = float3(0.72, 1.32, 0.16);
    float3 colour = lime * (1.04 - 0.2 * uv.y) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.35);

    // The ground: a low hill in a deeper green, and the tree's shadow on it.
    float hill = p.y - 0.8 + 0.05 * cos(p.x * 1.3);
    colour = lerp(colour, float3(0.3, 0.95, 0.12), smoothstep(-0.004, 0.004, hill));
    float2 shadowAt = (p - float2(0.12, 0.86)) * float2(1.4, 9.0);
    colour *= 1.0 - 0.3 * exp(-dot(shadowAt, shadowAt) * 2.0);

    // Walk up the tree. At every fork, fold the two sides together and carry on up one branch.
    float2 q = float2(p.x, 0.84 - p.y);
    q.x -= (0.05 * sin(Time * 0.4) + 0.1 * Bass * sin(BeatGlide * PI * 0.5)) * q.y * q.y;       // the whole tree leans
    float size = 1.0;
    float len = 0.5;
    float wood = 1e9;
    float woodAt = 0.0;
    float tips = 1e9;

    [unroll]
    for (int k = 0; k < Forks; k++)
    {
        float f = (float)k;
        float halfWide = 0.036 - 0.0018 * f;
        float d = (spBranch(q, len) - halfWide) * size;
        if (d < wood)
        {
            wood = d;
            woodAt = f;
        }

        float heard = spectrum(0.02 + 0.06 * f);
        float sway = 0.06 * sin(Time * 0.7 + f * 1.3) * (0.3 + 0.1 * f) + 0.28 * heard * sin(BeatGlide * PI + f * 0.9);
        float fork = 0.47 + 0.07 * sin(f * 2.1) + sway;
        q.y -= len;
        q.x = abs(q.x);
        q = rotate(q, fork);
        q /= 0.75;
        size *= 0.75;
    }

    tips = (length(q) - 0.26 - 0.22 * Treble - 0.12 * BeatSwell) * size;

    float edge = pixelSize() * 1.5;
    float3 bark = lerp(float3(0.1, 0.24, 0.12), float3(0.04, 0.5, 0.16), saturate(woodAt / 8.0));
    colour = lerp(colour, bark, smoothstep(edge, -edge, wood));
    colour = lerp(colour, float3(1.12, 1.1, 1.0), smoothstep(edge, -edge, tips));
    return colour * (0.94 + 0.08 * Level);
}
