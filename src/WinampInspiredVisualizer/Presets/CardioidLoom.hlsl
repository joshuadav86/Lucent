// Name: Cardioid Loom
// Two hundred threads of light strung across a ring, each joining a point to the point it lands on when its number is multiplied, weave the times-table figures: a cardioid, then a nephroid, then ever more petals as the multiplier climbs. The threads bow up and down into a glowing 3D lens, swell with their own band of the spectrum and carry pulses of light across on every beat.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-10-25 20:30

// The times table on a circle: point i of Points joins point i x m, taken round the ring. With m = 2
// the threads trace a cardioid, with 3 a nephroid, and every whole number after adds another cusp.
// Each thread is lifted into 3D as an arc above the ring and mirrored below it, the arc's height
// following the thread's length, so long threads make the lens and short ones hug the rim.
static const float Points = 200.0;
static const float Radius = 1.0;
static const float RimShare = 0.06;   // of the particles, how many trace the ring itself

// The multiplier: climbs from 2 to 11 and back over a few minutes, resting a while on each whole
// number so its figure can be seen, and nudged by the mids so the weave shivers with the music.
float multiplier()
{
    float sweep = 9.0 * (0.5 - 0.5 * cos(Time * 0.012));
    float whole = floor(sweep);
    float part = frac(sweep);
    part = part * part * (3.0 - 2.0 * part);
    part = part * part * (3.0 - 2.0 * part);
    return 2.0 + whole + part + Mid * 0.03;
}

float3 ringPoint(float angle)
{
    return float3(cos(angle), 0.0, sin(angle)) * Radius;
}

// How far a thread sits from the ring's line of symmetry, 0 at point 0 and 1 halfway round, so the
// colours and bands mirror across that line.
float threadMirror(float thread)
{
    return abs(thread / Points * 2.0 - 1.0);
}

// A particle's data: its thread, how far along it is (0 to 1), which side of the ring it bows to
// (-1 or 1, 0 on the ring itself) and its speed.
float3 threadPoint(float thread, float along, float side)
{
    float m = multiplier();
    float from = 2.0 * PI * thread / Points;
    float3 a = ringPoint(from);
    if (side == 0.0)
    {
        // The ring: the particle runs round the whole circle instead.
        return ringPoint(from + along * 2.0 * PI / Points);
    }
    float3 b = ringPoint(from * m);
    float span = length(b - a);
    float level = spectrum(0.03 + threadMirror(thread) * 0.6);
    float lift = side * sin(PI * along) * span * (0.1 + level * 0.28 + Bass * 0.08 + DropPulse * 0.08);
    return lerp(a, b, along) + float3(0.0, lift, 0.0);
}

void Spawn(inout Particle particle, uint id)
{
    float thread = min(floor(fixedRandom(id, 1.0) * Points), Points - 1.0);
    float along = fixedRandom(id, 2.0);
    float side = fixedRandom(id, 3.0) < RimShare ? 0.0 : (fixedRandom(id, 4.0) < 0.5 ? -1.0 : 1.0);
    particle.data = float4(thread, along, side, 0.8 + fixedRandom(id, 5.0) * 0.4);
    particle.velocity = 0.0;
    particle.position = threadPoint(thread, along, side);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float speed = (0.06 + Bass * 0.35 + Beat * 0.1 + DropPulse * 0.2) * particle.data.w;
    float along = frac(particle.data.y + speed * dt);
    float3 before = particle.position;
    particle.data.y = along;
    particle.position = threadPoint(particle.data.x, along, particle.data.z);
    // Remember the movement for the streak, but not across the jump back to the start.
    float3 moved = particle.position - before;
    particle.velocity = length(moved) > 0.15 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 cameraEye()
{
    float angle = Time * 0.04 + 0.6;
    return float3(sin(angle) * 1.9, 2.5 + sin(Time * 0.031) * 0.35, cos(angle) * 1.9);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = float3(0.0, 0.0, 0.0);
    float zoom = 2.5 * (1.0 + DropPulse * 0.05);
    float3 seen = project(particle.position, eye, target, zoom, sin(Time * 0.05) * 0.05);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, target, zoom, sin(Time * 0.05) * 0.05);

    float thread = particle.data.x;
    float along = particle.data.y;
    float side = particle.data.z;
    float mirror = threadMirror(thread);
    float level = spectrum(0.03 + mirror * 0.6);

    // Neon round the ring, teal through blue and violet to magenta, mirrored across it;
    // the threads below the ring run a little warmer than those above.
    float hue = frac(0.48 + mirror * 0.46 + side * 0.03 + sin(Time * 0.02) * 0.06);
    float3 colour = hsv2rgb(hue, side == 0.0 ? 0.6 : 0.8, 1.0);

    // A pulse of light crosses every thread on each beat. With a beat to follow it rides through
    // the whole beat; without one it shows just as it lands.
    float pulseAt = (along - BeatPhase) * 10.0;
    float pulse = exp(-pulseAt * pulseAt) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);
    float glow = side == 0.0 ? 0.035 + Bass * 0.04 : 0.055 * (0.45 + level * 1.4) + pulse * 0.09;
    colour *= glow * (0.8 + Intensity * 0.4);
    colour *= 1.0 + step(0.997, fixedRandom(id, 6.0)) * Treble * 4.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    // Short streaks only: the weave reshaping itself would otherwise smear it.
    float2 streak = seen.xy - seenBefore.xy;
    sprite.streak = streak * min(1.0, 0.015 / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    // Focus on the middle of the loom so its far side melts into soft light.
    focus(sprite, seen.z, length(eye - target), 0.08);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 2.2) * (0.01 + Bass * 0.012);
    return particles(uv) + previous(uv) * 0.5 + float3(0.3, 0.35, 0.9) * haze;
}
