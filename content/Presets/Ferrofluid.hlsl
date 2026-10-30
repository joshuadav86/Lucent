// Name: Ferrofluid
// A pool of glossy black magnetic liquid in a chrome dish rises into a crown of smooth, needle-sharp spikes set in perfect rings: they swell and pulse with the bass, each ring follows its own band of the spectrum, every beat sends a wave rolling out through them, and their mirror-smooth sides catch neon studio lights in clean lines of colour.
// Family: Ray traced
// Tags: Dark, 3D, Geometric
// Added: 2026-10-17 17:06

static const float DishRadius = 1.9;
static const float Spacing = 0.25;      // between neighbouring rings of spikes, and between spikes round a ring
static const float SpikeBase = 0.135;  // radius of each spike where it meets the pool
static const float LastRing = 6.0;     // rings past this lie flat near the rim

// How strongly the magnet under the dish pulls right now: the bass, with a kick on each beat.
float pull()
{
    return 0.35 + Bass * 0.8 + Beat * 0.25 + DropPulse * 0.2;
}

// The gentle hump of liquid gathered over the magnet.
float moundHeight(float2 xz)
{
    float r2 = dot(xz, xz);
    return (0.12 + pull() * 0.2) * exp(-r2 / 1.1) - 0.02;
}

// How tall the spikes of one ring stand: tallest in the middle where the field is strongest, each ring
// outwards following a higher band of the spectrum, with a wave rolling out from the centre on each beat.
float ringHeight(float ring)
{
    float r = ring * Spacing;
    float field = exp(-r * r / (0.55 + pull() * 0.5));
    float band = spectrum(0.03 + ring * 0.07);
    float wave = Beat * exp(-pow((r - (1.0 - Beat) * 1.8) / 0.22, 2.0)) * 0.14;
    float h = field * (0.18 + pull() * 0.5 + band * 0.32) + wave;
    return ring > LastRing ? 0.0 : max(h - 0.04, 0.0);
}

// The centre of the nearest spike in one ring. Ring k holds 6k spikes, like a honeycomb bent into circles,
// and each ring turns slowly, the inner ones faster, so the crown swirls like liquid round the magnet.
float2 ringSpike(float2 xz, float ring)
{
    if (ring < 0.5)
    {
        return float2(0.0, 0.0);
    }
    float count = ring * 6.0;
    float turn = Time * 0.06 / (1.0 + ring * 0.35) + (fmod(ring, 2.0) * 0.5) / count * 2.0 * PI;
    float angle = atan2(xz.y, xz.x) - turn;
    float cell = round(angle / (2.0 * PI) * count) / count * 2.0 * PI + turn;
    return float2(cos(cell), sin(cell)) * ring * Spacing;
}

// Distance to an upright cone with its tip at the origin, base radius w at depth h below.
float coneDistance(float3 p, float w, float h)
{
    float2 q = float2(w, -h);
    float2 v = float2(length(p.xz), p.y);
    float2 a = v - q * saturate(dot(v, q) / dot(q, q));
    float2 b = v - q * float2(saturate(v.x / q.x), 1.0);
    float d = min(dot(a, a), dot(b, b));
    float s = max(-(v.x * q.y - v.y * q.x), -(v.y - q.y));
    return sqrt(d) * sign(s);
}

