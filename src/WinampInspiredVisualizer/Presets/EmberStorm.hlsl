// Name: Ember Storm
// A roaring bonfire throws thousands of sparks into the night, curling up on the hot air and cooling from white-gold to deep red as they rise; the bass stokes the blaze and every beat sends a fresh burst of sparks skywards.
// Family: Particles
// Tags: Fire, Intense, Dark
// Particles: 262144

// y is up; the fire sits at the origin.
static const float Zoom = 1.6;

float3 cameraEye()
{
    float orbit = sin(Time * 0.045) * 0.5;
    return float3(sin(orbit) * 6.2, 1.1 + sin(Time * 0.07) * 0.25, -cos(orbit) * 6.2);
}

float3 cameraTarget()
{
    return float3(0.0, 3.1 + sin(Time * 0.05) * 0.2, 0.0);
}

// How hot something glows, from 0 (cold, dark red) to 1 (white-gold).
float3 blackbody(float heat)
{
    heat = saturate(heat);
    return float3(1.0, pow(heat, 1.6) * 0.85 + 0.04, pow(heat, 4.0) * 0.7) * (0.04 + heat * heat * 2.4);
}

// A new spark rising from the logs. Bursts leave faster and hotter.
void launch(inout Particle particle, uint id, float burst)
{
    float angle = random(id, 1.0) * 2.0 * PI;
    float radius = sqrt(random(id, 2.0)) * (0.55 - burst * 0.15);
    particle.position = float3(cos(angle) * radius, 0.3 + random(id, 3.0) * 0.7, sin(angle) * radius);
    float3 spread = float3(cos(angle), 0.0, sin(angle)) * radius * (1.0 + burst * 2.5) + (randomDirection(id, 4.0) * float3(1.0, 0.3, 1.0)) * (0.4 + burst * 1.4);
    particle.velocity = float3(0.0, 1.8 + random(id, 5.0) * 2.4 + burst * (2.5 + random(id, 6.0) * 4.0), 0.0) + spread;
    particle.life = 1.8 + random(id, 7.0) * 3.2 + burst * 1.0;
    particle.age = 0.0;
    // data: how hot it is, whether it's alive, how quickly it cools, and the beat that launched it.
    particle.data = float4(0.85 + random(id, 8.0) * 0.15, 1.0, 0.35 + random(id, 9.0) * 0.6, BeatCount);
}

// Nearly half the particles are licks of flame rather than sparks: short-lived blobs of glowing gas
// that rush up from the logs, narrowing into tongues and fading as they cool.
static const float FlameShare = 0.45;

bool isFlame(uint id)
{
    return (id + 0.5) / ParticleCount < FlameShare;
}

void launchFlame(inout Particle particle, uint id)
{
    float angle = random(id, 20.0) * 2.0 * PI;
    float radius = sqrt(random(id, 21.0)) * 0.7;
    particle.position = float3(cos(angle) * radius, 0.15 + random(id, 22.0) * 0.45, sin(angle) * radius);
    particle.velocity = float3(0.0, 1.0 + random(id, 23.0) * 1.5, 0.0);
    particle.life = (0.45 + random(id, 24.0) * 0.6) * (0.8 + saturate(Bass) * 0.5);
    particle.age = 0.0;
    // data: (unused), kind (2 = flame), how long it lives in all.
    particle.data = float4(1.0, 2.0, particle.life, 0.0);
}

