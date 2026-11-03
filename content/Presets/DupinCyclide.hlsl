// Name: Dupin Cyclide
// A neon Dupin cyclide, the torus turned inside out by inversion, woven from its two families of circles: every
// line of curvature on it is a perfect circle. Slowly it breathes from a ring to a horn to a spindle and back,
// tumbling as it goes, with its two focal curves, a gold ellipse and the hyperbola that threads through it, lit
// inside. Each circle plays its own band of the spectrum, mirrored so the shape stays symmetrical, light runs
// round every circle, each beat sends a pulse round the other family, and a drop spins it and flares it white.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-11-03 04:00
// Particles: 262144

// The cyclide with a = 1 and b = 0.8 (so c = 0.6), and its shape parameter d. For d between c and a it's a ring
// cyclide with a hole through it, at d = c the hole pinches shut (a horn), and below c it folds through itself (a
// spindle). Every circle of one family (u fixed) is where a sphere centred on the focal ellipse touches it, and
// every circle of the other (v fixed) where a sphere centred on the focal hyperbola does, so the two gold curves are
// the spines the whole shape hangs from.
static const float CyA = 1.0;
static const float CyB = 0.8;
static const float CyC = 0.6;
static const float Circles = 18.0;         // circles in each family
static const float ShareU = 0.42;
static const float ShareV = 0.84;          // the rest light the focal curves
static const float HyperbolaReach = 1.25;

// The tumble: steady, plus a gentle push on every beat. (BeatGlide eases forward and never goes back.)
float dcClock()
{
    return Time * 0.08 + BeatGlide * 0.03;
}

// The shape: a slow breath from ring to horn to spindle, opened a little by the bass and a build-up.
float dcShape()
{
    float breath = sin(Time * 0.045 + 0.6);
    return CyC + breath * 0.3 + BeatSwell * 0.025 + Bass * 0.03 + BuildUp * 0.04;
}

float3 dcTurn(float3 p)
{
    float clock = dcClock();
    p.yz = rotate(p.yz, 0.55 + sin(clock * 0.6) * 0.5 + DropPulse * 0.5);
    p.xz = rotate(p.xz, clock * 1.1 + DropPulse * 1.4);
    p.xy = rotate(p.xy, sin(clock * 0.37) * 0.3);
    return p;
}

float3 dcSurface(float u, float v, float d)
{
    float cu = cos(u);
    float cv = cos(v);
    float under = CyA - CyC * cu * cv;
    return float3((d * (CyC - CyA * cu * cv) + CyB * CyB * cu) / under,
                  CyB * sin(u) * (CyA - d * cv) / under,
                  CyB * sin(v) * (CyC * cu - d) / under);
}

// Circle `which` sits at this angle round its family. Half a step off zero, so the circles pair up mirrored
// across the shape's planes of symmetry.
float dcCircleAngle(float which)
{
    return (which + 0.5) / Circles * 2.0 * PI;
}

// The circle's band: mirrored, so a circle and its reflection always play the same band.
float dcBand(float which, float family)
{
    float across = abs(which + 0.5 - Circles * 0.5) / (Circles * 0.5);
    return spectrum(0.03 + (1.0 - across) * 0.55 + family * 0.04);
}

// data.x: kind (0 a u circle, 1 a v circle, 2 a focal curve); data.y: which circle, or for the focal curves 0 the
// ellipse and 1 and 2 the hyperbola's branches; data.z: angle along it; data.w: the particle's own random number.
float3 dcPosition(float4 data)
{
    float d = dcShape();
    float3 p;
    if (data.x < 0.5)
    {
        p = dcSurface(dcCircleAngle(data.y), data.z, d);
    }
    else if (data.x < 1.5)
    {
        p = dcSurface(data.z, dcCircleAngle(data.y), d);
    }
    else if (data.y < 0.5)
    {
        p = float3(CyA * cos(data.z), CyB * sin(data.z), 0.0);
    }
    else
    {
        // Light runs out along each branch and back.
        float s = sin(data.z) * HyperbolaReach;
        float side = data.y < 1.5 ? 1.0 : -1.0;
        float e = exp(s);
        p = float3(side * CyC * (e + 1.0 / e) * 0.5, 0.0, CyB * (e - 1.0 / e) * 0.5);
    }
    // The shape sits off to one side of the focal ellipse, by about d (up to c); this keeps it centred.
    p.x += min(d, CyC);
    return dcTurn(p);
}

