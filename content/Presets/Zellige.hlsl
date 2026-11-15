// Name: Zellige
// A wall of glazed tilework in cobalt, turquoise, ivory and saffron, cut the way the old geometers laid it out: stars
// set in octagons and squares. The stars open and close as the music moves, each tile glows as if lit from behind by
// its own band of the spectrum, and a soft light slides across the glaze.
// Family: Geometric
// Tags: Geometric, Colourful, Calm
// Added: 2026-11-15 17:45

// "Polygons in contact", the method behind much Islamic star-pattern work: take a tiling (here octagons and
// squares), and from the middle of every edge send two lines into the tile at the same angle to the edge, each
// running until it meets its neighbour's. Those lines are where the tiles are cut: inside them a star, outside
// them the pieces that join up across the edges. The one angle decides the whole pattern. Every tile is folded
// onto a single slice between an edge's middle and a corner, where the cut is one short line.
static const float Period = 0.7;             // from one octagon to the next
static const float Grout = 0.0045;

// For a point q of a regular tile (given from its centre): how far it is from the cut, whether it is on the
// star's side of it, and where the tile's nearest corner lies.
float fzTile(float2 q, float sides, float inradius, float contact, out float star, out float2 toCorner)
{
    float slice = PI / sides;
    float far = length(q);
    float angle = far > 1e-6 ? atan2(q.y, q.x) : 0.0;
    float sector = floor(angle / (2.0 * slice) + 0.5) * 2.0 * slice;
    float nearest = sector + (angle < sector ? -slice : slice);
    toCorner = float2(cos(nearest), sin(nearest)) * inradius / cos(slice);
    float within = abs(angle - sector);
    float2 f = float2(cos(within), sin(within)) * far;

    float2 start = float2(inradius, 0.0);
    float2 heading = float2(-sin(contact), cos(contact));
    float slope = tan(slice);
    float reach = inradius * slope / (heading.y + slope * sin(contact));
    float2 offset = f - start;
    star = heading.x * offset.y - heading.y * offset.x > 0.0 ? 1.0 : 0.0;
    float along = clamp(dot(offset, heading), 0.0, reach);
    return length(offset - heading * along);
}

float3 Render(float2 uv, float2 p)
{
    float2 q = rotate(p, Time * 0.01 + BeatGlide * 0.003) * (1.0 + 0.1 * sin(Time * 0.045));
    q += float2(Time * 0.012, Time * 0.007);

    // The angle the cuts leave the edges at: it wanders, and leans with the bass.
    float contact = radians(47.0 + 19.0 * sin(Time * 0.1 + BeatGlide * 0.07) + Bass * 7.0 + DropPulse * 8.0);

    float half0 = Period * 0.5;
    float2 tile = floor(q / Period + 0.5);
    float2 c = q - tile * Period;
    float edge = Period / (1.0 + sqrt(2.0));
    float star;
    float2 toCorner;
    float cut;
    float3 glaze;
    float2 anchor;
    if ((abs(c.x) + abs(c.y)) * 0.70710678 <= half0)
    {
        cut = fzTile(c, 8.0, half0, contact, star, toCorner);
        glaze = float3(0.03, 0.1, 0.5);                     // cobalt stars
        anchor = star > 0.5 ? tile * Period : tile * Period + toCorner;
    }
    else
    {
        // A square, standing on its corner between four octagons.
        float2 middle = float2(c.x < 0.0 ? -half0 : half0, c.y < 0.0 ? -half0 : half0);
        float2 local = rotate(c - middle, PI * 0.25);
        cut = fzTile(local, 4.0, edge * 0.5, contact, star, toCorner);
        glaze = float3(0.02, 0.42, 0.4);                    // turquoise stars
        anchor = tile * Period + middle + (star > 0.5 ? float2(0.0, 0.0) : rotate(toCorner, -PI * 0.25));
    }

    // The pieces left outside the stars join up round each corner where three tiles meet: all three name
    // the piece by that corner, so it is one tile of one colour. Ivory, with saffron and terracotta now and then.
    float2 piece = floor(anchor / Period * 20.0 + 0.5);
    float own = hash(piece * 0.173 + 1.7);
    if (star < 0.5)
    {
        glaze = own < 0.26 ? float3(0.78, 0.45, 0.07) : (own > 0.88 ? float3(0.55, 0.13, 0.07) : float3(0.74, 0.68, 0.54));
    }

    // Hand-cut and hand-glazed: every piece a slightly different shade, with pooled glaze.
    float pooled = fbm(q * 9.0 + own * 40.0);
    glaze *= 0.78 + 0.3 * own + 0.25 * (pooled - 0.5);

    // Lit from behind by its band, and a ring of light opening from the middle on the beat.
    float band = spectrum(0.02 + hash(piece * 0.311 + 9.2) * 0.75);
    float out0 = length(p);
    float ring = out0 - BeatPhase * 2.4;
    float pulse = exp(-ring * ring * 7.0) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);
    float3 colour = glaze * (0.42 + band * 0.75 + pulse * 0.45 + DropPulse * 0.2);

    // A soft light sliding over the glaze, catching its ripples.
    float2 lamp = float2(sin(Time * 0.13) * Aspect * 0.7, cos(Time * 0.1) * 0.6);
    float sheen = exp(-dot(p - lamp, p - lamp) * 1.6) * (0.55 + 0.9 * pooled);
    colour += float3(1.0, 0.95, 0.85) * sheen * 0.14;

    // Grout between the pieces (the cuts, and the tile edges inside the stars' outer pieces never show).
    float pixel = pixelSize();
    float grout = smoothstep(Grout + pixel, Grout, cut);
    colour = lerp(colour, float3(0.012, 0.01, 0.01), grout);
    colour *= 0.9 + 0.2 * smoothstep(Grout, Grout * 5.0, cut);
    return colour * (0.85 + Intensity * 0.25) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.8);
}
