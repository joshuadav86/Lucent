// Name: Cosmic Web
// Drift slowly through the largest structure in the universe: long glowing filaments of galaxies strung between bright clusters, a vast web stretching into the dark, its clusters flaring with the bass.
// Family: Particles
// Tags: Space, Calm, 3D
// Particles: 327680

// Every particle is a galaxy. Space is cut into a grid of cells that repeats every few cells, and
// each cell holds one cluster somewhere inside it. Neighbouring clusters are joined by thin
// filaments of galaxies, but not all of them, which leaves great empty voids between. The grid
// wraps round the camera, so the flight never ends.
static const float Cells = 6.0;
static const float Zoom = 1.5;
static const float ClusterShare = 0.16;
static const float FilamentShare = 0.9;

float cellRandom(float3 cell, float salt)
{
    uint3 c = (uint3)wrap(cell, Cells);
    return pcgHash(pcgHash(pcgHash(c.x * 73u + asuint(salt)) ^ (c.y * 1031u)) ^ (c.z * 7919u)) / 4294967295.0;
}

// The cluster in a cell, and how big it is (most are small; a few are huge).
float3 clusterIn(float3 cell)
{
    float3 jitter = float3(cellRandom(cell, 1.0), cellRandom(cell, 2.0), cellRandom(cell, 3.0)) - 0.5;
    return cell + 0.5 + jitter * 0.75;
}

float clusterMass(float3 cell)
{
    return pow(cellRandom(cell, 4.0), 2.5);
}

// The six ways a filament can leave a cell for one of its neighbours.
float3 neighbourOffset(int k)
{
    return k == 0 ? float3(1, 0, 0) : (k == 1 ? float3(0, 1, 0) : (k == 2 ? float3(0, 0, 1)
         : (k == 3 ? float3(1, 1, 0) : (k == 4 ? float3(0, 1, 1) : float3(1, 0, -1)))));
}

float3 cameraAt(float t)
{
    return float3(sin(t * 0.021) * 2.0, sin(t * 0.017) * 1.2, t * 0.18);
}

float3 cameraLook(float t)
{
    return cameraAt(t + 6.0) + float3(sin(t * 0.03) * 1.5, cos(t * 0.025) * 0.8, 0.0);
}

// Galaxies live in a box of space that travels with the camera, a little ahead of it.
float3 nearCamera(float3 position, float3 eye, float3 ahead)
{
    float3 middle = eye + ahead * Cells * 0.32;
    return middle + wrap(position - middle + Cells * 0.5, Cells) - Cells * 0.5;
}

