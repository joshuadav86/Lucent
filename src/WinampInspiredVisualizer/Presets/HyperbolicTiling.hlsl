// Name: Hyperbolic Tiling
// An endless kaleidoscope of tiles packed into a glowing disc, the way Escher drew infinity: every tile is the same size in its own curved world, so they shrink without end towards the rim; the whole plane glides and turns beneath you, rings of tiles light up outward from the centre with the beat, the centre answering the bass and the rim the treble, and every so often the pattern melts into a new tiling.
// Family: Kaleidoscope
// Tags: Geometric, Colourful
// Added: 2026-10-25 01:55

// The Poincare disc: the whole hyperbolic plane drawn inside a circle. A {p,q} tiling covers it
// with p-sided tiles, q meeting at each corner. It's built from one small triangle with corners at
// the centre (angle pi/p), at the middle of a tile's edge (a right angle) and at a tile's corner
// (pi/q), whose three sides are mirrors: two straight lines through the centre and one circle
// that meets the rim at right angles. Folding a point back across those mirrors until it lands in
// the triangle tells us where it is in the pattern; the number of folds across the circle is
// how many rings of tiles out from the centre it lies.
static const int MaxFolds = 40;

struct Tiling
{
    float2 centre;   // the circle mirror's centre (on the x axis)
    float radius;    // and its radius
    float sides;     // p
    float2 corner;   // where the tile's corner sits in the triangle
};

Tiling makeTiling(float p, float q)
{
    Tiling tiling;
    // In a hyperbolic right triangle the side from the centre to the edge's middle has
    // cosh(length) = cos(pi/q) / sin(pi/p); in the disc that point is tanh(length / 2) out.
    float c = cos(PI / q) / sin(PI / p);
    float reach = log(c + sqrt(c * c - 1.0));
    float x = tanh(reach * 0.5);
    // A circle through x that meets the rim at right angles: d^2 = 1 + r^2.
    float d = (1.0 + x * x) / (2.0 * x);
    tiling.centre = float2(d, 0.0);
    tiling.radius = d - x;
    tiling.sides = p;
    float2 along = float2(cos(PI / p), sin(PI / p));
    float b = d * along.x;
    tiling.corner = along * (b - sqrt(max(b * b - 1.0, 0.0)));
    return tiling;
}

// The tilings it moves between: {7,3}, {5,4}, {8,3}, {6,4}.
Tiling tilingNumber(float n)
{
    float k = wrap(n, 4.0);
    if (k < 1.0)
    {
        return makeTiling(7.0, 3.0);
    }
    if (k < 2.0)
    {
        return makeTiling(5.0, 4.0);
    }
    if (k < 3.0)
    {
        return makeTiling(8.0, 3.0);
    }
    return makeTiling(6.0, 4.0);
}

