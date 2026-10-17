// Name: Meteor Shower
// Meteors streak down a deep starry sky over snow-capped mountains, burning green, gold and violet and leaving glowing trains that twist slowly in the high winds; louder music brings more of them, and strong beats send a blazing fireball across the sky.
// Family: Particles
// Tags: Space, Calm, Dark
// Particles: 262144

// Every meteor is a line of particles: each one waits at its place along the meteor's path, lights
// up as the burning head passes, then glows on as part of the train left behind, fading and
// drifting in the wind. The particles of one meteor all make the same choices on the same frame,
// so they stay together as one meteor without talking to each other.
static const uint Trail = 1024;         // particles per meteor
static const uint Fireballs = 6;        // the first few meteors are kept for fireballs on strong beats

uint meteorOf(uint id)
{
    return id / Trail;
}

// A random number shared by every particle of one meteor on this frame.
float meteorChance(uint meteor, float salt)
{
    return pcgHash(meteor * 7919u ^ pcgHash((uint)FrameCount + asuint(salt))) / 4294967295.0;
}

float meteorRandom(float seed, float salt)
{
    return pcgHash(asuint(seed) ^ pcgHash(asuint(salt) + 0x632BE5ABu)) / 4294967295.0;
}

// The point in the sky the meteors seem to fly away from, just above the top of the picture.
float2 radiant()
{
    return float2(0.55 + sin(Time * 0.013) * 0.3, -1.45);
}

// A meteor's path, from its seed: where it starts (xy), its direction (zw).
float4 meteorPath(float seed, bool fireball)
{
    float2 start = float2((meteorRandom(seed, 1.0) * 2.0 - 1.0) * Aspect * 0.95, lerp(-0.95, 0.05, meteorRandom(seed, 2.0)));
    if (fireball)
    {
        start = float2((meteorRandom(seed, 1.0) * 1.2 - 0.6) * Aspect, lerp(-0.95, -0.7, meteorRandom(seed, 2.0)));
    }
    float2 direction = normalize(start - radiant());
    return float4(start, direction);
}

// Its length (x, in p units), speed (y, p units a second) and how long its train lingers (z, seconds).
float3 meteorShape(float seed, bool fireball)
{
    if (fireball)
    {
        return float3(lerp(1.3, 1.9, meteorRandom(seed, 3.0)), lerp(1.6, 2.2, meteorRandom(seed, 4.0)), 7.0);
    }
    float bright = meteorRandom(seed, 5.0);
    return float3(lerp(0.15, 0.75, meteorRandom(seed, 3.0)), lerp(1.6, 3.4, meteorRandom(seed, 4.0)), lerp(0.25, 3.5, bright * bright * bright));
}

// What a meteor burns with: green magnesium, gold sodium, violet calcium, or white iron.
float3 meteorColour(float seed)
{
    float pick = meteorRandom(seed, 6.0);
    if (pick < 0.35)
    {
        return float3(0.45, 1.0, 0.55);
    }
    if (pick < 0.6)
    {
        return float3(1.0, 0.7, 0.3);
    }
    if (pick < 0.8)
    {
        return float3(0.75, 0.5, 1.0);
    }
    return float3(0.8, 0.9, 1.0);
}

void Spawn(inout Particle particle, uint id)
{
    uint meteor = meteorOf(id);
    // A meteor that isn't flying is parked: data.z is 0. On the very first frame a few are
    // already part-way through, so the sky doesn't start empty.
    particle.life = 1e6;
    particle.velocity = 0.0;
    particle.position = 0.0;
    particle.data = float4(-100.0, 0.0, 0.0, 0.0);
    if (meteor >= Fireballs && meteorChance(meteor, 1.0) < 0.25)
    {
        particle.data = float4(Time - meteorChance(meteor, 2.0) * 3.0, meteor + 0.5, 1.0, 0.0);
    }
}