void Spawn(inout Particle particle, uint id)
{
    float pick = fixedRandom(id, 1.0);
    float kind = pick < ShareU ? 0.0 : (pick < ShareV ? 1.0 : 2.0);
    float which = kind < 1.5 ? min(floor(fixedRandom(id, 2.0) * Circles), Circles - 1.0)
                             : min(floor(fixedRandom(id, 2.0) * 3.0), 2.0);
    float a = fixedRandom(id, 3.0) * 2.0 * PI;
    particle.data = float4(kind, which, a, fixedRandom(id, 4.0));
    particle.velocity = 0.0;
    particle.position = dcPosition(particle.data);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    // Light runs round every circle, the two families opposite ways, and slower along the focal curves.
    float way = particle.data.x < 0.5 ? 1.0 : (particle.data.x < 1.5 ? -1.0 : 0.5);
    float speed = 0.22 + Mid * 0.4 + BeatSwell * 0.25 + DropPulse * 0.6;
    particle.data.z = wrap(particle.data.z + way * speed * dt, 2.0 * PI);
    float3 before = particle.position;
    particle.position = dcPosition(particle.data);
    float3 moved = particle.position - before;
    particle.velocity = length(moved) > 0.2 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 dcEye()
{
    float angle = Time * 0.03 + 0.4;
    float height = 1.2 + sin(Time * 0.023) * 0.8;
    return float3(sin(angle) * 6.0, -height, cos(angle) * 6.0);
}

float dcZoom()
{
    return 2.25 * (1.0 + BuildUp * 0.06 - DropPulse * 0.05);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = dcEye();
    float3 target = float3(0.0, 0.0, 0.0);
    float zoom = dcZoom();
    float roll = sin(Time * 0.04) * 0.05;
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.025, eye, target, zoom, roll);

    float kind = particle.data.x;
    float which = particle.data.y;
    float drift = sin(Time * 0.02) * 0.03;
    float3 colour;
    float glow;
    float size = 0.9;
    if (kind < 1.5)
    {
        float band = dcBand(which, kind);
        // One family runs teal into blue, the other violet into magenta, each mirrored so the colours meet
        // symmetrically across the shape.
        float across = abs(which + 0.5 - Circles * 0.5) / (Circles * 0.5);
        float hue = kind < 0.5 ? 0.47 + across * 0.16 : 0.76 + across * 0.16;
        colour = hsv2rgb(frac(hue + drift), 0.85, 1.0);
        glow = 0.11 * (0.3 + band * 1.6);
        // Every beat a pulse runs round the v circles, all together, so a bright ring sweeps over the shape.
        if (kind > 0.5)
        {
            float at = (frac(particle.data.z / (2.0 * PI)) - BeatPhase) * 7.0;
            at -= round(at / 7.0) * 7.0;
            glow += exp(-at * at) * 0.07 * (SongMapped > 0.0 ? 0.4 + BeatSwell * 0.6 : BeatSwell);
        }
    }
    else
    {
        // The focal curves: warm gold, brightening with the mids, white hot near the hyperbola's vertices.
        colour = lerp(hsv2rgb(0.1 + drift * 0.5, 0.6, 1.0), float3(1.0, 0.95, 0.85), which > 0.5 ? 0.25 : 0.0);
        glow = 0.14 * (0.5 + Mid * 0.8 + BeatSwell * 0.3);
        if (which > 0.5)
        {
            // Fade the hyperbola out towards its open ends.
            glow *= 0.35 + 0.65 * (1.0 - abs(sin(particle.data.z)));
        }
        size = 1.1;
    }
    // The particles are spread evenly by angle, so the small circles near the pinch pack them tight and would
    // glare; light every line by how far it actually runs instead, so the whole shape glows evenly.
    float4 ahead = particle.data;
    ahead.z += 0.02;
    float run = length(dcPosition(ahead) - particle.position) / 0.02;
    glow *= clamp(run / 1.3, 0.3, 1.4);
    colour = lerp(colour, float3(1.0, 1.0, 1.0), DropPulse * 0.5);
    colour *= glow * (0.8 + Intensity * 0.4);
    // A few sparks along the circles catch the treble.
    colour *= 1.0 + step(0.995, fixedRandom(id, 6.0)) * Treble * 5.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    float2 streak = seen.xy - seenBefore.xy;
    sprite.streak = streak * min(1.0, 0.008 / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.3 ? size : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.04);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    return particles(uv) + previous(uv) * 0.3;
}
