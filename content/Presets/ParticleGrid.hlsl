// Name: Particle Grid
// A calm, even grid of points resting on the dark; the waveform rolls through it like wind over a field, beats burst out as rings and the bass stirs slow eddies, and every point draws a fine comet tail as it's pushed before springing gently back to rest.
// Family: Particles
// Tags: Minimal
// Added: 2026-10-17 22:17
// Particles: 131072

// Each point of the grid is a little bundle of particles held to its resting place by springs of
// different stiffness. At rest they all sit on top of one another as one crisp dot. When the music
// pushes them, the stiff ones snap back first and the soft ones trail behind, so each point leaves
// a short tail that shortens to nothing as it settles.
static const float Spacing = 0.06;
static const float MaxLayers = 32.0;

float columns()
{
    return floor(2.0 * Aspect / Spacing) - 1.0;
}

float rows()
{
    return floor(2.0 / Spacing) - 1.0;
}

uint dotCount()
{
    return (uint)(columns() * rows());
}

float layerCount()
{
    return min(floor(ParticleCount / dotCount()), MaxLayers);
}

float layerOf(uint id)
{
    return (float)(id / dotCount());
}

float2 homeOf(uint id)
{
    uint index = id % dotCount();
    float cols = columns();
    float2 cell = float2(index % (uint)cols, index / (uint)cols);
    return (cell - float2(cols - 1.0, rows() - 1.0) * 0.5) * Spacing;
}

// Where the latest beat's ring started: a new spot every beat.
float2 beatCentre()
{
    float n = BeatCount;
    return float2((hash(float2(n, 1.7)) - 0.5) * Aspect * 1.4, (hash(float2(n, 9.3)) - 0.5) * 1.4);
}

// Two slow eddies that wander the screen; the bass sets how hard they stir.
float2 eddyCentre(int i)
{
    float t = Time * 0.07 + i * 3.1;
    return float2(sin(t * 1.3) * Aspect * 0.6, cos(t) * 0.6);
}

float2 musicForce(float2 at)
{
    float2 force = 0.0;

    // Each beat: a ring spreading out from a new spot, pushing the points outward as it passes.
    float2 fromBeat = at - beatCentre();
    float d = length(fromBeat);
    // The first beat of every bar rings out widest; the others are smaller.
    float downbeat = wrap(BeatCount, 4.0) < 0.5 ? 1.0 : 0.45;
    float ringRadius = (1.0 - Beat) * 0.7 * downbeat + 0.03;
    force += fromBeat / max(d, 1e-3) * Beat * 12.0 * downbeat * exp(-(d - ringRadius) * (d - ringRadius) * 90.0);

    // The waveform as a gust rolling up and down the screen along a slowly drifting line.
    float lineHeight = sin(Time * 0.23) * 0.6;
    float across = toUv(at).x;
    force.y += waveform(across) * 14.0 * (0.3 + Level) * exp(-(at.y - lineHeight) * (at.y - lineHeight) * 40.0);

    // The bass stirs the eddies.
    [unroll]
    for (int i = 0; i < 2; i++)
    {
        float2 offset = at - eddyCentre(i);
        float swirl = smoothstep(0.55, 0.95, Bass) * 14.0 * exp(-dot(offset, offset) * 14.0) * (i == 0 ? 1.0 : -1.0);
        force += float2(-offset.y, offset.x) * swirl;
    }
    return force;
}

void Spawn(inout Particle particle, uint id)
{
    float2 home = homeOf(id);
    particle.position = float3(home, 0.0);
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float2 home = homeOf(id);
    float layer = layerOf(id) / max(layerCount() - 1.0, 1.0);
    // Stiff, light particles at the front of each bundle; soft, heavy ones trailing behind.
    float stiffness = lerp(38.0, 9.0, layer);
    float mass = lerp(1.0, 1.6, layer);
    // Nearly critically damped, so the points settle back quickly without wobbling.
    float damping = 1.6 * sqrt(stiffness * mass);

    float2 position = particle.position.xy;
    float2 velocity = particle.velocity.xy;
    float2 acceleration = (musicForce(position) - stiffness * (position - home) - damping * velocity) / mass;
    velocity += acceleration * dt;
    // Keep things sane however wild the music gets.
    velocity = clamp(velocity, -4.0, 4.0);
    position += velocity * dt;
    position = clamp(position - home, -0.5, 0.5) + home;
    particle.position = float3(position, 0.0);
    particle.velocity = float3(velocity, 0.0);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float layers = layerCount();
    float layer = layerOf(id);
    float2 home = homeOf(id);
    float2 offset = particle.position.xy - home;
    float moved = length(offset);

    // An off-white dot at rest, warming to a soft coral the further it's pushed.
    float3 rest = float3(0.82, 0.86, 0.95);
    float3 pushed = float3(1.0, 0.5, 0.38);
    float3 colour = lerp(rest, pushed, smoothstep(0.0, 0.04, moved));
    // The dots at the edges of the grid fade gently, framing it.
    float2 edge = 1.0 - abs(home) / float2(Aspect, 1.0);
    float frame = smoothstep(0.0, 0.12, min(edge.x, edge.y));
    colour *= frame * (0.95 + Level * 0.4) / max(layers, 1.0) * 1.5;

    sprite.position = particle.position.xy;
    sprite.streak = particle.velocity.xy * 0.02;
    sprite.size = layer < layers ? 2.4 : 0.0;
    sprite.colour = colour;
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    return particles(uv) + previous(uv) * 0.35;
}
