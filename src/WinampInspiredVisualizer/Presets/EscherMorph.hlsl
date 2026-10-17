// Name: Escher Morph
// A tiled floor of interlocking shapes melts from squares into honeycomb, splits into triangles and curls into
// pinwheeling Escher creatures before settling back into squares, each change rolling in on the downbeat.
// Family: Geometric
// Tags: Geometric, Colourful, Retro

// The tiling is the set of cells around a lattice of points (each pixel belongs to its nearest point).
// Sliding the lattice from square to hexagonal, adding a second point per cell (triangles) and
// swirling the plane with a smooth periodic twist (creatures) are all continuous, so the tiles morph.

// One stage every two bars; the morph itself takes the first two beats of each stage.
float emStage()
{
    float clock = max(BeatCount - Beat, 0.0) + 1.0;
    float block = clock / 8.0;
    float within = frac(block) * 8.0;
    return floor(block) + smoothstep(0.0, 2.0, within);
}

// A smooth swirl that repeats with the hexagonal lattice, so it bends every tile edge the same way.
float2 emSwirl(float2 q, float phase)
{
    float2 k1 = float2(1.0, -0.57735) * 2.0 * PI;
    float2 k2 = float2(0.0, 1.1547) * 2.0 * PI;
    float2 k3 = float2(1.0, 0.57735) * 2.0 * PI;
    float2 v = float2(-k1.y, k1.x) * sin(dot(k1, q) + phase)
             + float2(-k2.y, k2.x) * sin(dot(k2, q) + phase)
             + float2(-k3.y, k3.x) * sin(dot(k3, q) + phase);
    return v / 7.255;
}

// The three tile colours: ink blue, terracotta and parchment, drifting gently.
float3 emTone(float index, float shift)
{
    float3 ink = float3(0.05, 0.12, 0.24);
    float3 clay = float3(0.62, 0.20, 0.09);
    float3 parchment = float3(0.62, 0.52, 0.36);
    float3 colour = index < 0.5 ? ink : (index < 1.5 ? clay : parchment);
    float3 teal = float3(0.06, 0.30, 0.32);
    return lerp(colour, teal, shift * (index < 0.5 ? 0.6 : 0.0));
}

float3 Render(float2 uv, float2 p)
{
    float stage = emStage();
    float s = wrap(stage, 4.0);
    float px = pixelSize();

    // Stage weights: 0 squares, 1 hexagons, 2 triangles, 3 creatures.
    float toHex = s < 1.0 ? s : (s < 3.0 ? 1.0 : 4.0 - s);
    float split = s < 1.0 ? 0.0 : (s < 2.0 ? s - 1.0 : (s < 3.0 ? 3.0 - s : 0.0));
    float creature = s < 2.0 ? 0.0 : (s < 3.0 ? s - 2.0 : 4.0 - s);
    split = smoothstep(0.0, 1.0, split);

    // Slow drift and turn of the whole floor; the bass breathes the scale.
    float scale = 3.4 - Bass * 0.25 + 0.2 * sin(Time * 0.05);
    float2 q = rotate(p, Time * 0.02 + 0.3) * scale + float2(Time * 0.05, Time * 0.031);
    q += emSwirl(q, Time * 0.1) * 0.11 * creature;

    // Lattice basis: a stays fixed, b slides from straight up to sixty degrees.
    float2 a = float2(1.0, 0.0);
    float2 b = lerp(float2(0.0, 1.0), float2(0.5, 0.8660254), toHex);
    float det = a.x * b.y - a.y * b.x;
    float2 lat = float2((q.x * b.y - q.y * b.x) / det, (a.x * q.y - a.y * q.x) / det);
    float2 base = floor(lat);
    float2 second = (a + b) / 3.0 * split;
    bool useSecond = split > 0.002;

    // First pass: the nearest lattice point.
    float best = 1e9;
    float2 bestRel = float2(0.0, 0.0);
    float2 bestCell = float2(0.0, 0.0);
    float bestType = 0.0;
    [loop]
    for (int j = -1; j <= 2; j++)
    {
        [loop]
        for (int i = -1; i <= 2; i++)
        {
            float2 cell = base + float2(i, j);
            float2 site = cell.x * a + cell.y * b;
            for (int t = 0; t < 2; t++)
            {
                if (t == 1 && !useSecond)
                {
                    continue;
                }
                float2 rel = site + (t == 1 ? second : float2(0.0, 0.0)) - q;
                float d = dot(rel, rel);
                if (d < best)
                {
                    best = d;
                    bestRel = rel;
                    bestCell = cell;
                    bestType = (float)t;
                }
            }
        }
    }

    // Second pass: distance to the nearest cell edge. Edges between a point and its newborn twin fade in.
    float edge = 1e9;
    [loop]
    for (int jj = -1; jj <= 2; jj++)
    {
        [loop]
        for (int ii = -1; ii <= 2; ii++)
        {
            float2 cell = base + float2(ii, jj);
            float2 site = cell.x * a + cell.y * b;
            for (int t = 0; t < 2; t++)
            {
                if (t == 1 && !useSecond)
                {
                    continue;
                }
                float2 rel = site + (t == 1 ? second : float2(0.0, 0.0)) - q;
                float2 diff = rel - bestRel;
                float len = length(diff);
                if (len > 1e-4)
                {
                    float d = dot(0.5 * (rel + bestRel), diff / len);
                    float fade = ((float)t != bestType) ? smoothstep(0.0, 0.35, split) : 1.0;
                    edge = min(edge, d / max(fade, 0.02));
                }
            }
        }
    }
    float edgePx = edge / scale;

    // Three-colour the tiles so neighbours always differ; the twin triangles take the next colour along.
    float index = wrap(bestCell.x - bestCell.y, 3.0);
    index = wrap(floor(index + 0.5), 3.0);
    float drift = 0.5 + 0.5 * sin(Time * 0.07 + bestCell.x * 0.3);
    float3 tone = emTone(index, drift);
    // The newborn twin tiles (the downward triangles) darken to a deep ink as they open.
    tone = lerp(tone, float3(0.035, 0.055, 0.1), bestType * split);

    // Each tile reacts to its own slice of the spectrum.
    float2 tileCentre = -bestRel / scale + p;
    float band = spectrum(saturate(toUv(tileCentre).x * 0.8 + 0.05));
    tone *= 0.85 + band * 0.55;

    // Two-tone shading: lit on the upper-left half of each tile, in shade on the lower right.
    float2 light = normalize(float2(-0.6, 0.8));
    float side = dot(-bestRel, light) / scale;
    float shade = smoothstep(-0.06, 0.06, side * 6.0);
    float inner = saturate(edgePx / 0.05);
    float3 colour = tone * lerp(0.62, 1.08, shade) * (0.75 + 0.25 * inner);

    // A crisp ink outline, and a fine gold highlight inside it that flares on the beat.
    float width = 1.1 * px + 0.0025;
    float outline = 1.0 - smoothstep(width - px, width + px, edgePx);
    float rimDist = abs(edgePx - width - 2.5 * px);
    float rim = (1.0 - smoothstep(0.0, 1.2 * px, rimDist)) * (1.0 - shade * 0.4);
    float3 gold = float3(1.0, 0.72, 0.36);
    colour += gold * rim * (0.18 + Beat * 0.7 + Treble * 0.2);
    colour = lerp(colour, float3(0.012, 0.012, 0.02), outline);

    // A soft vignette keeps the eye in the middle.
    colour *= 1.0 - 0.25 * dot(p, p) / (Aspect * Aspect);
    return colour;
}
