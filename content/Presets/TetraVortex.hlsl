// Name: Tetra Vortex
// Fly down a curving tunnel of neon blue rings towards a burning orange tetrahedron, turning inside a thin triangle of
// light that throws three blue beams back along the tunnel walls. Glowing springs coil down the tunnel beside you, each
// trailed by a twin of orange sparks, and orange dust spirals past. Every ring plays its own band of the spectrum and
// swells into a soft three-lobed shape with it, each beat surges the flight forward, the springs widen with the treble,
// the core swells with the bass, the triangle brightens with the mids, and a drop flares the whole tunnel white.
// Family: Particles
// Tags: Geometric, Neon, 3D, Intense
// Added: 2026-10-31 23:40
// Particles: 262144

// The camera sits at the origin looking down +z. The tunnel runs from just in front of it to the core at CoreDepth;
// its axis bends away to one side near the camera and straightens out at the core, so the nearest rings sweep round
// one edge of the picture while the far ones close in round the core. Every piece is placed from its own numbers each
// frame (no physics), so rings stay rings and springs stay springs.
static const float CoreDepth = 4.5;
static const float NearDepth = 0.25;
static const float TunnelRadius = 1.0;
static const float Rings = 36.0;
static const float RingGap = (CoreDepth - NearDepth) / Rings;
static const float Coils = 3.0;
static const float CoilLoops = 30.0;
static const float DustTrails = 12.0;
static const float Zoom = 1.2;

// Shares of the particles, in order: rings, beams, springs, spring sparks, the core's edges, the triangle, dust.
static const float ShareRings = 0.5;
static const float ShareBeams = 0.68;
static const float ShareCoils = 0.79;
static const float ShareSparks = 0.84;
static const float ShareCore = 0.88;
static const float ShareFrame = 0.9;

// The flight: steady, plus a surge on every beat. (BeatCount - Beat only moves forward, so the music can push it
// without ever pulling it back.)
float tvTravel()
{
    return Time * 0.55 + (BeatCount - Beat) * 0.22;
}

// The core and triangle turn; the triangle's tip points up the screen.
float tvSpin()
{
    return Time * 0.12 + (BeatCount - Beat) * 0.05;
}

// Where the tunnel's axis is at depth z: bent well aside near the camera, straight at the core.
float2 tvAxis(float z)
{
    float bend = pow(saturate(1.0 - z / CoreDepth), 1.6) * (0.75 + DropPulse * 0.3);
    float heading = Time * 0.06 + sin(Time * 0.045) * 1.2;
    return bend * float2(cos(heading), sin(heading));
}

// The tunnel's radius at depth z and angle a: three soft lobes that swell with this ring's band.
float tvRadius(float z, float a, float band)
{
    float lobe = (0.03 + band * 0.12 + DropPulse * 0.05) * cos(3.0 * (a - tvSpin() * 0.5));
    return TunnelRadius * (1.0 + lobe) * (1.0 - 0.18 * z / CoreDepth);
}

float tvRingBand(float ring)
{
    return spectrum(0.02 + ring / Rings * 0.6);
}

float tvRingDepth(float ring)
{
    return NearDepth + wrap(ring * RingGap - tvTravel(), CoreDepth - NearDepth);
}

float3 tvRing(float ring, float a)
{
    float z = tvRingDepth(ring);
    float turned = a + ring * 0.21 + Time * 0.03;
    float r = tvRadius(z, turned, tvRingBand(ring));
    return float3(tvAxis(z) + r * float2(cos(turned), sin(turned)), z);
}

// One of the three beams: a thin sheet from the axis out to the wall, pointing at a corner of the triangle,
// with light flowing along it towards the camera.
float3 tvBeam(float k, float out_, float flow, float spread)
{
    float z = NearDepth + wrap(flow * (CoreDepth - NearDepth) - tvTravel() * 1.4, CoreDepth - NearDepth);
    float a = -PI * 0.5 + k * 2.0 * PI / 3.0 + tvSpin() * 0.5 + spread;
    float r = out_ * tvRadius(z, a, 0.0) * 0.96;
    return float3(tvAxis(z) + r * float2(cos(a), sin(a)), z);
}

