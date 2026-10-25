// Name: Grand Spiral
// Drift slowly around a single vast spiral galaxy: blue star-forming arms wind around a golden core that flares with the bass, while each beat sends a ripple of light out through the arms.
// Family: Particles
// Tags: Space, Calm, 3D
// Added: 2026-10-17 01:51
// Particles: 262144

// How the particles are shared out: the golden bulge, the disc, glowing pink star-forming knots,
// and a scattering of far-off stars behind.
static const float BulgeShare = 0.12;
static const float DiscShare = 0.8;
static const float KnotShare = 0.05;
// How tightly the arms wind: each orbit is an ellipse turned a little more than the one inside it,
// and where neighbouring orbits bunch up the stars crowd into spiral arms.
static const float Twist = 3.6;
static const float DiscRadius = 1.6;

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

// How long ago the last beat was (Beat fades from 1 as exp(-5 t)), for the ripple through the arms.
float sinceBeat()
{
    return -log(max(Beat, 1e-3)) / 5.0;
}

// A star on an elliptical orbit of size a, at angle mean along it, in the plane of the disc (x, z).
float2 orbitPoint(float a, float mean)
{
    float eccentricity = 0.42 * smoothstep(0.05, 0.4, a) * (1.0 - 0.4 * smoothstep(0.9, DiscRadius, a));
    float2 local = float2(cos(mean) * a, sin(mean) * a * (1.0 - eccentricity));
    return rotate(local, a * Twist);
}

// The orbits: stars further out take longer to go round (a flat rotation curve).
float orbitAngle(float a, float clock, float start)
{
    return start + clock * 0.5 / max(a, 0.12);
}

void Spawn(inout Particle particle, uint id)
{
    particle.life = 1e6;
    particle.velocity = 0.0;
    particle.position = 0.0;
    particle.data = float4(0.0, 0.0, 0.0, Time);
}

void Simulate(inout Particle particle, uint id)
{
    // A clock all the stars share, which runs a little faster when the music is loud.
    particle.data.w += min(DeltaTime, 0.05) * (0.6 + Level * 0.5);
}

// Where a star sits in the galaxy, and its colour and brightness.
float3 starPlace(uint id, float share, float clock, out float3 colour)
{
    if (share < BulgeShare)
    {
        // The bulge: an old, golden swarm, slightly squashed, turning slowly.
        float3 q = float3(fixedRandom(id, 3.0), fixedRandom(id, 4.0), fixedRandom(id, 5.0)) * 2.0 - 1.0;
        float radius = pow(fixedRandom(id, 6.0), 2.4) * 0.45;
        float3 p = normalize(q + 1e-4) * radius;
        p.y *= 0.6;
        p.xz = rotate(p.xz, clock * 0.8);
        colour = lerp(float3(1.0, 0.62, 0.3), float3(1.0, 0.88, 0.65), fixedRandom(id, 7.0)) * (0.45 + 0.3 * exp(-radius * 12.0));
        return p;
    }

    if (share < BulgeShare + DiscShare + KnotShare)
    {
        bool knot = share >= BulgeShare + DiscShare;
        // Knots: a few dozen stars share one orbit, so they cluster into glowing beads along the arms.
        uint owner = knot ? id / 48u : id;
        float a = lerp(0.08, DiscRadius, pow(fixedRandom(owner, 8.0), 0.85));
        if (knot)
        {
            a = lerp(0.45, DiscRadius * 0.9, fixedRandom(owner, 8.0));
        }
        float mean = orbitAngle(a, clock, fixedRandom(owner, 9.0) * 2.0 * PI);
        float2 flat = orbitPoint(a, mean);
        float spread = knot ? 0.018 : 0.035 * (0.4 + a);
        flat += (float2(fixedRandom(id, 10.0), fixedRandom(id, 11.0)) - 0.5) * spread;
        float height = (fixedRandom(id, 12.0) - 0.5) * 0.05 * exp(-a * 0.8) * (knot ? 0.3 : 1.0);

        // Stars in the bunched-up arms are young and blue; between the arms they're older and paler.
        float arm = saturate(cos(2.0 * (mean + 0.15)) * 0.5 + 0.5);
        arm = pow(arm, 1.6);
        float3 young = float3(0.45, 0.65, 1.0);
        float3 old = float3(1.0, 0.85, 0.65);
        colour = lerp(old, young, saturate(arm * 1.4 * smoothstep(0.25, 0.6, a))) * (0.22 + arm * 0.9);
        // Clumpy star clouds, and a soft fade at the disc's edge.
        colour *= (0.55 + 0.9 * saturate(noise3(float3(flat * 7.0, a * 3.0)) + 0.4)) * (1.0 - 0.7 * smoothstep(1.1, DiscRadius, a));
        // Dust lanes along the arms' inner edges dim the starlight.
        colour *= 1.0 - 0.75 * smoothstep(0.75, 0.95, cos(2.0 * (mean - 0.45))) * smoothstep(0.3, 0.6, a);
        if (knot)
        {
            // They only shine while passing through an arm, where new stars are being born.
            colour = float3(1.0, 0.3, 0.55) * 3.0 * (0.6 + fixedRandom(owner, 13.0)) * pow(arm, 3.0);
        }
        return float3(flat.x, height, flat.y);
    }

    colour = hsv2rgb(0.55 + fixedRandom(id, 14.0) * 0.5, 0.2, 1.0) * pow(fixedRandom(id, 15.0), 5.0) * 0.8;
    return normalize(float3(fixedRandom(id, 17.0), fixedRandom(id, 18.0), fixedRandom(id, 19.0)) - 0.5) * 14.0;
}

