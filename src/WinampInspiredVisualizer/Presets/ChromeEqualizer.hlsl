// Name: Chrome Equalizer
// A city block of polished chrome and glowing coloured-glass cubes rises and falls as a giant 3D spectrum analyser, bass in the middle and treble round the edge, every block mirroring its neighbours as the camera circles slowly round.
// Family: Ray traced
// Tags: Geometric, Neon, 3D

// The blocks stand on a square grid. Each frame a ray steps from cell to cell across the grid
// (only the cells it passes over), checking the one block in each, so even a big grid stays cheap.
static const int GridSize = 14;
static const float HalfGrid = 7.0;
static const float BlockHalf = 0.4;
static const float Bevel = 0.06;

// Materials.
static const int Nothing = 0;
static const int Floor = 1;
static const int Chrome = 2;
static const int Glass = 3;

static const float3 KeyLight = float3(0.45, 0.8, 0.35);

struct Hit
{
    float distance;
    float3 normal;
    int material;
    float2 cell;
};

// Which band of the spectrum a block shows: the bass in the centre, the treble round the outside.
float bandOf(float2 cell)
{
    float2 centre = cell + 0.5 - HalfGrid;
    return saturate(length(centre) / (HalfGrid * 1.3));
}

float blockHeight(float2 cell)
{
    float band = bandOf(cell);
    float level = spectrum(0.02 + band * 0.75);
    // A ripple runs out from the middle on every beat.
    float2 centre = cell + 0.5 - HalfGrid;
    float ripple = Beat * exp(-pow(length(centre) - (1.0 - Beat) * 9.0, 2.0) * 0.5) * 0.6;
    return 0.15 + pow(level, 1.1) * 5.5 + ripple;
}

bool isGlass(float2 cell)
{
    return frac((cell.x + cell.y) * 0.5) > 0.25;
}

float3 glassColour(float2 cell)
{
    return hsv2rgb(0.5 + frac(bandOf(cell) * 0.42 + Time * 0.01) * 0.42, 0.8, 1.0);
}

// Ray against one block: an upright box on its cell. Returns the distance (or -1) and the normal.
float blockHit(float3 origin, float3 direction, float2 cell, float height, out float3 normal)
{
    float3 centre = float3(cell.x + 0.5 - HalfGrid, height * 0.5, cell.y + 0.5 - HalfGrid);
    float3 extent = float3(BlockHalf, height * 0.5, BlockHalf);
    float3 inverse = 1.0 / direction;
    float3 near = (centre - extent - origin) * inverse;
    float3 far = (centre + extent - origin) * inverse;
    float3 low = min(near, far);
    float3 high = max(near, far);
    float enter = max(max(low.x, low.y), low.z);
    float leave = min(min(high.x, high.y), high.z);
    normal = 0.0;
    if (enter > leave || leave < 1e-3 || enter < 1e-3)
    {
        return -1.0;
    }
    normal = -sign(direction) * step(low.yzx, low.xyz) * step(low.zxy, low.xyz);
    // Rounded edges: tip the normal towards the edge it's near, so the edges catch the light.
    float3 local = origin + direction * enter - centre;
    float3 inner = extent - Bevel;
    float3 bulge = local - clamp(local, -inner, inner);
    if (dot(bulge, bulge) > 1e-8)
    {
        normal = normalize(lerp(normal, normalize(bulge), 0.85));
    }
    return enter;
}

// Step across the grid cell by cell until the ray hits a block or the floor.
Hit trace(float3 origin, float3 direction)
{
    Hit hit;
    hit.distance = 1e4;
    hit.normal = float3(0.0, 1.0, 0.0);
    hit.material = Nothing;
    hit.cell = 0.0;
    if (direction.y < 0.0)
    {
        hit.distance = -origin.y / direction.y;
        hit.material = Floor;
    }

    // Where the ray enters the grid's box (x and z across the grid, y up to the tallest block).
    float3 boxMin = float3(-HalfGrid, 0.0, -HalfGrid);
    float3 boxMax = float3(HalfGrid, 7.0, HalfGrid);
    float3 inverse = 1.0 / direction;
    float3 t0 = (boxMin - origin) * inverse;
    float3 t1 = (boxMax - origin) * inverse;
    float enter = max(max(min(t0.x, t1.x), min(t0.y, t1.y)), max(min(t0.z, t1.z), 0.0));
    float leave = min(min(max(t0.x, t1.x), max(t0.y, t1.y)), max(t0.z, t1.z));
    if (enter > leave)
    {
        return hit;
    }
    float3 start = origin + direction * (enter + 1e-4);
    float2 cell = clamp(floor(start.xz + HalfGrid), 0.0, GridSize - 1.0);
    float2 stepDir = sign(direction.xz);
    float2 delta = abs(1.0 / max(abs(direction.xz), 1e-6));
    float2 edge = (cell - HalfGrid + max(stepDir, 0.0));
    float2 next = (edge - origin.xz) / (abs(direction.xz) > 1e-6 ? direction.xz : 1e-6);
    next = abs(direction.xz) > 1e-6 ? next : 1e9;
    [loop]
    for (int i = 0; i < GridSize * 2 + 2; i++)
    {
        float3 normal;
        float t = blockHit(origin, direction, cell, blockHeight(cell), normal);
        if (t > 0.0 && t < hit.distance)
        {
            hit.distance = t;
            hit.normal = normal;
            hit.material = isGlass(cell) ? Glass : Chrome;
            hit.cell = cell;
            return hit;
        }
        // On to the next cell along the ray.
        if (next.x < next.y)
        {
            cell.x += stepDir.x;
            next.x += delta.x;
        }
        else
        {
            cell.y += stepDir.y;
            next.y += delta.y;
        }
        if (any(cell < 0.0) || any(cell > GridSize - 1.0) || min(next.x, next.y) > leave + 1.0)
        {
            break;
        }
    }
    return hit;
}

