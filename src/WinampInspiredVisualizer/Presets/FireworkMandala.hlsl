// Name: Firework Mandala
// Fireworks burst on the beat and are mirrored six ways into a blooming mandala of peonies, rings and golden willows that drift outward as they fade.
// Particles: 262144

// Every spark is drawn six times: three turns around the centre, each with its mirror image.
static const uint Copies = 6;
// Shells take turns to launch: one on every other beat, and one every few seconds even in silence.
static const int Shells = 8;
// Each star in a burst is a short streak of dots, each a moment behind the one in front.
static const uint Trail = 16;

// Which star this particle belongs to, how far back along its streak it is, and its shell.
void identify(uint id, out uint star, out uint tail, out int shell)
{
    uint spark = id / Copies;
    star = spark / Trail;
    tail = spark % Trail;
    shell = (int)(star % (uint)Shells);
}

int launchClock()
{
    return (int)floor(BeatCount * 0.5) + (int)floor(Time / 3.0);
}

// The most recent launch that belonged to this shell (or -1 before its first).
int lastLaunch(int shell)
{
    int clock = launchClock();
    return clock - (int)wrap((float)(clock - shell), (float)Shells);
}

// A random number for one spark of one launch, shared by its six copies.
float sparkRandom(uint spark, int launch, float salt)
{
    return pcgHash(spark * 9781u + (uint)launch * 6271u + pcgHash(asuint(salt))) / 4294967295.0;
}

float shellRandom(int launch, float salt)
{
    return pcgHash((uint)launch * 7919u + pcgHash(asuint(salt) ^ 0x5bd1e995u)) / 4294967295.0;
}

// 0 peony (a sphere of sparks), 1 ring, 2 golden willow, 3 two-tone chrysanthemum.
int shellKind(int launch)
{
    return (int)(shellRandom(launch, 1.0) * 4.0);
}

float lifeOf(int kind, float r)
{
    return (kind == 2 ? 3.4 : 2.0) * (0.75 + 0.5 * r);
}

void Spawn(inout Particle particle, uint id)
{
    uint spark;
    uint tail;
    int shell;
    identify(id, spark, tail, shell);
    int launch = lastLaunch(shell);
    particle.life = 1e6;
    particle.velocity = 0.0;
    particle.position = 0.0;
    // Pretend the latest few shells went up a moment ago, so the show starts straight away.
    particle.data = float4(launch, Time - wrap((float)(launchClock() - shell), (float)Shells) * 0.45, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    uint spark;
    uint tail;
    int shell;
    identify(id, spark, tail, shell);
    int launch = lastLaunch(shell);
    if ((float)launch != particle.data.x)
    {
        particle.data.x = launch;
        particle.data.y = Time;
    }

    float t = max(Time - particle.data.y - tail * 0.02, 0.0);
    int kind = shellKind(launch);

    // Where this launch bursts, and which way this spark flies.
    // Bursts land inside one 60-degree wedge, so the mirrored copies form a ring of blooms.
    float reach = lerp(0.35, 1.05, shellRandom(launch, 2.0));
    float2 origin = rotate(float2(reach, 0.0), shellRandom(launch, 3.0) * PI / 3.0);
    float z = sparkRandom(spark, launch, 4.0) * 2.0 - 1.0;
    float angle = sparkRandom(spark, launch, 5.0) * 2.0 * PI;
    float3 direction = float3(sqrt(1.0 - z * z) * cos(angle), sqrt(1.0 - z * z) * sin(angle), z);
    if (kind == 1)
    {
        // A ring: every spark in one tilted plane.
        direction = float3(cos(angle), sin(angle) * (0.3 + 0.7 * shellRandom(launch, 6.0)), 0.0);
    }

    float speed = (kind == 2 ? 0.45 : 0.55) * (0.6 + reach * 0.5) * (0.85 + 0.3 * shellRandom(launch, 7.0)) * (0.92 + 0.08 * sparkRandom(spark, launch, 8.0));
    float drag = kind == 2 ? 2.6 : 1.9;
    float gravity = kind == 2 ? 0.32 : 0.2;
    float spread = (1.0 - exp(-drag * t)) / drag;
    float2 position = origin + direction.xy * speed * spread;
    position.y += gravity * (t - spread) / drag;

    particle.velocity = float3(direction.xy * speed * exp(-drag * t), 0.0);
    particle.position = float3(position, t);
    particle.data.z = t / lifeOf(kind, sparkRandom(spark, launch, 9.0));
    particle.data.w = Time - particle.data.y - tail * 0.02 > 0.0 ? pow(1.0 - tail / (float)Trail, 1.5) : 0.0;
}

Sprite Show(Particle particle, uint id)
{
    uint spark;
    uint tail;
    int shell;
    identify(id, spark, tail, shell);
    uint copy = id % Copies;
    int launch = (int)particle.data.x;
    float fade = particle.data.z;
    int kind = shellKind(launch);

    float2 position = particle.position.xy;
    if ((copy & 1u) != 0u)
    {
        position.y = -position.y;
    }
    position = rotate(position, (copy / 2u) * (2.0 * PI / 3.0) + Time * 0.03);

    // White-hot as it bursts, then the shell's colour, then a crackling, flickering end.
    float hue = shellRandom(launch, 10.0);
    float3 colour = hsv2rgb(hue, 0.85, 1.0);
    if (kind == 2)
    {
        colour = float3(1.0, 0.62, 0.25);
    }
    else if (kind == 3 && sparkRandom(spark, launch, 11.0) > 0.5)
    {
        colour = hsv2rgb(hue + 0.45, 0.8, 1.0);
    }

    float t = particle.position.z;
    colour = lerp(colour, float3(1.0, 0.95, 0.85), exp(-t * 7.0));
    float brightness = saturate(1.0 - fade) * particle.data.w * smoothstep(0.0, 0.45, t);
    float crackle = fade > 0.6 ? step(0.45, random(spark, 12.0)) * 1.6 : 1.0;

    Sprite sprite;
    sprite.position = position;
    sprite.size = launch < 0 || fade >= 1.0 ? 0.0 : lerp(1.4, 0.8, saturate(fade));
    sprite.colour = colour * brightness * crackle * 0.12 * (1.0 + Beat * 0.3);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The trails drift gently outward and turn with the mandala as they fade.
    float2 from = rotate(p * 0.994, -0.002);
    return particles(uv) + previous(toUv(from)) * 0.8;
}
