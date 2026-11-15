// Name: Soap Film
// A soap film stretched across the window, slowly draining: its real colours, the ones light makes when it reflects off
// both faces of a film a few hundred millionths of a millimetre thick. It runs black where it is thinnest, the bass
// thickens it, every beat sends a ripple through it and the music stirs its swirls.
// Family: Organic
// Tags: Colourful, Calm, Water
// Added: 2026-11-15 17:45

// Thin-film interference. Light reflecting off the front of the film meets light reflecting off the back, and
// how much of each colour survives depends on the film's thickness: sin^2(2 pi n d / wavelength), with n the
// film's refractive index (1.33) and d its thickness. Taking that for red, green and blue light gives the
// colours of a real film: black, silver-white, straw, magenta, blue, green, then fading pinks and greens.
static const float Thickest = 1050.0;          // nanometres, at the bottom of the frame
static const float3 Wavelengths = float3(640.0, 535.0, 455.0);

float3 fsFilm(float thickness)
{
    float3 phase = 2.0 * PI * 1.33 * thickness / Wavelengths;
    float3 light = sin(phase);
    return light * light;
}

// How loud the music is around the k-th of ten places across the spectrum.
float fsBand(float k)
{
    float at = 0.03 + k * 0.075;
    return (spectrum(at - 0.02) + spectrum(at - 0.01) + spectrum(at) + spectrum(at + 0.01) + spectrum(at + 0.02)) * 0.2;
}

// The film's thickness, 0..1, before it is turned into nanometres.
float fsThickness(float2 p, float flow)
{
    float2 q = p * 0.85;
    // Two layers of swirling: the soap is carried round by the air and by its own draining.
    float2 first = float2(fbm(q * 1.2 + float2(0.0, flow * 0.5)), fbm(q * 1.2 + float2(5.2, -flow * 0.4)));
    float2 second = float2(fbm(q * 1.9 + first * 2.6 + float2(1.7, 9.2) + flow * 0.3),
                           fbm(q * 1.9 + first * 2.6 + float2(8.3, 2.8) - flow * 0.3));
    // Gravity: thin at the top, thick at the bottom (y grows downward).
    float fall = p.y * 0.5 + 0.5;
    return fall * 0.8 + (second.x - 0.5) * 1.15 + (first.y - 0.5) * 0.45 + 0.08;
}

float3 Render(float2 uv, float2 p)
{
    float flow = Time * 0.1 + BeatGlide * 0.09;
    float still = fsThickness(p, flow);
    float thickness = still;

    // The bass thickens the whole film; each beat sends one ripple out across it; treble makes it shiver.
    float out0 = length(p);
    float ring = out0 - BeatPhase * 2.2;
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;
    thickness += Bass * 0.07 + DropPulse * 0.12;
    thickness += exp(-ring * ring * 24.0) * sin(ring * 40.0) * 0.035 * beatLight;
    thickness += sin(p.x * 31.0 + p.y * 17.0 + Time * 2.0) * sin(p.y * 29.0 - Time * 1.6) * Treble * 0.012;
    // The spectrum, laid across the film from left to right, swells it band by band. It is read at a few
    // places and joined up smoothly between them, so the film bulges instead of showing every spike.
    float place = uv.x * 9.0;
    float knot = floor(place);
    float between = smoothstep(0.0, 1.0, place - knot);
    float swell = lerp(fsBand(knot), fsBand(knot + 1.0), between);
    thickness += swell * 0.1 * smoothstep(0.0, 0.5, uv.y);

    float nanometres = max(thickness, 0.0) * Thickest;
    float3 colour = fsFilm(nanometres);
    // Thick film's colours wash out, as the bands of each colour drift out of step.
    float washed = saturate((nanometres - 800.0) / 900.0);
    colour = lerp(colour, float3(0.62, 0.6, 0.6), washed * 0.5);

    // What the film is reflecting: a soft window of light, bent a little by the film's own slope.
    float e = 0.01;
    float2 slope = float2(fsThickness(p + float2(e, 0.0), flow), fsThickness(p + float2(0.0, e), flow)) - still;
    float2 bent = p + slope * 2.5;
    float2 lamp = float2(sin(Time * 0.07) * 0.5, -0.25 + 0.2 * cos(Time * 0.05));
    float window = exp(-dot(bent - lamp, bent - lamp) * 0.9);
    colour *= 0.3 + 0.62 * window + Level * 0.12;

    return colour * (0.9 + Intensity * 0.25) * (1.0 - dot(uv - 0.5, uv - 0.5) * 1.1);
}
