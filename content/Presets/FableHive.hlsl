// Name: Fable Hive
// A wall of honeycomb, lit from behind. Honey rises in the cells with the music: each column of cells is one band of the
// spectrum, bass on the left and treble on the right, filling with glowing amber as its band gets loud and draining
// again as it falls quiet, with a warm light climbing the comb on every beat.
// Family: Music-shaped
// Tags: Geometric, Calm, Fire
// Added: 2026-11-15 17:45

// A grid of hexagons is two grids of rectangles, one shifted half a step from the other: a point belongs to
// whichever rectangle's middle is nearer. From there, how far a point is from its cell's wall is one line.
static const float Cell = 0.17;                // across one cell, flat side to flat side
static const float Wall = 0.085;               // half the wall between two cells, as a share of a cell

float3 Render(float2 uv, float2 p)
{
    float2 q = (p + float2(sin(Time * 0.05) * 0.03, cos(Time * 0.043) * 0.02)) / Cell;
    float2 step0 = float2(1.0, 1.7320508);
    float4 middles = floor(float4(q, q - float2(0.5, 0.8660254)) / step0.xyxy) + 0.5;
    float4 offsets = float4(q - middles.xy * step0, q - (middles.zw + 0.5) * step0);
    bool first = dot(offsets.xy, offsets.xy) < dot(offsets.zw, offsets.zw);
    float2 local = first ? offsets.xy : offsets.zw;                     // from the cell's middle, in cells
    float2 id = first ? middles.xy : middles.zw + 0.5;
    float2 centre = id * step0 * Cell;                                  // the cell's middle on screen (nearly)

    // How far it is inside its cell's wall (0 at the middle of the wall), and which way that wall faces.
    float2 folded = abs(local);
    float slant = folded.x * 0.5 + folded.y * 0.8660254;
    float toWall = 0.5 - max(slant, folded.x);
    float2 facing = folded.x > slant ? float2(sign(local.x), 0.0) : float2(sign(local.x) * 0.5, sign(local.y) * 0.8660254);

    // How full this cell is. Its column's band sets how high the honey stands in the comb.
    float column = 0.02 + saturate(centre.x / (2.0 * Aspect) + 0.5) * 0.72;
    float loud = spectrum(column) * 0.5 + (spectrum(column - 0.02) + spectrum(column + 0.02)) * 0.25;
    float stands = 0.06 + pow(saturate(loud), 0.8) * 0.9 + DropPulse * 0.08;        // 0 the bottom, 1 the top
    float cellUp = 0.5 - centre.y * 0.5;                                          // where this cell is, the same way
    float tall = 1.7320508 * Cell * 0.5;                                          // a cell's height, the same way
    float full = saturate((stands - cellUp) / tall + 0.5);
    // Inside a part-filled cell the honey lies in the bottom of it (y grows downward).
    float surface = (0.5 - full) * 1.1547;
    float pixel = pixelSize() / Cell;
    float honeyHere = smoothstep(surface - pixel, surface + pixel, local.y) * step(0.001, full);

    float own = hash(id * 0.173 + 1.7);
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;
    float climb = cellUp - BeatPhase * 1.1;
    float warm = exp(-climb * climb * 14.0) * beatLight;

    // An empty cell: a dark well, a little lighter towards its walls.
    float3 well = float3(0.13, 0.06, 0.016) * (0.45 + 1.5 * smoothstep(0.34, 0.0, toWall));
    // Honey: amber, some cells darker or paler, glowing with the band, deeper in colour towards the bottom.
    float3 amber = lerp(float3(1.0, 0.5, 0.04), float3(1.0, 0.72, 0.16), own);
    amber = lerp(amber, float3(0.75, 0.2, 0.02), step(0.9, own));
    float3 honey = amber * (0.34 + loud * 0.6 + warm * 0.35) * (0.82 - 0.4 * local.y);
    // A highlight on the honey's skin, and a bright line along its surface where the cell is only part full.
    float2 spot = local - float2(-0.14, max(surface, -0.5) + 0.12);
    honey += float3(1.0, 0.93, 0.75) * exp(-dot(spot, spot) * 70.0) * 0.4;
    honey += float3(1.0, 0.85, 0.5) * exp(-abs(local.y - surface) * 22.0) * 0.25 * step(full, 0.999);
    float3 colour = lerp(well, honey, honeyHere);
    // The wall's shadow falls into the cell from the top left.
    colour *= 0.62 + 0.38 * smoothstep(0.0, 0.2, toWall - Wall + dot(facing, float2(0.5, 0.6)) * 0.07);

    // The wax walls, lit from the top left so each has a bright side and a shaded one.
    float wall = smoothstep(Wall + pixel, Wall - pixel, toWall);
    float lit = dot(facing, float2(0.6, 0.75));
    float3 wax = float3(0.82, 0.55, 0.16) * (0.34 + 0.2 * lit + 0.25 * (toWall / Wall)) * (0.75 + 0.5 * fbm(q * 3.0));
    wax *= 0.7 + loud * 0.5 + warm * 0.5;
    colour = lerp(colour, wax, wall);

    return colour * (0.9 + Intensity * 0.3) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.7);
}
