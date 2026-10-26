// Name: Flower of Life
// Nested Flower of Life medallions in fine line work over glowing enamel, each sitting inside the heart of the
// next, zooming forever inward as they counter-rotate; rings of circles light with the spectrum and beats ripple out.
// Family: Kaleidoscope
// Tags: Calm, Geometric
// Added: 2026-10-16 23:54

float flStroke(float d, float halfWidth, float px)
{
    float w = max(halfWidth, px * 0.5);
    return saturate((w - abs(d)) / px + 0.5) * (halfWidth / w);
}

// One medallion: the 19 circles of the Flower of Life (radius 1, lattice spacing 1) inside a double ring.
// q in medallion units. Returns line brightness (x), circles covering q (y), distance to nearest centre (z),
// and how strongly the lines answer the music (w).
float4 flMedallion(float2 q, float pxl, float hw, float ripple, float3 levels, out float edge)
{
    edge = 10.0;
    float j = q.y / 0.8660254;
    float i = q.x - j * 0.5;
    float2 base = floor(float2(i, j) + 0.5);
    float lines = 0.0;
    float cover = 0.0;
    float nearest = 10.0;
    [loop]
    for (int k = 0; k < 9; k++)
    {
        float2 cell = base + float2(k % 3, k / 3) - 1.0;
        float hex = max(max(abs(cell.x), abs(cell.y)), abs(cell.x + cell.y));
        float2 centre = float2(cell.x + cell.y * 0.5, cell.y * 0.8660254);
        float d = length(q - centre);
        float inFlower = step(hex, 2.5);
        // Each ring of circles answers its own part of the spectrum; the beat ripples outward ring by ring.
        float level = (hex < 0.5) ? levels.x : ((hex < 1.5) ? levels.y : levels.z);
        float wave = exp(-abs(hex - ripple) * 2.0);
        lines += flStroke(d - 1.0, hw, pxl) * inFlower * (0.4 + level * 0.9 + wave * Beat * 1.2);
        cover += step(d, 1.0) * inFlower;
        nearest = (inFlower > 0.5) ? min(nearest, d) : nearest;
        edge = (inFlower > 0.5) ? min(edge, abs(d - 1.0)) : edge;
    }
    float r = length(q);
    lines += flStroke(r - 3.0, hw, pxl) * 1.2 + flStroke(r - 3.12, hw, pxl) * 0.7;
    edge = min(edge, abs(r - 3.0));
    return float4(lines, cover * step(r, 3.0), nearest, r);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float hw = max(px * 0.5, 0.0014);

    // Medallions three times larger each: every flower fits exactly inside the central circle of the next.
    float clock = Time * 0.06 + Bass * 0.02;
    float phase = frac(clock);
    float3 colour = float3(0.0, 0.0, 0.0);
    float ripple = (1.0 - Beat) * 3.0;
    float3 levels = float3(spectrum(0.06), spectrum(0.36), spectrum(0.66));

    [loop]
    for (int layer = 0; layer < 5; layer++)
    {
        float octave = layer + phase;                         // 0..5
        float radius = 0.0075 * pow(3.0, octave);              // 0.0075 .. 1.8
        float weight = smoothstep(0.0, 1.2, octave) * (1.0 - smoothstep(4.0, 5.0, octave));
        // Fine detail fades in only once it is large enough to resolve.
        weight *= saturate(radius / px / 10.0 - 0.6);
        if (weight < 0.002)
        {
            continue;
        }
        float generation = floor(clock) - layer;
        float spin = (wrap(generation, 2.0) < 0.5 ? 1.0 : -1.0) * Time * 0.05 + generation * 0.5;
        float2 q = rotate(p, spin) / radius;
        float pxl = px / radius;
        float edge = 0.0;
        float4 f = flMedallion(q, pxl, hw / radius, ripple, levels, edge);

        // Inside its central circle a medallion steps back, leaving the smaller flower there clear.
        float innerWeight = smoothstep(0.0, 1.2, octave - 1.0) * saturate(radius / 3.0 / px / 10.0 - 0.6);
        float heart = 1.0 - 0.75 * innerWeight * (1.0 - smoothstep(0.97, 1.0, f.w));

        float hue = generation * 0.17 + Time * 0.012;
        float3 ink = hsv2rgb(hue + 0.5, 0.45, 1.0);
        float3 lune = hsv2rgb(hue + 0.14, 0.9, 1.0);
        float3 petal = hsv2rgb(hue + 0.07, 0.85, 1.0);
        float3 knot = hsv2rgb(hue, 0.75, 1.0);

        // Lunes (one circle), petals (two) and curved triangles (three) take different jewel tints.
        float3 fill = (f.y > 2.5) ? knot * 0.13 : ((f.y > 1.5) ? petal * 0.075 : lune * 0.03 * step(0.5, f.y));
        // The enamel glows along the lines and darkens toward the middle of each cell.
        fill *= 0.15 + 1.6 * exp(-edge * 9.0);
        colour += (ink * f.x + fill) * weight * heart;

        // Sparkles where six circles meet.
        colour += float3(1.0, 0.95, 0.85) * exp(-f.z * f.z / (pxl * pxl * 1.5)) * weight * (0.15 + Treble * 0.9);
    }

    return colour;
}
