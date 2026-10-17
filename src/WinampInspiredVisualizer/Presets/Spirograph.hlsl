// Name: Spirograph
// Two glowing pens trace looping spirograph flowers that slowly fade behind them; every few bars the
// gears change and a new figure blooms over the last, its petals swelling with the bass and coloured by the music.
// Family: Geometric
// Tags: Neon, Geometric, Colourful

// The gear sets: rolling-gear size as a fraction of the ring (teeth q over p), how far out the pen sits,
// and whether the gear rolls inside the ring (0) or around the outside (1).
static const float4 SP_GEARS[10] = {
    float4(5.0, 13.0, 1.20, 0.0),
    float4(3.0, 11.0, 0.75, 1.0),
    float4(7.0, 17.0, 1.35, 0.0),
    float4(4.0, 15.0, 0.70, 0.0),
    float4(5.0, 16.0, 1.10, 1.0),
    float4(6.0, 19.0, 0.90, 0.0),
    float4(7.0, 24.0, 1.25, 0.0),
    float4(3.0, 14.0, 0.60, 1.0),
    float4(8.0, 21.0, 1.40, 0.0),
    float4(5.0, 18.0, 0.85, 1.0)
};

float2 spPen(float t, float r, float d, float outside, float scale)
{
    float2 at;
    if (outside > 0.5)
    {
        float k = (1.0 + r) / r;
        at = float2((1.0 + r) * cos(t) - d * cos(k * t), (1.0 + r) * sin(t) - d * sin(k * t));
    }
    else
    {
        float k = (1.0 - r) / r;
        at = float2((1.0 - r) * cos(t) + d * cos(k * t), (1.0 - r) * sin(t) - d * sin(k * t));
    }
    return at * scale;
}

float spSegment(float2 q, float2 a, float2 b)
{
    float2 ab = b - a;
    float h = saturate(dot(q - a, ab) / max(dot(ab, ab), 1e-9));
    return length(q - a - ab * h);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // A new gear set every thirty-two beats (eight bars).
    int figure = (int)floor(BeatCount / 32.0);
    float4 gear = SP_GEARS[figure % 10];
    float r = gear.x / gear.y;
    float d = gear.z * r * (1.0 + Bass * 0.08);
    float outside = gear.w;
    float extent = (outside > 0.5 ? 1.0 + r : 1.0 - r) + gear.z * r * 1.12;
    float scale = 0.84 / extent;
    float2 q = rotate(p, -figure * 0.7);

    // The two pens close the whole figure in about seven seconds, then keep retracing it.
    float period = 2.0 * PI * gear.x;
    float rate = period / 14.0;
    float tNow = Time * rate;
    float tThen = (Time - min(DeltaTime, 0.1)) * rate;

    // Colour follows the music: warm when the bass leads, cool when the treble does.
    float tilt = Treble - Bass;
    float hueBase = 0.58 + tilt * 0.35 + 0.08 * sin(Time * 0.05) + figure * 0.13;

    float3 fresh = float3(0.0, 0.0, 0.0);
    [unroll]
    for (int pen = 0; pen < 2; pen++)
    {
        float offset = pen * period * 0.5;
        float nearest = 1e3;
        float2 prevAt = spPen(tThen + offset, r, d, outside, scale);
        // Draw this frame's stretch of the curve as short straight pieces.
        [unroll]
        for (int s = 1; s <= 10; s++)
        {
            float2 at = spPen(lerp(tThen, tNow, s / 10.0) + offset, r, d, outside, scale);
            nearest = min(nearest, spSegment(q, prevAt, at));
            prevAt = at;
        }
        float3 ink = hsv2rgb(hueBase + pen * 0.16, 0.75, 1.0);
        float core = saturate(1.0 - nearest / px);
        float halo = exp(-nearest / (px * 3.5)) * 0.35;
        fresh = max(fresh, ink * (core * (1.0 + spectrum(0.1 + pen * 0.4) * 0.8) + halo));

    }

    // A faint guide ring, the fixed gear the pens roll against, flashes on the beat.
    float ringRadius = scale;
    float ringDist = abs(length(q) - ringRadius);
    float3 background = float3(0.006, 0.008, 0.02) + float3(0.02, 0.03, 0.07) * exp(-dot(p, p) * 1.2);
    background += float3(0.05, 0.06, 0.12) * saturate(1.0 - ringDist / px) * (0.5 + Beat * 0.6);

    // Keep the strongest of the fading old ink and the fresh ink, so crossings never pile up into hot spots.
    float fade = exp(-min(DeltaTime, 0.1) * (0.14 + Beat * 0.2));
    float3 old = previous(uv) * fade;
    return max(old, background + fresh);
}
