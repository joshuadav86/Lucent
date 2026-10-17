// Name: Orrery
// The whole solar system turning around a boiling sun: lit planets with banded clouds and Saturn's rings, an asteroid belt, a comet swinging past, and orbits that glow with their own slice of the music.
// Family: Particles
// Tags: Space, 3D, Colourful
// Particles: 262144

// How the particles are shared out between the parts of the system, as running totals.
static const float SunEnd = 0.2;
static const float CoronaEnd = 0.27;
static const float PlanetsEnd = 0.61;
static const float RingEnd = 0.67;
static const float OrbitsEnd = 0.76;
static const float AsteroidsEnd = 0.88;
static const float CometEnd = 0.95;
// The rest: far-off stars.

static const float SunRadius = 0.22;
// Mercury to Neptune, then the Moon: orbit size, planet size, and each one's share of the planet particles.
static const int Planets = 9;
static const float OrbitSize[9] = { 0.5, 0.68, 0.88, 1.08, 1.55, 2.0, 2.42, 2.78, 0.1 };
static const float PlanetSize[9] = { 0.026, 0.042, 0.045, 0.033, 0.115, 0.095, 0.062, 0.06, 0.013 };
static const float PlanetWeight[9] = { 0.03, 0.06, 0.08, 0.04, 0.3, 0.24, 0.1, 0.1, 0.05 };

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

// The kth of n points spread evenly over a sphere.
float3 spherePoint(uint k, uint n)
{
    float z = 1.0 - (k + 0.5) * 2.0 / max((float)n, 1.0);
    float angle = (k * 1640531527u) / 4294967296.0 * 2.0 * PI;
    float r = sqrt(saturate(1.0 - z * z));
    return float3(r * cos(angle), z, r * sin(angle));
}

// Where a planet on a circular orbit of this size is at this clock time (the orbits lie flat in x and z).
float3 orbitPosition(float radius, float clock, float start)
{
    float angle = clock * 0.9 / pow(radius, 1.5) + start;
    return float3(cos(angle), 0.0, sin(angle)) * radius;
}

// Where planet i is; the Moon (the last one) goes round the Earth.
float3 planetPosition(int index, float clock)
{
    if (index == 8)
    {
        return orbitPosition(OrbitSize[2], clock, 2.0 * 2.4) + float3(cos(clock * 3.0), 0.0, sin(clock * 3.0)) * OrbitSize[8];
    }
    return orbitPosition(OrbitSize[index], clock, index * 2.4);
}

// The comet's long orbit: swinging close past the sun, then far out again (Kepler's equation).
float3 cometPosition(float clock)
{
    const float a = 1.5;
    const float e = 0.8;
    float meanAngle = clock * 0.9 / pow(a, 1.5) + 2.5;
    float angle = meanAngle;
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        angle = meanAngle + e * sin(angle);
    }
    float2 flat = float2(a * (cos(angle) - e), a * sqrt(1.0 - e * e) * sin(angle));
    flat = rotate(flat, 0.8);
    return float3(flat.x, flat.y * 0.25, flat.y);
}