float3 cameraEye()
{
    float angle = Time * 0.03;
    float lift = 0.75 + 0.5 * sin(Time * 0.019);
    float distance = 2.7 + 0.35 * sin(Time * 0.013);
    return float3(sin(angle) * cos(lift), -sin(lift), cos(angle) * cos(lift)) * distance;
}

float3 cameraTarget()
{
    return float3(sin(Time * 0.023) * 0.25, 0.0, cos(Time * 0.017) * 0.2);
}

Sprite Show(Particle particle, uint id)
{
    float share = shareOf(id);
    float clock = particle.data.w;
    float3 colour;
    float3 position = starPlace(id, share, clock, colour);

    float3 eye = cameraEye();
    float3 seen = project(position, eye, cameraTarget(), 1.9, 0.0);

    // Every beat a ripple of light runs outward through the disc.
    float r = length(position.xz);
    float ripple = exp(-pow((r - sinceBeat() * 1.8) / 0.12, 2.0)) * Beat * 1.5;
    // The core flares with the bass; blue stars sparkle with the treble.
    float core = exp(-length(position) * 8.0) * Bass * 1.5;
    bool blue = colour.b > colour.r;
    float sparkle = blue ? 1.0 + step(0.993, fixedRandom(id, 20.0)) * Treble * 12.0 : 1.0;

    Sprite sprite = (Sprite)0;
    sprite.position = seen.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour * 0.3 * (1.0 + ripple + core) * sparkle;
    if (share >= BulgeShare + DiscShare + KnotShare)
    {
        sprite.size = 0.9;
    }
    focus(sprite, seen.z, length(eye - cameraTarget()), 0.035);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A soft golden glow around the core, swelling with the bass and flashing on the beat.
    float3 eye = cameraEye();
    float3 core = project(0.0, eye, cameraTarget(), 1.9, 0.0);
    float2 fromCore = p - core.xy;
    float glowAmount = exp(-dot(fromCore, fromCore) * 22.0) * (0.06 + Bass * 0.1 + Beat * 0.08);
    glowAmount += exp(-dot(fromCore, fromCore) * 3.0) * 0.025;
    return particles(uv) + previous(uv) * 0.45 + float3(1.0, 0.72, 0.42) * glowAmount;
}
