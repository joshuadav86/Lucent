// Name: Enneper Bloom
// Enneper's minimal surface, the saddle a soap film makes, drawn as a flower of neon contour lines: rose-to-gold rings
// crossed by electric cyan spokes, with light flowing outward along every one. It slowly changes order, folding from two
// petals to three, four, and five and back. Each ring rides its own band of the spectrum, the bass deepens the saddle,
// every beat sends a ripple outward from the centre, and a drop throws the petals open.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-10-31 19:30
// Particles: 262144

// Enneper's surface of order k, over the disc w = r e^(i theta):
//     x = r cos(theta) - r^(2k+1) / (2k+1) cos((2k+1) theta)
//     y = -r sin(theta) - r^(2k+1) / (2k+1) sin((2k+1) theta)
//     z = 2 r^(k+1) / (k+1) cos((k+1) theta)
// Order k has k+1 lobes rising and k+1 falling. Within r = 1 it never passes through itself. Rings are lines
// of constant r (closed in theta) and spokes lines of constant theta (r from 0 to the rim).
static const float Rings = 16.0;
static const float Spokes = 36.0;
static const float RingShare = 0.62;    // the share of particles on rings
static const float Rim = 1.0;

// The order: the slow clock steps it 1, 2, 3, 4 and round again, blending into the next over the last
// part of each step. (The beat clock BeatCount - Beat only moves forward, so the music can push it.)
float2 ebOrder()
{
    float clock = Time * 0.022 + (BeatCount - Beat) * 0.008;
    float stage = floor(clock);
    float blend = smoothstep(0.8, 1.0, clock - stage);
    return float2(1.0 + wrap(stage, 4.0), blend);
}

float ebRing(float outward)
{
    float at = (outward - BeatPhase) * 6.0;
    return exp(-at * at) * (SongMapped > 0.0 ? 0.4 + Beat * 0.6 : Beat);
}

float3 ebSurface(float r, float theta, float k)
{
    float n = 2.0 * k + 1.0;
    float rn = pow(r, n) / n;
    float x = r * cos(theta) - rn * cos(n * theta);
    float y = -r * sin(theta) - rn * sin(n * theta);
    float z = 2.0 * pow(r, k + 1.0) / (k + 1.0) * cos((k + 1.0) * theta);
    // Higher orders reach a little further outward at the rim; scale them back to the same size.
    return float3(x, z, y) / (1.0 + 1.0 / n);
}

float3 ebPoint(float r, float theta)
{
    float2 order = ebOrder();
    float3 position = lerp(ebSurface(r, theta, order.x), ebSurface(r, theta, order.x + 1.0 > 4.5 ? 1.0 : order.x + 1.0), order.y);

    // Everything here depends only on where on the surface the point is, so crossing lines still cross.
    float outward = r / Rim;
    float band = spectrum(0.02 + outward * 0.6);
    float depth = 0.75 + Bass * 0.35 + DropPulse * 0.5 + BuildUp * 0.1 + band * 0.25;
    float spread = 1.0 + band * 0.12 + ebRing(outward) * 0.05 + DropPulse * 0.25;
    position.xz *= spread;
    // (Particle space has y growing downward.)
    position.y *= -depth;
    position.xz = rotate(position.xz, Time * 0.05);
    return position * 1.15;
}

// data.x: 0 for a ring, 1 for a spoke; data.y: the ring's r or the spoke's theta; data.z: how far along
// (theta round a ring, r outward along a spoke); data.w: speed.
float3 ebPosition(float4 data)
{
    return data.x < 0.5 ? ebPoint(data.y, data.z) : ebPoint(data.z, data.y);
}

void Spawn(inout Particle particle, uint id)
{
    bool ring = fixedRandom(id, 1.0) < RingShare;
    float count = ring ? Rings : Spokes;
    float which = min(floor(fixedRandom(id, 2.0) * count), count - 1.0);
    float onLine = ring ? (which + 1.0) / Rings * Rim : which / Spokes * 2.0 * PI;
    float along = ring ? fixedRandom(id, 3.0) * 2.0 * PI : fixedRandom(id, 3.0) * Rim;
    particle.data = float4(ring ? 0.0 : 1.0, onLine, along, 0.6 + fixedRandom(id, 4.0) * 0.8);
    particle.velocity = 0.0;
    particle.position = ebPosition(particle.data);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float speed = (0.1 + Bass * 0.35 + Beat * 0.2 + DropPulse * 0.5) * particle.data.w;
    if (particle.data.x < 0.5)
    {
        // Rings turn, alternate ones each way.
        float way = fmod(round(particle.data.y / Rim * Rings), 2.0) < 0.5 ? 1.0 : -1.0;
        particle.data.z = wrap(particle.data.z + way * speed * 1.6 * dt, 2.0 * PI);
    }
    else
    {
        // Light flows outward along the spokes and starts again at the centre.
        particle.data.z = wrap(particle.data.z + speed * 0.5 * dt, Rim);
    }

    float3 before = particle.position;
    particle.position = ebPosition(particle.data);
    float3 moved = particle.position - before;
    particle.velocity = dot(moved, moved) > 0.0225 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 ebEye()
{
    float angle = Time * 0.03 + 1.1;
    return float3(sin(angle) * 2.6, -2.0 + sin(Time * 0.023) * 0.7, cos(angle) * 2.6);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = ebEye();
    float3 target = float3(0.0, 0.0, 0.0);
    float zoom = 2.2 * (1.0 - DropPulse * 0.08);
    float roll = sin(Time * 0.04) * 0.08;
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, target, zoom, roll);

    bool ring = particle.data.x < 0.5;
    float outward = (ring ? particle.data.y : particle.data.z) / Rim;
    float drift = sin(Time * 0.02) * 0.04;
    // Rings run rose at the centre through coral to gold at the rim; spokes are electric cyan.
    float3 colour = ring ? hsv2rgb(frac(0.93 + outward * 0.22 + drift), 0.85, 1.0)
                         : hsv2rgb(frac(0.52 + outward * 0.04 + drift * 0.5), 0.8, 1.0);

    float band = spectrum(0.02 + outward * 0.6);
    float glow = (ring ? 0.11 : 0.08) * (0.3 + band * 1.6) + ebRing(outward) * 0.16 + DropPulse * 0.08;
    // The spokes all meet at the centre, so fade them in from it to keep it from burning outward.
    if (!ring)
    {
        glow *= smoothstep(0.0, 0.3, outward);
    }
    colour *= glow * (0.8 + Intensity * 0.4);
    // A few sparks along the lines catch the treble.
    colour *= 1.0 + step(0.996, fixedRandom(id, 6.0)) * Treble * 5.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    float2 streak = seen.xy - seenBefore.xy;
    sprite.streak = streak * min(1.0, 0.014 / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.2 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 2.2) * (0.006 + Bass * 0.012 + DropPulse * 0.02);
    return particles(uv) + previous(uv) * 0.55 + float3(0.9, 0.3, 0.5) * haze;
}
