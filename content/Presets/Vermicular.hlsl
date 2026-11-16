// Name: Vermicular
// Slow clouds of colour laid down in five inks, navy, cobalt, pink, yellow, and cream, through a worm-like grain:
// where one ink gives way to the next, the two lace together in fine wriggling threads. The clouds drift and
// fold while the grain stays put. The music plays in the ink: each part of the sheet brightens with its own part
// of the music, bass at the left and treble at the right, and each beat sends a faint ring of lighter ink out
// from the middle.
// Family: Organic
// Tags: Colourful, Retro, Calm
// Added: 2026-11-16 04:48

float rgLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

float3 rgInk(float n)
{
    float3 c = float3(0.03, 0.04, 0.16);
    c = n > 0.5 ? float3(0.1, 0.25, 1.1) : c;
    c = n > 1.5 ? float3(1.4, 0.25, 0.6) : c;
    c = n > 2.5 ? float3(1.5, 1.15, 0.2) : c;
    c = n > 3.5 ? float3(1.25, 1.2, 1.0) : c;
    return c;
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since);

    // The clouds: one slow field of hills folded through another.
    float2 q = rotate(p, 0.2 * sin(Time * 0.04)) * 0.75;
    float2 fold = float2(fbm(q * 1.2 + float2(Time * 0.031, 4.7)), fbm(q * 1.2 + float2(9.2, -Time * 0.027)));
    float cloud = fbm(q + 1.6 * fold + float2(-Time * 0.017, Time * 0.012));

    // How loud this part of the sheet's music is, read at eight fixed places across it and blended between them.
    float k = saturate(0.5 + 0.5 * p.x / Aspect) * 7.0;
    float low = floor(k);
    float loud = lerp(rgLoud(0.08 + 0.075 * low), rgLoud(0.155 + 0.075 * low), smoothstep(0.0, 1.0, k - low));
    float ring = length(p) - since * 2.2 * max(Aspect, 1.0);
    float lift = 0.05 * flash * exp(-ring * ring * 30.0);

    // The grain: threads that follow the contours of a fine field of bumps.
    float2 g = p / (pixelSize() * max(7.0, 1.0 / (150.0 * pixelSize())));
    float grain = frac(noise(g) * 2.6 + noise(g * 0.31 + 5.3) * 1.9);

    // Each spot takes the ink its cloud calls for, or the next one up where the grain lets it through.
    float level = saturate((cloud + lift - 0.24) / 0.5) * 4.0;
    float x = level + grain;
    float step1 = floor(x);
    float3 colour = lerp(rgInk(step1 - 1.0), rgInk(step1), smoothstep(0.0, 0.14, x - step1));
    return colour * (0.82 + 0.42 * loud) * (1.0 - 0.1 * dot(p, p) / (1.0 + Aspect * Aspect));
}