void Spawn(inout Particle particle, uint id)
{
    if (isFlame(id))
    {
        launchFlame(particle, id);
        if (ParticleReset > 0.5)
        {
            particle.life *= random(id, 25.0);
        }
        return;
    }
    // Only some of the sparks are flying at any moment; the rest wait in the logs. The harder the bass,
    // the more of them leave. On the very first frame they're spread through their lives so the air is already full.
    float stoke = 0.04 + saturate(Bass * 1.3) * 0.12;
    if (random(id, 10.0) < stoke)
    {
        launch(particle, id, 0.0);
        if (ParticleReset > 0.5)
        {
            float head = random(id, 11.0) * particle.life;
            particle.life -= head;
            particle.position += particle.velocity * head * 0.7;
            particle.data.x *= exp(-particle.data.z * head);
        }
    }
    else
    {
        particle.position = float3(0.0, 0.5, 0.0);
        particle.velocity = 0.0;
        particle.life = 0.05 + random(id, 12.0) * 0.3;
        particle.data = float4(0.0, 0.0, 0.0, BeatCount);
    }
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float3 position = particle.position;
    float height = position.y;
    float3 churn = curlNoise(position * float3(0.45, 0.32, 0.45) + float3(0.0, -Time * 0.9, Time * 0.2)) * (1.0 + height * 0.35) * (1.0 + Mid * 0.8);

    if (particle.data.y > 1.5)
    {
        // A lick of flame: buoyant, drawn in towards the middle so the fire tapers to tongues, and torn by fine eddies.
        float3 eddies = curlNoise(position * 1.6 + float3(0.0, -Time * 3.0, 0.0)) * 0.9;
        float3 inward = -float3(position.x, 0.0, position.z) * 2.2;
        float3 lift = float3(0.0, 3.8 + Bass * 2.0, 0.0);
        particle.velocity += (lift + (churn * 0.5 + eddies + inward - particle.velocity) * 2.0) * dt;
        particle.position += particle.velocity * dt;
        return;
    }

    if (particle.data.y < 0.5)
    {
        // Waiting in the logs: a beat throws a big handful of them out at once.
        if (Beat > 0.8 && particle.data.w != BeatCount && random(id, 13.0) < 0.05 + Bass * 0.08)
        {
            launch(particle, id, 1.0);
        }
        return;
    }

    // Hot sparks are carried up by the rising air, which churns and curls; as they cool the lift fades
    // and the air's drag slows them, so the coolest drift and tumble.
    float heat = particle.data.x;
    // The plume draws air in towards its middle low down, and spreads it out higher up.
    float3 inward = -float3(position.x, 0.0, position.z) * (height < 3.0 ? 0.25 : -0.12);
    float3 lift = float3(0.0, 2.6 * heat + 0.8, 0.0) * (1.0 + Bass * 0.6);
    float3 airVelocity = churn * 1.4 + inward + float3(0.25, 0.0, 0.1) * height * 0.08;
    float3 acceleration = lift + (airVelocity - particle.velocity) * 1.2 - float3(0.0, 1.0, 0.0);
    particle.velocity += acceleration * dt;
    particle.position += particle.velocity * dt;
    particle.data.x = heat * exp(-dt * particle.data.z);
    // A spark that's cooled to nothing or flown out of sight is done.
    if (particle.data.x < 0.06 || particle.position.y > 14.0)
    {
        particle.life = 0.0;
    }
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    if (particle.data.y < 0.5)
    {
        return sprite;
    }
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 seen = project(particle.position, eye, target, Zoom, 0.0);
    if (seen.z < 0.3)
    {
        return sprite;
    }
    sprite.position = float2(seen.x, -seen.y);

    if (particle.data.y > 1.5)
    {
        // Flame: a big soft blob, white-gold when new, cooling to orange and red as it rises and thins.
        float left = saturate(particle.life / max(particle.data.z, 1e-3));
        float heat = left * (0.75 + 0.25 * saturate(1.0 - particle.position.y * 0.3));
        sprite.size = 3.0 + (1.0 - left) * 4.0;
        float swell = smoothstep(0.0, 0.12, particle.age);
        sprite.colour = blackbody(0.15 + heat * 0.75) * left * swell * 0.035 * (0.8 + Bass * 0.5);
        float3 below = project(particle.position - particle.velocity * 0.05, eye, target, Zoom, 0.0);
        sprite.streak = float2(seen.x - below.x, below.y - seen.y);
        return sprite;
    }

    // Drawn as a streak along its path over a sixteenth of a second, like a long camera exposure.
    float3 before = project(particle.position - particle.velocity * 0.06, eye, target, Zoom, 0.0);
    sprite.streak = float2(seen.x - before.x, before.y - seen.y);
    float heat = particle.data.x;
    // Some sparks flicker as they tumble, and a few are fat glowing embers.
    float flicker = 0.7 + 0.3 * sin(particle.age * (14.0 + fixedRandom(id, 14.0) * 20.0) + id);
    float ember = fixedRandom(id, 15.0) > 0.93 ? 2.2 : 1.0;
    sprite.size = (0.9 + fixedRandom(id, 16.0) * 0.8) * ember;
    // Inside the flames they're lost in the glow, so they fade in as they clear it.
    float clear = smoothstep(0.5, 2.2, particle.position.y);
    sprite.colour = blackbody(heat) * flicker * (0.7 + Treble * 0.5) * ember * (0.2 + 0.8 * clear);
    focus(sprite, seen.z, length(eye - target), 0.06);
    return sprite;
}