void Simulate(inout Particle particle, uint id)
{
    uint meteor = meteorOf(id);
    bool fireball = meteor < Fireballs;
    if (particle.data.z > 0.5)
    {
        // Flying: park it again once its train has faded.
        float3 shape = meteorShape(particle.data.y, fireball);
        if (Time - particle.data.x > shape.x / shape.y + shape.z * 2.5)
        {
            particle.data.z = 0.0;
        }
        return;
    }

    // Parked: wait for a turn. Louder music launches meteors more often; a strong beat launches a fireball.
    bool launch;
    if (fireball)
    {
        launch = Beat > 0.7 && Bass > 0.5 && ((uint)BeatCount % 16u) == 0u && meteor == ((uint)BeatCount / 16u) % Fireballs;
    }
    else
    {
        float perSecond = 0.012 + Level * 0.05 + Treble * 0.03;
        launch = meteorChance(meteor, 3.0) < perSecond * min(DeltaTime, 0.1);
    }
    if (launch)
    {
        particle.data = float4(Time, (float)(meteor * 977u + (uint)FrameCount % 4096u) + 0.5, 1.0, 0.0);
    }
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    if (particle.data.z < 0.5)
    {
        return sprite;
    }
    uint meteor = meteorOf(id);
    bool fireball = meteor < Fireballs;
    float seed = particle.data.y;
    float4 path = meteorPath(seed, fireball);
    float3 shape = meteorShape(seed, fireball);
    float along = ((id % Trail) + 0.5) / Trail;
    float flown = Time - particle.data.x;
    // How long ago the burning head passed this particle's place on the path.
    float since = flown - along * shape.x / shape.y;
    if (since < 0.0)
    {
        return sprite;
    }

    // The meteor flares up, burns brightest part-way along, and fizzles out.
    float burn = pow(sin(along * PI), 0.6) * (0.75 + 0.5 * meteorRandom(seed, 7.0));
    // A fireball breaks up near the end in a couple of bright bursts.
    if (fireball)
    {
        burn *= 1.0 + 2.5 * exp(-pow((along - 0.72) * 18.0, 2.0)) + 1.5 * exp(-pow((along - 0.88) * 25.0, 2.0));
    }
    float3 tint = meteorColour(seed);
    float head = exp(-since * 30.0);
    float glowing = exp(-since * 4.0);
    float train = exp(-since / shape.z);
    float3 colour = lerp(tint, float3(1.0, 1.0, 1.0), head * 0.7) * head * (fireball ? 0.12 : 0.3)
                  + tint * glowing * 0.12
                  // The persistent train shines a faint ionised green, whatever the meteor's colour.
                  + lerp(tint, float3(0.35, 1.0, 0.6), 0.6) * train * 0.04 * (shape.z / 3.5);
    colour *= burn * (fireball ? 1.4 : 1.0) * (0.75 + Level * 0.5);

    // The train is blown slowly sideways and twisted by winds high in the atmosphere.
    float2 place = path.xy + path.zw * along * shape.x;
    float2 wind = curlNoise2(place * 2.5 + seed * 0.01, since * 0.15) * 0.012 + float2(0.006, 0.002);
    place += wind * since;

    sprite.position = place;
    sprite.size = (fireball ? 2.2 : 1.1) + head * (fireball ? 4.0 : 1.5) + since * 0.15;
    // Light from the fastest part streaks along the path.
    sprite.streak = path.zw * shape.y * 0.02 * head;
    sprite.colour = colour;
    return sprite;
}

// ---- The night sky and the mountains ----

float3 stars(float2 q)
{
    float3 total = 0.0;
    [unroll]
    for (int layer = 0; layer < 3; layer++)
    {
        float scale = 60.0 + layer * 70.0;
        float2 grid = q * scale + layer * 13.7;
        float2 cell = floor(grid);
        float2 f = frac(grid) - 0.5;
        float h = hash(cell);
        float2 offset = float2(hash(cell + 3.1), hash(cell + 8.7)) - 0.5;
        float2 d = f - offset * 0.7;
        float star = exp(-dot(d, d) * (90.0 + layer * 50.0)) * step(0.88, h);
        float twinkle = 0.75 + 0.25 * sin(Time * (1.5 + h * 6.0) + h * 70.0) * (0.5 + Treble);
        float3 colour = lerp(float3(0.7, 0.8, 1.0), float3(1.0, 0.85, 0.65), hash(cell + 1.7));
        total += colour * star * twinkle * pow((h - 0.88) / 0.12, 3.0) * (2.5 - layer * 0.6);
    }
    return total;
}

