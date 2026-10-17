// Name: Dot Matrix
// A calm grid of round dots, like a halftone print, whose sizes trace the music: the spectrum rises as a soft
// hill of larger dots, the waveform runs through it as a bright thread, and each beat sends a gentle wave across.
// Family: Minimal
// Tags: Minimal, Calm, Geometric

static const float DotRows = 31.0;

float dmSquare(float x)
{
    return x * x;
}

// The halftone "ink" at a point: how large the dot there should be, 0..1.
float dmInk(float2 c, float beatRadius)
{
    float halfWidth = Aspect;
    // The spectrum is mirrored about the centre, deep bass in the middle and treble at the edges.
    float band = saturate(abs(c.x) / halfWidth);
    float level = spectrum(0.02 + band * 0.7) * 0.5
                + (spectrum(0.02 + band * 0.7 - 0.02) + spectrum(0.02 + band * 0.7 + 0.02)) * 0.25;
    float height = 0.08 + 0.8 * saturate(level * 1.2);
    // Dots fill up from the middle to the band's height, fading softly near the top like a halftone gradient.
    float hill = smoothstep(height + 0.06, height - 0.18, abs(c.y)) * (0.45 + 0.55 * saturate(1.0 - abs(c.y) / (height + 0.05)));

    // The waveform as a slow, smoothed thread across the grid.
    float x = c.x / (2.0 * halfWidth) + 0.5;
    float wave = (waveform(x) * 0.5 + (waveform(x - 0.012) + waveform(x + 0.012)) * 0.25) * 0.45;
    float thread = exp(-dmSquare((c.y - wave) / 0.075));

    // A faint drifting texture so the quiet parts of the grid are never completely still.
    float drift = noise(c * 1.7 + float2(Time * 0.07, -Time * 0.05)) * 0.09;

    // The beat wave: a soft ring travelling outwards from the centre.
    float ring = Beat * exp(-dmSquare((length(c * float2(0.8, 1.0)) - beatRadius) / 0.12));

    return saturate(drift + hill * 0.72 + thread * 0.5 * (0.4 + Level) + ring * 0.45);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float cell = 2.0 / DotRows;

    // The grid slides very slowly sideways so the picture breathes even in silence.
    float2 shift = float2(Time * 0.012, 0.0);
    float2 g = (p + shift) / cell;
    float2 index = floor(g);
    float2 local = (frac(g) - 0.5) * cell;
    float2 centre = (index + 0.5) * cell - shift;

    float beatRadius = (1.0 - Beat) * 2.2;
    float ink = dmInk(centre, beatRadius);

    // Dot radius follows the ink like a halftone screen; the area grows with ink, not the radius.
    float radius = cell * 0.5 * (0.1 + 0.86 * sqrt(ink));
    float d = length(local) - radius;
    float disc = saturate(0.5 - d / px);

    // Restrained two-colour palette: warm paper-white dots that turn coral where the ink is heaviest.
    float hueTurn = 0.5 + 0.5 * sin(Time * 0.03);
    float3 paper = float3(0.92, 0.86, 0.76);
    float3 accent = lerp(float3(1.0, 0.42, 0.30), float3(0.95, 0.55, 0.22), hueTurn);
    float3 dotColour = lerp(paper * 0.55, accent * 1.05, smoothstep(0.55, 1.0, ink));
    dotColour = lerp(dotColour, paper * 0.95, smoothstep(0.15, 0.5, ink) * (1.0 - smoothstep(0.55, 0.95, ink)) * 0.6);
    // The newest beat wave lifts the dots it passes through.
    float ring = Beat * exp(-dmSquare((length(centre * float2(0.8, 1.0)) - beatRadius) / 0.12));
    dotColour *= 1.0 + ring * 0.35;

    // Deep ink-blue ground with a soft pool of light in the middle.
    float3 ground = lerp(float3(0.010, 0.014, 0.030), float3(0.030, 0.040, 0.070), exp(-dot(p, p) * 0.6));
    float3 colour = lerp(ground, dotColour, disc);
    // A faint halo round the heaviest dots.
    colour += accent * 0.05 * smoothstep(0.6, 1.0, ink) * exp(-max(d, 0.0) / (cell * 0.12));
    return colour;
}
