// Name: Paper Birds
// A paper-cut sky: layers of white cloud sliding past, and fifteen red birds flying in a V. The V is a
// picture of the music. The leader takes the bass and each bird behind takes a higher note, so the two lines
// ripple as the song plays. The cloud layers rise and fall with it, the sun swells with the bass, and the
// birds beat their wings in time with the beat.
// Family: Music-shaped
// Tags: Calm, Colourful, Minimal
// Added: 2026-11-15 21:09

// Distance from q to the line from a to b, which is `wideA` thick at a and `wideB` thick at b.
float pbStroke(float2 q, float2 a, float2 b, float wideA, float wideB)
{
    float2 ab = b - a;
    float along = saturate(dot(q - a, ab) / max(dot(ab, ab), 1e-6));
    return length(q - a - ab * along) - lerp(wideA, wideB, along);
}

// One bird at the origin, `size` across, its wings `flap` up (1) or down (-1).
float pbBird(float2 q, float size, float flap)
{
    q /= size;
    q.x = abs(q.x);
    float2 elbow = float2(0.45, -0.1 - 0.22 * flap);
    float2 tip = float2(1.0, 0.05 - 0.5 * flap);
    float wing = min(pbStroke(q, float2(0.0, 0.0), elbow, 0.13, 0.1), pbStroke(q, elbow, tip, 0.1, 0.015));
    float body = length(q * float2(2.2, 1.0) - float2(0.0, 0.05)) - 0.2;
    return min(wing, body) * size;
}

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float pbLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

// The whole V. Returns the distance to the nearest bird.
float pbFlock(float2 q, float wing)
{
    float d = 1e9;
    [unroll]
    for (int k = 0; k < 8; k++)
    {
        float f = (float)k;
        float heard = pbLoud(0.07 + 0.08 * f);
        float size = (0.135 - 0.007 * f) * (0.86 + 0.26 * heard);
        float lift = 0.36 * heard + 0.018 * sin(Time * 1.1 + f * 1.7);
        float flap = sin(wing - f * 0.55);
        float2 home = float2(0.95 - 0.26 * f, -0.2 + 0.1 * f - lift);
        d = min(d, pbBird(q - home, size, flap));
        if (k > 0)
        {
            float2 mate = float2(home.x, -0.2 - 0.1 * f - lift);
            d = min(d, pbBird(q - mate, size, flap));
        }
    }

    return d;
}

float3 Render(float2 uv, float2 p)
{
    // Sky, deep blue at the top and pale at the bottom, with a paper sun.
    float3 colour = lerp(float3(0.16, 0.55, 1.4), float3(0.75, 0.95, 1.15), smoothstep(-1.0, 0.9, p.y));
    float2 sunAt = p - float2(-1.0, -0.45);
    float sunSize = 0.21 + 0.02 * (pbLoud(0.08) + pbLoud(0.14));                     // the sun swells with the bass
    colour = lerp(colour, float3(1.5, 1.2, 0.45), smoothstep(sunSize + 0.005, sunSize - 0.005, length(sunAt)));

    float drift = Time * 0.05 + BeatGlide * 0.03;
    float wing = BeatGlide * 2.0 * PI;
    float2 q = p - float2(0.0, 0.05 * sin(Time * 0.23));

    // Cloud layers behind the birds, the birds with their shadow, then the nearest cloud layer in front.
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        if (k == 3)
        {
            float shade = pbFlock(q - float2(0.03, 0.045), wing);
            colour *= 1.0 - 0.22 * smoothstep(0.02, -0.01, shade);
            float birds = pbFlock(q, wing);
            float3 red = float3(1.55, 0.2, 0.12) * (0.85 + 0.25 * smoothstep(0.0, -0.03, birds));
            colour = lerp(colour, red, smoothstep(0.003, -0.003, birds));
        }

        float f = (float)k;
        float pace = wrap(drift * (0.5 + 0.45 * f), 97.0);
        float top = 0.12 + 0.3 * f + 0.16 * sin(p.x * 0.9 + f * 2.0 + pace * 0.7) - 0.11 * pbLoud(0.1 + 0.16 * f);     // each layer rises with its own part of the music
        float puffs = 0.2 * fbm(float2(p.x * (1.5 - 0.15 * f) + pace + f * 13.7, f * 5.1)) + 0.06 * noise(float2(p.x * 7.0 + pace * 4.0, f * 9.3));
        float edge = p.y - top + puffs;
        float3 paper = lerp(float3(0.92, 1.0, 1.14), float3(1.14, 1.12, 1.06), f / 3.0);
        paper *= 1.0 - 0.12 * saturate(edge * 1.6);
        colour *= 1.0 - 0.2 * smoothstep(-0.05, 0.0, edge) * step(edge, 0.0);
        colour = lerp(colour, paper, smoothstep(0.0, 0.006, edge));
    }

    return colour * (0.94 + 0.08 * Level) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.35);
}
