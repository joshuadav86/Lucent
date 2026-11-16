// Name: Phosphor Terraces
// A flight over crags built from grey stone blocks, every outer edge of every block glowing with a cold light:
// deep blue on the low ground, through sea blue, to green on the highest ledges. Each level of the rock listens to
// its own part of the music, bass on the low ground and treble on the highest ledges, and its edges brighten
// toward white as that part sounds. The flight is fast and level. On every beat the glow spreads in from the
// edges and a ring of light races out across the rock, filling blocks here and there with light as it passes
// and leaving them to fade behind it.
// Family: Fly-through
// Tags: 3D, Geometric, Neon, Dark
// Added: 2026-11-16 01:58

static float EtSound;                   // how loud the music is just now, taken across all its parts
static float EtSpread;                  // how far the glow reaches in from an edge: further just after a beat
static float EtWide = 0.07;             // how wide an edge's bright line is, in blocks: wider far off, so it never shimmers

// One part of the music, with the high notes made up to stand level with the low ones.
float etPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one. It listens to a broad part and answers gently, so the light swells and never jitters.
float etLoud(float x)
{
    float heard = (etPart(x - 0.035) + etPart(x) + etPart(x + 0.035)) / 3.0;
    return smoothstep(0.0, 1.0, saturate((heard - 0.55 * EtSound) / (0.9 * EtSound + 0.15)));
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
    return float3(open * (smoothstep(EtWide, 0.5 * EtWide, away) * 1.6 * (0.07 / EtWide) + exp(-away * EtSpread) * 0.4),
                  rising * exp(-away * 3.5),
                  beside * (1.0 - rising) * smoothstep(0.04, 0.015, away) * saturate(0.14 / EtWide - 1.0));
}

float3 Render(float2 uv, float2 p)
{
    EtSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        EtSound += etPart(0.08 + 0.12 * part) / 6.0;
    EtSpread = 9.0 - 3.5 * BeatSwell;

    // Fast and level over the rock, looking ahead and down, with only the slowest lean from side to side.
    float along = wrap(Time * 3.3, 4096.0);
    float3 eye = float3(etPath(along), 12.6 + 0.4 * sin(Time * 0.17), along);
    float3 target = float3(etPath(along + 16.0), 7.4, along + 16.0);
    float3 ray = cameraRay(eye, target, float2(p.x, -p.y), 1.15, 0.035 * sin(Time * 0.09));

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

    float3 colour = float3(0.0, 0.0, 0.0);
    if (found > 0.5 && dot(crossed, crossed) > 0.5)
    {
        float reach = dot(crossed, next - stride);
        EtWide = max(0.07, 1.5 * reach * pixelSize() / 1.15);
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
        float3 stone = float3(0.22, 0.25, 0.27) * sideOn * (0.9 + 0.2 * hash(cell.xz + cell.y * 7.0));
        stone *= (1.0 - 0.6 * shadow) * (1.0 - 0.6 * joint);

        // How bright this level's edges are: its own part of the music, the swell of each beat, and the ring
        // each beat sends out. With a steady beat to follow the ring keeps pace with it; without one it follows
        // how far the last beat has faded.
        float loud = etLoud(min(0.09 + 0.04 * cell.y, 0.64));
        float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
        float behind = 3.0 + since * 46.0 - length(at - eye);
        float ring = exp(-behind * behind * 0.05) * (1.0 - since);
        float heat = 0.45 + 1.0 * loud + 1.0 * ring + 0.45 * BeatSwell + 0.3 * DropPulse;

        // The light of each level: deep blue low down, sea blue between, and green on the heights, in bands that
        // lean a little as the flight goes on.
        float high = smoothstep(1.5, 12.5, cell.y + 1.5 * sin(at.z * 0.07 + at.x * 0.05));
        float3 light = lerp(float3(0.08, 0.3, 1.9), float3(0.03, 1.15, 1.45), smoothstep(0.0, 0.55, high));
        light = lerp(light, float3(0.15, 1.75, 0.4), smoothstep(0.45, 1.0, high));

        // The ring lays a wash of light on the stone as it passes, and here and there a block fills with its
        // level's light as the ring reaches it and fades behind it.
        float chosen = step(0.87, hash(cell.xz * 1.3 + cell.y * 7.0));
        float wake = smoothstep(-2.5, 0.0, behind) * exp(-max(behind, 0.0) * 0.055) * (1.0 - since);
        float fill = 0.5 * chosen * wake + 0.12 * ring;

        colour = stone + light * (glow * heat + fill * sideOn);
        colour += lerp(light, float3(1.0, 1.25, 1.2), 0.75) * smoothstep(1.0, 1.9, glow) * (0.3 + 0.35 * loud + 0.35 * BeatSwell);
        colour *= exp(-reach * 0.034);
    }

    return colour;
}
