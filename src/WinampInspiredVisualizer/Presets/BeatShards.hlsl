// Name: Beat Shards
// A black mirror holding a vortex of neon shatters outward from a new spot on every beat, its shards trembling with the spectrum before the glass heals.
// Family: Music-shaped
// Tags: Intense, Neon, Colourful
// Added: 2026-10-16 23:54

static const float ShardSize = 0.34;

float3 shardHash(float2 cell)
{
    float3 p3 = frac(float3(cell.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

// The centre of a shard: jittered in its grid cell and drifting slowly, so the pattern keeps changing.
float2 shardSeed(float2 cell)
{
    float3 h = shardHash(cell);
    // Each seed glides slowly to and fro along its own direction (no trigonometry: this runs a lot).
    float swing = abs(frac(Time * 0.06 + h.z) * 2.0 - 1.0);
    swing = swing * swing * (3.0 - 2.0 * swing) * 2.0 - 1.0;
    float2 wander = (h.yx - 0.5) * 0.3 * swing;
    return (cell + 0.5 + (h.xy - 0.5) * 0.6 + wander) * ShardSize;
}

// Which shard owns point q in the assembled mirror.
float2 shardOwner(float2 q)
{
    float2 base = floor(q / ShardSize);
    float best = 1e9;
    float2 owner = base;
    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 cell = base + float2(i, j);
            float2 d = q - shardSeed(cell);
            float d2 = dot(d, d);
            if (d2 < best)
            {
                best = d2;
                owner = cell;
            }
        }
    }
    return owner;
}

// Distance from q to the edge of the shard grown from `cell`.
float shardEdge(float2 q, float2 cell)
{
    float2 s1 = shardSeed(cell);
    float edge = 1e9;
    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 other = cell + float2(i, j);
            if (i != 0 || j != 0)
            {
                float2 s2 = shardSeed(other);
                float2 axis = s2 - s1;
                edge = min(edge, dot((s1 + s2) * 0.5 - q, axis / length(axis)));
            }
        }
    }
    return edge;
}

// A dark room crossed by a spiral of thin neon lines, for the shards to reflect.
float3 shardRoom(float3 r)
{
    float around = atan2(r.y, r.x) / (2.0 * PI);
    float depth = r.z;
    float swirl = around * 4.0 + depth * 7.0 + length(r.xy) * 3.0 - Time * 0.15;
    float lines = 0.5 + 0.5 * cos(2.0 * PI * swirl);
    lines = pow(lines, 120.0) + 0.08 * pow(lines, 10.0);
    float3 hue = 0.5 + 0.5 * cos(2.0 * PI * (floor(swirl) * 0.17 + around + Time * 0.03 + float3(0.0, 0.33, 0.67)));
    hue = hue * hue + 0.04;
    float3 colour = hue * lines * 1.8 + hue * 0.012;
    colour += lerp(hue, 1.0, 0.5) * pow(saturate(dot(r, normalize(float3(0.4, -0.5, -0.75)))), 300.0) * 0.6;
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float2 view = rotate(p, Time * 0.03);

    // Every beat strikes the mirror somewhere new; a wave of shattering races out from the impact
    // and the pieces settle back into one smooth mirror behind it.
    float sinceBeat = -log(max(Beat, 1e-3)) / 5.0;
    float3 strike = shardHash(float2(BeatCount, 7.0));
    float2 impact = rotate((strike.xy - 0.5) * float2(2.6, 1.4), Time * 0.03);
    float force = (0.6 + 0.6 * saturate(Bass * 1.3)) * step(1e-3, Beat);

    float2 base = floor(view / ShardSize);
    float2 owner = base;
    float2 local = view;
    float nearest = -1.0;
    float3 tumble = float3(0.0, 0.0, 1.0);
    float broken = 0.0;
    [loop]
    for (int j = -1; j <= 1; j++)
    {
        [loop]
        for (int i = -1; i <= 1; i++)
        {
            float2 cell = base + float2(i, j);
            float3 h = shardHash(cell + 71.0);
            float2 centre = shardSeed(cell);
            float2 away = centre - impact;
            float reach = length(away);
            float lag = sinceBeat - reach * 0.12;
            float amount = force * step(0.0, lag) * exp(-lag * 9.0) * exp(-reach * 0.6);
            // Each shard is flung away from the impact, turns and comes towards us by its own amount.
            float2 moved = centre + away / max(reach, 0.05) * amount * 0.09 + (h.xy - 0.5) * amount * 0.1;
            float turn = (h.z - 0.5) * amount * 0.9;
            float lift = 1.0 + h.x * amount * 0.3;
            float2 offset = view - moved;
            if (dot(offset, offset) > ShardSize * ShardSize * 1.6 * lift * lift)
            {
                continue;
            }
            float2 q = rotate(offset, -turn) / lift + centre;
            float2 who = shardOwner(q);
            if (all(who == cell) && lift > nearest)
            {
                nearest = lift;
                owner = cell;
                local = q;
                broken = amount;
                // Every shard trembles with its own band of the spectrum.
                float band = spectrum(0.04 + h.y * 0.8);
                float shiver = band * band * 0.04 * sin(Time * (9.0 + h.z * 7.0) + h.x * 6.28);
                tumble = float3((h.yz - 0.5) * (0.03 + amount * 1.3) + shiver, lift);
            }
        }
    }

    float3 colour = 0.0;
    if (nearest > 0.0)
    {
        float edge = shardEdge(local, owner) * tumble.z;
        float3 h = shardHash(owner + 13.0);
        // The shard's mirror: tipped by its own angle, reflecting a vortex of neon.
        float3 normal = normalize(float3(tumble.xy, -1.0));
        float3 eye = normalize(float3(view * 0.6, 1.0));
        float3 mirror = shardRoom(reflect(eye, normal));
        float band = spectrum(0.04 + h.x * 0.8);
        colour = mirror * (0.5 + band * band * 1.2 + Level * 0.4) + float3(0.008, 0.006, 0.014);
        // Each shard also catches a tilted view of the last frame: reflections within reflections,
        // which break into many while it is flying.
        float shattered = saturate(broken * 3.0);
        float2 echoAt = lerp(view * 0.97, rotate(local - shardSeed(owner), tumble.x * 3.0) * 1.8 + tumble.xy, shattered);
        colour += previous(toUv(rotate(echoAt, -Time * 0.03))) * (0.15 + 0.3 * shattered);
        // Cracks: hairline gaps between the shards, opening wider as they fly apart.
        float inside = edge - px * (0.5 + broken * 5.0);
        // Bright bevelled edges catching the light.
        float rim = exp(-max(inside, 0.0) / (px * 1.3));
        float3 rimTint = lerp(shardRoom(normal * float3(1.0, 1.0, -1.0)), 1.0, 0.4);
        colour += rimTint * rim * (0.25 + broken * 1.5);
        colour *= smoothstep(0.0, px * 1.2, inside);
    }
    return colour;
}