float2 complexTimes(float2 a, float2 b)
{
    return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

float2 complexDivide(float2 a, float2 b)
{
    return float2(a.x * b.x + a.y * b.y, a.y * b.x - a.x * b.y) / max(dot(b, b), 1e-9);
}

// Glides the hyperbolic plane so the point a comes to the centre (a disc-preserving Mobius map).
float2 glide(float2 z, float2 a)
{
    return complexDivide(z - a, float2(1.0, 0.0) - complexTimes(float2(a.x, -a.y), z));
}

// Folds z into the wedge between the x axis and the line at pi/p, mirroring as it goes. Unlike
// kaleidoscope() it leaves a point already in the wedge where it is, which the folding relies on.
float2 foldWedge(float2 z, float sides)
{
    float wedge = 2.0 * PI / sides;
    float angle = wrap(atan2(z.y, z.x), wedge);
    angle = min(angle, wedge - angle);
    return float2(cos(angle), sin(angle)) * length(z);
}

// The colour of one tiling at z (inside the disc). pixel is the size of a screen pixel at z.
float3 shadeTiling(Tiling tiling, float2 z, float pixel)
{
    float startScale = 1.0 - dot(z, z);
    float rings = 0.0;
    [loop]
    for (int i = 0; i < MaxFolds; i++)
    {
        z = foldWedge(z, tiling.sides);
        float2 fromCentre = z - tiling.centre;
        float distance2 = dot(fromCentre, fromCentre);
        if (distance2 >= tiling.radius * tiling.radius)
        {
            break;
        }
        z = tiling.centre + fromCentre * (tiling.radius * tiling.radius / distance2);
        rings += 1.0;
    }
    z = foldWedge(z, tiling.sides);

    // How much the folds have magnified things: hyperbolic distances look (1 - |z|^2) times
    // smaller on screen, so a line drawn here at a fixed width keeps its shape as tiles shrink.
    float toScreen = startScale / max(1.0 - dot(z, z), 1e-4);
    float edge = abs(length(z - tiling.centre) - tiling.radius) * toScreen;
    float2 across = float2(-sin(PI / tiling.sides), cos(PI / tiling.sides));
    float spoke = min(abs(z.y), abs(dot(z, across))) * toScreen;
    float corner = length(z - tiling.corner) * toScreen;
    float middle = length(z) * toScreen;

    // Rings of tiles: the centre listens to the bass, the outer rings to higher and higher notes.
    float band = saturate(rings / 9.0);
    float level = spectrum(0.02 + band * 0.7);
    float hue = rings * 0.11 + Time * 0.01 + 0.6;
    float3 tint = pow(palette(hue), 1.6);
    // A ring lights up outward from the centre through each beat.
    float wave = rings - BeatPhase * 7.0;
    float pulse = exp(-wave * wave * 1.5) * (SongMapped > 0.0 ? 0.35 + Beat * 0.4 : Beat * 0.6);

    // The tile's body: a soft gradient from its middle out to its edge.
    float fill = (0.03 + level * 0.1 + pulse * 0.15) * (1.0 - smoothstep(0.0, 1.0, edge / max(middle + edge, 1e-4)) * 0.6);
    float3 colour = tint * fill;
    // Bright tile outlines, faint spokes from each tile's middle to its corners and edges.
    float lineWidth = pixel * (1.4 + Bass * 1.2);
    colour += lerp(tint, float3(1.0, 1.0, 1.0), 0.45) * glow(edge, 1.0 / lineWidth) * (0.6 + level * 0.8 + pulse);
    colour += tint * glow(spoke, 1.0 / (pixel * 0.9)) * (0.08 + Mid * 0.2);
    // Corners where q tiles meet twinkle with the treble.
    colour += float3(1.0, 0.95, 0.9) * glow(corner, 1.0 / (pixel * 3.0)) * (0.25 + Treble * 0.9);
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float discRadius = 0.96 + Bass * 0.015;
    float2 z = rotate(p, Time * 0.03) / discRadius;
    float r2 = dot(z, z);
    float pixel = pixelSize() / discRadius;
    float3 colour = 0.0;

    if (r2 < 0.9995)
    {
        // Glide across the plane on a slow looping path, a little further with the mids.
        float2 heading = float2(cos(Time * 0.071), sin(Time * 0.053 + 1.0));
        float2 a = heading * (0.35 + 0.15 * sin(Time * 0.11) + Mid * 0.08);
        float2 w = glide(z, a);

        // Every 40 seconds the pattern melts into the next tiling over two seconds.
        float stage = floor(Time / 40.0);
        float blend = smoothstep(37.5, 40.0, Time - stage * 40.0);
        colour = shadeTiling(tilingNumber(stage), w, pixel);
        if (blend > 0.0)
        {
            colour = lerp(colour, shadeTiling(tilingNumber(stage + 1.0), w, pixel), blend);
        }
        // Fade the finest tiles at the rim into a soft glow rather than noise.
        colour *= 1.0 - smoothstep(0.86, 0.9995, r2) * 0.85;
    }

    // The rim, and a faint halo beyond it.
    float rim = abs(sqrt(r2) - 1.0) * discRadius;
    colour += float3(0.55, 0.65, 1.0) * (glow(rim, 1.0 / (pixelSize() * 2.0)) * 0.5 + exp(-rim * 14.0) * 0.03 * (1.0 + Level));
    return colour + previous(uv) * 0.25;
}
