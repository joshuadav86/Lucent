// Name: Smoke Rings
// Glowing smoke rings puff out one on every beat, rolling forward and slowly widening as they drift towards you; now and then one catches up with the ring ahead, slips through its middle and leapfrogs it, before each ring wobbles, frays and dissolves into the dark.
// Family: Particles
// Tags: Calm, Dark, 3D
// Added: 2026-10-17 17:15
// Particles: 262144

// Rings come in pairs: one on an even beat, its partner on the next. A pair of smoke rings
// launched one behind the other can play leapfrog: the one behind shrinks, speeds up and passes
// through the one in front, which widens and slows, and then they swap. Six pairs take turns,
// so a dozen rings are in the air at once, each made of a share of the particles.
static const uint Pairs = 6;
static const float3 Muzzle = float3(-5.0, -0.3, 1.2);
static const float FadeStart = 3.2;
static const float FadeEnd = 6.0;

// How far a ring has drifted after t seconds: quick at first, then coasting to a stop.
float travel(float t)
{
    return 11.0 * (1.0 - exp(-t / 3.4));
}

// Rings widen as they go.
float baseRadius(float t)
{
    return 0.55 + 0.15 * t;
}

// Each pair flies along its own slightly different line.
float3 pairAxis(float pair)
{
    float a = frac(sin(pair * 12.9898) * 43758.5453);
    float b = frac(sin(pair * 78.233) * 12543.1234);
    return normalize(float3(1.0, 0.08 + (a - 0.5) * 0.2, -0.36 + (b - 0.5) * 0.3));
}

float pairSeed(float pair)
{
    return frac(sin(pair * 3.7193 + 1.3) * 24634.6345);
}

// Where along its line a ring is, and how wide. j is 0 for the first ring of a pair, 1 for its partner.
void ringState(float pair, float launch0, float launch1, float j, out float along, out float radius, out float age)
{
    float age0 = Time - launch0;
    float age1 = launch1 > 0.0 ? Time - launch1 : -1.0;
    age = j < 0.5 ? age0 : age1;
    along = travel(max(age, 0.0));
    radius = baseRadius(max(age, 0.0));
    if (age1 >= 0.0)
    {
        // About half the pairs leapfrog; the rest fly on side by side in single file.
        float leaps = pairSeed(pair) < 0.55 ? 1.0 : 0.0;
        float rate = 2.0 * PI / (3.0 + pairSeed(pair) * 0.8);
        float swap = cos(age1 * rate);
        float squeeze = sin(age1 * rate) * 0.32;
        float d0 = travel(age0);
        float d1 = travel(age1);
        float middle = (d0 + d1) * 0.5;
        float halfGap = (d0 - d1) * 0.5;
        float leap0 = middle + halfGap * swap;
        float leap1 = middle - halfGap * swap;
        along = lerp(along, j < 0.5 ? leap0 : leap1, leaps);
        radius *= 1.0 + (j < 0.5 ? squeeze : -squeeze) * leaps;
    }
}

float3 ringPoint(uint id, float pair, float launch0, float launch1, float j, out float age, out float core)
{
    float along;
    float radius;
    ringState(pair, launch0, launch1, j, along, radius, age);
    float t = max(age, 0.0);
    float seed = pairSeed(pair) * 10.0 + j * 3.1;

    float3 axis = pairAxis(pair);
    float3 u = normalize(cross(axis, float3(0.0, 1.0, 0.0)));
    float3 v = cross(u, axis);

    // Round the ring, with gentle wobbles running round it like a wobbling hoop.
    float theta = fixedRandom(id, 1.0) * 2.0 * PI;
    float wobble = 1.0 + 0.05 * sin(3.0 * theta + t * 1.7 + seed) + 0.035 * sin(5.0 * theta - t * 2.3 + seed * 2.0);
    float tilt = 0.06 * radius * sin(2.0 * theta + t * 1.3 + seed);

    // Round the core: the smoke spins about the ring's own tube as it rolls forward. Most of it
    // gathers in a few twisted strands; the rest is a looser haze that spreads as the ring ages.
    float strand = floor(fixedRandom(id, 2.0) * 3.0);
    bool haze = fixedRandom(id, 3.0) < 0.35;
    float coreRadius = 0.06 + 0.035 * t;
    float spread = haze ? coreRadius * (1.2 + fixedRandom(id, 4.0) * 1.6) : coreRadius * (0.55 + fixedRandom(id, 4.0) * 0.45);
    float phi = strand * 2.0 * PI / 3.0 + theta * 4.0 - t * 5.5 + (haze ? fixedRandom(id, 5.0) * 2.0 * PI : (fixedRandom(id, 5.0) - 0.5) * 0.7);
    core = haze ? 0.35 : 1.0;

    float ringR = radius * wobble + spread * cos(phi);
    float3 position = Muzzle + axis * (along + spread * sin(phi) + tilt) + (u * cos(theta) + v * sin(theta)) * ringR;

    // As it dies the ring frays into curling wisps.
    float fray = smoothstep(FadeStart - 0.8, FadeEnd, t);
    position += curlNoise(position * 1.4 + float3(seed, 0.0, Time * 0.08)) * (0.008 + fray * fray * 0.3) * (haze ? 1.6 : 1.0);
    return position;
}