// Soft shadow towards the key light: how narrowly the ray clears each block it passes
// (over the top or round the side), against how far it has come, says how much light gets through.
float clearance(float3 position, float2 cell)
{
    float2 centre = cell + 0.5 - HalfGrid;
    float side = max(abs(position.x - centre.x), abs(position.z - centre.y)) - BlockHalf;
    return max(side, position.y - blockHeight(cell));
}

float softShadow(float3 origin, float3 direction, bool fromBlock)
{
    float light = 1.0;
    float2 cell = floor(origin.xz + HalfGrid);
    float2 stepDir = sign(direction.xz);
    float2 delta = abs(1.0 / max(abs(direction.xz), 1e-6));
    float2 edge = cell - HalfGrid + max(stepDir, 0.0);
    float2 next = (edge - origin.xz) / (abs(direction.xz) > 1e-6 ? direction.xz : 1e-6);
    next = abs(direction.xz) > 1e-6 ? next : 1e9;
    float t = 0.0;
    [loop]
    for (int i = 0; i < 16; i++)
    {
        if (any(cell < 0.0) || any(cell > GridSize - 1.0))
        {
            break;
        }
        float exitT = min(next.x, next.y);
        // A block can't shade its own lit face, so the starting block is skipped.
        if (i > 0 || !fromBlock)
        {
            float middle = (t + exitT) * 0.5;
            float3 a = origin + direction * middle;
            float3 b = origin + direction * exitT;
            light = min(light, saturate(5.0 * clearance(a, cell) / max(middle, 0.05)));
            light = min(light, saturate(5.0 * clearance(b, cell) / max(exitT, 0.05)));
        }
        if (light < 0.01 || origin.y + direction.y * exitT > 7.0)
        {
            break;
        }
        t = exitT;
        if (next.x < next.y)
        {
            cell.x += stepDir.x;
            next.x += delta.x;
        }
        else
        {
            cell.y += stepDir.y;
            next.y += delta.y;
        }
    }
    return smoothstep(0.0, 1.0, light);
}

// Ambient occlusion on the floor and the lower sides of the blocks: darker close to tall neighbours.
float occlusion(float3 position, float3 normal)
{
    float2 cell = floor(position.xz + HalfGrid);
    float open = 1.0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 other = cell + float2(x, y);
            if (all(other >= 0.0) && all(other <= GridSize - 1.0))
            {
                float2 centre = other + 0.5 - HalfGrid;
                float2 gap = max(abs(position.xz - centre) - BlockHalf, 0.0);
                float distance = length(gap) + 0.05;
                float towers = saturate(blockHeight(other) - position.y);
                float facing = saturate(dot(normalize(float3(centre - position.xz, 0.0).xzy + 1e-4), normal) * 0.5 + 0.5);
                open -= towers * facing * 0.12 / (distance * 3.0 + 0.3);
            }
        }
    }
    return saturate(open);
}

// The dark studio round the grid: a deep gradient with a low band of neon on the horizon.
float3 background(float3 direction)
{
    float up = direction.y;
    float3 colour = lerp(float3(0.02, 0.01, 0.04), float3(0.002, 0.002, 0.006), saturate(up * 2.0));
    float around = atan2(direction.z, direction.x);
    float3 neonBand = hsv2rgb(around / (2.0 * PI) + Time * 0.02, 0.7, 1.0);
    colour += neonBand * exp(-abs(up - 0.02) * 30.0) * (0.12 + Level * 0.2);
    // A big soft light panel overhead, and a ring of tall strip lights round the studio for the chrome to mirror.
    colour += float3(0.8, 0.85, 1.0) * smoothstep(0.8, 0.9, dot(direction, normalize(KeyLight))) * 1.6;
    float strips = smoothstep(0.75, 0.9, cos(around * 6.0 + Time * 0.05)) * smoothstep(0.7, 0.3, abs(up - 0.25));
    colour += lerp(float3(1.0, 0.3, 0.7), float3(0.3, 0.7, 1.0), 0.5 + 0.5 * sin(around * 3.0)) * strips * (0.5 + Level * 0.8);
    colour += float3(0.03, 0.03, 0.04) * smoothstep(-0.1, 0.5, up);
    return colour;
}

