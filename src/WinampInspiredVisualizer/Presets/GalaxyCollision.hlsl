// Name: Galaxy Collision
// Two spiral galaxies swing past each other again and again, tearing out glowing tidal tails and bridges of stars that the music sets shimmering.
// Particles: 262144

// Each galaxy's core weighs the same; this is its pull.
static const float Pull = 0.11;
// Their orbit around each other: its size, how stretched it is, and how long one lap takes.
static const float OrbitSize = 1.25;
static const float Stretch = 0.72;

float orbitPeriod()
{
    return 2.0 * PI * sqrt(OrbitSize * OrbitSize * OrbitSize / (2.0 * Pull));
}

// Where galaxy 0's core is at time t (galaxy 1's is opposite), from Kepler's equation.
float3 corePosition(float t)
{
    float meanAngle = 2.0 * PI * t / orbitPeriod() + 2.2;
    float angle = meanAngle;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        angle = meanAngle + Stretch * sin(angle);
    }
    float2 relative = float2(OrbitSize * (cos(angle) - Stretch), OrbitSize * sqrt(1.0 - Stretch * Stretch) * sin(angle));
    return float3(relative.x, 0.0, relative.y) * 0.5;
}

float3 gravity(float3 position, float t)
{
    float3 core = corePosition(t);
    float3 toA = core - position;
    float3 toB = -core - position;
    const float soften = 0.0025;
    float a = Pull / pow(dot(toA, toA) + soften, 1.5);
    float b = Pull / pow(dot(toB, toB) + soften, 1.5);
    return toA * a + toB * b;
}

// The two discs are tilted differently, which is what makes the tails so twisted.
float3 discAxis(uint galaxy)
{
    return galaxy == 0u ? normalize(float3(0.25, 1.0, 0.15)) : normalize(float3(-0.5, 1.0, 0.35));
}

void Spawn(inout Particle particle, uint id)
{
    uint galaxy = id & 1u;
    float sign = galaxy == 0u ? 1.0 : -1.0;
    float3 core = corePosition(Time) * sign;
    float3 coreVelocity = (corePosition(Time + 0.01) - corePosition(Time - 0.01)) / 0.02 * sign;

    // A new star joins its galaxy's disc on a circular orbit, denser towards the middle.
    float3 axis = discAxis(galaxy);
    float3 side = normalize(cross(axis, float3(0.0, 0.0, 1.0)));
    float3 front = cross(axis, side);
    float radius = 0.05 + pow(random(id, 1.0), 1.4) * 0.65;
    float angle = random(id, 2.0) * 2.0 * PI;
    // Two loose spiral arms: stars bunch up along them.
    angle += step(0.4, random(id, 3.0)) * (radius * 7.0 - angle + floor(random(id, 4.0) * 2.0) * PI + (random(id, 5.0) - 0.5) * 0.9);
    float3 offset = (side * cos(angle) + front * sin(angle)) * radius + axis * (random(id, 6.0) - 0.5) * 0.02;

    float speed = sqrt(Pull * radius * radius / pow(radius * radius + 0.0025, 1.5));
    particle.position = core + offset;
    particle.velocity = coreVelocity + cross(axis, normalize(offset)) * speed;
    particle.life = lerp(20.0, 45.0, random(id, 7.0));
    if (ParticleReset > 0.5)
    {
        particle.life *= random(id, 8.0);
    }
    particle.data = float4(galaxy, radius, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    // Leapfrog steps keep orbits steady, even close to the cores.
    float dt = min(DeltaTime, 0.05) / 4.0;
    float t = Time - min(DeltaTime, 0.05);
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        particle.velocity += gravity(particle.position, t) * dt * 0.5;
        particle.position += particle.velocity * dt;
        t += dt;
        particle.velocity += gravity(particle.position, t) * dt * 0.5;
    }

    if (dot(particle.position, particle.position) > 25.0)
    {
        particle.life = 0.0;
    }
}

Sprite Show(Particle particle, uint id)
{
    float angle = Time * 0.04;
    float3 eye = float3(sin(angle) * 2.6, -1.6 + sin(Time * 0.03) * 0.5, cos(angle) * 2.6);
    float3 seen = project(particle.position, eye, 0.0, 2.0 + Bass * 0.05, 0.0);

    // One galaxy burns blue and violet, the other rose and gold; fast-moving stars run hot and white.
    bool first = particle.data.x < 0.5;
    float hue = (first ? 0.58 : 0.92) + particle.data.y * 0.15 + sin(Time * 0.02) * 0.05;
    float speed = length(particle.velocity);
    float3 colour = hsv2rgb(hue, saturate(0.85 - speed * 0.25), 1.0);
    float core = exp(-particle.data.y * 7.0);
    float fadeIn = saturate(particle.age * 0.8) * saturate(particle.life * 0.5);

    // A few bright young stars, sparkling with the treble.
    float bright = step(0.996, fixedRandom(id, 9.0)) * (3.0 + Treble * 8.0);

    Sprite sprite = (Sprite)0;
    sprite.position = seen.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour * (0.22 + core * 0.5 + bright) * fadeIn * (1.0 + Beat * 0.35);
    focus(sprite, seen.z, length(eye), 0.04);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    return particles(uv) + previous(uv) * 0.5;
}
