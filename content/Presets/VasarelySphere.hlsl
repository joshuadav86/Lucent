// Name: Vasarely Sphere
// A turning chequered globe swells out of an op-art field of squares, each square holding a circle, diamond or
// smaller square that grows with its own band of the music, while every beat sends a ring of inverted colour
// rippling out from the sphere.
// Family: Geometric
// Tags: Geometric, Colourful, Retro, 3D
// Added: 2026-10-17 21:37

// The light, from the upper left and in front (y grows downward on screen).
static const float3 vsLight = float3(-0.42, -0.55, 0.72);

// A Vasarely unit: a square cell holding a smaller shape in the opposite colour. f is the position in the
// cell (-0.5..0.5), size the shape's radius, morph moves it from circle (0) to diamond (1) to square (2),
// width the size of a pixel in cell units. 1 inside the shape, 0 outside, smooth at the edge.
float vsUnit(float2 f, float size, float morph, float width)
{
    float2 a = abs(f);
    float circle = length(f);
    float diamond = (a.x + a.y) * 0.7071;
    float square = max(a.x, a.y);
    float d = morph < 1.0 ? lerp(circle, diamond, morph) : lerp(diamond, square, morph - 1.0);
    return 1.0 - smoothstep(size - width, size + width, d);
}

// Which colour a cell starts with: 0 or 1 in a chequer, swapped by flip.
float vsParity(float2 cell, float flip)
{
    return abs(wrap(cell.x + cell.y, 2.0) - flip);
}

// The beat rings: each of the last few beats sends a ring outward from the sphere, and every cell the ring
// is crossing swaps its colours. distance is in cells from the sphere's rim.
float vsFlip(float distance)
{
    float flip = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        // Beat i beats ago, travelling at 6 cells a beat-ish and fading out as it goes.
        float age = BeatPhase + i;
        float front = age * 5.0;
        float band = step(abs(distance - front), 0.9) * step(i * 1.0, BeatCount);
        flip = abs(flip - band * step(age, 3.5));
    }
    return flip;
}

// Colours for the two halves of a unit: hue drifts in rings out from the sphere.
float3 vsColour(float tone, float hue, float band)
{
    float3 light = hsv2rgb(hue, 0.65, 0.72 + band * 0.35);
    float3 dark = hsv2rgb(hue + 0.45, 0.8, 0.045 + band * 0.06);
    return lerp(dark, light, tone);
}

