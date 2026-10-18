// Name: Fractal Temple
// Floating through an endless gravity-free temple of pointed arches whose tracery holds smaller arches, and smaller still.
// Family: Fly-through
// Tags: Colourful, Neon, 3D
// Added: 2026-10-16 23:54

static const float StoreyHeight = 2.6;
static const float ArchWidth = 0.78;      // half-width of the great arches
static const float ArchSpring = 1.25;     // where their pointed heads begin
static const float WallHalf = 0.07;       // half-thickness of the walls
static const float SubArch = 0.38;        // size of each arch inside an arch head

// Opening of a pointed arch of half-width 1 whose head starts at y = 0 (negative inside).
float templeOpening(float2 q)
{
    return min(max(abs(q.x) - 1.0, q.y), length(float2(abs(q.x) + 0.4, q.y)) - 1.4);
}

// One wall's tracery in the arch's own units: the great arch, two arches and a round window
// in its head, and the same again inside each of those, three times over. Negative inside stone;
// depth returns how many levels in the nearest stone lies (0 = the wall itself).
float templeTracery(float2 q, out float depth)
{
    float material = -templeOpening(q);
    float scale = 1.0;
    depth = 0.0;
    [unroll]
    for (int level = 0; level < 3; level++)
    {
        float head = max(templeOpening(q), -q.y);
        float2 sub = float2(abs(q.x) - 0.5, q.y) / SubArch;
        float holes = min(templeOpening(sub) * SubArch, length(q - float2(0.0, 0.78)) - 0.25);
        float tracery = max(head, -holes) * scale;
        depth = tracery < material ? level + 1.0 : depth;
        material = min(material, tracery);
        q = sub;
        scale *= SubArch;
    }
    return material;
}

// A wall standing across a cell edge: across = distance from its middle plane, along = position
// along it, height = height within the storey. Each storey begins with a lintel, so the arch
// openings stop at its top.
// edge returns the distance to the nearest carved edge.
float templeWall(float across, float along, float height, out float depth, out float edge)
{
    float2 q = float2(along, height - ArchSpring) / ArchWidth;
    float stone = min(templeTracery(q, depth) * ArchWidth, height - 0.25);
    float slab = abs(across) - WallHalf;
    edge = max(abs(slab), abs(stone));
    return max(slab, stone);
}

// The temple: walls of arches along every edge of a square grid, storey upon storey with no
// floors, and slender columns where the walls meet. part: 0 wall, 1 column; depth: tracery level;
// edge: distance to the nearest carved edge. A lamp floats in the round window of every great arch;
// orb returns the distance to the nearest one (part is 2 on a lamp).
float templeDE(float3 pos, out float part, out float depth, out float edge, out float orb)
{
    float2 cell = pos.xz - 2.0 * floor(pos.xz * 0.5 + 0.5);    // -1..1 within the cell
    float2 toEdge = 1.0 - abs(cell);                           // distance to the cell's edges
    float up = -pos.y;                                         // world -y is up on screen
    float height = up - StoreyHeight * floor(up / StoreyHeight);
    float depthX = 0.0;
    float depthZ = 0.0;
    float edgeX = 0.0;
    float edgeZ = 0.0;
    float wallX = templeWall(toEdge.x, cell.y, height, depthX, edgeX);
    float wallZ = templeWall(toEdge.y, cell.x, height, depthZ, edgeZ);
    // Fluted columns.
    float column = length(toEdge) - 0.13 - 0.008 * cos(atan2(toEdge.y, toEdge.x) * 16.0);
    float walls = min(wallX, wallZ);
    depth = wallX < wallZ ? depthX : depthZ;
    edge = wallX < wallZ ? edgeX : edgeZ;
    float orbHeight = height - (ArchSpring + 0.78 * ArchWidth);
    orb = min(length(float3(toEdge.x, cell.y, orbHeight)), length(float3(toEdge.y, cell.x, orbHeight))) - 0.08;
    float solid = min(walls, column);
    part = orb < solid ? 2.0 : (column < walls ? 1.0 : 0.0);
    return min(solid, orb);
}

// Blind arcades inlaid in the flat stone: a lattice of small pointed arches traced in light on a
// wall's face. Returns the distance (in world units) to the nearest inlaid line.
float templeInlay(float3 hit, float3 normal)
{
    float2 onWall = abs(normal.x) > abs(normal.z) ? hit.zy : hit.xy;
    float2 tile = float2(0.4, 0.55);
    float2 local = onWall - tile * floor(onWall / tile) - float2(0.2, 0.3);
    local.y = -local.y;                                        // world -y is up on screen
    return abs(templeOpening(local / 0.13)) * 0.13;
}

float3 templePath(float x)
{
    return float3(x, -0.75 - 0.22 * sin(x * 0.37), 0.28 * sin(x * 0.23));
}

