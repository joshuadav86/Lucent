// Name: Iris
// A flight down a throat of camera shutters, one behind another, each a ring of flat paper blades in cream,
// pink, violet, and lime. Every shutter turns its own way and opens with its own part of the spectrum, the
// nearest with the bass, and each beat carries you through one more of them.
// Family: Fly-through
// Tags: Geometric, Colourful, Intense
// Added: 2026-11-15 20:43

static const int Shutters = 10;
static const float Apart = 0.5;     // distance from one shutter to the next

// The flat colours the blades are cut from.
float3 irPaper(float pick)
{
    if (pick < 0.3) return float3(1.22, 1.12, 0.92);
    if (pick < 0.55) return float3(1.4, 0.45, 0.72);
    if (pick < 0.8) return float3(0.62, 0.3, 1.35);
    return float3(0.72, 1.35, 0.22);
}

float3 Render(float2 uv, float2 p)
{
    float travel = Time * 0.2 + BeatGlide * 0.25;
    float first = floor(travel);
    float part = travel - first;
    float3 far = float3(0.035, 0.015, 0.06);
    float3 colour = float3(0.0, 0.0, 0.0);
    float open = 1.0;       // how much of this pixel no shutter has covered yet

    [unroll]
    for (int k = 0; k < Shutters; k++)
    {
        float depth = ((float)k + 1.0 - part) * Apart;
        float seed = wrap(first + (float)k, 64.0);
        float blades = 5.0 + floor(hash(float2(seed, 2.0)) * 5.0);
        float way = hash(float2(seed, 7.0)) > 0.5 ? 1.0 : -1.0;
        float turn = way * (0.25 + 0.4 * hash(float2(seed, 4.0))) * (Time * 0.3 + BeatGlide * 0.35) + seed * 1.3;

        // The nearest shutters listen to the bass, the far ones to the treble.
        float where = saturate(depth / (Shutters * Apart));
        float heard = (spectrum(where * 0.6) + spectrum(where * 0.6 + 0.04) + spectrum(where * 0.6 + 0.08)) / 3.0;
        float hole = 0.3 + 0.16 * hash(float2(seed, 9.0)) + 0.2 * heard + 0.05 * BeatSwell + 0.1 * DropPulse;

        // This pixel, seen on the shutter's own plane.
        float2 q = p * depth;
        float reach = max(length(q), 1e-5);
        float wedge = 2.0 * PI / blades;
        float angle = atan2(q.y, q.x) - turn;

        // A blade is a straight-edged sheet that leaves the hole clear. The blade nearest in angle decides
        // whether the pixel is covered at all.
        float nearest = floor(angle / wedge + 0.5) * wedge;
        float past = reach * cos(angle - nearest) - hole;
        float soft = pixelSize() * depth * 1.5;
        float cover = smoothstep(-soft, soft, past);

        // Each blade lies over the one before it, so the one on show is the last whose edge the pixel is past.
        float spread = acos(saturate(hole / reach));
        float blade = floor((angle + spread) / wedge);
        float own = reach * cos(angle - blade * wedge) - hole;
        float next = hole - reach * cos(angle - (blade + 1.0) * wedge);

        float3 paper = irPaper(hash(float2(seed, 13.0)));
        float accent = hash(float2(wrap(blade, blades), seed + 21.0));
        if (accent < 0.2) paper = irPaper(hash(float2(seed + 3.0, wrap(blade, blades))));
        if (accent > 0.93) paper = float3(0.05, 0.035, 0.08);
        paper *= 0.74 + 0.26 * hash(float2(wrap(blade, blades) + 5.0, seed));
        // The blade above casts a narrow shadow, and each blade has a dark cut edge.
        paper *= 0.55 + 0.45 * smoothstep(0.0, 0.07, next);
        paper *= 0.35 + 0.65 * smoothstep(0.0, soft * 2.0 + 0.004, own);

        float fog = saturate((depth - Apart) / ((Shutters - 1.5) * Apart));
        colour += open * cover * lerp(paper, far, fog * fog * 0.2 + fog * 0.8);
        open *= 1.0 - cover;
    }

    colour += open * far;
    return colour * (0.9 + 0.15 * Intensity) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.5);
}