void Spawn(inout Particle particle, uint id)
{
    // Nothing launched yet; the first beat sets this particle's ring going.
    particle.data = float4(-1.0, -100.0, -1.0, 0.0);
    particle.position = Muzzle;
    particle.velocity = 0.0;
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    uint slot = id % Pairs;
    float j = (float)((id / Pairs) & 1u);
    float pairNow = floor(BeatCount * 0.5);

    // A new pair takes over this slot on every fourth even beat; the partner follows one beat later.
    if (pairNow >= 0.0 && (uint)wrap(pairNow, (float)Pairs) == slot && pairNow != particle.data.x)
    {
        particle.data = float4(pairNow, Time, -1.0, 0.0);
    }
    if (particle.data.x >= 0.0 && particle.data.z < 0.0 && floor(BeatCount) >= particle.data.x * 2.0 + 1.0)
    {
        particle.data.z = Time;
    }

    float age;
    float core;
    float3 position = ringPoint(id, particle.data.x, particle.data.y, particle.data.z, j, age, core);
    particle.velocity = (position - particle.position) / max(DeltaTime, 1e-4);
    // A ring that has just launched jumps from the old one's place: don't streak that.
    if (age < 0.05 || length(particle.velocity) > 6.0)
    {
        particle.velocity = 0.0;
    }
    particle.position = position;
    particle.data.w = age;
}

float3 cameraEye()
{
    float drift = Time * 0.05;
    return float3(0.2 + sin(drift) * 1.5, 0.7 + sin(Time * 0.037) * 0.45, -6.3 + cos(drift * 0.7) * 0.5);
}

float3 cameraTarget()
{
    return float3(0.2 + sin(Time * 0.03) * 0.5, 0.15, 0.0);
}

static const float Zoom = 1.5;

// Smoke colours: deep sea blues, violets and a few warm amber rings.
float3 ringColour(float pair, float j)
{
    float pick = pairSeed(pair * 1.7 + j * 0.37);
    float3 cool = lerp(float3(0.25, 0.55, 1.0), float3(0.6, 0.3, 1.0), frac(pick * 3.0));
    float3 warm = float3(1.0, 0.55, 0.22);
    return lerp(cool, warm, step(0.78, pick));
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float j = (float)((id / Pairs) & 1u);
    float age = particle.data.w;
    bool launched = particle.data.x >= 0.0 && (j < 0.5 || particle.data.z > 0.0);
    if (!launched || age > FadeEnd)
    {
        return sprite;
    }

    float3 eye = cameraEye();
    float3 flip = float3(1.0, -1.0, 1.0);
    float3 seen = project(particle.position * flip, eye * flip, cameraTarget() * flip, Zoom, 0.0);
    float3 seenBefore = project((particle.position - particle.velocity * 0.03) * flip, eye * flip, cameraTarget() * flip, Zoom, 0.0);
    if (seen.z < 0.3)
    {
        return sprite;
    }

    bool haze = fixedRandom(id, 3.0) < 0.35;
    float3 colour = ringColour(particle.data.x, j);
    // The twisted strands burn paler and hotter; the haze keeps the colour.
    if (!haze)
    {
        colour = lerp(colour, float3(0.9, 0.95, 1.0), 0.35);
    }

    // A ring flares as it leaves the muzzle, then glows with the music and fades as it dissolves.
    float birth = exp(-age * 2.5) * 2.0;
    float music = 0.55 + Level * 0.5 + Bass * 0.35 + spectrum(frac(particle.data.x * 0.37) * 0.6 + 0.05) * 0.6;
    float fade = smoothstep(0.0, 0.12, age) * (1.0 - smoothstep(FadeStart, FadeEnd, age));
    // With a fast beat the slot is needed again sooner, so fade out ahead of that.
    float beatsLeft = (particle.data.x + Pairs) * 2.0 - BeatCount;
    fade *= saturate((beatsLeft - 1.0) * 0.5);
    float brightness = (haze ? 0.022 : 0.05) * (music + birth) * fade;

    // A few motes in each ring twinkle with the treble.
    brightness *= 1.0 + step(0.993, fixedRandom(id, 6.0)) * Treble * 10.0;

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = haze ? 1.6 : 1.05;
    sprite.colour = colour * brightness;
    focus(sprite, seen.z, length(eye - cameraTarget()), 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);

    // A dark smoky room: the faintest blue haze, a little lighter towards the middle.
    float3 colour = float3(0.004, 0.006, 0.012) + float3(0.012, 0.016, 0.03) * exp(-dot(p, p) * 0.8);
    colour += float3(0.01, 0.012, 0.025) * (fbm(p * 1.3 + float2(Time * 0.02, 0.0)) - 0.3);

    // The mouth that puffs the rings out: a dim ring of light that flashes on each beat.
    float3 axis = pairAxis(floor(BeatCount * 0.5));
    float facing = dot(direction, axis);
    float across = dot(Muzzle - eye, axis) / (abs(facing) > 1e-4 ? facing : 1e-4);
    float3 offset = eye + direction * max(across, 0.0) - Muzzle;
    float rim = abs(length(offset) - 0.45);
    float flash = Beat * (0.6 + Bass * 0.8);
    colour += float3(0.35, 0.55, 1.0) * (exp(-rim * 60.0) * (0.25 + flash * 1.5) + exp(-length(offset) * 3.5) * (0.04 + flash * 0.3));

    // Soft trails keep the rolling smoke silky.
    return max(colour, 0.0) + particles(uv) + previous(uv) * 0.6;
}
