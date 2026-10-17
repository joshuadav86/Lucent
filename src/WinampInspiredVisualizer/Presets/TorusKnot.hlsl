// Name: Torus Knot
// Riding inside a glowing lattice tube tied into an endless torus knot, glimpsing its other loops through the mesh.
// Family: Fly-through
// Tags: Colourful, Neon, 3D

static const float KnotTurns = 3.0;       // times the knot circles the axis
static const float KnotWinds = 5.0;       // times it winds through the hole
static const float KnotRing = 3.0;        // radius of the ring it winds around
static const float KnotSwing = 1.25;      // how far it swings away from that ring
static const float TubeRadius = 0.62;
static const float StrutRadius = 0.012;
static const float CellsAlong = 150.0;    // lattice cells along the whole knot
static const float CellsAround = 6.0;     // lattice cells around the tube
static const float2 NextStrand = float2(-0.5, -0.8660254);   // turn of 2pi * 5/3 between crossings

float3 knotPoint(float t)
{
    float swing = KnotWinds * t;
    float ring = KnotRing + KnotSwing * cos(swing);
    return float3(ring * cos(KnotTurns * t), KnotSwing * sin(swing), ring * sin(KnotTurns * t));
}

// atan2 from a short polynomial (accurate to about 0.001 radians), cheaper than the built-in.
float knotAngle(float y, float x)
{
    float2 a = abs(float2(x, y));
    float ratio = min(a.x, a.y) / max(max(a.x, a.y), 1e-8);
    float s = ratio * ratio;
    float r = ((-0.0464964749 * s + 0.15931422) * s - 0.327622764) * s * ratio + ratio;
    r = a.y > a.x ? 1.5707963 - r : r;
    r = x < 0.0 ? PI - r : r;
    return y < 0.0 ? -r : r;
}

// The knot crosses the half-plane through the axis and pos three times. Finds the nearest
// crossing: returns a rough distance to the knot there, its parameter t along the knot, and the
// cosines and sines of its two turning angles (xy: around the axis, zw: around the ring).
float knotCrossing(float3 pos, out float t, out float4 angles)
{
    float rho = length(pos.xz);
    float2 plane = float2(rho - KnotRing, pos.y);
    float theta = knotAngle(pos.z, pos.x);
    float swing0 = KnotWinds * theta / KnotTurns;
    float2 cs = float2(cos(swing0), sin(swing0));
    float best = 1e5;
    float bestStrand = 0.0;
    float2 bestCs = cs;
    [unroll]
    for (int k = 0; k < 3; k++)
    {
        float2 offset = plane - KnotSwing * cs;
        // The knot slants through the plane: discount the part of the offset along its direction.
        float2 slant = float2(-cs.y, cs.x) * (KnotSwing * KnotWinds);
        float ring = KnotTurns * (KnotRing + KnotSwing * cs.x);
        float along = dot(offset, slant);
        float d2 = dot(offset, offset) - along * along / (ring * ring + KnotSwing * KnotSwing * KnotWinds * KnotWinds);
        if (d2 < best)
        {
            best = d2;
            bestStrand = k;
            bestCs = cs;
        }
        cs = float2(cs.x * NextStrand.x - cs.y * NextStrand.y, cs.x * NextStrand.y + cs.y * NextStrand.x);
    }
    t = (theta + 2.0 * PI * bestStrand) / KnotTurns;
    angles = float4(pos.xz / max(rho, 1e-5), bestCs);
    return sqrt(max(best, 0.0));
}

