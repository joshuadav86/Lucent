// Name: Strange Attractors
// Half a million points of light trace the hidden shapes of chaos, flowing from one strange attractor to the next as the music breathes through them.
// Family: Particles
// Tags: Calm, Dark, Neon
// Particles: 393216

// Four chaotic systems, each pulling its particles onto a shape it never quite repeats.
// Every 24 seconds or so the particles let go and stream across to the next one.
static const float AttractorTime = 24.0;

int attractorFor(uint id)
{
    float stagger = fixedRandom(id, 7.0) * 5.0;
    return (int)wrap(floor((Time + stagger) / AttractorTime), 4.0);
}

// Each system in its own coordinates: where its centre is, how big it is, and how fast it runs.
void frameOf(int index, out float3 centre, out float size, out float rate)
{
    centre = 0.0;
    size = 4.0;
    rate = 2.4;
    if (index == 1)
    {
        centre = float3(0.0, 0.0, 0.74);
        size = 1.5;
        rate = 0.32;
    }
    else if (index == 2)
    {
        centre = float3(-2.9, -2.9, -2.9);
        size = 12.0;
        rate = 0.16;
    }
    else if (index == 3)
    {
        centre = float3(0.0, 0.0, 24.0);
        size = 26.0;
        rate = 0.13;
    }
}

float3 flow(int index, float3 q)
{
    if (index == 0)
    {
        // Thomas: a lattice of looping threads. The bass loosens it into a wilder tangle.
        float b = 0.205 - Bass * 0.035;
        return float3(sin(q.y) - b * q.x, sin(q.z) - b * q.y, sin(q.x) - b * q.z);
    }
    if (index == 1)
    {
        // Aizawa: a glowing orb with a tube spun through its heart.
        float r2 = q.x * q.x + q.y * q.y;
        return float3((q.z - 0.7) * q.x - 3.5 * q.y,
                      3.5 * q.x + (q.z - 0.7) * q.y,
                      0.6 + 0.95 * q.z - q.z * q.z * q.z / 3.0 - r2 * (1.0 + 0.25 * q.z) + 0.1 * q.z * q.x * q.x * q.x);
    }
    if (index == 2)
    {
        // Halvorsen: three curling arms, each chasing the next.
        const float a = 1.89;
        return float3(-a * q.x - 4.0 * q.y - 4.0 * q.z - q.y * q.y,
                      -a * q.y - 4.0 * q.z - 4.0 * q.x - q.z * q.z,
                      -a * q.z - 4.0 * q.x - 4.0 * q.y - q.x * q.x);
    }
    // Lorenz: the butterfly.
    return float3(10.0 * (q.y - q.x), q.x * (28.0 - q.z) - q.y, q.x * q.y - 8.0 / 3.0 * q.z);
}

// The world keeps every system about the same size, with each one's "up" pointing up on screen.
float3 toSystem(int index, float3 world, float3 centre, float size)
{
    float3 w = index == 3 ? float3(world.x, world.z, -world.y) : float3(world.x, -world.y, world.z);
    return w * (size / 1.1) + centre;
}

float3 toWorld(int index, float3 q, float3 centre, float size)
{
    float3 w = (q - centre) * (1.1 / size);
    return index == 3 ? float3(w.x, -w.z, w.y) : float3(w.x, -w.y, w.z);
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = randomInSphere(id, 1.0) * 1.3;
    particle.velocity = 0.0;
    particle.life = lerp(15.0, 40.0, random(id, 2.0));
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    int index = attractorFor(id);
    float3 centre;
    float size;
    float rate;
    frameOf(index, centre, size, rate);

    float dt = min(DeltaTime, 0.05) * rate * (0.75 + Level * 0.6);
    float3 q = toSystem(index, particle.position, centre, size);
    [unroll]
    for (int substep = 0; substep < 2; substep++)
    {
        float h = dt * 0.5;
        float3 k1 = flow(index, q);
        float3 k2 = flow(index, q + k1 * h * 0.5);
        q += k2 * h;
    }

    float3 moved = toWorld(index, q, centre, size);
    particle.velocity = (moved - particle.position) / max(DeltaTime, 1e-4);
    particle.position = moved;

    // Anything flung far away (some systems throw out a few strays while switching) starts again.
    if (dot(moved, moved) > 9.0)
    {
        particle.life = 0.0;
    }
}

float3 cameraEye()
{
    float angle = Time * 0.07;
    return float3(sin(angle) * 3.1, sin(Time * 0.043) * 1.3, cos(angle) * 3.1);
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float zoom = 2.2 + Bass * 0.1;
    float3 seen = project(particle.position, eye, 0.0, zoom, sin(Time * 0.05) * 0.3);

    // Each system has its own colours; slow stretches glow deep, fast ones flare brighter and paler.
    int index = attractorFor(id);
    float baseHue = index == 0 ? 0.04 : index == 1 ? 0.52 : index == 2 ? 0.42 : 0.76;
    float speed = length(particle.velocity) / 0.55;
    float hue = baseHue + Time * 0.01 + fixedRandom(id, 3.0) * 0.06 + saturate(speed * 0.5) * 0.22;
    float3 colour = hsv2rgb(hue, 0.9 - saturate(speed - 1.5) * 0.35, 1.0);
    float gain = index == 3 ? 0.4 : 1.0;  // the butterfly packs its light tighter
    colour *= 0.2 * gain * (1.0 + Beat * 0.6);

    // A few in every thousand catch the treble and sparkle.
    colour *= 1.0 + step(0.997, fixedRandom(id, 5.0)) * Treble * 30.0;

    Sprite sprite = (Sprite)0;
    sprite.position = seen.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye), 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    return particles(uv) + previous(uv) * 0.6;
}
