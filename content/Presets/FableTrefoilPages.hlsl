// Name: Fable Trefoil Pages
// A gold trefoil knot bound like the spine of a book whose pages are surfaces: three neon sheets each span the knot
// and turn about it without ever touching, sweeping out through the far distance and back, their ribs lit by the
// spectrum, with light running out along them on every beat.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-11-15 17:00
// Particles: 262144

// The Milnor fibration of the trefoil. On the unit sphere of two complex numbers (z1, z2), the points where
// z1^2 + z2^3 = 0 form a trefoil knot, and everywhere else the angle of z1^2 + z2^3 says which "page" a point is on:
// each page is a surface whose only edge is the knot, and together the pages fill all of space around it.
// Turning z1 by 3t and z2 by 2t carries every page to the page 6t further round, which is both how a page is
// found here (turn a point until it lands on the page wanted) and how the pages are made to turn.
// The sphere is then laid out into ordinary space (stereographic projection) from a point that keeps the
// knot's three-fold symmetry.
static const float Pages = 3.0;
static const float Rings = 8.0;               // lines round each sheet
static const float Spokes = 18.0;             // lines across each sheet, from the core circle to the axis
static const float KnotShare = 0.08;
static const float RingShare = 0.42;
static const float KnotOut = 0.7153;          // where the knot sits between the core circle (0) and the axis (pi/2)
static const float Scale = 0.6;
static const float Far = 3.3;                 // sheets fade out this far from the middle

// The point of the page at angle page that lies on the circle of points (out, around), on sheet 0..5 of the six
// places that circle meets the page.
float3 ftPoint(float out0, float around, float sheet, float page)
{
    float r1 = sin(out0);
    float r2 = cos(out0);
    float2 f = float2(r1 * r1 * cos(around) + r2 * r2 * r2, r1 * r1 * sin(around));
    float angle = dot(f, f) > 1e-12 ? atan2(f.y, f.x) : 0.0;
    float t = (page - angle) / 6.0 + sheet * PI / 3.0;
    float2 z1 = r1 * float2(cos(around * 0.5 + 3.0 * t), sin(around * 0.5 + 3.0 * t));
    float2 z2 = r2 * float2(cos(2.0 * t), sin(2.0 * t));
    // Particle space has y growing downward.
    return float3(z2.x, -z1.x, z2.y) / max(1.0 - z1.y, 0.02) * Scale;
}

float3 ftKnot(float along)
{
    float r2 = 0.7548777;
    float r1 = 0.6558656;
    float turn = (3.0 * along + PI) * 0.5;
    float2 z1 = r1 * float2(cos(turn), sin(turn));
    float2 z2 = r2 * float2(cos(along), sin(along));
    return float3(z2.x, -z1.x, z2.y) / max(1.0 - z1.y, 0.02) * Scale;
}

// What each particle is, fixed for good: 0 a ring, 1 a spoke, 2 the knot; which page and sheet; which line.
struct FtRole
{
    float kind;
    float page;
    float sheet;
    float value;
};

FtRole ftRole(uint id)
{
    FtRole role;
    float pick = fixedRandom(id, 1.0);
    role.kind = pick < KnotShare ? 2.0 : (pick < KnotShare + RingShare ? 0.0 : 1.0);
    role.page = min(floor(fixedRandom(id, 2.0) * Pages), Pages - 1.0);
    role.sheet = min(floor(fixedRandom(id, 3.0) * 6.0), 5.0);
    float count = role.kind < 0.5 ? Rings : Spokes;
    float which = min(floor(fixedRandom(id, 4.0) * count), count - 1.0);
    role.value = role.kind < 0.5 ? 0.1 + (which + 0.5) / Rings * 1.36 : (which + 0.5) / Spokes * 2.0 * PI;
    return role;
}

float ftTurn()
{
    return Time * 0.28 + BeatGlide * 0.1;
}