// ---- The fire itself ----

float capsuleHit(float3 origin, float3 direction, float3 a, float3 b, float radius)
{
    float3 ba = b - a;
    float3 oa = origin - a;
    float baba = dot(ba, ba);
    float bard = dot(ba, direction);
    float baoa = dot(ba, oa);
    float rdoa = dot(direction, oa);
    float oaoa = dot(oa, oa);
    float qa = baba - bard * bard;
    float qb = baba * rdoa - baoa * bard;
    float qc = baba * oaoa - baoa * baoa - radius * radius * baba;
    float h = qb * qb - qa * qc;
    if (h >= 0.0)
    {
        float t = (-qb - sqrt(h)) / qa;
        float y = baoa + t * bard;
        if (y > 0.0 && y < baba)
        {
            return t > 1e-3 ? t : -1.0;
        }
        float3 oc = (y <= 0.0) ? oa : origin - b;
        qb = dot(direction, oc);
        qc = dot(oc, oc) - radius * radius;
        h = qb * qb - qc;
        if (h > 0.0)
        {
            t = -qb - sqrt(h);
            return t > 1e-3 ? t : -1.0;
        }
    }
    return -1.0;
}

// Six logs leaning together in a cone.
void logEnds(int i, out float3 a, out float3 b)
{
    float angle = i * (2.0 * PI / 6.0) + 0.3;
    float3 outward = float3(cos(angle), 0.0, sin(angle));
    a = outward * 1.05 + float3(0.0, 0.12, 0.0);
    b = outward * 0.12 + float3(0.0, 1.05 + 0.1 * (i & 1), 0.0);
}

// How brightly the fire is burning right now: it roars with the bass and flickers on its own.
float fireStrength()
{
    return 0.75 + Bass * 0.7 + Beat * 0.25 + (noise(float2(Time * 7.0, 0.0)) - 0.5) * 0.25;
}

