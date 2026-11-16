// Name: Stone Flower
// A steady flight down an eight-sided shaft walled with bars and steps of cut stone, every face flat and lit in
// one of three tones, pale grey, khaki, and deep umber, so the walls open round the dark centre like the petals
// of a stone flower. The music plays in the light on the stone: each bar warms and brightens with its own part
// of the music, bass at the foot of the shaft and treble at its crown, and each beat sends a band of light running away down the shaft.
// Family: Fly-through
// Tags: Geometric, 3D, Minimal
// Added: 2026-11-16 01:52

static const float SfWalls = 8.0;       // walls round the shaft; an even number, so the left side mirrors the right
static const float SfBlock = 0.14;      // the width of the narrowest bar, with each wall 1 from the middle of the shaft

float sfLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// How far the stone in one place stands out from its wall: broad bars and narrow ones that run down the shaft,
// and steps that run across it.
float sfHeight(float2 cell, float wall)
{
    float col = cell.x + 48.0 * wall + 120.0;
    float row = wrap(cell.y, 600.0);
    float broadCol = floor(col / 2.0);
    float broadSeed = hash(float2(broadCol * 3.7 + 1.3, floor((row + floor(hash(float2(broadCol, 5.5)) * 3.0)) / 3.0) * 1.9 + 7.1));
    float barSeed = hash(float2(col * 1.7 + 9.1, floor((row + floor(hash(float2(col, 8.5)) * 2.0)) / 2.0) * 2.9 + 3.1));
    float stepSeed = hash(float2(row * 2.3 + 11.9, wall * 5.1 + floor(col / 3.0) * 0.7));

    float h = 0.17 * floor(broadSeed * 3.99) + 0.13 * floor(barSeed * 2.99) * step(0.4, frac(barSeed * 13.0)) + 0.08 * step(0.7, stepSeed);
    return clamp(h + 0.09 * barSeed, 0.0, 0.64);
}