// The middle of spring k at s (0 near the camera, 1 by the core): it spirals down the tunnel near the wall.
float3 tvCoilAxis(float k, float s)
{
    float z = NearDepth * 0.4 + s * (CoreDepth - 0.6);
    float a = k * 2.0 * PI / Coils + PI / 3.0 + s * 2.2 + Time * 0.05;
    float r = TunnelRadius * (0.72 - 0.25 * s);
    return float3(tvAxis(z) + r * float2(cos(a), sin(a)), z);
}

// A point on spring k's wire at s; the loops turn as you fly, so the spring seems to screw towards you.
float3 tvCoil(float k, float s, float shift, float grow)
{
    float3 centre = tvCoilAxis(k, s);
    float3 along = normalize(tvCoilAxis(k, s + 0.004) - centre);
    float3 outward = normalize(cross(along, float3(0.0, 0.0, 1.0)) + float3(1e-4, 0.0, 0.0));
    float3 across = cross(along, outward);
    float u = s * CoilLoops * 2.0 * PI - tvTravel() * 6.0 + k * 2.0 + shift;
    float size = (0.075 + Treble * 0.05 + DropPulse * 0.05) * grow * (1.0 - 0.4 * s);
    return centre + size * (cos(u) * outward + sin(u) * across);
}

float3 tvCoreCentre()
{
    return float3(tvAxis(CoreDepth), CoreDepth);
}

// The tetrahedron's corners, tumbling.
float3 tvCorner(int i)
{
    static const float3 Corners[4] = { float3(1, 1, 1), float3(1, -1, -1), float3(-1, 1, -1), float3(-1, -1, 1) };
    float3 c = Corners[i] * 0.57735;
    float spin = tvSpin() * 2.5;
    c.xz = rotate(c.xz, spin);
    c.yz = rotate(c.yz, spin * 0.63 + 0.6);
    float size = 0.42 * (1.0 + Bass * 0.2 + Beat * 0.12 + DropPulse * 0.3);
    return tvCoreCentre() + c * size;
}

float3 tvCoreEdge(float e, float t)
{
    static const int From[6] = { 0, 0, 0, 1, 1, 2 };
    static const int To[6] = { 1, 2, 3, 2, 3, 3 };
    int i = (int)e;
    return lerp(tvCorner(From[i]), tvCorner(To[i]), t);
}

// The thin triangle round the core, in the plane of the core.
float3 tvFrame(float side, float t)
{
    float a0 = -PI * 0.5 + side * 2.0 * PI / 3.0 + tvSpin() * 0.5;
    float a1 = a0 + 2.0 * PI / 3.0;
    float r = TunnelRadius * 0.8 * (1.0 + Beat * 0.04);
    float2 p = lerp(float2(cos(a0), sin(a0)), float2(cos(a1), sin(a1)), t) * r;
    return tvCoreCentre() + float3(p, 0.0);
}

float3 tvDust(float trail, float flow, float jitter)
{
    float z = NearDepth + wrap(flow * (CoreDepth - NearDepth) - tvTravel() * 1.15, CoreDepth - NearDepth);
    float a = trail * 2.0 * PI / DustTrails + z * 0.9 + Time * 0.08;
    float r = TunnelRadius * (0.86 + jitter * 0.08) * (1.0 - 0.18 * z / CoreDepth);
    return float3(tvAxis(z) + r * float2(cos(a), sin(a)), z);
}

