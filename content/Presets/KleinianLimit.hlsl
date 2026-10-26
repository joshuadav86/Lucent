// Name: Kleinian Limit
// The limit set of a morphing Kleinian group: necklaces of jewelled pearls that coil into lace-fine spirals and slowly turn inside out.
// Family: Fractal
// Tags: Calm, Dark, Geometric
// Added: 2026-10-16 23:54

float3 kleinianJewel(float t)
{
    return 0.5 + 0.5 * cos(2.0 * PI * (t + float3(0.0, 0.33, 0.67)));
}

float3 Render(float2 uv, float2 p)
{
    // Maskit group a(z) = 1/z + mu, b(z) = z + 2 with mu = -kb + i ka. The path hugs the edge of the
    // Maskit slice, where the necklaces coil into spirals.
    float ka = 1.91 + 0.045 * sin(Time * 0.043);
    float kb = 0.07 - 0.55 * (ka - 1.86) + 0.012 + 0.024 * (0.5 + 0.5 * sin(Time * 0.061 + 1.3)) + Mid * 0.006;

    // View: the plane inverted about a point drifting along the band, z = c + 1/w, so the endless
    // frieze closes into a necklace that keeps turning itself inside out.
    const float viewScale = 1.15;
    float2 w = rotate(p, Time * 0.02) * viewScale;
    float w2 = max(dot(w, w), 1.0e-8);
    float2 centre = float2(wrap(Time * 0.045, 2.0), ka * 0.5 + 0.38 * sin(Time * 0.067));
    float2 z = centre + float2(w.x, -w.y) / w2;
    float pixel = (2.0 * viewScale / min(Resolution.y, 540.0)) / w2;     // glow scale: 1/540th of the screen, in z units
    float linePixel = (2.0 * viewScale * pixelSize()) / w2;             // line widths: two screen pixels, in z units

    // Jos Leys' algorithm: fold into the fundamental strip, apply a or a^-1, until the orbit cycles.
    float f = 1.0;
    float scale = 1.0;
    float n = 0.0;
    bool cycled = false;
    float2 last = z + 1.0;
    float2 beforeLast = z - 1.0;
    int iterations = qualitySteps(56);
    [loop]
    for (int i = 0; i < iterations; i++)
    {
        float skew = kb / ka;
        z.x += skew * z.y;
        z.x = wrap(z.x + 1.0, 2.0) - 1.0;
        z.x -= skew * z.y;

        float side = z.x + kb * 0.5;
        float separation = ka * 0.5 + f * (2.0 * ka - 1.95) * 0.25 * (side < 0.0 ? -1.0 : 1.0)
                         * (1.0 - exp(-(7.2 - (1.95 - ka) * 15.0) * abs(side)));
        if (z.y >= separation)
        {
            z = float2(-kb, ka) - z;
        }

        float r2 = max(dot(z, z), 1.0e-12);
        z = float2(-kb + z.x / r2, ka - z.y / r2);
        scale /= r2;

        float2 gap = z - beforeLast;
        if (dot(gap, gap) < 1.0e-6)
        {
            cycled = true;
            break;
        }
        beforeLast = last;
        last = z;
        n += 1.0;
    }

    // Each tile of the tiling is an image of the fundamental domain; its size on screen measures how
    // close the pixel is to the limit set, which glows where tiles shrink below a pixel.
    float tilePx = cycled ? 1.0 / max(scale * pixel, 1.0e-20) : 0.0;
    float tileLinePx = cycled ? 1.0 / max(scale * linePixel, 1.0e-20) : 0.0;   // the same, measured for line widths
    float lace = saturate(1.5 - tileLinePx * 0.7);
    float aura = exp(-tilePx * 0.012);
    float halo = exp(-tilePx * (0.16 - Bass * 0.06));

    float hue = (BeatCount - Beat) * 0.025 + Time * 0.01;
    float3 jewel = kleinianJewel(n * 0.075 + hue);
    float3 colour = jewel * (0.025 + 0.3 * exp(-tilePx * 0.045))
                  + lerp(jewel, float3(0.85, 0.92, 1.0), 0.7) * lace * (0.9 + Bass * 0.4 + Beat * 0.25)
                  + jewel * halo * 0.22
                  + kleinianJewel(hue + 0.5) * aura * (0.05 + Bass * 0.04);

    // Treble glitter running along the lace.
    float glitter = step(0.93, hash(floor(p * 0.5 / pixelSize()) + floor(Time * 10.0)));
    colour += float3(0.8, 0.9, 1.0) * glitter * lace * Treble * 1.2;
    return colour + previous(uv) * 0.1;
}