// Flames: rising tongues of turbulent noise inside a teardrop, hottest low in the middle.
float flameHeat(float3 position)
{
    float height = position.y;
    float strength = fireStrength();
    float top = 1.9 + strength * 1.2;
    float rise = saturate(height / top);
    // The tongues lick upward and lean as the hot air sways.
    float3 flow = float3(position.x * 2.2, height * 1.5 - Time * 3.4, position.z * 2.2);
    float turbulence = noise3(flow) * 0.55 + noise3(flow * 2.1 + 7.0) * 0.3 + noise3(flow * 4.3 - 3.0) * 0.15;
    float3 swayed = position + float3(sin(Time * 1.3 + height * 1.5), 0.0, cos(Time * 1.1 + height)) * 0.12 * rise;
    float across = length(swayed.xz) + turbulence * 0.35 * rise;
    float width = 0.85 * pow(1.0 - rise, 1.4) * (0.55 + 0.45 * saturate(height * 3.0));
    float body = saturate(1.0 - across / max(width, 1e-3));
    return saturate(body * (1.2 - rise * 0.6) * (0.6 + turbulence * 0.8) * strength);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);

    // The night: a deep blue sky with faint stars and a glow of smoke over the fire.
    float3 colour = float3(0.006, 0.008, 0.02) + float3(0.02, 0.025, 0.05) * saturate(direction.y * 2.0);
    float2 starCell = floor(direction.xy / max(direction.z + 1.6, 0.1) * 300.0);
    colour += pow(hash(starCell), 3000.0) * 0.35 * smoothstep(0.05, 0.3, direction.y);
    float strength = fireStrength();

    // The ground, lit warm by the fire, fading to darkness.
    float distance = 1e4;
    if (direction.y < 0.0)
    {
        distance = -eye.y / direction.y;
        float3 ground = eye + direction * distance;
        float fromFire = length(ground.xz);
        float grit = 0.6 + 0.4 * fbm(ground.xz * 3.0) + 0.3 * noise(ground.xz * 20.0);
        float3 firelight = float3(1.0, 0.45, 0.15) * strength * 1.6 / (0.3 + fromFire * fromFire);
        colour = float3(0.05, 0.045, 0.04) * grit * firelight + float3(0.004, 0.005, 0.01);
        // Hot coals glowing in a ring under the logs.
        float coals = smoothstep(1.3, 0.4, fromFire) * pow(noise(ground.xz * 9.0 + Time * 0.2), 2.0);
        colour += blackbody(0.5 + coals * 0.4) * coals * 1.6 * strength;
    }

    // The logs: charred dark, with glowing cracks on the sides facing the flames.
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        float3 a, b;
        logEnds(i, a, b);
        float t = capsuleHit(eye, direction, a, b, 0.11);
        if (t > 0.0 && t < distance)
        {
            distance = t;
            float3 position = eye + direction * t;
            float3 ba = b - a;
            float3 normal = normalize(position - (a + ba * saturate(dot(position - a, ba) / dot(ba, ba))));
            float facingFire = saturate(dot(normal, normalize(float3(0.0, 1.0, 0.0) - position)));
            float cracks = pow(saturate(1.0 - abs(noise(position.xz * 14.0 + position.y * 9.0) - 0.5) * 6.0), 3.0);
            float inner = saturate(1.0 - length(position.xz) * 0.9);
            colour = float3(0.03, 0.022, 0.018) * (0.3 + facingFire * strength * 1.5)
                   + blackbody(0.45 + inner * 0.3) * cracks * (0.4 + inner * 1.5) * strength;
        }
    }

    // March through the flames along the ray, adding up their glow until something solid is hit.
    float3 flames = 0.0;
    float3 closest = eye + direction * max(dot(-eye, direction), 0.0);
    float entry = max(dot(float3(0.0, 1.4, 0.0) - eye, direction) - 1.6, 0.05);
    int steps = qualitySteps(32);
    float stride = 3.2 / steps;
    [loop]
    for (int s = 0; s < steps; s++)
    {
        float t = entry + (s + hash(p * 300.0 + Time)) * stride;
        if (t > distance)
        {
            break;
        }
        float3 position = eye + direction * t;
        if (position.y < 0.0 || position.y > 4.5 || length(position.xz) > 1.0)
        {
            continue;
        }
        float heat = flameHeat(position);
        flames += blackbody(0.25 + heat * 0.6) * smoothstep(0.05, 0.7, heat) * stride * 0.9;
    }
    colour += flames;

    // A warm haze of heat and smoke hangs over the fire.
    float haze = exp(-length(closest - float3(0.0, 2.0, 0.0)) * 0.6);
    colour += float3(0.5, 0.18, 0.05) * haze * 0.18 * strength;

    // The sparks, with a short trail to soften their streaks.
    return colour + particles(uv) + previous(uv) * 0.4;
}
