// Name: Ferrofluid
// A pool of black magnetic liquid in a steel dish rises into a crown of glossy spikes that grow and sink with the music, its mirror-smooth surface reflecting the coloured studio lights.
// Family: Ray traced
// Tags: Dark, 3D
// Added: 2026-10-17 17:06

static const float DishRadius = 1.9;
static const float Spacing = 0.22;     // between neighbouring spikes
static const float SpikeBase = 0.115;  // radius of each spike where it meets the pool

// How strongly the magnet under the dish pulls right now: the bass, with a kick on each beat.
float pull()
{
    return 0.35 + Bass * 0.75 + Beat * 0.25;
}

// The gentle hump of liquid gathered over the magnet.
float moundHeight(float2 xz)
{
    float r2 = dot(xz, xz);
    return (0.12 + pull() * 0.22) * exp(-r2 / 1.1) - 0.02;
}

// How tall the spike standing at this spot is: tallest in the middle where the field is strongest;
// each ring outwards follows a higher band of the spectrum.
float spikeHeight(float2 centre)
{
    float r = length(centre);
    float field = exp(-r * r / (0.45 + pull() * 0.45));
    float band = spectrum(0.04 + r * 0.32);
    float wobble = 0.85 + 0.15 * sin(Time * 2.0 + dot(centre, float2(7.1, 4.3)));
    return max(field * (0.1 + pull() * 0.3 + band * 0.3) * wobble - 0.05, 0.0);
}

