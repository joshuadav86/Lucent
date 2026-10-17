// Name: Paper Cut
// Layers of cut paper—rolling hills, a little pine wood and a pale paper moon—stacked in soft dusk tones,
// each casting a gentle shadow on the one behind. The layers sway and drift at their own depths with the music.
// Family: Minimal
// Tags: Calm, Minimal

static const int PaperLayers = 5;

// Height of the paper edge for layer i at x (screen y grows downwards, so smaller is higher).
float pcRidge(float i, float x)
{
    float depth = i / (PaperLayers - 1.0);
    float base = -0.16 + 0.68 * pow(depth, 1.25);
    float roll = 0.11 - depth * 0.03;
    float y = base
        - roll * sin(x * (0.9 + depth * 0.5) + i * 1.7)
        - roll * 0.5 * sin(x * (2.1 + depth * 0.7) + i * 4.1)
        - 0.02 * sin(x * 5.3 + i * 2.3);

    // Small pine trees on two of the layers.
    if (abs(i - 1.0) < 0.5 || abs(i - 3.0) < 0.5)
    {
        float width = 0.09 + 0.05 * depth;
        float cell = floor(x / width);
        float seed = hash(float2(cell, i * 7.0));
        if (seed > 0.42)
        {
            float centre = (cell + 0.3 + 0.4 * hash(float2(cell, i * 3.0 + 1.0))) * width;
            float height = width * (0.8 + 1.0 * hash(float2(cell, i * 5.0 + 2.0)));
            float halfWidth = width * (0.3 + 0.12 * seed);
            float slope = height / halfWidth;
            float dx = abs(x - centre);
            // A pine in tiers: a cone with a zig-zag edge.
            float tier = frac(dx / halfWidth * 3.0) * height * 0.1;
            float treeTop = y - height * 1.1 + dx * slope + tier;
            y = min(y, treeTop);
        }
    }
    return y;
}

// The colour of each paper sheet: pale dusty rose at the back to deep indigo at the front.
float3 pcTone(float i, float drift)
{
    float depth = i / (PaperLayers - 1.0);
    float3 back = lerp(float3(0.78, 0.55, 0.58), float3(0.62, 0.55, 0.72), drift);
    float3 front = lerp(float3(0.27, 0.17, 0.34), float3(0.17, 0.20, 0.36), drift);
    float3 tone = lerp(back, front, pow(depth, 0.8));
    return tone * tone;   // the tones are picked by eye in display space
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float drift = 0.5 + 0.5 * sin(Time * 0.013);
    float paperGrain = fbm(p * 38.0) - 0.5;

    // ---- Sky sheet: a warm dusk gradient ----
    float3 horizon = lerp(float3(0.98, 0.78, 0.60), float3(0.92, 0.70, 0.66), drift);
    float3 zenith = lerp(float3(0.55, 0.45, 0.62), float3(0.36, 0.36, 0.58), drift);
    float3 colour = lerp(horizon, zenith, smoothstep(0.35, -1.0, p.y));
    colour = colour * colour * (0.9 + Level * 0.12);

    // ---- The paper moon, cut from cream card, with its own soft shadow ----
    float2 moonCentre = float2(Aspect * 0.42 + sin(Time * 0.02) * 0.04, -0.52 + Beat * 0.004);
    float moonRadius = 0.15 + Bass * 0.006;
    float moonD = length(p - moonCentre) - moonRadius;
    float moonShadowD = length(p - moonCentre - float2(0.012, 0.018)) - moonRadius;
    colour *= 1.0 - 0.28 * (1.0 - smoothstep(-0.01, 0.035, moonShadowD));
    float3 moonColour = float3(1.0, 0.93, 0.78);
    moonColour = moonColour * moonColour * (0.95 + Level * 0.15);
    colour = lerp(colour, moonColour * (1.0 + 0.04 * paperGrain), saturate(0.5 - moonD / px));
    // A faint warm halo round the moon, brighter as the music swells.
    colour += float3(1.0, 0.85, 0.6) * 0.08 * (0.6 + Level) * exp(-max(moonD, 0.0) * 9.0) * step(0.0, moonD);

    // ---- Hills, back to front ----
    [unroll]
    for (int k = 0; k < PaperLayers; k++)
    {
        float i = k;
        float depth = i / (PaperLayers - 1.0);
        // Each layer listens to its own part of the spectrum: treble at the back, bass at the front.
        float band = spectrum(0.75 - depth * 0.7);
        float sway = sin(Time * (0.5 + depth * 0.3) + i * 1.3) * (0.004 + 0.02 * band);
        // Parallax: nearer layers slide further as the view drifts.
        float slide = Time * (0.01 + 0.035 * depth) + sin(Time * 0.04) * 0.3 * depth;
        float bob = Beat * 0.004 * depth;
        float x = p.x + slide + sway;
        float ridge = pcRidge(i, x) + bob;

        // The layer in front casts a soft shadow upward onto everything behind it.
        float shadowEdge = pcRidge(i, x - 0.01) + bob + 0.012;
        float shadow = smoothstep(shadowEdge - 0.07, shadowEdge, p.y);
        colour *= 1.0 - 0.32 * shadow;

        float cover = saturate((p.y - ridge) / px + 0.5);
        float3 tone = pcTone(i, drift);
        // Paper grain and a gentle lift of light just under each cut edge.
        float3 sheet = tone * (1.0 + 0.05 * paperGrain + 0.12 * exp(-(p.y - ridge) * 40.0));
        sheet *= 1.0 - 0.15 * smoothstep(0.0, 1.0, p.y - ridge);
        colour = lerp(colour, sheet, cover);
    }

    // Soft vignette, like a lightbox edge.
    colour *= 1.0 - 0.3 * smoothstep(0.7, 2.0, length(p * float2(0.8, 1.0)));
    return colour;
}
