// Name: Ember Terraces
// A flight over crags built from grey stone blocks, every outer edge of every block glowing like the rim of a
// coal. Each level of the rock listens to its own part of the music, bass on the low ground and treble on the
// highest ledges, and its edges burn from orange to yellow as that part sounds. The flight is steady, and each
// beat sends a ring of heat racing out across the rock.
// Family: Fly-through
// Tags: 3D, Geometric, Fire, Dark
// Added: 2026-11-16 01:58

float etLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

// Where the line of flight runs, side to side, at a distance along it.
float etPath(float along)
{
    return 7.0 * sin(along * 0.045) + 3.0 * sin(along * 0.019 + 1.3);
}

// 1 where a block of stone fills this cell. The rock is built in three sizes of block, set one upon another,
// low along the line of flight and rising to crags on either side, with hollows eaten out of it.
float etSolid(float3 c)
{
    float away = abs(c.x - etPath(c.z));
    float2 pad4 = floor(c.xz / 4.0);
    float2 pad2 = floor(c.xz / 2.0);
    float rise = smoothstep(3.0, 15.0, away);
    float h = 2.0 + 3.0 * floor(noise(pad4 * 0.37 + 3.0) * 3.99 * (0.4 + 0.6 * rise));
    h += 5.0 * rise * step(0.52, noise(pad4 * 0.21 + 9.0));
    h += floor(noise(pad2 * 0.83 + 17.0) * 2.99) + step(0.9, hash(c.xz));
    float rock = step(c.y + 0.5, h);

    // Hollows, in blocks two wide.
    float3 q = floor(c / 2.0);
    if (c.y > 1.5 && noise(q.xz * 0.31 + q.y * 0.77 + 50.0) < 0.3)
        rock = 0.0;

    // Now and then a slab of stone hangs out over the low ground.
    if (abs(c.y - 10.0) < 1.0 && away > 5.0)
        rock = max(rock, step(0.66, noise(pad2 * 0.19 + 57.0)));

    return rock;
}

// The glow, shadow, and joint line an edge of a block's face gives a spot that is `away` from it.
// x: glow from an open edge. y: shadow where another block rises from the edge. z: the joint between two level blocks.
float3 etEdge(float3 cell, float3 toward, float3 facing, float away)
{
    float beside = etSolid(cell + toward);
    float rising = etSolid(cell + toward + facing);
    float open = (1.0 - beside) * (1.0 - rising);
    return float3(open * (smoothstep(0.07, 0.035, away) * 1.6 + exp(-away * 9.0) * 0.4),
                  rising * exp(-away * 3.5),
                  beside * (1.0 - rising) * smoothstep(0.04, 0.015, away));
}

float3 Render(float2 uv, float2 p)
{
    // Over the rock at a steady pace, looking ahead and down.
    float along = wrap(Time * 1.7, 4096.0);
    float3 eye = float3(etPath(along), 12.6 + 0.5 * sin(Time * 0.31), along);
    float3 target = float3(etPath(along + 13.0), 7.4, along + 13.0);
    float3 ray = cameraRay(eye, target, float2(p.x, -p.y), 1.15, 0.1 * sin(Time * 0.23));

    // Step from block to block along the ray until one is stone.
    float3 cell = floor(eye);
    float3 way = float3(ray.x < 0.0 ? -1.0 : 1.0, ray.y < 0.0 ? -1.0 : 1.0, ray.z < 0.0 ? -1.0 : 1.0);
    float3 stride = 1.0 / (abs(ray) + 1e-5);
    float3 next = (way * (cell - eye) + 0.5 + way * 0.5) * stride;
    float3 crossed = float3(0.0, 0.0, 0.0);
    float found = 0.0;
    int count = qualitySteps(110);
    [loop]
    for (int i = 0; i < count; i++)
    {
        if (etSolid(cell) > 0.5)
        {
            found = 1.0;
            break;
        }

        crossed = step(next.xyz, next.yzx) * step(next.xyz, next.zxy);
        next += crossed * stride;
        cell += crossed * way;
    }

    float3 ember = float3(1.9, 0.5, 0.04);
    float3 colour = float3(0.0, 0.0, 0.0);
    if (found > 0.5 && dot(crossed, crossed) > 0.5)
    {
        float reach = dot(crossed, next - stride);
        float3 at = eye + ray * reach;
        float3 inside = saturate(at - cell);
        float3 facing = -way * crossed;
        float3 acrossAxis = crossed.x > 0.5 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
        float3 upAxis = crossed.z > 0.5 ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, 1.0);
        float u = dot(inside, acrossAxis);
        float v = dot(inside, upAxis);

        float3 edges = etEdge(cell, acrossAxis, facing, 1.0 - u);
        float3 e1 = etEdge(cell, -acrossAxis, facing, u);
        float3 e2 = etEdge(cell, upAxis, facing, 1.0 - v);
        float3 e3 = etEdge(cell, -upAxis, facing, v);
        float glow = max(max(edges.x, e1.x), max(e2.x, e3.x));
        float shadow = max(max(edges.y, e1.y), max(e2.y, e3.y));
        float joint = max(max(edges.z, e1.z), max(e2.z, e3.z));

        // The stone: lightest on top, darker on its sides.
        float sideOn = facing.y > 0.5 ? 1.0 : (abs(facing.x) > 0.5 ? 0.7 : 0.5);
        float3 stone = float3(0.25, 0.27, 0.25) * sideOn * (0.9 + 0.2 * hash(cell.xz + cell.y * 7.0));
        stone *= (1.0 - 0.6 * shadow) * (1.0 - 0.6 * joint);

        // How hot this level's edges are: its own part of the music, and the ring each beat sends out.
        float loud = etLoud(min(0.07 + 0.04 * cell.y, 0.68));
        float since = frac(BeatGlide);
        float ring = exp(-pow(length(at - eye) - 3.0 - since * 46.0, 2.0) * 0.06) * (1.0 - since);
        float heat = 0.55 + 0.9 * loud + 0.6 * ring + 0.3 * DropPulse;

        colour = stone + ember * glow * heat;
        colour += float3(1.1, 1.0, 0.4) * smoothstep(1.0, 1.9, glow) * (0.25 + 0.75 * loud);
        colour *= exp(-reach * 0.034);
    }

    return colour;
}