void Spawn(inout Particle particle, uint id)
{
    float role = fixedRandom(id, 0.0);
    float3 cell = floor(float3(fixedRandom(id, 1.0), fixedRandom(id, 2.0), fixedRandom(id, 3.0)) * Cells);
    float3 position;
    float kind;
    float mass = clusterMass(cell);

    if (role < ClusterShare)
    {
        // A cluster: a dense ball of galaxies, packed tightest at the centre.
        float radius = 0.05 + mass * 0.22;
        position = clusterIn(cell) + randomDirection(id, 4.0) * radius * pow(fixedRandom(id, 5.0), 1.8) * (fixedRandom(id, 14.0) > 0.75 ? 1.8 : 1.0);
        kind = 0.0;
    }
    else
    {
        int k = (int)(fixedRandom(id, 6.0) * 6.0);
        float3 other = cell + neighbourOffset(k);
        bool exists = cellRandom(cell, 10.0 + k) < 0.42;
        if (role < FilamentShare && exists)
        {
            // A filament: thickest and richest where it meets its clusters, gently bowed.
            float u = fixedRandom(id, 7.0) * 2.0 - 1.0;
            float t = 0.5 + 0.5 * sign(u) * pow(abs(u), 0.7);
            float3 a = clusterIn(cell);
            float3 b = clusterIn(other);
            float3 bow = (float3(cellRandom(cell, 20.0 + k), cellRandom(cell, 30.0 + k), cellRandom(cell, 40.0 + k)) - 0.5) * 0.5;
            float thickness = 0.025 + 0.05 * (1.0 - sin(t * PI)) + mass * 0.04;
            position = lerp(a, b, t) + bow * sin(t * PI) + randomDirection(id, 8.0) * thickness * fixedRandom(id, 9.0);
            kind = 1.0;
            mass = lerp(clusterMass(cell), clusterMass(other), t);
        }
        else
        {
            // A lonely field galaxy out in the voids.
            position = (float3(fixedRandom(id, 11.0), fixedRandom(id, 12.0), fixedRandom(id, 13.0))) * Cells;
            kind = 2.0;
        }
    }
    particle.position = position;
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = float4(kind, mass, cellRandom(cell, 50.0), 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    // The web is still; it's the camera that moves.
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraAt(Time);
    float3 look = cameraLook(Time);
    float3 ahead = normalize(look - eye);
    float roll = sin(Time * 0.013) * 0.4;
    float3 position = nearCamera(particle.position, eye, ahead);
    float3 seen = project(position, eye, look, Zoom, roll);

    float kind = particle.data.x;
    float mass = particle.data.y;
    float band = particle.data.z;
    float distance = length(position - eye);
    // Far galaxies fade into the dark, and those right by the camera fade out before they pass.
    float fog = smoothstep(Cells * 0.62, Cells * 0.25, length(position - (eye + ahead * Cells * 0.32))) * smoothstep(0.15, 0.6, distance);
    float personal = fixedRandom(id, 14.0);
    float3 colour;
    float brightness;
    float size = 0.9;

    if (kind < 0.5)
    {
        // Clusters burn gold at their hearts, flaring with the bass, each to its own part of the spectrum.
        float flare = 1.0 + spectrum(0.02 + band * 0.25) * 1.5 * (0.4 + mass) + Bass * mass * 2.0 + Beat * mass * 1.2;
        colour = lerp(float3(1.0, 0.75, 0.45), float3(1.0, 0.45, 0.6), personal * 0.6);
        brightness = (0.25 + mass * 0.6) * flare;
        // A quarter of them are the hot gas the cluster sits in: big, soft and dim.
        if (personal > 0.75)
        {
            colour = float3(1.0, 0.5, 0.4);
            brightness *= 0.3;
            size = 4.0;
        }
    }
    else if (kind < 1.5)
    {
        // Filament galaxies are bluer, with a haze of violet gas along the strands.
        bool gas = personal < 0.18;
        colour = gas ? float3(0.45, 0.25, 1.0) : lerp(float3(0.55, 0.7, 1.0), float3(1.0, 0.9, 0.8), personal);
        brightness = gas ? 0.1 : 0.3 + mass * 0.4;
        brightness *= 1.0 + Mid * 0.4;
        size = gas ? 3.5 : 0.9;
    }
    else
    {
        colour = float3(0.7, 0.75, 1.0);
        brightness = 0.06;
    }
    // A rare bright galaxy sparkles with the treble.
    brightness += step(0.997, fixedRandom(id, 15.0)) * (0.4 + Treble * 0.8);

    Sprite sprite = (Sprite)0;
    sprite.position = float2(seen.x, -seen.y);
    sprite.size = seen.z > 0.1 && fog > 0.0 ? size : 0.0;
    sprite.colour = colour * brightness * fog;
    focus(sprite, seen.z, 3.0, 0.035);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The faint glow of the deep background, a little warmer in one corner of the sky.
    float3 eye = cameraAt(Time);
    float3 look = cameraLook(Time);
    float3 direction = cameraRay(eye, look, float2(p.x, -p.y), Zoom, sin(Time * 0.013) * 0.4);
    float haze = noise3(direction * 2.5 + Time * 0.005) * 0.5 + 0.5;
    float3 colour = lerp(float3(0.004, 0.003, 0.012), float3(0.018, 0.008, 0.03), haze * haze);
    return colour + particles(uv) + previous(uv) * 0.3;
}