// Nearest spike centre on a honeycomb grid, as the spikes of real ferrofluid pack.
float2 nearestSpike(float2 xz)
{
    float2 cellSize = float2(1.0, 1.7320508) * Spacing;
    float2 a = xz - cellSize * floor(xz / cellSize) - cellSize * 0.5;
    float2 shifted = xz - cellSize * 0.5;
    float2 b = shifted - cellSize * floor(shifted / cellSize) - cellSize * 0.5;
    return xz - (dot(a, a) < dot(b, b) ? a : b);
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

// The liquid's surface: the mound, with spikes blended smoothly out of it like real ferrofluid.
float fluidDistance(float3 p)
{
    float surface = (p.y - moundHeight(p.xz)) * 0.85;
    float2 centre = nearestSpike(p.xz);
    float height = spikeHeight(centre);
    float baseHeight = moundHeight(centre);
    // Tall spikes are a little narrower and lean slightly outwards.
    float3 local = p - float3(centre.x, baseHeight + height, centre.y);
    local.xz += centre * 0.12 * (local.y + height);
    if (height < 0.003)
    {
        return surface;
    }
    float cone = coneDistance(local, SpikeBase * saturate(height * 6.0 + 0.3), height + 0.02);
    return smoothUnion(surface, cone, 0.045);
}

float3 fluidNormal(float3 p)
{
    const float2 e = float2(0.0015, 0.0);
    return normalize(float3(fluidDistance(p + e.xyy) - fluidDistance(p - e.xyy),
                            fluidDistance(p + e.yxy) - fluidDistance(p - e.yxy),
                            fluidDistance(p + e.yyx) - fluidDistance(p - e.yyx)));
}

// ---- The studio ----

// The softbox colours change every eight beats; the mids and treble brighten two of them.
float3 lightColour(int i)
{
    float hue = floor(BeatCount / 8.0) * 0.17 + Time * 0.01;
    if (i == 0)
    {
        return float3(1.0, 0.97, 0.92) * 3.0;
    }
    if (i == 1)
    {
        return hsv2rgb(hue + 0.58, 0.8, 1.0) * (5.0 + Mid * 5.0);
    }
    if (i == 2)
    {
        return hsv2rgb(hue + 0.95, 0.75, 1.0) * (5.0 + Treble * 6.0);
    }
    return hsv2rgb(hue + 0.12, 0.8, 1.0) * (3.0 + Bass * 3.0);
}

// The lights stand round the dish relative to the camera, like a product photographer's: a softbox
// overhead, two tall coloured strips behind on either side to outline the spikes, and one low behind.
static const float3 LightDirections[4] =
{
    float3(0.0, 1.0, 0.35),
    float3(-0.75, 0.4, -0.65),
    float3(0.8, 0.35, -0.55),
    float3(0.0, 0.2, -1.0)
};
static const float2 LightSizes[4] = { float2(0.3, 0.12), float2(0.07, 0.7), float2(0.07, 0.7), float2(1.2, 0.04) };

// Which way round the camera is; the studio turns with it.
static float CameraTurn = 0.0;

// What a mirror sees in a direction: dark studio, with soft-edged glowing panels.
float3 studio(float3 direction)
{
    direction.xz = rotate(direction.xz, CameraTurn);
    float3 colour = float3(0.008, 0.008, 0.011) * (0.6 + 0.4 * saturate(direction.y + 0.5));
    // A faint pale band round the walls gives the edges of the spikes something to catch.
    colour += float3(0.02, 0.02, 0.026) * exp(-abs(direction.y - 0.15) * 6.0);
    // The dim ceiling, brightest straight up, gives the black liquid a soft sheen.
    colour += float3(0.16, 0.17, 0.2) * pow(saturate(direction.y), 3.0);
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float3 axis = normalize(LightDirections[i]);
        float3 side = normalize(cross(axis, float3(0.0, 1.0, 0.01)));
        float3 up = cross(side, axis);
        float facing = dot(direction, axis);
        if (facing > 0.0)
        {
            float2 local = float2(dot(direction, side), dot(direction, up)) / facing;
            float2 edge = abs(local) - LightSizes[i];
            float panel = smoothstep(0.015, -0.015, max(edge.x, edge.y));
            colour += lightColour(i) * panel * (0.8 + 0.2 * smoothstep(0.0, -0.2, max(edge.x, edge.y)));
            // A soft halo of spill round each panel.
            colour += lightColour(i) * 0.03 * exp(-max(max(edge.x, edge.y), 0.0) * 6.0);
        }
    }
    return colour;
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
    float height = 0.95 + sin(Time * 0.07) * 0.3;
    float3 eye = float3(sin(orbit) * 2.9, height, cos(orbit) * 2.9);
    float3 target = float3(0.0, 0.12, 0.0);
    const float zoom = 2.5;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.0);

    // Behind the dish the studio is dark; the lights only show softly, out of focus.
    float3 colour = studio(direction) * 0.04;

    // The table: a dark slate plane with a dim reflection.
    float tableDistance = 1e4;
    if (direction.y < 0.0)
    {
        tableDistance = (-0.08 - eye.y) / direction.y;
        float3 table = eye + direction * tableDistance;
        float fade = exp(-length(table.xz) * 0.12);
        // A soft pool of light from overhead on the table round the dish.
        float pool = exp(-dot(table.xz, table.xz) * 0.12);
        colour = (float3(0.01, 0.01, 0.012) + float3(0.05, 0.05, 0.055) * pool + gloss(direction, float3(0.0, 1.0, 0.0), 0.02) * 0.12) * fade
               + studio(direction) * 0.03 * (1.0 - fade);
    }

    // The steel dish: a ring round the pool, polished so it reflects the panels.
    float ringDistance = 1e4;
    {
        // March a little torus (ring of radius DishRadius, tube 0.09).
        float t = 0.0;
        [loop]
        for (int i = 0; i < 40; i++)
        {
            float3 q = eye + direction * t;
            float2 ring = float2(length(q.xz) - DishRadius, q.y);
            float d = length(ring) - 0.09;
            if (d < 0.001)
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
    float t0 = max((0.95 - eye.y) / direction.y, 0.0);
    if (direction.y < 0.0)
    {
        float t = t0;
        int steps = qualitySteps(110);
        float closeEnough = 0.0008 * surfacePrecision();
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
            t += d * 0.8;
        }
    }

    if (fluidHit > 0.0)
    {
        float3 position = eye + direction * fluidHit;
        if (length(position.xz) < DishRadius && fluidHit < ringDistance)
        {
            float3 normal = fluidNormal(position);
            // Black and glassy: almost no colour of its own, everything is reflection.
            // The valleys between spikes are darker, since the spikes shade each other.
            float valley = saturate((position.y - moundHeight(position.xz)) * 6.0 + 0.6);
            float3 reflection = gloss(direction, normal, 0.045);
            colour = float3(0.004, 0.004, 0.005) + reflection * (0.35 + 0.65 * valley);
            ringDistance = 1e4;
        }
    }

    if (ringDistance < 1e3 && ringDistance < tableDistance + 1.0)
    {
        float3 q = eye + direction * ringDistance;
        float2 radial = normalize(q.xz);
        float2 ring = float2(length(q.xz) - DishRadius, q.y);
        float3 normal = normalize(float3(radial.x * ring.x, ring.y, radial.y * ring.x));
        colour = gloss(direction, normal, 0.6) * float3(0.85, 0.86, 0.9);
    }

    return colour;
}