// data.x: kind (0 ring, 1 beam, 2 spring, 3 spring spark, 4 core, 5 triangle, 6 dust);
// data.y and data.z: where on that piece; data.w: the particle's own random number.
float3 tvPosition(float4 data)
{
    float kind = data.x;
    if (kind < 0.5)
    {
        return tvRing(data.y, data.z);
    }
    if (kind < 1.5)
    {
        return tvBeam(floor(data.y), frac(data.y), data.z, (data.w - 0.5) * 0.12 * (1.0 - frac(data.y) * 0.5));
    }
    if (kind < 2.5)
    {
        return tvCoil(floor(data.y), data.z, 0.0, 1.0);
    }
    if (kind < 3.5)
    {
        return tvCoil(floor(data.y), data.z, 0.7, 1.3);
    }
    if (kind < 4.5)
    {
        return tvCoreEdge(floor(data.y), data.z);
    }
    if (kind < 5.5)
    {
        return tvFrame(floor(data.y), data.z);
    }
    return tvDust(data.y, data.z, data.w);
}

void Spawn(inout Particle particle, uint id)
{
    float pick = fixedRandom(id, 1.0);
    float a = fixedRandom(id, 2.0);
    float b = fixedRandom(id, 3.0);
    float own = fixedRandom(id, 4.0);
    float4 data;
    if (pick < ShareRings)
    {
        data = float4(0.0, min(floor(a * Rings), Rings - 1.0), b * 2.0 * PI, own);
    }
    else if (pick < ShareBeams)
    {
        // Packed towards the axis, where the beams burn brightest.
        data = float4(1.0, min(floor(a * 3.0), 2.0) + pow(b, 1.6) * 0.999, fixedRandom(id, 5.0), own);
    }
    else if (pick < ShareCoils)
    {
        data = float4(2.0, min(floor(a * Coils), Coils - 1.0), b, own);
    }
    else if (pick < ShareSparks)
    {
        data = float4(3.0, min(floor(a * Coils), Coils - 1.0), b, own);
    }
    else if (pick < ShareCore)
    {
        data = float4(4.0, min(floor(a * 6.0), 5.0), b, own);
    }
    else if (pick < ShareFrame)
    {
        data = float4(5.0, min(floor(a * 3.0), 2.0), b, own);
    }
    else
    {
        data = float4(6.0, min(floor(a * DustTrails), DustTrails - 1.0), b, own);
    }
    particle.data = data;
    particle.velocity = 0.0;
    particle.position = tvPosition(data);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float3 before = particle.position;
    particle.position = tvPosition(particle.data);
    float3 moved = particle.position - before;
    // A piece that wrapped from the camera back to the core jumps; it gets no streak for that frame.
    particle.velocity = dot(moved, moved) > 0.04 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = float3(0.0, 0.0, 0.0);
    float3 target = float3(sin(Time * 0.05) * 0.06, cos(Time * 0.037) * 0.04, 1.0);
    float roll = sin(Time * 0.03) * 0.15;
    float zoom = Zoom * (1.0 + BuildUp * 0.08 - DropPulse * 0.06);
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.02, eye, target, zoom, roll);

    float kind = particle.data.x;
    float z = particle.position.z;
    // Everything fades in from the core and out as it nears the camera.
    float fade = smoothstep(NearDepth, NearDepth + 0.5, z) * smoothstep(CoreDepth + 0.05, CoreDepth - 0.6, z);
    float3 white = float3(0.85, 0.95, 1.0);
    float3 orange = float3(1.0, 0.45, 0.08);
    float3 colour;
    float glow;
    float size = 0.9;
    if (kind < 0.5)
    {
        // Neon blue rings, every fourth a brighter blue-white; each glows with its band and the beat.
        float ring = particle.data.y;
        float band = tvRingBand(ring);
        bool bright = wrap(ring, 4.0) < 0.5;
        colour = bright ? white : hsv2rgb(0.58 + band * 0.04, 0.8, 1.0);
        glow = (bright ? 0.5 : 0.3) * (0.45 + band * 1.3 + Beat * 0.35);
        colour = lerp(colour, white, DropPulse * 0.6);
        // A ring's light spreads round a circle whose size on screen goes as 1 / depth, so scale it to match:
        // near rings stay as bright as far ones, and the far ones don't burn into a solid disc.
        glow *= clamp(1.6 / z, 0.3, 4.0);
        size = 1.0;
    }
    else if (kind < 1.5)
    {
        // The beams: soft blue light, brightest near the axis and deep in the tunnel.
        float out_ = frac(particle.data.y);
        colour = hsv2rgb(0.6, 0.85, 1.0);
        glow = 0.12 * (1.0 - out_ * 0.7) * (0.5 + Bass * 0.9 + BuildUp * 0.5) * smoothstep(0.0, 2.5, z);
        size = 2.4;
    }
    else if (kind < 2.5)
    {
        // The springs: blue-white wire.
        colour = lerp(white, hsv2rgb(0.52, 0.6, 1.0), particle.data.w * 0.5);
        glow = 0.12 * (0.7 + Treble * 0.8 + Beat * 0.3);
        size = 1.0;
    }
    else if (kind < 3.5)
    {
        // Their twins: a dotted trail of orange sparks that twinkle with the treble.
        colour = orange;
        float twinkle = step(0.6, particle.data.w) * (0.6 + 0.4 * sin(Time * 9.0 + particle.data.w * 60.0));
        glow = 0.6 * twinkle * (0.6 + Treble * 1.2);
        size = 0.8;
    }
    else if (kind < 4.5)
    {
        // The core: a burning orange-gold tetrahedron.
        colour = lerp(orange, float3(1.0, 0.8, 0.45), Beat * 0.5);
        glow = 0.5 * (0.8 + Bass * 0.8 + Beat * 0.6 + DropPulse * 1.5);
        size = 1.1;
        fade = 1.0;
    }
    else if (kind < 5.5)
    {
        // The thin triangle of light round it.
        colour = lerp(white, hsv2rgb(0.58, 0.5, 1.0), 0.4);
        glow = 0.1 * (0.6 + Mid * 0.8 + Beat * 0.3);
        size = 0.8;
        fade = 1.0;
    }
    else
    {
        // Orange dust, spiralling past in sparse dots.
        colour = orange * lerp(0.8, 1.2, particle.data.w);
        glow = step(0.55, frac(particle.data.w * 7.31)) * 2.5 * (0.5 + Level * 0.8) * clamp(1.5 / z, 0.4, 3.0);
        size = 0.7;
    }
    colour *= glow * fade * (0.8 + Intensity * 0.4);
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    float2 streak = seen.xy - seenBefore.xy;
    // Rings stay crisp lines and the dust stays dotted; everything else streaks as it flies.
    float most = kind < 0.5 ? 0.0 : (kind > 5.5 ? 0.008 : 0.03);
    sprite.streak = streak * min(1.0, most / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.1 ? size : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, CoreDepth, 0.015);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A warm glow round the core and a faint blue one filling the far end of the tunnel.
    float3 eye = float3(0.0, 0.0, 0.0);
    float3 target = float3(sin(Time * 0.05) * 0.06, cos(Time * 0.037) * 0.04, 1.0);
    float zoom = Zoom * (1.0 + BuildUp * 0.08 - DropPulse * 0.06);
    float2 core = project(tvCoreCentre(), eye, target, zoom, sin(Time * 0.03) * 0.15).xy;
    float d = dot(p - core, p - core);
    float3 warm = float3(1.0, 0.5, 0.12) * exp(-d * 260.0) * (0.25 + Bass * 0.35 + Beat * 0.25 + DropPulse * 0.6);
    float3 cool = float3(0.15, 0.35, 1.0) * exp(-d * 9.0) * (0.03 + BuildUp * 0.04 + Bass * 0.03);
    return particles(uv) + previous(uv) * 0.3 + warm + cool;
}