float3 Render(float2 uv, float2 p)
{
    float x = Time * 0.45 + (BeatCount - Beat) * 0.08;
    float3 eye = templePath(x);
    float3 ahead = templePath(x + 2.0) + float3(0.0, sin(Time * 0.09) * 0.4, sin(Time * 0.11) * 0.6);
    float3 dir = cameraRay(eye, ahead, p, 0.85 - Beat * 0.06, sin(Time * 0.07) * 0.25);

    const float farPlane = 18.0;
    int maxSteps = qualitySteps(90);
    float travelled = 0.0;
    float part = 0.0;
    float depth = 0.0;
    float edge = 0.0;
    float orb = 0.0;
    float orbGlow = 0.0;
    bool landed = false;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = templeDE(eye + dir * travelled, part, depth, edge, orb);
        orbGlow += exp(-orb * 14.0) * exp(-travelled * 0.12);
        if (d < 0.0008 * travelled)
        {
            landed = true;
            break;
        }
        travelled += d;
        if (travelled > farPlane)
        {
            break;
        }
    }

    float flow = Time * 0.03;
    // Hazy light fills the endless halls, its colour slowly turning.
    float3 haze = palette(flow + 0.6) * 0.03 * (0.7 + Bass * 0.6);
    float3 colour = haze;
    if (landed)
    {
        float3 hit = eye + dir * travelled;
        float p2 = 0.0;
        float d2 = 0.0;
        float g2 = 0.0;
        float o2 = 0.0;
        float e1 = 0.0005 + 0.0008 * travelled;
        float2 e = float2(e1, -e1);
        float3 normal = normalize(e.xyy * templeDE(hit + e.xyy, p2, d2, g2, o2) + e.yyx * templeDE(hit + e.yyx, p2, d2, g2, o2)
                                + e.yxy * templeDE(hit + e.yxy, p2, d2, g2, o2) + e.xxx * templeDE(hit + e.xxx, p2, d2, g2, o2));
        float occlusion = saturate(0.4 + 0.6 * (templeDE(hit + normal * 0.06, p2, d2, g2, o2) + templeDE(hit + normal * 0.18, p2, d2, g2, o2)) / 0.24);

        // Dark polished stone, faintly washed by slow waves of colour rolling through the temple.
        float hue = flow + hit.x * 0.07 - hit.y * 0.09 + hit.z * 0.05 + depth * 0.18;
        float3 tint = palette(hue);
        float3 stone = lerp(float3(0.16, 0.15, 0.18), tint * 0.3, 0.4);

        float3 toLamp = eye + float3(0.0, -0.25, 0.0) - hit;
        float lampDist2 = dot(toLamp, toLamp);
        toLamp *= rsqrt(lampDist2);
        float lamp = 1.0 / (1.0 + lampDist2 * 0.25);
        float diffuse = saturate(dot(normal, toLamp));
        float specular = pow(saturate(dot(reflect(dir, normal), toLamp)), 40.0);
        float rim = pow(1.0 - saturate(dot(normal, -dir)), 3.0);

        // Every carved edge is traced in light: each tracery level in its own colour and tuned to
        // its own part of the spectrum.
        float energy = spectrum(0.08 + depth * 0.22 + part * 0.1);
        float pixel = travelled * pixelSize() / 0.85;
        float edgeLine = saturate(1.0 - edge / (pixel * 1.3)) * step(part, 0.5);
        float3 edgeColour = hsv2rgb(hue + 0.4, 0.75, 1.0);

        // After each beat a ring of light sweeps out through the halls.
        float wave = glow(length(hit - eye) - (1.0 - Beat) * 9.0, 3.0) * Beat;

        // The lamps in the round windows.
        float3 orbColour = lerp(palette(hit.x * 0.07 + hit.z * 0.11 + hit.y * 0.2 + flow), 1.0, 0.3);

        colour = stone * (diffuse * lamp * 1.4 + 0.03) * occlusion
               + specular * lamp * 0.5 + tint * rim * 0.12 * occlusion
               + edgeColour * edgeLine * (0.35 + energy * 1.6 + wave * 2.0) * (0.6 + Bass * 0.5);
        // Faint inlaid arches on the walls' faces, fading out before they get too fine to see.
        float inlay = saturate(1.0 - templeInlay(hit, normal) / (pixel * 1.2)) * saturate(2.5 - travelled * 0.4)
                    * step(part, 0.5) * saturate(1.0 - edgeLine) * saturate(edge * 12.0);
        colour += edgeColour * inlay * (0.08 + energy * 0.25) * occlusion;
        colour = part > 1.5 ? orbColour * (1.2 + energy * 1.5 + Beat * 0.8) : colour;
        colour = lerp(haze, colour, exp(-travelled * 0.11));
    }
    colour += palette(flow + 0.3) * orbGlow * 0.025 * (0.6 + Bass * 0.8);
    return colour + previous(uv) * 0.08;
}