// The lights: a broad cool key light from above and two coloured lights low at the sides.
float3 lighting(float3 position, float3 normal, float3 view, float3 albedo, float shininess, float shadowed)
{
    float3 key = normalize(KeyLight);
    float3 colour = albedo * float3(0.9, 0.92, 1.0) * saturate(dot(normal, key)) * shadowed * 1.1;
    float3 halfway = normalize(key - view);
    colour += float3(1.0, 1.0, 1.0) * pow(saturate(dot(normal, halfway)), shininess) * shadowed * 2.0;
    float3 left = normalize(float3(-1.0, 0.25, -0.3));
    float3 right = normalize(float3(1.0, 0.2, 0.5));
    colour += albedo * float3(1.0, 0.2, 0.6) * saturate(dot(normal, left)) * (0.25 + Bass * 0.4);
    colour += albedo * float3(0.2, 0.7, 1.0) * saturate(dot(normal, right)) * (0.25 + Treble * 0.4);
    return colour + albedo * 0.03;
}

float3 render(float3 origin, float3 direction)
{
    float3 colour = 0.0;
    float3 throughput = 1.0;
    [loop]
    for (int bounce = 0; bounce < 3; bounce++)
    {
        Hit hit = trace(origin, direction);
        if (hit.material == Nothing)
        {
            colour += throughput * background(direction);
            break;
        }
        float3 position = origin + direction * hit.distance;
        float3 normal = hit.normal;
        float facing = saturate(dot(-direction, normal));
        float3 nextOrigin = position + normal * 2e-3;
        float shadowed = softShadow(nextOrigin, normalize(KeyLight), hit.material != Floor);
        float ao = occlusion(position, normal);

        if (hit.material == Floor)
        {
            // A dark polished floor with faint grid lines between the blocks.
            float2 grid = abs(frac(position.xz + HalfGrid) - 0.5);
            float lines = smoothstep(0.47, 0.5, max(grid.x, grid.y)) * step(max(abs(position.x), abs(position.z)), HalfGrid);
            float3 albedo = float3(0.02, 0.02, 0.025) + lines * 0.02;
            float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
            colour += throughput * lighting(position, normal, direction, albedo, 60.0, shadowed) * ao;
            // The coloured glass blocks spill their glow onto the floor nearby.
            float2 cell = floor(position.xz + HalfGrid);
            if (all(cell >= 0.0) && all(cell <= GridSize - 1.0) && isGlass(cell))
            {
                float2 centre = cell + 0.5 - HalfGrid;
                float near = max(abs(position.x - centre.x), abs(position.z - centre.y)) - BlockHalf;
                colour += throughput * glassColour(cell) * exp(-max(near, 0.0) * 8.0) * 0.08 * (0.3 + blockHeight(cell));
            }
            throughput *= lerp(0.15, 1.0, fresnel) * 0.7;
        }
        else if (hit.material == Chrome)
        {
            // Polished chrome: almost a perfect mirror, a little darker head-on.
            float3 tint = float3(0.85, 0.87, 0.92);
            colour += throughput * lighting(position, normal, direction, 0.02, 200.0, shadowed) * ao;
            throughput *= lerp(tint * 0.75, 1.0, pow(1.0 - facing, 5.0)) * lerp(0.6, 1.0, ao);
        }
        else
        {
            // Coloured glass lit from within, glowing brighter the taller (louder) the block.
            float3 tint = glassColour(hit.cell);
            float height = blockHeight(hit.cell);
            float fresnel = 0.05 + 0.95 * pow(1.0 - facing, 5.0);
            float rise = saturate(position.y / max(height, 0.1));
            float glow = (0.12 + height * 0.25) * (0.5 + rise * 0.8);
            // Brighter edges, where the light inside the glass gathers.
            float3 local = abs(position - float3(hit.cell.x + 0.5 - HalfGrid, height * 0.5, hit.cell.y + 0.5 - HalfGrid));
            float edges = smoothstep(BlockHalf - 0.08, BlockHalf, max(local.x, local.z)) + smoothstep(height * 0.5 - 0.08, height * 0.5, local.y);
            colour += throughput * (1.0 - fresnel) * tint * glow * (1.0 + edges * 1.2);
            colour += throughput * lighting(position, normal, direction, tint * 0.05, 120.0, shadowed);
            throughput *= fresnel;
        }

        if (max(throughput.x, max(throughput.y, throughput.z)) < 0.02)
        {
            break;
        }
        direction = reflect(direction, normal);
        origin = nextOrigin;
    }
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float angle = Time * 0.07;
    float radius = 11.5 + 1.5 * sin(Time * 0.05);
    float3 eye = float3(sin(angle) * radius, 4.2 + 1.6 * sin(Time * 0.09), cos(angle) * radius);
    float3 target = float3(0.0, 1.2, 0.0);
    return render(eye, cameraRay(eye, target, float2(p.x, -p.y), 1.9, 0.0));
}