// The colour of each planet's surface at a point on it (in the planet's own turning frame).
float3 surfaceColour(int index, float3 n)
{
    float latitude = n.y;
    float swirl = noise3(n * 3.0 + index * 7.0);
    if (index == 0 || index == 8)
    {
        return float3(0.55, 0.52, 0.5) * (0.75 + 0.35 * noise3(n * 6.0 + 2.0));
    }
    if (index == 1)
    {
        return float3(1.0, 0.85, 0.55) * (0.85 + 0.15 * sin(latitude * 6.0 + swirl * 2.0));
    }
    if (index == 2)
    {
        // Earth: blue oceans, green and brown land, white clouds and ice caps.
        float land = noise3(n * 2.2 + 11.0) + noise3(n * 5.0) * 0.4;
        float3 colour = land > 0.15 ? lerp(float3(0.2, 0.5, 0.15), float3(0.6, 0.5, 0.3), saturate(land - 0.3)) : float3(0.08, 0.25, 0.85);
        float cloud = smoothstep(0.25, 0.6, noise3(n * 4.0 + float3(Time * 0.05, 0.0, 0.0)));
        colour = lerp(colour, float3(1.0, 1.0, 1.0), max(cloud * 0.8, smoothstep(0.82, 0.9, abs(latitude))));
        return colour;
    }
    if (index == 3)
    {
        float3 colour = lerp(float3(0.85, 0.35, 0.12), float3(0.5, 0.2, 0.1), smoothstep(0.0, 0.5, noise3(n * 4.0 + 5.0)));
        return lerp(colour, float3(1.0, 0.95, 0.9), smoothstep(0.88, 0.93, abs(latitude)));
    }
    if (index == 4)
    {
        // Jupiter: cream and rust bands, and the Great Red Spot.
        float bands = sin(latitude * 16.0 + swirl * 1.2);
        float3 colour = lerp(float3(0.95, 0.85, 0.7), float3(0.75, 0.45, 0.25), smoothstep(-0.3, 0.6, bands));
        float2 spot = float2(atan2(n.z, n.x) - 1.0, (latitude + 0.35) * 2.2);
        return lerp(colour, float3(0.85, 0.3, 0.15), smoothstep(0.35, 0.2, length(spot)));
    }
    if (index == 5)
    {
        float bands = sin(latitude * 12.0 + swirl * 0.6);
        return lerp(float3(0.95, 0.85, 0.6), float3(0.8, 0.65, 0.4), smoothstep(-0.2, 0.8, bands));
    }
    if (index == 6)
    {
        return float3(0.55, 0.9, 0.95) * (0.92 + 0.08 * sin(latitude * 8.0));
    }
    float3 colour = float3(0.2, 0.4, 1.0) * (0.85 + 0.15 * sin(latitude * 10.0 + swirl));
    return lerp(colour, float3(0.05, 0.1, 0.35), smoothstep(0.3, 0.15, length(float2(atan2(n.z, n.x) + 2.0, (latitude + 0.25) * 2.5))));
}

void Spawn(inout Particle particle, uint id)
{
    float share = shareOf(id);
    particle.life = 1e6;
    particle.velocity = 0.0;
    particle.position = 0.0;
    particle.data = float4(0.0, 0.0, 0.0, Time * 0.6);
    if (share >= SunEnd && share < CoronaEnd)
    {
        // The corona: gas streaming off the sun, faster on the beat.
        particle.data.x = 0.0;
        particle.life = lerp(1.5, 4.0, random(id, 1.0));
        if (ParticleReset > 0.5)
        {
            particle.data.x = random(id, 2.0) * 0.4;
        }
        float3 direction = randomDirection(id, 3.0);
        // Streamers: the gas leaves mostly from a few active regions.
        float active = noise3(direction * 2.5 + float3(0.0, Time * 0.05, 0.0));
        particle.velocity = direction * (0.04 + 0.1 * saturate(active + 0.3)) * (1.0 + Beat * 2.5 + Bass * 0.8);
        particle.position = direction;
    }
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    // A clock all the particles share, which runs a little faster when the music is loud.
    particle.data.w += dt * (0.45 + Level * 0.45);
    float share = shareOf(id);
    if (share >= SunEnd && share < CoronaEnd)
    {
        particle.data.x += length(particle.velocity) * dt;
    }
}

float3 cameraEye()
{
    float angle = Time * 0.025 + 0.6;
    float lift = 0.45 + 0.2 * sin(Time * 0.017);
    float distance = 3.9 + 0.4 * sin(Time * 0.011);
    return float3(sin(angle) * cos(lift), -sin(lift), cos(angle) * cos(lift)) * distance;
}

static const float Zoom = 1.75;

// Draws a point on a lit sphere: dots on the far side are hidden, and the night side is dim.
Sprite sphereDot(float3 centre, float radius, float3 normal, float3 colour, uint n, float3 eye, float3 light, float ambient)
{
    Sprite sprite = (Sprite)0;
    float3 position = centre + normal * radius;
    float3 seen = project(position, eye, 0.0, Zoom, 0.0);
    float facing = dot(normal, normalize(eye - position));
    if (facing < 0.0 || seen.z < 0.1)
    {
        return sprite;
    }
    float lit = light.x > 1e3 ? 1.0 : saturate(dot(normal, light));
    // Dots sized so the surface looks solid however many there are and however close it is.
    float pixels = radius * Zoom / seen.z / spritePixel();
    sprite.position = seen.xy;
    float wanted = pixels * sqrt(2.0 * PI / max((float)n, 1.0)) * 0.9;
    sprite.size = clamp(wanted, 0.8, 32.0);
    // Far away the dots can't shrink any further, so they dim instead, keeping the planet's brightness.
    float shrink = min(wanted / sprite.size, 1.0);
    sprite.colour = colour * (ambient + lit) * (0.5 + 0.5 * facing) * 0.55 * shrink * shrink;
    // The dots already share out the surface's light between them, however many the Detail
    // setting gives, so undo the engine's usual brightening or dimming for that.
    sprite.colour /= max(ParticleWeight, 1e-3);
    return sprite;
}