// Distance to the lattice tube near a crossing found by knotCrossing: one Newton step to the
// closest point of the knot (sines and cosines of the small step from short series), a frame
// that turns with the knot, and a diamond mesh of struts laid around the tube.
// widen thickens the struts with distance so they never get thinner than a pixel.
// surface returns: x = position along the knot (0..2pi), y = angle around the tube, z = 1 near a node,
// w = position along the nearest strut.
float knotMesh(float3 pos, float t, float4 angles, float widen, out float4 surface)
{
    float2 turn = angles.xy;
    float2 wind = angles.zw;
    float ring = KnotRing + KnotSwing * wind.x;
    float3 velocity = float3(-KnotSwing * KnotWinds * wind.y * turn.x - ring * KnotTurns * turn.y, KnotSwing * KnotWinds * wind.x,
                             -KnotSwing * KnotWinds * wind.y * turn.y + ring * KnotTurns * turn.x);
    float nudge = dot(pos - float3(ring * turn.x, KnotSwing * wind.y, ring * turn.y), velocity) / dot(velocity, velocity);
    t += nudge;
    float2 a = float2(KnotTurns, KnotWinds) * nudge;
    float2 c = 1.0 - a * a * (0.5 - a * a / 24.0);
    float2 sn = a * (1.0 - a * a / 6.0);
    turn = float2(turn.x * c.x - turn.y * sn.x, turn.y * c.x + turn.x * sn.x);
    wind = float2(wind.x * c.y - wind.y * sn.y, wind.y * c.y + wind.x * sn.y);
    ring = KnotRing + KnotSwing * wind.x;
    float3 offset = pos - float3(ring * turn.x, KnotSwing * wind.y, ring * turn.y);
    velocity = float3(-KnotSwing * KnotWinds * wind.y * turn.x - ring * KnotTurns * turn.y, KnotSwing * KnotWinds * wind.x,
                      -KnotSwing * KnotWinds * wind.y * turn.y + ring * KnotTurns * turn.x);
    float speed = length(velocity);
    float3 tangent = velocity / speed;
    float3 outward = float3(wind.x * turn.x, wind.y, wind.x * turn.y);
    offset -= tangent * dot(offset, tangent);
    float across = length(offset);
    float shell = across - TubeRadius;
    float nodeRadius = StrutRadius * 2.5 + widen * 1.5;
    surface = float4(t, 0.0, 0.0, 0.0);

    // Away from the tube wall, the mesh can't be closer than the wall itself.
    if (abs(shell) > 0.1)
    {
        return (abs(shell) - nodeRadius) * 0.95;
    }
    float around = knotAngle(dot(offset, cross(tangent, outward)), dot(offset, outward));

    // Two families of helical struts, with nodes where they cross.
    float u = t * CellsAlong / (2.0 * PI);
    float v = around * CellsAround / (2.0 * PI);
    float2 lines = abs(frac(float2(u + v, u - v) + 0.5) - 0.5);
    float cellAlong = speed * 2.0 * PI / CellsAlong;
    float cellAround = TubeRadius * 2.0 * PI / CellsAround;
    float lineScale = rsqrt(1.0 / (cellAlong * cellAlong) + 1.0 / (cellAround * cellAround));
    float struts = length(float2(shell, min(lines.x, lines.y) * lineScale)) - StrutRadius - widen;
    float nodes = length(float2(shell, length(lines) * lineScale)) - nodeRadius;
    surface = float4(t, around, nodes < struts ? 1.0 : 0.0, lines.x < lines.y ? u - v : u + v);
    return min(struts, nodes) * 0.95;
}

float knotDE(float3 pos, float widen, out float4 surface)
{
    surface = float4(0.0, 0.0, 0.0, 0.0);
    // Far from the knot, the distance to its bounding torus is good enough.
    float hull = length(float2(length(pos.xz) - KnotRing, pos.y)) - (KnotSwing + TubeRadius + 0.05);
    if (hull > 0.4)
    {
        return hull;
    }
    float t = 0.0;
    float4 angles = 0.0;
    float rough = knotCrossing(pos, t, angles);
    // Deep inside the tube the rough distance is accurate enough.
    if (rough < TubeRadius - 0.2)
    {
        return (TubeRadius - rough) * 0.85 - StrutRadius * 2.5 - widen * 1.5;
    }
    return knotMesh(pos, t, angles, widen, surface);
}