float3 Render(float2 uv, float2 p)
{
    // The sphere wanders slowly and breathes with the bass.
    float2 centre = float2(0.32 * sin(Time * 0.071), 0.1 * sin(Time * 0.053 + 1.0));
    float radius = 0.6 + Bass * 0.07 + Beat * 0.02 + 0.03 * sin(Time * 0.2);
    float2 d = p - centre;
    float r = length(d);
    float2 outward = r > 1e-4 ? d / r : float2(1.0, 0.0);
    float morph = 1.0 + sin(Time * 0.09);  // the units turn slowly from circles to diamonds to squares and back

    // ---- The field ----
    // The sphere pushes up through the field, so the squares crowd together round its rim, as if the page
    // were stretched over it.
    float cells = 7.0;  // cells per p unit
    float outside = max(r - radius, 0.0);
    float squeezed = r + radius * 0.35 * exp(-outside / (radius * 0.45));
    float2 field = (centre + outward * squeezed) * cells;
    float turn = 0.05 * sin(Time * 0.04);
    float2 scroll = float2(Time * 0.2, 0.0);
    field = rotate(field, turn) + scroll;
    float2 cell = floor(field);
    float2 f = field - cell - 0.5;
    float width = max(fwidth(field.x), fwidth(field.y));

    // Each ring of cells round the sphere listens to its own band: bass close in, treble far out. The ring
    // is measured from the cell's centre, so a whole cell grows and flips together.
    float2 cellCentre = rotate(cell + 0.5 - scroll, -turn);
    float ring = max(length(cellCentre - centre * cells) - radius * cells, 0.0);
    float band = spectrum(saturate(ring / 16.0) * 0.8 + 0.02);
    float size = 0.16 + 0.26 * band;
    float flip = vsFlip(ring);
    float parity = vsParity(cell, flip);
    float unit = vsUnit(f, size, morph, width);
    float hue = 0.8 + 0.22 * sin(ring * 0.2 - Time * 0.3) + Mid * 0.04;
    float3 colour = vsColour(abs(parity - unit), hue, band);
    // Fine dark seams between squares.
    float seam = saturate(min(0.5 - abs(f.x), 0.5 - abs(f.y)) / width - 0.4);
    colour *= lerp(0.5, 1.0, seam);
    // The sphere's shadow falls down and to the right.
    float shadow = smoothstep(radius * 1.25, radius * 0.85, length(d - float2(0.06, 0.09)));
    colour *= 1.0 - 0.55 * shadow;

    // ---- The globe ----
    float inside = 1.0 - smoothstep(radius - pixelSize(), radius + pixelSize(), r);
    if (inside > 0.0)
    {
        float2 q = d / radius;
        float3 normal = float3(q, sqrt(saturate(1.0 - dot(q, q))));
        // Turn the globe: a tilted axis, spinning faster with the music.
        float3 n = normal;
        n.yz = rotate(n.yz, 0.45 + 0.15 * sin(Time * 0.13));
        n.xz = rotate(n.xz, Time * (0.18 + Level * 0.1));
        n.xy = rotate(n.xy, 0.25 * sin(Time * 0.07));
        float around = 20.0;
        float up = 10.0;
        float longitude = atan2(n.x, n.z + 1e-6) / (2.0 * PI);
        float latitude = asin(clamp(n.y, -1.0, 1.0)) / PI;
        float2 g = float2(longitude * around, latitude * up + up * 0.5);
        // The longitude jumps where it wraps round; measure pixel width from a copy that wraps elsewhere.
        float2 gShifted = float2(frac(longitude + 0.5) * around, g.y);
        float gWidth = max(min(fwidth(g.x), fwidth(gShifted.x)), fwidth(g.y));
        float2 gCell = floor(g);
        float2 gf = g - gCell - 0.5;
        // Units squash towards the poles, where the cells narrow: keep them round.
        float squash = max(cos(latitude * PI), 0.08);
        gf.x *= squash * around / (up * 2.0);
        float cellLatitude = (gCell.y + 0.5 - up * 0.5) / up;
        float gBand = spectrum(abs(cellLatitude) * 1.4 + 0.02);
        float gSize = (0.14 + 0.24 * gBand) * min(squash * around / (up * 2.0), 1.0);
        float gParity = vsParity(gCell, 0.0);
        float gUnit = vsUnit(gf, gSize, morph, gWidth * squash);
        // The globe is gold, turning to teal while the field round it runs warm.
        float warm = smoothstep(0.2, 0.8, sin(-Time * 0.3));
        float gHue = lerp(0.11, 0.5, warm) + gBand * 0.04;
        float gSaturation = 0.7 - 0.4 * warm * (1.0 - warm) * 4.0;
        float3 globe = lerp(hsv2rgb(gHue + 0.5, 0.7, 0.04 + gBand * 0.05), hsv2rgb(gHue, gSaturation, 0.85 + gBand * 0.3), abs(gParity - gUnit));
        float gSeam = saturate(min(0.5 - abs(g.x - gCell.x - 0.5), 0.5 - abs(gf.y)) / gWidth - 0.4);
        globe *= lerp(0.5, 1.0, gSeam);

        // Lit from the upper left, with a soft highlight and a rim that catches the treble.
        float3 lightDir = normalize(vsLight);
        float lambert = saturate(dot(normal, lightDir));
        float spec = pow(saturate(dot(reflect(float3(0.0, 0.0, -1.0), normal), lightDir)), 30.0);
        float fresnel = pow(1.0 - normal.z, 3.0);
        float3 lit = globe * lerp(0.35, 1.2, lambert) + float3(1.0, 0.96, 0.9) * spec * (0.35 + Beat * 0.2);
        lit += hsv2rgb(hue, 0.4, 1.0) * fresnel * (0.12 + Treble * 0.5);
        colour = lerp(colour, lit, inside);
    }

    // A thin glowing rim lifts the globe off the page.
    float rim = exp(-abs(r - radius) / (pixelSize() * 2.0 + 0.003));
    colour += hsv2rgb(hue, 0.3, 1.0) * rim * (0.08 + Treble * 0.35);
    return colour;
}