// The height of a mountain range (in p units, y down) at x.
float ridge(float x, float layer)
{
    float height = 0.0;
    float amplitude = 0.5;
    float frequency = 1.3 + layer * 0.6;
    [unroll]
    for (int octave = 0; octave < 5; octave++)
    {
        float n = noise(float2(x * frequency, layer * 7.3 + octave * 3.1));
        height += (1.0 - abs(n * 2.0 - 1.0)) * amplitude;
        amplitude *= 0.5;
        frequency *= 2.1;
    }
    return lerp(0.5, 0.95, layer / 2.0) - height * lerp(0.42, 0.18, layer / 2.0);
}

float3 Render(float2 uv, float2 p)
{
    float pan = sin(Time * 0.021) * 0.25;

    // The sky slowly wheels round a pole far above the picture.
    float2 skyPoint = rotate(p - float2(0.2, -2.6), Time * 0.004) + float2(pan * 0.3, 0.0);
    float altitude = saturate((0.6 - p.y) / 1.6);
    float3 colour = lerp(float3(0.025, 0.035, 0.06), float3(0.004, 0.006, 0.016), pow(altitude, 0.6));
    // A faint green airglow and the last of the city glow along the horizon.
    colour += float3(0.02, 0.05, 0.03) * exp(-(0.55 - p.y) * 5.0) + float3(0.05, 0.025, 0.02) * exp(-max(0.55 - p.y, 0.0) * 10.0);

    // The Milky Way: a broad river of faint light crossed by dark dust lanes.
    float2 band = rotate(skyPoint, 0.9);
    float river = exp(-band.y * band.y * 4.0);
    float dust = fbm(band * float2(1.6, 4.0) + 3.0);
    float3 milky = lerp(float3(0.35, 0.3, 0.45), float3(0.6, 0.5, 0.4), fbm(band * 3.0)) * river * (0.08 + 0.16 * fbm(band * 6.0));
    milky *= smoothstep(0.35, 0.65, dust + 0.15 * (1.0 - river));
    colour += milky * (0.9 + Level * 0.2);
    colour += stars(skyPoint) * (1.0 + river * 0.6);

    colour += particles(uv);

    // Three ranges of mountains, the far ones pale with distance, the near ones nearly black,
    // with snow on the far peaks catching the starlight.
    [unroll]
    for (int layer = 0; layer < 3; layer++)
    {
        float depth = layer;
        float x = p.x + pan * (0.3 + depth * 0.5);
        float top = ridge(x, depth);
        float cover = smoothstep(-pixelSize(), pixelSize(), p.y - top);
        float below = max(p.y - top, 0.0);
        float3 rock = lerp(float3(0.03, 0.04, 0.07), float3(0.004, 0.005, 0.008), depth / 2.0);
        // Snow lies in streaks down from the far peaks, catching the starlight, and every ridge has a faint lit edge.
        float snowLine = max(0.02 + 0.07 * fbm(float2(x * 14.0, depth * 5.0)), 0.02);
        float snow = smoothstep(snowLine, snowLine * 0.4, below) * smoothstep(0.35, 0.75, 0.9 - depth * 0.3 - top) * (1.0 - depth / 2.0);
        snow *= 0.5 + 0.5 * noise(float2(x * 60.0, below * 30.0));
        rock += float3(0.09, 0.1, 0.14) * snow * (1.0 + Beat * Bass * 0.4);
        rock += float3(0.02, 0.025, 0.04) * exp(-below * 150.0) * (1.0 + depth);
        // Valley mist pooling between the ranges.
        rock += float3(0.02, 0.025, 0.04) * exp(-below * 12.0) * 0.4 * (1.0 - depth / 2.0);
        colour = lerp(colour, rock, cover);
    }
    return colour;
}