// What one line of sight down the shaft sees.
float3 sfLook(float2 q, float2 eye, float travel, float ripple, float swell)
{
    // The left side of the shaft is the right side in a mirror. Each wall has its own blocks, square to it.
    float3 ray = normalize(float3(abs(q.x), -q.y, 0.62));
    float2 first = eye + ray.xy * 1e-3;
    float turn = 2.0 * PI / SfWalls;
    float corner = tan(0.5 * turn);
    float wall = fmod(floor(atan2(first.x, first.y + 1e-7) / turn + 0.5) + SfWalls, SfWalls);

    float2 outward = float2(0.0, 1.0);
    float2 across = float2(1.0, 0.0);
    float2 from = float2(0.0, 0.0);
    float2 dir = float2(0.0, 1.0);
    float2 cell = float2(0.0, 0.0);
    float2 way = float2(1.0, 1.0);
    float2 pace = float2(1.0, 1.0);
    float2 next = float2(1.0, 1.0);
    float leave = 1e9;
    float clockwise = 1.0;

    float t = 0.0;
    float found = 0.0;
    float fresh = 1.0;
    float face = 0.0;       // 0: turned to the middle of the shaft. 1: the side of a bar. 2: looking back up the shaft.
    float turned = 1.0;     // which way round the shaft a side looks
    float height = 0.0;
    int count = qualitySteps(80);
    [loop]
    for (int i = 0; i < count; i++)
    {
        // On coming to a new wall, measure everything square to it.
        if (fresh > 0.5)
        {
            outward = float2(sin(wall * turn), cos(wall * turn));
            across = float2(outward.y, -outward.x);
            from = float2(dot(eye, across), dot(eye, outward));
            dir = float2(dot(ray.xy, across), dot(ray.xy, outward));
            float2 slope = float2(dir.x, ray.z);
            float2 start = float2(from.x, travel);
            way = float2(slope.x < 0.0 ? -1.0 : 1.0, 1.0);
            cell = floor((start + slope * (t + 2e-4)) / SfBlock);
            pace = SfBlock / max(abs(slope), 1e-5);
            next = ((cell + 0.5 + 0.5 * way) * SfBlock - start) * way / max(abs(slope), 1e-5);
            float toNext = dir.x - corner * dir.y > 1e-6 ? (corner * from.y - from.x) / (dir.x - corner * dir.y) : 1e9;
            float toLast = dir.x + corner * dir.y < -1e-6 ? -(from.x + corner * from.y) / (dir.x + corner * dir.y) : 1e9;
            leave = min(toNext, toLast);
            clockwise = toNext < toLast ? 1.0 : -1.0;
            fresh = 0.0;
        }

        height = sfHeight(cell, wall);
        float inner = 1.0 - height;

        // Stepping into this block's place, the ray may already be inside the block: it has met one of its sides.
        if (from.y + dir.y * t > inner)
        {
            found = 1.0;
            break;
        }

        // Otherwise it meets the block's face, or passes on to the next block or round the corner to the next wall.
        float tFace = dir.y > 1e-6 ? (inner - from.y) / dir.y : 1e9;
        if (tFace <= min(leave, min(next.x, next.y)))
        {
            t = tFace;
            face = 0.0;
            found = 1.0;
            break;
        }

        if (leave <= min(next.x, next.y))
        {
            t = leave;
            wall = fmod(wall + clockwise + SfWalls, SfWalls);
            face = 1.0;
            turned = -clockwise;
            fresh = 1.0;
        }
        else if (next.x < next.y)
        {
            t = next.x;
            next.x += pace.x;
            cell.x += way.x;
            face = 1.0;
            turned = -way.x;
        }
        else
        {
            t = next.y;
            next.y += pace.y;
            cell.y += 1.0;
            face = 2.0;
        }
    }

    if (found < 0.5)
        return float3(0.0, 0.0, 0.0);

    // Three tones of stone, by how a face takes the light that falls from above: pale grey where it looks up,
    // khaki where it stands upright, and umber where it looks down.
    float3 normal = float3(-outward, 0.0);
    if (face > 1.5)
        normal = float3(0.0, 0.0, -1.0);
    else if (face > 0.5)
        normal = float3(turned * across, 0.0);
    float lit = 0.5 + 0.5 * dot(normal, normalize(float3(0.0, 0.9, -0.3)));
    float3 stone = lerp(float3(0.1, 0.088, 0.078), float3(0.4, 0.35, 0.27) * (0.72 + 0.5 * lit), smoothstep(0.3, 0.36, lit));
    stone = lerp(stone, float3(0.58, 0.58, 0.6) * (0.5 + 0.5 * lit), smoothstep(0.76, 0.82, lit));

    // The music's light on the stone. Which part of the music a bar takes its light from: by how far round the
    // shaft it sits, from the crown to the foot.
    float round1 = (wall + atan((cell.x + 0.5) * SfBlock) * SfWalls / (2.0 * PI)) / (SfWalls * 0.5);
    float loud = sfLoud(0.08 + 0.6 * saturate(1.0 - round1));
    float band = swell * exp(-(cell.y - ripple) * (cell.y - ripple) * 0.1);
    stone *= float3(1.0, 1.0, 1.0) + float3(0.5, 0.1, -0.32) * 0.8 * loud + float3(1.0, 0.72, 0.4) * 0.32 * band;

    // The dark of the shaft's depth.
    return stone * min(1.3 / (1.0 + 0.9 * t * t), 1.0) * smoothstep(9.5, 6.0, t);
}

float3 Render(float2 uv, float2 p)
{
    float travel = wrap(Time * 0.3, 600.0 * SfBlock);
    float2 q = rotate(p, 0.2 * sin(Time * 0.09));
    float2 eye = float2(0.0, 0.24 * sin(Time * 0.15));
    float e = pixelSize() * 0.25;

    // The band of light each beat sends down the shaft, counted in blocks.
    float since = frac(BeatGlide);
    float ripple = travel / SfBlock + 1.0 + since * 24.0;
    float swell = smoothstep(0.0, 0.15, since) * (1.0 - since);

    // Two looks at each spot, half a pixel apart, keep the cut edges of the stone clean.
    float3 colour = sfLook(q + float2(e, e), eye, travel, ripple, swell);
    colour += sfLook(q - float2(e, e), eye, travel, ripple, swell);
    return colour * 0.5;
}
