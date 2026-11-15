// Name: Fable Risograph
// A three-colour print fresh off the drum: a pink sun, teal hills and yellow rays, each laid down as its own screen of
// halftone dots a little out of register, making oranges, greens and purples where the inks cross. The sun swells
// with the bass, the hills take the shape of the spectrum, and the rays turn with the beat.
// Family: Minimal
// Tags: Retro, Colourful, Calm
// Added: 2026-11-15 17:45

// How a risograph (or any halftone print) makes a picture: each ink can only be on or off, so a tone is made of
// dots on a grid, bigger where the tone is darker, with each ink's grid turned to a different angle so the
// dots don't fall on top of one another. Inks are see-through, so where two overlap their colours multiply.
static const float Pitch = 0.03;               // from one dot to the next

// How much ink a screen of dots at this angle puts down where the tone is tone (0 none, 1 solid).
float frScreen(float2 p, float angle, float tone)
{
    float2 cell = frac(rotate(p, angle) / Pitch) - 0.5;
    float radius = sqrt(saturate(tone)) * 0.74;
    float pixel = pixelSize() / Pitch;
    return smoothstep(radius + pixel, radius - pixel, length(cell)) * step(0.02, tone);
}

// How loud the music is around the n-th of a row of places across the spectrum.
float frBand(float n)
{
    float at = 0.03 + n * 0.05;
    return (spectrum(at - 0.016) + spectrum(at - 0.008) + spectrum(at) + spectrum(at + 0.008) + spectrum(at + 0.016)) * 0.2;
}

// The outline of one range of hills: a slow swell, with the spectrum's shape standing on it (bass on the
// left), read at a row of places and joined up smoothly so the hills stay rounded. Higher is negative.
float frHills(float x, float k)
{
    float place = (x / (2.0 * Aspect) + 0.5) * 12.0 + k * 0.5;
    float knot = floor(place);
    float loud = lerp(frBand(knot), frBand(knot + 1.0), smoothstep(0.0, 1.0, place - knot));
    return 0.1 * sin(x * (1.1 + 0.35 * k) + k * 2.3 + Time * 0.05 * (1.0 + k)) - loud * (0.3 - 0.05 * k);
}

float3 Render(float2 uv, float2 p)
{
    // Each ink went through the machine separately, and none landed quite where the last did.
    float2 slipPink = float2(0.006, -0.004) + 0.002 * float2(sin(Time * 0.31), cos(Time * 0.23));
    float2 slipTeal = float2(-0.005, 0.005) + 0.002 * float2(cos(Time * 0.27), sin(Time * 0.19));

    // Pink: the sun, darker towards its top, and rings going out from it with the beat.
    float2 sun = float2(0.0, -0.12);
    float2 a = p + slipPink - sun;
    float out0 = length(a);
    float size = 0.5 + Bass * 0.06 + BeatSwell * 0.02 + DropPulse * 0.08;
    float disc = smoothstep(size, size - 0.004, out0);
    float tonePink = disc * (0.5 + 0.45 * saturate(0.5 - a.y / size * 0.5));
    float rings = 0.5 + 0.5 * sin((out0 - size) * 26.0 - BeatGlide * 2.0 * PI);
    tonePink += (1.0 - disc) * smoothstep(0.55, 0.95, rings) * 0.42 * exp(-(out0 - size) * 1.6) * (0.5 + Level * 0.8);

    // Yellow: rays turning about the sun, wider with the treble, over a pale wash of sky.
    float2 b = p - sun;
    float around = (dot(b, b) > 1e-9 ? atan2(b.y, b.x) : 0.0) / (2.0 * PI);
    float ray = abs(frac(around * 14.0 + Time * 0.01 + BeatGlide * 0.04) - 0.5) * 2.0;
    float toneYellow = smoothstep(0.62 - Treble * 0.3, 0.7 - Treble * 0.3, ray) * 0.85 + 0.16;
    // Inside the sun the yellow thins towards the top, so the sun runs from pink down to orange.
    toneYellow = lerp(toneYellow, saturate(0.5 + b.y / size * 0.55), smoothstep(size, size - 0.004, length(b)));

    // Teal: four ranges of hills, each nearer one darker.
    float2 c = p + slipTeal;
    float toneTeal = 0.0;
    for (int k = 0; k < 4; k++)
    {
        float ridge = 0.3 + 0.2 * (float)k + frHills(c.x, (float)k);
        toneTeal = c.y > ridge ? 0.26 + 0.22 * (float)k : toneTeal;
    }

    float pink = frScreen(p + slipPink, radians(15.0), tonePink);
    float yellow = frScreen(p, radians(0.0), toneYellow);
    float teal = frScreen(p + slipTeal, radians(75.0), toneTeal);

    // Ink never goes down perfectly even: it is grainy, and misses a speck here and there.
    float grain = 0.82 + 0.18 * noise(p * 240.0);
    float missed = step(0.03, hash(floor(p * 330.0) * 0.37 + 1.1));
    pink *= grain * missed;
    yellow *= (0.82 + 0.18 * noise(p * 240.0 + 31.0));
    teal *= (0.82 + 0.18 * noise(p * 240.0 + 77.0)) * step(0.03, hash(floor(p * 330.0) * 0.37 + 5.7));

    float3 colour = float3(0.86, 0.83, 0.75) * (0.93 + 0.1 * noise(p * 130.0));     // the paper
    colour *= lerp(1.0, float3(1.0, 0.83, 0.1), yellow * 0.9);
    colour *= lerp(1.0, float3(1.0, 0.2, 0.45), pink * 0.9);
    colour *= lerp(1.0, float3(0.0, 0.5, 0.56), teal * 0.92);
    return colour * (0.76 + Intensity * 0.12) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.5);
}
