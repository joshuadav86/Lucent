// Name: Phase Portrait
// The portrait of a complex function: every colour of the wheel spins once round each of its zeros and the other way
// round each pole, ringed by contours like a map. Four zeros and two poles wander the plane, pushed apart by the
// bass, and the whole wheel of colour turns with the beat.
// Family: Geometric
// Tags: Colourful, Geometric
// Added: 2026-11-15 17:45

// Domain colouring. The function is f(z) = (z - a1)(z - a2)(z - a3)(z - a4) / ((z - b1)(z - b2)). Each pixel z
// is painted with the hue of f(z)'s angle, so colours meet in a pinwheel at every zero (a) and pole (b), and
// shaded in rings by its size and in rays by its angle, which draws the curved grid f pulls the plane into.
// With two more zeros than poles, the whole wheel also goes round twice at the edge of the picture.
float2 fpTimes(float2 a, float2 b)
{
    return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

float2 fpOver(float2 a, float2 b)
{
    return float2(a.x * b.x + a.y * b.y, a.y * b.x - a.x * b.y) / max(dot(b, b), 1e-9);
}

// Where the k-th zero or pole is: each has a home, spread round the middle, and rides a slow loop about it.
float2 fpPlace(float k, float clock, float spread)
{
    float turn = clock * (0.6 + 0.17 * k) + k * 2.4;
    float2 home = float2(cos(k * 2.399), sin(k * 2.399)) * (0.3 + 0.15 * k);
    return (home + 0.36 * float2(cos(turn), sin(turn * 1.3 + k))) * spread;
}

float3 Render(float2 uv, float2 p)
{
    float2 z = float2(p.x, -p.y) * 1.3;
    float clock = Time * 0.12 + BeatGlide * 0.07;
    float spread = 0.95 + Bass * 0.2 + DropPulse * 0.15;

    float2 top = float2(1.0, 0.0);
    float2 bottom = float2(1.0, 0.0);
    float nearest = 1e9;
    for (int k = 0; k < 6; k++)
    {
        float2 place = fpPlace((float)k, clock, spread);
        if (k == 1 || k == 4)
        {
            bottom = fpTimes(bottom, z - place);
        }
        else
        {
            top = fpTimes(top, z - place);
        }

        nearest = min(nearest, length(z - place));
    }

    float2 w = fpOver(top, bottom);
    float size = 0.5 * log(max(dot(w, w), 1e-12));
    float angle = (dot(w, w) > 1e-12 ? atan2(w.y, w.x) : 0.0) / (2.0 * PI);

    // The wheel of colour turns with the beat.
    float hue = frac(angle + Time * 0.02 + BeatGlide * 0.03);
    // Rings of equal size and rays of equal angle: each a soft ramp, like the shading of a contour map.
    float flowing = size * 1.5 - Time * 0.05;
    float rings = frac(flowing);
    float rays = frac(angle * 12.0);
    float band = spectrum(0.03 + frac(floor(flowing) * 0.137) * 0.7);
    float shade = (0.45 + 0.55 * rings) * (0.7 + 0.3 * rays);
    float3 colour = hsv2rgb(hue, 0.8, 1.0) * shade * (0.5 + band * 0.7 + BeatSwell * 0.08);

    // Fine dark lines where the ramps start again.
    colour *= 0.6 + 0.4 * smoothstep(0.0, 0.05, min(rings, 1.0 - rings));
    colour *= 0.75 + 0.25 * smoothstep(0.0, 0.05, min(rays, 1.0 - rays));

    // The zeros and poles themselves: small bright eyes.
    colour += float3(1.0, 0.95, 0.85) * exp(-nearest * nearest * 900.0) * (0.5 + Treble * 1.2);
    return colour * (0.85 + Intensity * 0.3 + DropPulse * 0.2) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.7);
}
