// Name: Fireworks Night
// A fireworks show over a city harbour: rockets climb on the beat and burst into peonies, golden willows, palms and glittering rings, raining embers that shimmer in the water below.
// Family: Particles
// Tags: Intense, Colourful, Fire
// Particles: 262144

// Shells take turns: a new rocket goes up on every other beat, and every few seconds even in silence.
static const int Shells = 12;
// Each shell has this many stars; every star sheds a stream of sparks behind it as it burns.
static const uint Stars = 128;
// Where the water meets the sky (screen y grows downward).
static const float Horizon = 0.46;

int launchClock()
{
    return (int)floor(BeatCount * 0.5) + (int)floor(Time / 3.2);
}

// The most recent launch that belonged to this shell (or -1 before its first).
int lastLaunch(int shell)
{
    int clock = launchClock();
    return clock - (int)wrap((float)(clock - shell), (float)Shells);
}

float shellRandom(int launch, float salt)
{
    return pcgHash((uint)launch * 7919u + pcgHash(asuint(salt) ^ 0x5bd1e995u)) / 4294967295.0;
}

// A random number for one star (or one spark) of one launch.
float starRandom(uint star, int launch, float salt)
{
    return pcgHash(star * 9781u + (uint)launch * 6271u + pcgHash(asuint(salt))) / 4294967295.0;
}

// 0 peony, 1 chrysanthemum, 2 golden willow, 3 ring, 4 palm, 5 glitter.
int shellKind(int launch)
{
    return (int)(shellRandom(launch, 1.0) * 6.0);
}

// Classic firework colours: red, green, blue, gold, violet, silver, orange and teal.
float3 shellColour(float pick)
{
    int index = (int)(pick * 8.0);
    float3 colours[8] = { float3(1.0, 0.12, 0.08), float3(0.2, 1.0, 0.25), float3(0.2, 0.35, 1.0), float3(1.0, 0.7, 0.2),
                          float3(0.75, 0.25, 1.0), float3(0.85, 0.9, 1.0), float3(1.0, 0.4, 0.05), float3(0.1, 0.9, 0.85) };
    return colours[min(index, 7)];
}

// Each kind's behaviour: how long its stars burn, the air's drag on them, gravity, burst speed and how
// long each spark lingers behind its star.
void kindSettings(int kind, out float life, out float drag, out float gravity, out float speed, out float sparkLife)
{
    life = 2.0;
    drag = 1.8;
    gravity = 0.2;
    speed = 0.7;
    sparkLife = 0.14;
    if (kind == 1)
    {
        life = 2.3;
        sparkLife = 0.5;
    }
    else if (kind == 2)
    {
        life = 4.4;
        drag = 2.6;
        gravity = 0.3;
        speed = 0.62;
        sparkLife = 1.4;
    }
    else if (kind == 3)
    {
        sparkLife = 0.22;
        speed = 0.62;
    }
    else if (kind == 4)
    {
        life = 2.4;
        drag = 1.3;
        gravity = 0.34;
        speed = 0.78;
        sparkLife = 0.9;
    }
    else if (kind == 5)
    {
        life = 2.6;
        sparkLife = 0.7;
    }
}

// Where a launch's rocket starts and where it bursts, and how long it climbs.
void launchPlan(int launch, float bass, out float2 ground, out float2 burst, out float climb)
{
    float x = (shellRandom(launch, 2.0) * 2.0 - 1.0) * Aspect * 0.72;
    float height = lerp(0.25, 0.85, shellRandom(launch, 3.0)) * (0.85 + bass * 0.3);
    ground = float2(x - (shellRandom(launch, 4.0) - 0.5) * 0.2, Horizon - 0.02);
    burst = float2(x, Horizon - 0.25 - height);
    climb = 0.9 + height * 0.7;
}

// The rocket's position a moment after launch: quick off the ground, slowing to a stop at the top.
float2 rocketAt(float2 ground, float2 burst, float climb, float t)
{
    float f = saturate(t / climb);
    return lerp(ground, burst, 1.0 - (1.0 - f) * (1.0 - f));
}

