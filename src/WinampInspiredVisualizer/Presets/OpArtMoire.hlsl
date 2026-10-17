// Name: Op Art Moire
// Two gratings of flower-shaped rings drift across each other in a twelvefold mirror, and their interference
// blooms into a turning mandala of moire petals in flowing colour that swell with the bass and ripple with the beat.
// Family: Kaleidoscope
// Tags: Colourful, Geometric, Retro

// Anti-aliased square wave of a phase; fades to grey where stripes get finer than a couple of pixels.
float omStripes(float phase, float perPixel)
{
    float x = abs(frac(phase) - 0.5) - 0.25;
    float w = max(perPixel, 1e-4);
    float aa = saturate(x / w + 0.5);
    return lerp(aa, 0.5, saturate(w * 2.5 - 0.6));
}

// Phase of flower-shaped rings around centre c: frequency f, n petals of relative depth a.
float omFlower(float2 p, float2 c, float f, float n, float a, float turn, out float gradient)
{
    float2 d = p - c;
    float r = max(length(d), 1e-4);
    float theta = atan2(d.y, d.x);
    float s = 0.0;
    float co = 0.0;
    sincos(n * theta + turn, s, co);
    float radial = 1.0 + a * s;
    gradient = f * sqrt(radial * radial + (a * n * co) * (a * n * co));
    return f * r * radial;
}

float3 Render(float2 uv, float2 p)
{
    float px = 2.0 / Resolution.y;
    float beatPhase = BeatCount - Beat;

    // Fold the screen into twelve mirror images so the interference forms a turning mandala.
    float2 k = kaleidoscope(rotate(p, Time * 0.025), 6.0);

    // Two ring systems orbit each other; their frequencies differ slightly so the moire never settles.
    float2 c1 = float2(0.45 + 0.3 * sin(Time * 0.13), 0.12 + 0.12 * cos(Time * 0.11));
    float2 c2 = float2(0.45 + 0.3 * sin(Time * 0.09 + 2.0), 0.12 + 0.12 * cos(Time * 0.15 + 1.0));
    float f1 = 18.0 + Bass * 1.5;
    float f2 = 18.0 + 1.4 * sin(Time * 0.05) + Bass * 1.5;
    float a1 = 0.07 + 0.05 * sin(Time * 0.07) + Beat * 0.03;
    float a2 = 0.06 + 0.05 * sin(Time * 0.083 + 1.0) + Beat * 0.03;

    float g1 = 0.0;
    float g2 = 0.0;
    float phase1 = omFlower(k, c1, f1, 6.0, a1, Time * 0.3 + beatPhase * 0.1, g1) - Time * 0.4;
    float phase2 = omFlower(k, c2, f2, 5.0, a2, -Time * 0.25, g2) - Time * 0.33;
    float s1 = omStripes(phase1, g1 * px);
    float s2 = omStripes(phase2, g2 * px);

    // XOR of the two gratings: the classic op-art interference.
    float xorMask = s1 + s2 - 2.0 * s1 * s2;

    // Colour follows the moire itself: the phase difference sweeps hue across each interference petal.
    float moire = phase1 - phase2;
    // Kept to the cyan, blue, violet, magenta and coral side of the wheel; dim yellows turn muddy.
    float hue = 0.75 + 0.32 * sin((Time * 0.02 + moire * 0.03) * 6.2831853);
    float3 bright = hsv2rgb(hue, 0.75, 1.0);
    float3 deep = hsv2rgb(hue + 0.5, 0.9, 1.0);

    float3 colour = bright * xorMask * (0.45 + Level * 0.35) + deep * (1.0 - xorMask) * 0.035;

    // A beat ripple runs outward through the rings.
    float r = length(p);
    colour *= 1.0 + Beat * 0.6 * exp(-abs(r - (1.0 - Beat) * 1.6) * 5.0);

    return colour;
}