Sprite Show(Particle particle, uint id)
{
    float share = shareOf(id);
    float clock = particle.data.w;
    float3 eye = cameraEye();
    Sprite sprite = (Sprite)0;

    if (share < SunEnd)
    {
        // The sun: a boiling surface of bright cells, swelling with the bass.
        uint first = 0u;
        uint n = (uint)(SunEnd * ParticleCount);
        float3 normal = spherePoint(id - first, n);
        float3 turning = normal;
        turning.xz = rotate(turning.xz, Time * 0.05);
        float cells = noise3(turning * 9.0 + float3(0.0, Time * 0.25, 0.0)) * 0.5 + noise3(turning * 22.0 - Time * 0.4) * 0.3;
        float3 colour = lerp(float3(1.0, 0.45, 0.08), float3(1.0, 0.85, 0.5), saturate(cells + 0.5)) * (1.6 + Bass * 0.8 + Beat * 0.6);
        return sphereDot(0.0, SunRadius * (1.0 + Bass * 0.05), normal, colour, n, eye, float3(1e4, 0.0, 0.0), 0.0);
    }

    if (share < CoronaEnd)
    {
        float travelled = particle.data.x;
        float3 position = particle.position * (SunRadius + travelled);
        float3 seen = project(position, eye, 0.0, Zoom, 0.0);
        float fade = exp(-travelled * 6.0) * saturate(particle.age * 3.0) * saturate(particle.life);
        sprite.position = seen.xy;
        sprite.size = seen.z > 0.1 ? 1.4 : 0.0;
        sprite.colour = float3(1.0, 0.55, 0.2) * fade * 0.25;
        return sprite;
    }

    if (share < PlanetsEnd)
    {
        // Which planet this particle belongs to, and which of its dots.
        float local = (share - CoronaEnd) / (PlanetsEnd - CoronaEnd);
        float total = 0.0;
        [unroll]
        for (int i = 0; i < Planets; i++)
        {
            total += PlanetWeight[i];
        }
        int index = 0;
        float start = 0.0;
        float running = 0.0;
        [unroll]
        for (int j = 0; j < Planets; j++)
        {
            if (local * total >= running)
            {
                index = j;
                start = running;
            }
            running += PlanetWeight[j];
        }
        float planetShare = PlanetWeight[index] / total * (PlanetsEnd - CoronaEnd);
        uint first = (uint)ceil((CoronaEnd + start / total * (PlanetsEnd - CoronaEnd)) * ParticleCount - 0.5);
        uint n = (uint)(planetShare * ParticleCount);
        float3 normal = spherePoint(id - min(first, id), n);

        // The surface pattern turns with the planet; the light comes from the sun.
        float3 turning = normal;
        turning.xz = rotate(turning.xz, Time * (index >= 4 && index <= 7 ? 0.3 : 0.12) + index);
        float3 centre = planetPosition(index, clock);
        float3 light = normalize(-centre);
        float3 colour = surfaceColour(index, turning);
        return sphereDot(centre, PlanetSize[index] * (1.0 + Beat * 0.06), normal, colour, n, eye, light, 0.05);
    }

    if (share < RingEnd)
    {
        // Saturn's rings: bands of ice and dust with a dark gap, tilted towards the sun.
        float r = lerp(1.25, 2.3, fixedRandom(id, 4.0));
        if (r > 1.92 && r < 2.0)
        {
            r += 0.1;
        }
        float angle = fixedRandom(id, 5.0) * 2.0 * PI + clock * 0.5 / pow(r, 1.5);
        float3 spot = float3(cos(angle), 0.0, sin(angle)) * r * PlanetSize[5];
        spot.xy = rotate(spot.xy, 0.45);
        float3 position = planetPosition(5, clock) + spot;
        float3 seen = project(position, eye, 0.0, Zoom, 0.0);
        float bands = 0.6 + 0.4 * sin(r * 40.0) * sin(r * 13.0);
        sprite.position = seen.xy;
        sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
        sprite.colour = float3(0.95, 0.85, 0.65) * bands * 0.12;
        return sprite;
    }

    if (share < OrbitsEnd)
    {
        // The orbits: faint rings, each glowing with its own slice of the spectrum, bass inside.
        float local = (share - RingEnd) / (OrbitsEnd - RingEnd) * 8.0;
        int index = min((int)local, 7);
        float angle = frac(local) * 2.0 * PI;
        float3 position = float3(cos(angle), 0.0, sin(angle)) * OrbitSize[index];
        float3 seen = project(position, eye, 0.0, Zoom, 0.0);
        float level = spectrum(0.03 + index * 0.11);
        float3 colour = hsv2rgb(0.55 + index * 0.06, 0.6, 1.0);
        sprite.position = seen.xy;
        sprite.size = seen.z > 0.1 ? 1.0 : 0.0;
        sprite.colour = colour * (0.02 + level * level * 0.16) / (0.6 + OrbitSize[index] * 0.4);
        return sprite;
    }

    if (share < AsteroidsEnd)
    {
        // The asteroid belt between Mars and Jupiter.
        float r = lerp(1.22, 1.38, fixedRandom(id, 6.0)) + (fixedRandom(id, 7.0) - 0.5) * 0.06;
        float angle = fixedRandom(id, 8.0) * 2.0 * PI + clock * 0.9 / pow(r, 1.5);
        float3 position = float3(cos(angle) * r, (fixedRandom(id, 9.0) - 0.5) * 0.04, sin(angle) * r);
        float3 seen = project(position, eye, 0.0, Zoom, 0.0);
        sprite.position = seen.xy;
        sprite.size = seen.z > 0.1 ? 0.8 : 0.0;
        sprite.colour = float3(0.75, 0.68, 0.6) * (0.04 + pow(fixedRandom(id, 10.0), 8.0) * 0.3) * (1.0 + Treble * 0.6);
        return sprite;
    }

    if (share < CometEnd)
    {
        // The comet: a bright head with a blue ion tail pointing straight away from the sun and a
        // curving golden dust tail, both longer when it's close to the sun.
        float3 head = cometPosition(clock);
        float3 behind = cometPosition(clock - 0.03);
        float3 away = normalize(head);
        float distance = length(head);
        float tailLength = 0.06 + saturate(0.6 / distance - 0.15) * 0.9;
        float s = pow(fixedRandom(id, 11.0), 1.5);
        bool ion = fixedRandom(id, 12.0) < 0.45;
        float3 trailing = normalize(behind - head + 1e-5);
        float3 direction = ion ? away : normalize(away + trailing * s * 1.2);
        float3 scatter = (float3(fixedRandom(id, 13.0), fixedRandom(id, 14.0), fixedRandom(id, 15.0)) - 0.5) * (0.004 + s * (ion ? 0.03 : 0.08) * tailLength);
        // A few in every hundred make the fuzzy head.
        bool coma = fixedRandom(id, 21.0) < 0.03;
        float3 position = coma ? head + normalize(scatter + 1e-5) * 0.018 * fixedRandom(id, 22.0) : head + direction * s * tailLength + scatter;
        float3 seen = project(position, eye, 0.0, Zoom, 0.0);
        float3 colour = ion ? float3(0.4, 0.7, 1.0) : float3(1.0, 0.85, 0.6);
        // The tail's light is spread along it, so a short tail is dimmer.
        float brightness = (coma ? 0.1 : 0.05 * (1.0 - s) * tailLength) * saturate(1.2 / distance) * (1.0 + Beat * 0.5);
        colour = coma ? float3(0.9, 0.95, 1.0) : colour;
        sprite.position = seen.xy;
        sprite.size = seen.z > 0.1 ? 1.1 : 0.0;
        sprite.colour = colour * brightness;
        return sprite;
    }

    float3 direction = normalize(float3(fixedRandom(id, 16.0), fixedRandom(id, 17.0), fixedRandom(id, 18.0)) - 0.5);
    float3 seen = project(direction * 30.0, eye, 0.0, Zoom, 0.0);
    sprite.position = seen.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = hsv2rgb(0.55 + fixedRandom(id, 19.0) * 0.5, 0.2, 1.0) * pow(fixedRandom(id, 20.0), 6.0) * 1.0;
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The sun's glow, spilling out into space and swelling with the bass.
    float3 sun = project(0.0, cameraEye(), 0.0, Zoom, 0.0);
    float2 fromSun = p - sun.xy;
    float glowAmount = exp(-length(fromSun) * 7.0) * (0.25 + Bass * 0.25 + Beat * 0.15) + exp(-length(fromSun) * 2.0) * 0.03;
    return particles(uv) + previous(uv) * 0.35 + float3(1.0, 0.55, 0.2) * glowAmount;
}