// A star's position t seconds after the burst: thrown out against the air's drag, then falling.
float2 starAt(float2 burst, float2 direction, float speed, float drag, float gravity, float t)
{
    float spread = (1.0 - exp(-drag * t)) / drag;
    float2 position = burst + direction * speed * spread;
    position.y += gravity * (t - spread) / drag;
    return position;
}

float2 starVelocity(float2 direction, float speed, float drag, float gravity, float t)
{
    float slowing = exp(-drag * t);
    return direction * speed * slowing + float2(0.0, gravity * (1.0 - slowing) / drag);
}

// This star's direction (in 3D: z tilts it towards or away from us, which dims and shortens it).
float3 starDirection(uint star, int launch, int kind)
{
    float z = starRandom(star, launch, 5.0) * 2.0 - 1.0;
    float angle = starRandom(star, launch, 6.0) * 2.0 * PI;
    if (kind == 4)
    {
        // A palm: a dozen thick fronds, spread evenly but leaning upward.
        angle = (star % 12u + 0.5) / 12.0 * 2.0 * PI + shellRandom(launch, 7.0);
        float3 frond = float3(cos(angle) * 0.9, sin(angle) * 0.7 - 0.35, (frac(star * 0.37) - 0.5) * 0.6);
        return normalize(frond);
    }
    if (kind == 3)
    {
        // A ring: every star in one plane, tipped towards us.
        float tilt = 0.25 + 0.6 * shellRandom(launch, 8.0);
        float2 flat = rotate(float2(cos(angle), sin(angle) * tilt), (shellRandom(launch, 9.0) - 0.5) * 1.2);
        return float3(flat, 0.0);
    }
    float r = sqrt(max(1.0 - z * z, 0.0));
    return float3(r * cos(angle), r * sin(angle), z);
}