float smoothUnion(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// One spike: a slender needle rising out of a wider, lower cone, melted together so the sides curve in
// the way real ferrofluid spikes do, with the tip just rounded.
float spikeDistance(float3 p, float2 centre, float ring)
{
    float height = ringHeight(ring);
    if (height < 0.003)
    {
        return 1e3;
    }
    float3 local = p - float3(centre.x, moundHeight(centre) + height, centre.y);
    // Tall spikes lean slightly outwards, away from the magnet.
    local.xz += centre * 0.1 * (local.y + height);
    float width = SpikeBase * saturate(height * 5.0 + 0.35);
    float needle = coneDistance(local, width * 0.5, height + 0.02);
    float3 skirtLocal = local + float3(0.0, height * 0.6, 0.0);
    float skirt = coneDistance(skirtLocal, width, height * 0.4 + 0.02);
    return smoothUnion(needle, skirt, 0.035) - 0.002;
}

// The liquid's surface: the mound, with the spikes of the two nearest rings blended smoothly out of it.
float fluidDistance(float3 p)
{
    float surface = (p.y - moundHeight(p.xz)) * 0.85;
    float ringPosition = length(p.xz) / Spacing;
    float inner = floor(ringPosition);
    float outer = inner + 1.0;
    float spikes = min(spikeDistance(p, ringSpike(p.xz, inner), inner),
                       spikeDistance(p, ringSpike(p.xz, outer), outer));
    return smoothUnion(surface, spikes, 0.05);
}

float3 fluidNormal(float3 p)
{
    const float2 e = float2(0.0012, 0.0);
    return normalize(float3(fluidDistance(p + e.xyy) - fluidDistance(p - e.xyy),
                            fluidDistance(p + e.yxy) - fluidDistance(p - e.yxy),
                            fluidDistance(p + e.yyx) - fluidDistance(p - e.yyx)));
}

// How much of the studio a point on the liquid can see: the valleys between spikes are shaded by them.
float fluidOcclusion(float3 p, float3 normal)
{
    float shade = 0.0;
    [unroll]
    for (int i = 1; i <= 3; i++)
    {
        float h = 0.03 * i;
        shade += (h - fluidDistance(p + normal * h)) / h / i;
    }
    return saturate(1.0 - shade * 0.7);
}

// ---- The studio ----

// The studio's two neon colours: a pair of complementary hues that changes every eight beats.
float3 accent(int i)
{
    float hue = floor(BeatCount / 8.0) * 0.17 + Time * 0.008;
    return i == 0 ? hsv2rgb(hue + 0.55, 0.8, 1.0) : hsv2rgb(hue + 0.97, 0.75, 1.0);
}

// A soft-edged glowing panel seen in a direction: smooth falloff, so reflections stay clean.
float panel(float3 direction, float3 axis, float2 size, float softness)
{
    axis = normalize(axis);
    float3 side = normalize(cross(axis, float3(0.0, 1.0, 0.01)));
    float3 up = cross(side, axis);
    float facing = dot(direction, axis);
    if (facing <= 0.0)
    {
        return 0.0;
    }
    float2 local = float2(dot(direction, side), dot(direction, up)) / facing;
    float2 edge = max(abs(local) - size, 0.0);
    return exp(-dot(edge, edge) / (softness * softness));
}

// Which way round the camera is; the studio turns with it.
static float CameraTurn = 0.0;

// What a mirror sees in a direction: a dark studio lit by a big white softbox overhead, two tall neon strips
// behind on either side to outline the spikes, a soft white card in front for a highlight down each spike's
// face and a faint ring of colour round the walls so every edge catches a little light.
float3 studio(float3 direction)
{
    direction.xz = rotate(direction.xz, CameraTurn);
    float3 colour = float3(0.006, 0.007, 0.01) * (0.6 + 0.4 * saturate(direction.y + 0.5));
    colour += float3(0.14, 0.15, 0.18) * pow(saturate(direction.y), 4.0);
    colour += lerp(accent(0), accent(1), 0.5 + 0.5 * direction.x) * 0.05 * exp(-pow((direction.y - 0.12) / 0.12, 2.0));
    colour += float3(1.0, 0.98, 0.95) * 3.2 * panel(direction, float3(0.0, 1.0, 0.25), float2(0.35, 0.2), 0.12);
    colour += accent(0) * (6.0 + Mid * 6.0) * panel(direction, float3(-0.8, 0.3, -0.6), float2(0.08, 0.7), 0.06);
    colour += accent(1) * (6.0 + Treble * 7.0) * panel(direction, float3(0.8, 0.3, -0.6), float2(0.08, 0.7), 0.06);
    // A long low glow behind the dish gives the flat pool a soft sheen towards the far rim.
    colour += lerp(float3(0.7, 0.75, 0.85), lerp(accent(0), accent(1), 0.5), 0.4) * 0.5 * panel(direction, float3(0.0, 0.12, -1.0), float2(0.8, 0.02), 0.2);
    colour += float3(0.9, 0.92, 1.0) * (0.9 + Level * 0.6) * panel(direction, float3(0.0, 0.3, 1.0), float2(0.22, 0.3), 0.1);
    return colour;
}

// What the camera sees past the dish: a smooth dark backdrop, a touch lighter low down, with a faint wash of
// the two neon colours behind the dish.
float3 backdrop(float3 direction)
{
    float3 turned = direction;
    turned.xz = rotate(turned.xz, CameraTurn);
    float3 colour = float3(0.004, 0.005, 0.009) + float3(0.008, 0.009, 0.014) * exp(-abs(direction.y) * 4.0);
    float3 wash = lerp(accent(0), accent(1), 0.5 + 0.5 * turned.x);
    return colour + wash * 0.012 * exp(-direction.y * direction.y * 12.0) * (0.7 + Level * 0.6);
}

// Glossy reflection of the studio from a surface, with Fresnel: little head-on, almost a mirror at a glance.
float3 gloss(float3 direction, float3 normal, float baseReflect)
{
    float3 mirrored = reflect(direction, normal);
    float fresnel = baseReflect + (1.0 - baseReflect) * pow(1.0 - saturate(dot(normal, -direction)), 5.0);
    return studio(mirrored) * fresnel;
}

float3 Render(float2 uv, float2 p)
{
    float orbit = Time * 0.045;
    CameraTurn = orbit;
    float height = 0.9 + sin(Time * 0.07) * 0.3;
    float3 eye = float3(sin(orbit) * 2.9, height, cos(orbit) * 2.9);
    float3 target = float3(0.0, 0.15, 0.0);
    const float zoom = 2.5;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.0);

    float3 colour = backdrop(direction);

    // The floor: dark and faintly glossy, melting into the backdrop with distance so there is no horizon line.
    float floorDistance = 1e4;
    if (direction.y < 0.0)
    {
        floorDistance = (-0.08 - eye.y) / direction.y;
        float3 spot = eye + direction * floorDistance;
        float fade = exp(-length(spot.xz) * 0.25);
        float pool = exp(-dot(spot.xz, spot.xz) * 0.15);
        float3 surface = float3(0.004, 0.004, 0.006) + float3(0.03, 0.03, 0.035) * pool
                       + gloss(direction, float3(0.0, 1.0, 0.0), 0.02) * 0.1;
        colour = lerp(colour, surface, fade);
    }

    // The chrome dish: a ring round the pool.
    float ringDistance = 1e4;
    {
        float t = 0.0;
        [loop]
        for (int i = 0; i < 48; i++)
        {
            float3 q = eye + direction * t;
            float2 ring = float2(length(q.xz) - DishRadius, q.y);
            float d = length(ring) - 0.075;
            if (d < 0.0005)
            {
                ringDistance = t;
                break;
            }
            t += d;
            if (t > 12.0)
            {
                break;
            }
        }
    }

    // The ferrofluid. Only march where the ray passes over the pool.
    float fluidHit = -1.0;
    if (direction.y < 0.0)
    {
        float t = max((1.0 - eye.y) / direction.y, 0.0);
        int steps = qualitySteps(120);
        float closeEnough = 0.0006 * surfacePrecision();
        [loop]
        for (int i = 0; i < steps; i++)
        {
            float3 q = eye + direction * t;
            if (q.y < -0.1 || t > 12.0)
            {
                break;
            }
            float d = fluidDistance(q);
            if (d < closeEnough * t)
            {
                fluidHit = t;
                break;
            }
            t += d * 0.7;
        }
    }

    if (fluidHit > 0.0)
    {
        float3 position = eye + direction * fluidHit;
        if (length(position.xz) < DishRadius && fluidHit < ringDistance)
        {
            float3 normal = fluidNormal(position);
            // Black and glassy: almost no colour of its own, everything is reflection.
            float occlusion = fluidOcclusion(position, normal);
            float3 reflection = gloss(direction, normal, 0.05);
            // A faint cool sheen from the oily surface, strongest on the slopes.
            float sheen = pow(1.0 - abs(normal.y), 2.0);
            colour = float3(0.003, 0.003, 0.004) + reflection * (0.25 + 0.75 * occlusion)
                   + lerp(accent(0), accent(1), 0.5) * 0.004 * sheen;
            ringDistance = 1e4;
        }
    }

    if (ringDistance < 1e3 && ringDistance < floorDistance + 1.0)
    {
        float3 q = eye + direction * ringDistance;
        float2 radial = normalize(q.xz);
        float2 ring = float2(length(q.xz) - DishRadius, q.y);
        float3 normal = normalize(float3(radial.x * ring.x, ring.y, radial.y * ring.x));
        colour = gloss(direction, normal, 0.65) * float3(0.86, 0.87, 0.9);
    }

    return colour;
}