float3 Render(float2 uv, float2 p)
{
    float ride = Time * 0.05 + (BeatCount - Beat) * 0.008;
    float3 eye = knotPoint(ride);
    float3 dir = cameraRay(eye, knotPoint(ride + 0.06), p, 0.85 - Beat * 0.07, sin(Time * 0.11) * 0.6);

    const float farPlane = 14.0;
    int maxSteps = qualitySteps(90);
    float pixelAngle = 0.7 / (Resolution.y * 0.85);
    float travelled = 0.0;
    float4 surface = 0.0;
    float haloSum = 0.0;
    float haloAround = 0.0;
    bool landed = false;
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float d = knotDE(eye + dir * travelled, travelled * pixelAngle, surface);
        float h = exp(-d * 70.0) * exp(-travelled * 0.15);
        haloSum += h;
        haloAround += h * abs(surface.y);
        if (d < 0.002 * travelled)
        {
            landed = true;
            break;
        }
        travelled += d;
        if (travelled > farPlane)
        {
            break;
        }
    }

    float flow = Time * 0.04;
    // Deep space beyond the knot.
    float3 space = hsv2rgb(0.7 + flow + dir.y * 0.15, 0.8, 0.015 + 0.05 * noise(dir.xy * 3.0 + dir.z * 2.0) * noise(dir.zy * 5.0 - 2.0));
    float3 colour = space;
    if (landed)
    {
        float3 hit = eye + dir * travelled;
        float4 s2 = 0.0;
        float e1 = 0.0004 + 0.0008 * travelled;
        float2 e = float2(e1, -e1);
        float widen = travelled * pixelAngle;
        float t0 = 0.0;
        float4 angles = 0.0;
        float rough = knotCrossing(hit, t0, angles);
        float3 normal = normalize(e.xyy * knotMesh(hit + e.xyy, t0, angles, widen, s2) + e.yyx * knotMesh(hit + e.yyx, t0, angles, widen, s2)
                                + e.yxy * knotMesh(hit + e.yxy, t0, angles, widen, s2) + e.xxx * knotMesh(hit + e.xxx, t0, angles, widen, s2));

        // Around the tube the struts carry the spectrum: bass on the outside of each loop,
        // treble on the inside. Colour also drifts along the knot.
        float band = abs(surface.y) / PI;
        float energy = spectrum(band);
        float hue = band * 0.45 + surface.x / PI - flow;
        float3 tint = hsv2rgb(hue, 0.85, 1.0);

        float3 toLamp = normalize(eye - hit + float3(0.0, -0.2, 0.0));
        float diffuse = saturate(dot(normal, toLamp));
        float facing = saturate(dot(normal, -dir));
        float specular = pow(saturate(dot(reflect(dir, normal), toLamp)), 24.0);

        // Each strut is a neon tube: a hot core and a coloured rim, with pulses of light
        // streaming along it in the direction we travel.
        float dashes = glow(abs(frac(surface.w * 0.5 - Time * 0.9) - 0.5), 7.0);
        float core = facing * facing;
        float3 neonTube = tint * (0.12 + energy * 1.3) * (0.6 + Bass * 0.5) * (0.4 + core) * (0.35 + dashes * 1.3)
                        + lerp(tint, 1.0, 0.6) * core * dashes * (0.2 + energy * 0.6);
        float3 nodeGlow = lerp(hsv2rgb(hue + 0.5, 0.7, 1.0), 1.0, core * 0.5) * (0.4 + energy * 1.5 + Beat * 0.6) * (0.3 + core);

        // After each beat a ring of light races ahead along the tube.
        float ahead = (wrap(surface.x - ride + PI, 2.0 * PI) - PI) * 11.0;
        float pulse = glow(ahead - (1.0 - Beat) * 9.0, 2.5) * Beat;

        colour = lerp(neonTube, nodeGlow, surface.z) + tint * diffuse * 0.08 + specular * (0.3 + Treble * 1.2)
               + lerp(tint, 1.0, 0.5) * pulse * 1.5;
        colour = lerp(space, colour, exp(-travelled * 0.12));
    }

    // Neon haze around every strut.
    float haloBand = haloAround / max(haloSum, 1e-4) / PI;
    colour += hsv2rgb(haloBand * 0.45 - flow, 0.85, 1.0) * haloSum * 0.004 * (0.5 + Bass);
    return colour + previous(uv) * 0.1;
}
