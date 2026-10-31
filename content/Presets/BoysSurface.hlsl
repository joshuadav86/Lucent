// Name: Boy's Surface
// Boy's surface, the shape that closes a sphere through itself with three-fold symmetry and no edge, drawn in neon contour
// lines: rings of every colour of the spectrum crossed by gold meridians, with light streaming along each one. Each ring
// swells with its own band of the spectrum, the bass twists the shape round its axis, every beat sends a pulse from its
// middle loop to its pole, and a drop throws it wide open.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-10-31 19:00
// Particles: 262144

// Apery's parametrisation of Boy's surface over u in -pi/2..pi/2, v in 0..pi:
//     D = 2 - sqrt2 sin3u sin2v
//     x = (sqrt2 cos2u cos^2 v + cos u sin2v) / D
//     y = (sqrt2 sin2u cos^2 v - sin u sin2v) / D
//     z = 3 cos^2 v / D
// D never reaches 0, so the surface is smooth everywhere it doesn't pass through itself.
// Lines of constant u ("meridians") close on themselves over v. A line of constant v ("ring") runs off
// the end of u and comes back in at v' = pi - v, so a ring is the pair v, pi - v walked one after the other.
static const float Rings = 14.0;
static const float Meridians = 18.0;
static const float RingShare = 0.6;          // the share of particles on rings
static const float Centre = 1.5;             // the surface's height runs 0..3; this centres it
static const float Scale = 0.62;
static const float SQRT2 = 1.41421356;

// Where a point lies between the surface's middle loop (v = 0 or pi, where the three lobes cross) and its
// pole (v = pi/2, the single point every meridian passes through): 0 on the loop, 1 at the pole.
// It's the same at both ends of a ring's join, so everything driven by it keeps lines meeting.
float bsToPole(float v)
{
    return min(v, PI - v) / (PI * 0.5);
}

float bsRing(float toPole)
{
    float at = (toPole - BeatPhase) * 6.0;
    return exp(-at * at) * (SongMapped > 0.0 ? 0.4 + Beat * 0.6 : Beat);
}

float3 bsPoint(float u, float v)
{
    float s2v = sin(2.0 * v);
    float c2 = cos(v) * cos(v);
    float d = 2.0 - SQRT2 * sin(3.0 * u) * s2v;
    float x = (SQRT2 * cos(2.0 * u) * c2 + cos(u) * s2v) / d;
    float y = (SQRT2 * sin(2.0 * u) * c2 - sin(u) * s2v) / d;
    float z = 3.0 * c2 / d;
    // Particle space has y growing downward: the surface stands upright.
    float3 position = float3(x, Centre - z, y) * Scale;

    // Every part swells with its own band, the beat's pulse and the drop. It depends only on where
    // on the surface the point is, so crossing lines still cross.
    float toPole = bsToPole(v);
    float band = spectrum(0.02 + toPole * 0.6);
    float swell = 1.0 + band * 0.22 + bsRing(toPole) * 0.07 + Bass * 0.05 + DropPulse * 0.25 + BuildUp * 0.04;

    // The bass twists the shape round its axis of symmetry, more the further down.
    // (The beat clock BeatCount - Beat only moves forward, so the music can push it without jitter.)
    float clock = Time * 0.06 + (BeatCount - Beat) * 0.02;
    float twist = sin(clock) * 0.35 + Bass * 0.3 + DropPulse * 0.35;
    float spin = Time * 0.07 + position.y * twist;
    position.xz = rotate(position.xz, spin);
    return position * swell;
}

// data.x: 0 for a ring, 1 for a meridian; data.y: the line's v (ring, 0..pi/2) or u (meridian);
// data.z: how far along the line; data.w: speed.
float3 bsPosition(float4 data)
{
    if (data.x < 0.5)
    {
        float s = data.z;   // 0..2pi
        return s < PI ? bsPoint(s - PI * 0.5, data.y) : bsPoint(s - PI * 1.5, PI - data.y);
    }
    return bsPoint(data.y, data.z);   // 0..pi
}

float bsLength(float kind)
{
    return kind < 0.5 ? 2.0 * PI : PI;
}

void Spawn(inout Particle particle, uint id)
{
    bool ring = fixedRandom(id, 1.0) < RingShare;
    float count = ring ? Rings : Meridians;
    float which = min(floor(fixedRandom(id, 2.0) * count), count - 1.0);
    float onLine = ring ? (which + 0.5) / Rings * PI * 0.5 : -PI * 0.5 + (which + 0.5) / Meridians * PI;
    float kind = ring ? 0.0 : 1.0;
    particle.data = float4(kind, onLine, fixedRandom(id, 3.0) * bsLength(kind), 0.6 + fixedRandom(id, 4.0) * 0.8);
    particle.velocity = 0.0;
    particle.position = bsPosition(particle.data);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    // Rings flow one way and meridians the other, so the light seems to weave through the crossings.
    float way = particle.data.x < 0.5 ? 1.0 : -1.0;
    float speed = (0.14 + Bass * 0.45 + Beat * 0.25 + DropPulse * 0.6) * particle.data.w;
    float span = bsLength(particle.data.x);
    particle.data.z = wrap(particle.data.z + way * speed * dt, span);

    float3 before = particle.position;
    particle.position = bsPosition(particle.data);
    float3 moved = particle.position - before;
    particle.velocity = dot(moved, moved) > 0.0225 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 bsEye()
{
    float angle = Time * 0.035 + 0.4;
    return float3(sin(angle) * 3.3, -2.2 + sin(Time * 0.027) * 0.8, cos(angle) * 3.3);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = bsEye();
    float3 target = float3(0.0, 0.05, 0.0);
    float zoom = 2.6 * (1.0 - DropPulse * 0.08);
    float roll = sin(Time * 0.045) * 0.08;
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, target, zoom, roll);

    bool ring = particle.data.x < 0.5;
    float v = ring ? (particle.data.z < PI ? particle.data.y : PI - particle.data.y) : particle.data.z;
    float toPole = bsToPole(v);
    float drift = sin(Time * 0.02) * 0.05;
    float3 colour;
    if (ring)
    {
        // Rings run the neon spectrum from teal at the middle loop through blue and violet to magenta at the pole.
        colour = hsv2rgb(frac(0.47 + particle.data.y / (PI * 0.5) * 0.45 + drift), 0.85, 1.0);
    }
    else
    {
        // Meridians glow gold to amber.
        colour = hsv2rgb(frac(0.09 + toPole * 0.04 + drift * 0.3), 0.7, 1.0);
    }

    float band = spectrum(0.02 + toPole * 0.6);
    float glow = (ring ? 0.12 : 0.085) * (0.3 + band * 1.6) + bsRing(toPole) * 0.15 + DropPulse * 0.08;
    // Every meridian meets at the pole and the last rings shrink round it, so dim it there to keep it from burning out.
    glow *= lerp(1.0, ring ? 0.5 : 0.25, smoothstep(0.6, 1.0, toPole));
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
    return particles(uv) + previous(uv) * 0.55 + float3(0.5, 0.25, 0.9) * haze;
}