float3 ftPosition(FtRole role, float along)
{
    float3 position;
    if (role.kind > 1.5)
    {
        position = ftKnot(along * 4.0 * PI);
    }
    else
    {
        float page = ftTurn() + role.page * 2.0 * PI / Pages;
        position = role.kind < 0.5
            ? ftPoint(role.value, along * 2.0 * PI, role.sheet, page)
            : ftPoint(0.02 + along * (PI * 0.5 - 0.04), role.value, role.sheet, page);
    }

    return position * (1.0 + Bass * 0.05 + DropPulse * 0.12);
}

void Spawn(inout Particle particle, uint id)
{
    FtRole role = ftRole(id);
    particle.data = float4(fixedRandom(id, 5.0), 0.6 + fixedRandom(id, 6.0) * 0.8, 0.0, 0.0);
    particle.velocity = 0.0;
    particle.position = ftPosition(role, particle.data.x);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    FtRole role = ftRole(id);
    float dt = min(DeltaTime, 0.05);
    float speed = (0.1 + Bass * 0.3 + Beat * 0.2 + DropPulse * 0.5) * particle.data.y;
    float pace = role.kind > 1.5 ? 0.05 : (role.kind < 0.5 ? 0.12 : 0.22);
    particle.data.x = frac(particle.data.x + speed * pace * dt);

    particle.position = ftPosition(role, particle.data.x);
    // Which way its line runs here, so its streak lies along the line however the pages are turning.
    float3 heading = ftPosition(role, particle.data.x + 0.0008) - particle.position;
    float reach = dot(heading, heading);
    particle.velocity = reach < 0.01 ? heading * rsqrt(max(reach, 1e-12)) * speed : 0.0;
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    FtRole role = ftRole(id);
    float angle = Time * 0.05 + 0.5;
    float3 eye = float3(sin(angle) * 2.0, -3.6 + sin(Time * 0.037) * 0.5, cos(angle) * 2.0);
    float3 target = float3(0.0, 0.05, 0.0);
    float zoom = 2.5 * (1.0 - DropPulse * 0.08);
    float roll = sin(Time * 0.045) * 0.08;
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.05, eye, target, zoom, roll);

    float away = length(particle.position) / Scale;
    float near = smoothstep(Far, Far * 0.55, away);
    float3 colour;
    float glow;
    if (role.kind > 1.5)
    {
        colour = float3(1.0, 0.72, 0.22);
        glow = 0.2 + Bass * 0.25 + Beat * 0.15;
    }
    else
    {
        // How far out the point is, from the core circle (bass) to the axis (treble).
        float out0 = role.kind < 0.5 ? role.value / (PI * 0.5) : particle.data.x;
        float band = spectrum(0.02 + out0 * 0.7);
        float run = out0 - BeatPhase;
        float pulse = exp(-run * run * 30.0) * (SongMapped > 0.0 ? 0.4 + Beat * 0.6 : Beat);
        float hue = 0.5 + role.page * 0.165 + out0 * 0.1 + sin(Time * 0.02) * 0.04;
        colour = hsv2rgb(frac(hue), role.kind < 0.5 ? 0.86 : 0.6, 1.0);
        glow = (role.kind < 0.5 ? 0.13 : 0.065) * (0.3 + band * 1.6) + pulse * 0.14 + DropPulse * 0.06;
        glow *= near;
    }

    colour *= glow * (0.8 + Intensity * 0.4);
    colour *= 1.0 + step(0.996, fixedRandom(id, 7.0)) * Treble * 5.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    float2 streak = seen.xy - seenBefore.xy;
    sprite.streak = streak * min(1.0, 0.014 / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.2 && near > 0.0 ? (role.kind > 1.5 ? 1.3 : 0.9) : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 2.2) * (0.006 + Bass * 0.012 + DropPulse * 0.02);
    return particles(uv) + previous(uv) * 0.55 + float3(0.4, 0.2, 0.9) * haze;
}