void Spawn(inout Particle particle, uint id)
{
    int shell = (int)(id % (uint)Shells);
    int launch = lastLaunch(shell);
    particle.life = 1e6;
    particle.velocity = 0.0;
    particle.position = 0.0;
    // Pretend the latest few shells went up a moment ago, so the show starts straight away.
    particle.data = float4(launch, Time - wrap((float)(launchClock() - shell), (float)Shells) * 0.55, 0.5, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    int shell = (int)(id % (uint)Shells);
    int launch = lastLaunch(shell);
    if ((float)launch != particle.data.x)
    {
        // A new rocket for this shell: remember when it went up and how hard the bass was hitting.
        particle.data.x = launch;
        particle.data.y = Time;
        particle.data.z = Bass;
    }
}

Sprite Show(Particle particle, uint id)
{
    int launch = (int)particle.data.x;
    float bass = particle.data.z;
    float sinceLaunch = Time - particle.data.y;
    uint inShell = id / (uint)Shells;
    uint star = inShell % Stars;
    uint spark = inShell / Stars;
    uint sparkCount = max((uint)ParticleCount / ((uint)Shells * Stars), 2u);
    int kind = shellKind(launch);

    float life;
    float drag;
    float gravity;
    float speed;
    float sparkLife;
    kindSettings(kind, life, drag, gravity, speed, sparkLife);
    float2 ground;
    float2 burst;
    float climb;
    launchPlan(launch, bass, ground, burst, climb);
    float3 hue = shellColour(shellRandom(launch, 10.0));
    float3 gold = float3(1.0, 0.62, 0.22);

    Sprite sprite = (Sprite)0;
    if (launch < 0)
    {
        return sprite;
    }

    // Sparks take turns leaving their star, each lingering for sparkLife seconds, so together they
    // draw a continuous glittering trail. This spark's latest turn started at emitted.
    float turn = (spark + fixedRandom(id, 11.0) * 0.5) / sparkCount;
    float cycleTime = sinceLaunch / sparkLife - turn;
    float cycle = floor(cycleTime);
    float sparkAge = (cycleTime - cycle) * sparkLife;
    float emitted = sinceLaunch - sparkAge;
    uint cycleSeed = spark * 7349u + (uint)(cycle + 4096.0) * 1013u;
    float2 drift = float2(starRandom(cycleSeed, launch + (int)star, 12.0), starRandom(cycleSeed, launch + (int)star, 13.0)) - 0.5;

    if (sinceLaunch < climb)
    {
        // The climb: two stars' worth of particles make the rocket's spitting golden tail.
        if (star >= 2u)
        {
            return sprite;
        }
        float2 rocket = rocketAt(ground, burst, climb, sinceLaunch);
        if (spark == 0u && star == 0u)
        {
            sprite.position = rocket;
            sprite.size = 2.0;
            sprite.colour = float3(1.0, 0.85, 0.6) * 3.0;
            sprite.streak = (rocketAt(ground, burst, climb, sinceLaunch + 0.03) - rocket);
            return sprite;
        }
        float tailAge = sparkAge * 0.6;
        if (emitted < 0.0 || tailAge > 0.5)
        {
            return sprite;
        }
        float2 from = rocketAt(ground, burst, climb, sinceLaunch - tailAge);
        sprite.position = from + drift * 0.05 * tailAge + float2(0.0, 0.12 * tailAge * tailAge);
        sprite.size = 1.1;
        sprite.colour = gold * 1.2 * pow(1.0 - tailAge / 0.5, 2.0);
        return sprite;
    }

    float t = sinceLaunch - climb;
    float starLife = life * (0.85 + 0.3 * starRandom(star, launch, 14.0));
    float3 direction = starDirection(star, launch, kind);
    // Shells launched on a heavy bass bloom wider.
    float starSpeed = speed * (0.8 + bass * 0.45) * (0.9 + 0.1 * starRandom(star, launch, 15.0));
    float2 flatDirection = direction.xy;
    float depthShade = 1.0 - direction.z * 0.3;

    // The heart of the burst: a white flash that melts into the shell's colour.
    float3 starColour = kind == 2 ? float3(1.0, 0.7, 0.3) : kind == 5 ? float3(0.9, 0.92, 1.0) : hue;
    if (kind == 3 && (star & 1u) != 0u)
    {
        starColour = shellColour(frac(shellRandom(launch, 10.0) + 0.37));
    }

    bool head = spark == 0u && (kind != 4 || star < 12u);
    if (head)
    {
        if (t > starLife)
        {
            return sprite;
        }
        float2 position = starAt(burst, flatDirection, starSpeed, drag, gravity, t);
        float fade = saturate((starLife - t) * 2.0);
        float3 colour = lerp(starColour, float3(1.0, 0.97, 0.9), exp(-t * 6.0));
        // Stars near the end of their life crackle and flicker out.
        float flicker = t > starLife - 0.5 ? step(0.4, random(id, 16.0)) * 1.5 : 1.0;
        sprite.position = position;
        sprite.size = kind == 4 ? 2.6 : 1.8;
        sprite.colour = colour * 2.2 * fade * flicker * depthShade * (1.0 + Beat * 0.3);
        sprite.streak = starVelocity(flatDirection, starSpeed, drag, gravity, t) * 0.025;
        return sprite;
    }

    // A spark, shed from its star at time emitted: it drifts and falls behind as it fades.
    float shedAt = emitted - climb;
    if (shedAt < 0.0 || shedAt > starLife)
    {
        return sprite;
    }
    uint parent = kind == 4 ? star % 12u : star;
    float3 parentDirection = kind == 4 ? starDirection(parent, launch, kind) : direction;
    float2 from = starAt(burst, parentDirection.xy, starSpeed, drag, gravity, shedAt);
    float2 carried = starVelocity(parentDirection.xy, starSpeed, drag, gravity, shedAt) * 0.25;
    float sparkDrag = 3.0;
    float spread = (1.0 - exp(-sparkDrag * sparkAge)) / sparkDrag;
    float2 position = from + (carried + drift * 0.08) * spread;
    position.y += (kind == 2 ? 0.12 : 0.22) * (sparkAge - spread) / sparkDrag;

    float fade = 1.0 - sparkAge / sparkLife;
    fade *= fade * saturate((starLife - shedAt) * 2.0);
    float3 colour = starColour;
    if (kind == 1 || kind == 4)
    {
        // Chrysanthemums and palms shed charcoal-gold sparks that redden as they cool.
        colour = lerp(float3(1.0, 0.35, 0.08), float3(1.0, 0.78, 0.45), fade);
    }
    else if (kind == 2)
    {
        colour = lerp(float3(0.9, 0.3, 0.05), gold, fade);
    }
    float brightness = kind == 0 || kind == 3 ? 0.55 : 0.4;
    if (kind == 5)
    {
        // Glitter: sparks strobe on and off, more wildly with the treble.
        brightness *= step(0.6 - Treble * 0.3, random(id, 17.0)) * 2.5;
    }
    sprite.position = position;
    sprite.size = 1.0;
    sprite.colour = colour * brightness * fade * depthShade;
    return sprite;
}

// The skyline along the far shore: how tall the building at x is.
float skylineHeight(float x)
{
    float column = floor(x * 9.0 + 40.0 + sin(x * 3.0) * 0.4);
    float tall = hash(float2(column, 3.1));
    float height = 0.06 + tall * tall * tall * 0.36;
    // Now and then a slender spire on top of a tall one.
    float middle = abs(frac(x * 9.0 + 40.0 + sin(x * 3.0) * 0.4) - 0.5);
    height += step(0.8, tall) * step(middle, 0.06) * 0.08;
    // A gap in the skyline every so often, like a park or a river mouth.
    return height * step(0.15, hash(float2(floor(x * 2.1 + 9.0), 7.7)));
}

float3 sky(float2 p)
{
    float towardsHorizon = saturate((p.y + 1.0) / (Horizon + 1.0));
    float3 colour = lerp(float3(0.002, 0.004, 0.016), float3(0.02, 0.022, 0.06), towardsHorizon * towardsHorizon);
    // The city's glow hanging over the horizon, so the buildings stand out against it.
    colour += float3(0.11, 0.06, 0.12) * exp(-(Horizon - p.y) * 5.0) * 0.5;
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float2 pixel = spritePixel() / (2.0 * float2(Aspect, 1.0));

    if (p.y > Horizon)
    {
        // The harbour reflects the sky, broken up by ripples that grow towards us.
        float depth = p.y - Horizon;
        float ripple = noise3(float3(p.x * 6.0, depth * 40.0 / (depth + 0.05), Time * 0.8));
        float2 mirrored = float2(p.x + ripple * depth * 0.08, Horizon - depth * 1.1 + ripple * depth * 0.02);
        float3 reflected = previous(toUv(mirrored)) + previous(toUv(mirrored + float2(0.0, 0.015))) * 0.5;
        float3 water = float3(0.002, 0.006, 0.014);
        return water + reflected * 0.42 * exp(-depth * 1.6);
    }

    // Buildings stand in front of the sky and the rising rockets.
    float roof = Horizon - skylineHeight(p.x);
    if (p.y > roof)
    {
        // Office windows: a neat grid on each building, about a quarter of them lit.
        float2 cell = float2(p.x * 95.0, p.y * 70.0);
        float2 inCell = frac(cell);
        float lit = step(0.75, hash(floor(cell) * 0.37 + 4.0)) * step(0.3, inCell.x) * step(inCell.x, 0.75) * step(0.35, inCell.y) * step(inCell.y, 0.8);
        lit *= step(0.012, p.y - roof);
        float3 windows = float3(1.0, 0.75, 0.42) * lit * 0.22;
        // Each burst lights the buildings a little.
        float3 flash = previous(toUv(float2(p.x, roof - 0.12))) * 0.15;
        return float3(0.004, 0.004, 0.008) + windows + flash;
    }

    // Smoke and trails: the last frame drifts gently up and across on the breeze and fades.
    float3 background = sky(p);
    float2 breeze = float2(0.6, -0.4) * pixel;
    float3 trails = (previous(uv + breeze) * 2.0 + previous(uv + breeze + float2(pixel.x, 0.0)) + previous(uv + breeze - float2(pixel.x, 0.0))) * 0.25;
    return background + particles(uv) + max(trails - background, 0.0) * 0.86;
}
