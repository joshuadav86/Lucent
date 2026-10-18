// Name: Spectral Sunflower
// Thousands of glowing seeds packed in golden spirals that bend and re-form as the angle between them drifts; the spectrum swells them around the circle and every beat ripples out through the head.
// Family: Music-shaped
// Tags: Colourful, Geometric
// Added: 2026-10-16 23:54

static const float SeedCount = 2600.0;
static const float SeedScale = 0.0245;          // seed n sits at radius SeedScale * sqrt(n + 0.5)
static const float Golden = 0.38196601;         // the golden angle, as a fraction of a turn

// The spectrum around the circle: mirrored, bass at the bottom and treble at the top.
float sunLevel(float u)
{
    float x = 0.03 + u * 0.85;
    float s = spectrum(x) * 0.5 + (spectrum(x - 0.02) + spectrum(x + 0.02)) * 0.25;
    return s * s;
}

float3 Render(float2 uv, float2 p)
{
    float px = 2.0 / Resolution.y;
    // The angle between seeds drifts either side of golden, so the spirals bend, break into spokes and re-form.
    float turn = Golden + 0.0032 * sin(Time * 0.045) + 0.0008 * sin(Time * 0.11);
    float spin = Time * 0.04;
    float2 q = rotate(p, spin) * 1.05;
    float r = length(q) + 1e-5;
    float u = (r / SeedScale) * (r / SeedScale) - 0.5;
    float v = atan2(q.y, q.x) / (2.0 * PI);

    // The seeds near this pixel form a lattice in (index, angle). Reduce its basis (Lagrange-Gauss) in the
    // local distance metric, then only a handful of seeds around this pixel's lattice cell need checking.
    float2 metric = float2(SeedScale * SeedScale / (2.0 * r), 2.0 * PI * r);
    float2 nm1 = float2(1.0, 0.0);
    float2 nm2 = float2(0.0, 1.0);
    [loop]
    for (int it = 0; it < 40; it++)
    {
        float2 e1 = float2(nm1.x, nm1.x * turn - nm1.y) * metric;
        float2 e2 = float2(nm2.x, nm2.x * turn - nm2.y) * metric;
        if (dot(e1, e1) > dot(e2, e2))
        {
            float2 swap = nm1;
            nm1 = nm2;
            nm2 = swap;
            float2 swapE = e1;
            e1 = e2;
            e2 = swapE;
        }
        float mu = round(dot(e1, e2) / dot(e1, e1));
        if (mu == 0.0)
        {
            break;
        }
        nm2 -= mu * nm1;
    }
    float2 w0 = float2(nm1.x, nm1.x * turn - nm1.y);
    float2 w1 = float2(nm2.x, nm2.x * turn - nm2.y);
    float det = w0.x * w1.y - w1.x * w0.y;
    float a = floor((u * w1.y - w1.x * v) / det);
    float b = floor((w0.x * v - u * w0.y) / det);

    float sinceBeat = -log(max(Beat, 1e-3)) / 5.0;
    float ripple = Beat > 0.01 ? sinceBeat * 1.9 : -10.0;
    float rim = SeedScale * sqrt(SeedCount);

    float3 colour = 0.0;
    [unroll]
    for (int j = -1; j <= 2; j++)
    {
        [unroll]
        for (int i = -1; i <= 2; i++)
        {
            float n = (a + i) * nm1.x + (b + j) * nm2.x;
            if (n >= 0.0 && n < SeedCount)
            {
                float seedR = SeedScale * sqrt(n + 0.5);
                float seedA = 2.0 * PI * frac(n * turn);
                float2 seed = seedR * float2(cos(seedA), sin(seedA));
                float d = length(q - seed);
                // Where this seed is on screen decides which band of the spectrum it hears.
                float2 onScreen = rotate(seed, -spin);
                float level = sunLevel(abs(atan2(onScreen.x, onScreen.y)) / PI);
                float wave = exp(-(seedR - ripple) * (seedR - ripple) * 50.0) * Beat;
                float swell = 0.25 + 0.9 * level + wave * 0.8;
                float size = SeedScale * (0.2 + 0.26 * saturate(swell)) * (0.6 + 0.4 * seedR / rim);
                float3 hue = 0.5 + 0.5 * cos(2.0 * PI * (n / SeedCount * 1.5 - Time * 0.05 + float3(0.0, 0.33, 0.67)));
                hue = hue * hue * 1.15 + 0.04;
                float body = saturate((size - d) / px + 0.5);
                float halo = exp(-d / (size * 1.2));
                float fade = smoothstep(1.06, 0.9, seedR / rim);
                colour += (lerp(hue, 1.0, 0.3) * body * (0.4 + 1.2 * swell) + hue * halo * 0.3 * (0.3 + swell)) * fade;
            }
        }
    }
    // A soft glow in the heart of the flower.
    colour += float3(1.0, 0.8, 0.55) * exp(-r * r * 30.0) * (0.12 + Bass * 0.15);
    return colour;
}
