// Name: Arc Rings
// Thin concentric arcs turn at their own unhurried speeds like the hands of a minimalist clock,
// one ring for each part of the spectrum, each arc growing and shrinking with how loud its band is.
// Family: Minimal
// Tags: Minimal, Calm, Geometric

static const float RingCount = 22.0;
static const float InnerRadius = 0.16;
static const float OuterRadius = 0.9;

// Distance to an arc of radius r centred on angle 'facing', spanning 'halfAngle' either side, with round ends.
float arDistance(float2 p, float r, float facing, float halfAngle)
{
    float2 q = rotate(p, -facing);
    q.x = abs(q.x);
    float2 sc = float2(sin(halfAngle), cos(halfAngle));
    return (sc.y * q.x > sc.x * q.y) ? length(q - sc * r) : abs(length(q) - r);
}

// A smoothed reading of one band of the spectrum.
float arBand(float i)
{
    float x = 0.02 + (i + 0.5) / RingCount * 0.9;
    return spectrum(x) * 0.5 + (spectrum(x - 0.015) + spectrum(x + 0.015)) * 0.25;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    // A very slow tilt of the whole clock face so it never sits perfectly still.
    float2 q = rotate(p, sin(Time * 0.05) * 0.2);
    float r = length(q);
    float spacing = (OuterRadius - InnerRadius) / (RingCount - 1.0);

    float3 colour = float3(0.012, 0.013, 0.018) + float3(0.02, 0.018, 0.016) * exp(-r * r * 1.5);

    // Only the two nearest rings can touch this pixel.
    float nearest = clamp(round((r - InnerRadius) / spacing), 0.0, RingCount - 1.0);
    [unroll]
    for (int k = -1; k <= 1; k++)
    {
        float i = nearest + k;
        if (i < 0.0 || i > RingCount - 1.0)
        {
            continue;
        }
        float radius = InnerRadius + i * spacing;
        float level = saturate(arBand(i) * 1.15);
        // Each ring has its own speed and direction, as though geared to its neighbours.
        float seed = frac(sin(i * 12.9898) * 43758.5453);
        float speed = (0.05 + 0.25 * seed) * (fmod(i, 2.0) < 0.5 ? 1.0 : -1.0) * (1.0 + Bass * 0.6);
        float facing = Time * speed + seed * 6.2831853;
        float halfAngle = PI * clamp(0.04 + 0.9 * level, 0.02, 0.97);

        // The faint track the arc runs on.
        float track = abs(r - radius);
        colour += float3(0.05, 0.05, 0.055) * saturate(0.5 - (track - px * 0.25) / px) * 0.5;

        // The arc itself: a fine line, a touch thicker on loud bands, with a faint glow.
        float d = arDistance(q, radius, facing, halfAngle);
        float width = px * (0.6 + level * 0.9) + Beat * px * 0.4;
        float lineCover = saturate(0.5 - (d - width) / px);
        // Warm white inside, cooler towards the outside, with a single amber accent on the beat.
        float3 tone = lerp(float3(1.0, 0.86, 0.66), float3(0.70, 0.84, 1.0), i / (RingCount - 1.0));
        float brightness = 0.55 + 0.45 * level + Beat * 0.25;
        colour += tone * (lineCover * brightness + exp(-d / (spacing * 0.18)) * 0.05 * level);

        // A small bead marks the leading tip of each arc, like a clock hand.
        float2 tip = rotate(float2(-sign(speed) * sin(halfAngle), cos(halfAngle)) * radius, facing);
        float tipD = length(q - tip) - width * 1.8;
        colour += tone * saturate(0.5 - tipD / px) * 0.35;
    }

    // The centre: a small still disc that swells slightly on the beat.
    float hub = length(q) - (0.03 + Beat * 0.008 + Bass * 0.01);
    colour = lerp(colour, float3(1.0, 0.72, 0.42) * (0.75 + Level * 0.4), saturate(0.5 - hub / px));
    colour += float3(1.0, 0.6, 0.3) * 0.08 * exp(-max(hub, 0.0) * 30.0);
    return colour;
}
