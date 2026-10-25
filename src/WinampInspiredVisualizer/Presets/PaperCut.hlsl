// Name: Paper Cut
// Layers of cut paper in soft dusk tones: rolling hills and a little pine wood under a moon cut like a paper
// mandala, its turning rings opening with the music. Each hill's edge rises into a mirrored ridge of the
// spectrum, with light leaking through the cut between the sheets, and pinprick stars twinkle with the treble.
// Family: Minimal
// Tags: Calm, Minimal, Geometric
// Added: 2026-10-17 21:41

static const int PaperLayers = 8;

// Height of the paper edge for layer i at x (screen y grows downwards, so smaller is higher).
float pcRidge(float i, float x, float screenX)
{
    float depth = i / (PaperLayers - 1.0);
    float base = -0.16 + 0.98 * pow(depth, 1.15);
    float roll = 0.1 - depth * 0.05;
    float y = base
        - roll * sin(x * (0.9 + depth * 0.5) + i * 1.7)
        - roll * 0.5 * sin(x * (2.1 + depth * 0.7) + i * 4.1)
        - 0.02 * sin(x * 5.3 + i * 2.3);

    // The edge rises into a ridge of the spectrum, mirrored left and right: bass in the middle, treble at
    // the sides, highest in the centre like a pulsar's trace. Each sheet listens a little higher up.
    // The ridge is cut with straight edges between points along it, like scissors would.
    float across = abs(screenX) / max(Aspect, 1.0);
    float knots = 26.0;
    float k = floor(across * knots);
    float f = across * knots - k;
    float lift = 0.0;
    float a = 0.0;
    float b = 0.0;
    [unroll]
    for (int n = 0; n < 2; n++)
    {
        float at = (k + n) / knots;
        float music = spectrum(saturate(at * 0.7 + 0.04 + (4.0 - i) * 0.03));
        // Every other point is a peak, the ones between are valleys.
        float peak = wrap(k + n + i, 2.0) < 0.5 ? 1.0 : 0.25;
        float height = music * music * peak * exp(-at * at * 3.0);
        if (n == 0) { a = height; } else { b = height; }
    }
    lift = lerp(a, b, f);
    y -= lift * (0.08 + 0.14 * i / (PaperLayers - 1.0));

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

    // ---- Pinprick stars: tiny holes in the sky sheet with light behind, twinkling with the treble ----
    float2 starGrid = p * 22.0;
    float2 starCell = floor(starGrid);
    float starSeed = hash(starCell + 3.7);
    float2 starAt = starCell + 0.25 + 0.5 * float2(hash(starCell + 1.1), hash(starCell + 5.9));
    float pin = exp(-pow(length(starGrid - starAt) / (px * 22.0 * 1.2), 2.0)) * step(0.8, starSeed) * smoothstep(0.1, -0.6, p.y);
    colour += float3(1.0, 0.92, 0.8) * pin * (0.25 + Treble * 0.9 * (0.5 + 0.5 * sin(Time * 3.0 + starSeed * 40.0)));

    // ---- The moon: three discs of cream card cut like a paper mandala, stacked, each turning its own
    // way, with the moon's light shining through the cuts. Each ring's cuts open with its band ----
    float2 moonCentre = float2(Aspect * 0.42 + sin(Time * 0.02) * 0.04, -0.5 + Beat * 0.004);
    float2 m = p - moonCentre;
    float glowing = exp(-max(length(m) - 0.12, 0.0) * 9.0);
    colour += float3(1.0, 0.85, 0.6) * 0.1 * (0.6 + Level) * glowing;
    [unroll]
    for (int disc = 0; disc < 3; disc++)
    {
        float k = disc;
        float radius = 0.19 - k * 0.05 + Bass * 0.006;
        float folds = disc == 0 ? 12.0 : disc == 1 ? 8.0 : 6.0;
        float turn = Time * (disc == 1 ? -0.05 : 0.04 + k * 0.02);
        float discBand = spectrum(0.08 + k * 0.25);
        float holeAt = radius * 0.72;
        float holeSize = radius * (0.14 + 0.16 * discBand);
        // One wedge of the disc, mirrored round: a petal-shaped cut near the rim and a small round one inside.
        float2 w = kaleidoscope(rotate(m, turn), folds);
        float petal = length((w - float2(holeAt, 0.0)) * float2(1.0, 2.2)) - holeSize;
        float inner = length(w - float2(radius * 0.42, 0.0)) - holeSize * 0.45;
        float sheetD = max(length(m) - radius, -min(petal, inner));
        // Each disc's shadow falls on what's behind it, and the light behind glows warm through its cuts.
        float shadowD = max(length(m - float2(0.008, 0.012)) - radius, -min(petal, inner));
        colour *= 1.0 - 0.25 * (1.0 - smoothstep(-0.005, 0.025, shadowD));
        float3 card = disc == 0 ? float3(0.95, 0.72, 0.5) : disc == 1 ? float3(1.0, 0.86, 0.66) : float3(1.0, 0.95, 0.84);
        card = card * card * (0.9 + Level * 0.15 + k * 0.04) * (1.0 + 0.04 * paperGrain);
        float inCut = step(length(m), radius) * (1.0 - saturate(0.5 - sheetD / px));
        colour = lerp(colour, float3(1.0, 0.75, 0.45) * (0.5 + discBand * 0.8), inCut * 0.6);
        colour = lerp(colour, card, saturate(0.5 - sheetD / px));
    }

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
        float ridge = pcRidge(i, x, p.x + sway) + bob;

        // The layer in front casts a soft shadow upward onto everything behind it.
        float shadowEdge = pcRidge(i, x - 0.01, p.x + sway - 0.01) + bob + 0.012;
        float shadow = smoothstep(shadowEdge - 0.07, shadowEdge, p.y);
        colour *= 1.0 - 0.32 * shadow;

        float cover = saturate((p.y - ridge) / px + 0.5);
        float3 tone = pcTone(i, drift);
        // Paper grain and a gentle lift of light just under each cut edge.
        float3 sheet = tone * (1.0 + 0.05 * paperGrain + 0.12 * exp(-(p.y - ridge) * 40.0));
        sheet *= 1.0 - 0.15 * smoothstep(0.0, 1.0, p.y - ridge);
        colour = lerp(colour, sheet, cover);

        // Light leaks through the cut between this sheet and the one behind, glowing with this sheet's band.
        float leak = exp(-max(ridge - p.y, 0.0) / (px * 2.5)) * (1.0 - cover);
        float3 leakColour = hsv2rgb(0.92 - depth * 0.35 + 0.05 * sin(Time * 0.05), 0.65, 1.0);
        colour += leakColour * leak * (0.06 + band * band * 1.0 + Beat * 0.06);
    }

    // Soft vignette, like a lightbox edge.
    colour *= 1.0 - 0.3 * smoothstep(0.7, 2.0, length(p * float2(0.8, 1.0)));
    return colour;
}
