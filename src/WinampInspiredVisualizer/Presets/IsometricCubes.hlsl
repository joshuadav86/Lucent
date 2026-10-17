// Name: Isometric Cubes
// The classic tumbling-blocks pattern of shaded cubes, seen dead-on so it tiles perfectly: waves of cubes lift
// out of the field and sink back, flipping which way the illusion reads, while the beat raises rings of them in light.
// Family: Geometric
// Tags: Geometric, Retro, 3D

// How far the column at grid cell c has lifted. Waves lift whole cubes by exactly one step and set them
// down again, so at rest the pattern is always a perfect tiling.
float icRise(float2 c, float2 screen, out float accent)
{
    float2 dir = normalize(float2(cos(Time * 0.05), sin(Time * 0.05)));
    float sweep = frac(dot(screen, dir) * 0.09 - Time * 0.11);
    float lift = smoothstep(0.0, 0.07, sweep) * (1.0 - smoothstep(0.2, 0.27, sweep));
    float seed = frac(sin(dot(c, float2(12.9898, 78.233))) * 43758.5453);
    float ripple = Beat * exp(-pow(length(screen) - (1.0 - Beat) * 9.0 - 0.5, 2.0) * 0.5);
    float level = spectrum(0.05 + seed * 0.6);
    float bob = smoothstep(0.35, 0.9, level) * 0.45 * step(0.62, seed);
    accent = saturate(ripple * 1.5 + bob * 1.6 + lift * 0.2);
    return lift + ripple * 0.7 + bob;
}

float icLine(float d, float w, float aa)
{
    return saturate((w - abs(d)) / aa + 0.5);
}

float3 Render(float2 uv, float2 p)
{
    // An orthographic camera looking straight down the cube diagonal: the angle that turns a staircase of
    // cubes into the flat, perfectly regular tumbling-blocks pattern.
    float3 forward = -normalize(float3(1.0, 1.0, 1.0));
    float3 right = normalize(float3(1.0, 0.0, -1.0));
    float3 up = normalize(cross(right, forward));
    float scale = 6.5 - Bass * 0.25;
    float2 view = rotate(float2(p.x, -p.y), 0.04 * sin(Time * 0.03)) * scale;
    float3 centre = float3(Time * 0.35, 0.0, -Time * 0.35) + float3(1.0, -1.0, 0.0) * sin(Time * 0.04) * 2.0;
    float3 origin = centre + right * view.x + up * view.y;
    float pixelWorld = pixelSize() * scale;

    // Start the walk just above the tallest possible cube: the staircase surface lies near x + y + z = 0..2.
    float startLevel = 2.0 + 2.5;
    float t = (dot(origin, float3(1.0, 1.0, 1.0)) - startLevel) / 1.7320508;
    float3 hitPos = origin;
    float2 hitCell = float2(0.0, 0.0);
    float hitTop = 1.0;
    float hitAxis = 0.0;
    float hitHeight = 0.0;
    float hitAccent = 0.0;
    float entryAxis = 0.0;
    bool found = false;

    [loop]
    for (int i = 0; i < 40; i++)
    {
        float3 pos = origin + forward * t;
        float2 cell = floor(pos.xz + forward.xz * 1e-4);
        float2 cellCentre3 = cell + 0.5;
        float3 world = float3(cellCentre3.x, -(cell.x + cell.y), cellCentre3.y) - centre;
        float2 screen = float2(dot(world, right), dot(world, up));
        float accent = 0.0;
        float h = -(cell.x + cell.y) + icRise(cell, screen, accent);
        if (pos.y <= h)
        {
            hitPos = pos;
            hitCell = cell;
            hitTop = 0.0;
            hitAxis = entryAxis;
            hitHeight = h;
            hitAccent = accent;
            found = true;
            break;
        }
        // Distance to the far walls of this cell (the ray always heads towards smaller x and z).
        float tx = (pos.x - cell.x) / -forward.x;
        float tz = (pos.z - cell.y) / -forward.z;
        float exitT = min(tx, tz);
        float topT = (h - pos.y) / forward.y;
        if (topT <= exitT)
        {
            hitPos = pos + forward * topT;
            hitCell = cell;
            hitTop = 1.0;
            hitHeight = h;
            hitAccent = accent;
            found = true;
            break;
        }
        entryAxis = (tx < tz) ? 0.0 : 1.0;
        t += exitT + 1e-4;
    }

    // Three flat tones per cube, from a slowly turning palette; lifted cubes catch an accent colour.
    float2 drift = hitCell * 0.035;
    float hue = 0.58 + 0.08 * sin(Time * 0.02 + drift.x - drift.y) + 0.05 * sin(Time * 0.013);
    float3 base = hsv2rgb(hue, 0.55, 1.0);
    float3 accentColour = hsv2rgb(hue + 0.45 + 0.1 * sin(Time * 0.03), 0.75, 1.0);
    float3 topTone = lerp(base, float3(1.0, 1.0, 1.0), 0.55) * 0.62;
    float3 rightTone = base * 0.3;
    float3 leftTone = base * 0.11;

    float3 colour;
    float edge;
    if (hitTop > 0.5)
    {
        colour = topTone;
        float2 f = hitPos.xz - hitCell;
        edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
    }
    else
    {
        // Side faces: x-facing ones a little lighter than z-facing ones. Lines mark each cube in the column.
        colour = hitAxis < 0.5 ? rightTone : leftTone;
        float along = hitAxis < 0.5 ? hitPos.z - hitCell.y : hitPos.x - hitCell.x;
        float drop = hitHeight - hitPos.y;
        edge = min(min(along, 1.0 - along), abs(drop - round(drop)));
        // A gentle shadow near the foot of a lifted cube's exposed side.
        colour *= 1.0 - 0.35 * saturate(drop - 1.0);
    }
    colour = lerp(colour, accentColour * (hitTop > 0.5 ? 0.9 : (hitAxis < 0.5 ? 0.45 : 0.2)), hitAccent * 0.75);
    colour *= 0.85 + Level * 0.3;

    // Crisp seams between faces, with a faint glow along the edges of lifted cubes.
    float seam = icLine(edge, pixelWorld * 0.55, pixelWorld);
    colour = lerp(colour, colour * 0.25, seam);
    colour += accentColour * exp(-edge / (pixelWorld * 2.5)) * hitAccent * 0.22;

    if (!found)
        colour = float3(0.02, 0.02, 0.03);
    return colour;
}
