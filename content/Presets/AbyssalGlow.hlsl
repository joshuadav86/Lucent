// Name: Abyssal Glow
// Sink slowly into the deep ocean through drifting marine snow, past radiolarians: plankton whose glass skeletons are
// neon lattice shells bristling with spines, turning slowly in the dark. Each face of a shell glows with its own band of the
// spectrum, the bass stretches the spines, every beat sends light racing out to their gold tips, and waves of plankton
// flash blue-green around them.
// Family: Particles
// Tags: Calm, Dark, Water, Geometric, Neon, 3D
// Added: 2026-10-17 01:51
// Particles: 262144

// How the particles are shared out: marine snow, then bioluminescent plankton, then radiolarians.
static const float SnowShare = 0.06;
static const float PlanktonShare = 0.6;
static const int Radiolarians = 5;
// Snow and plankton fill a box that travels with the camera, wrapping around as we sink.
static const float3 BoxSize = float3(10.0, 7.0, 9.0);

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

// We sink slowly (world y grows downward, as on screen), drifting a little from side to side.
float3 cameraAt(float t)
{
    return float3(sin(t * 0.031) * 1.2, t * 0.12, sin(t * 0.017) * 0.8);
}

// Where something drifting in the box appears, kept in the box in front of the camera.
float3 inBox(float3 position)
{
    float3 middle = cameraAt(Time) + float3(0.0, 0.0, BoxSize.z * 0.5);
    float3 relative = position - middle;
    return middle + relative - BoxSize * floor(relative / BoxSize + 0.5);
}

// A burst of plankton light spreads out from a new spot on every other beat.
float3 waveOrigin(float index, float startTime)
{
    uint seed = (uint)index;
    float3 pick = float3(pcgHash(seed * 3u + 1u), pcgHash(seed * 3u + 2u), pcgHash(seed * 3u + 3u)) / 4294967295.0;
    return cameraAt(startTime) + float3((pick.x - 0.5) * 4.0, (pick.y - 0.5) * 2.0 + 0.4, 1.5 + pick.z * 3.0);
}

void Spawn(inout Particle particle, uint id)
{
    float3 corner = cameraAt(Time) - float3(BoxSize.x * 0.5, BoxSize.y * 0.5, 0.0);
    particle.position = corner + float3(random(id, 1.0), random(id, 2.0), random(id, 3.0)) * BoxSize;
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = float4(floor(BeatCount * 0.5), Time - 10.0, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    float share = shareOf(id);
    if (share >= SnowShare + PlanktonShare)
    {
        return;
    }

    // Everything drifts on a slow, curling current; marine snow also sinks.
    float dt = min(DeltaTime, 0.05);
    float3 current = curlNoise(particle.position * 0.35 + float3(0.0, 0.0, Time * 0.03)) * 0.025;
    float sink = share < SnowShare ? 0.03 + fixedRandom(id, 4.0) * 0.04 : 0.0;
    particle.velocity = current + float3(0.0, sink, 0.0);
    particle.position = inBox(particle.position + particle.velocity * dt);

    if (share >= SnowShare)
    {
        // Plankton: note each new wave, light up as its front sweeps past, then slowly fade.
        float index = floor(BeatCount * 0.5);
        if (index != particle.data.x)
        {
            particle.data.x = index;
            particle.data.y = Time;
            particle.data.w = 0.6 + Bass * 0.8;
        }
        float radius = (Time - particle.data.y) * 1.8;
        float distance = length(particle.position - waveOrigin(particle.data.x, particle.data.y));
        float front = 1.0 - saturate(abs(distance - radius) / 0.14);
        particle.data.z = max(particle.data.z * exp(-dt * 2.6), front * particle.data.w * exp(-radius * 0.25));
    }
}

// The radiolarians: plankton with glass skeletons, each a lattice shell round a smaller inner shell, with
// long spines through both. Their shapes come from the icosahedron: 12 corners, 20 faces.
static const float Golden = 1.618034;
static const float3 IcoCorners[12] =
{
    float3(-1.0, Golden, 0.0), float3(1.0, Golden, 0.0), float3(-1.0, -Golden, 0.0), float3(1.0, -Golden, 0.0),
    float3(0.0, -1.0, Golden), float3(0.0, 1.0, Golden), float3(0.0, -1.0, -Golden), float3(0.0, 1.0, -Golden),
    float3(Golden, 0.0, -1.0), float3(Golden, 0.0, 1.0), float3(-Golden, 0.0, -1.0), float3(-Golden, 0.0, 1.0)
};
static const uint3 IcoFaces[20] =
{
    uint3(0, 11, 5), uint3(0, 5, 1), uint3(0, 1, 7), uint3(0, 7, 10), uint3(0, 10, 11),
    uint3(1, 5, 9), uint3(5, 11, 4), uint3(11, 10, 2), uint3(10, 7, 6), uint3(7, 1, 8),
    uint3(3, 9, 4), uint3(3, 4, 2), uint3(3, 2, 6), uint3(3, 6, 8), uint3(3, 8, 9),
    uint3(4, 9, 5), uint3(2, 4, 11), uint3(6, 2, 10), uint3(8, 6, 7), uint3(9, 8, 1)
};

float3 icoCorner(uint index)
{
    return normalize(IcoCorners[index]);
}

// A point t of the way along the great-circle arc from a to b (both on the unit sphere).
float3 arcPoint(float3 a, float3 b, float t)
{
    return normalize(lerp(a, b, t));
}

// One radiolarian: where it is, how big, its colour, and how it's turned.
void radState(int index, out float3 centre, out float size, out float hue, out float2 turn)
{
    float salt = index * 2.17 + 0.4;
    float order[5] = { 0.1, 0.7, 0.3, 0.9, 0.5 };
    float3 home = float3((order[index] - 0.5) * 6.5, (frac(salt * 5.3) - 0.5) * 1.6 + 0.2, 2.4 + frac(salt * 7.9) * 3.0);
    float3 wander = float3(sin(Time * 0.05 + salt * 4.0) * 0.6, sin(Time * 0.07 + salt * 2.0) * 0.35, sin(Time * 0.04 + salt) * 0.5);
    centre = cameraAt(Time) + home + wander;
    size = 0.3 + frac(salt * 9.1) * 0.12;
    // Glassy teal, ice blue, and violet, all with gold spine tips.
    float hues[5] = { 0.48, 0.56, 0.72, 0.5, 0.62 };
    hue = hues[index];
    // Each turns slowly on two axes, its own way.
    float way = index % 2 == 0 ? 1.0 : -1.0;
    turn = float2(Time * (0.13 + frac(salt * 3.7) * 0.08) * way + salt, sin(Time * 0.06 + salt) * 0.8 + salt);
}

float3 radTurn(float3 v, float2 turn)
{
    v.xz = rotate(v.xz, turn.x);
    v.yz = rotate(v.yz, turn.y);
    return v;
}

// A point on a radiolarian (in units of its shell's radius), how brightly it glows, how gold it is,
// and which band of the spectrum it plays.
float3 radPoint(uint id, uint k, uint n, int index, out float glowAmount, out float gold, out float band)
{
    float u = (k + 0.5) / max((float)n, 1.0);
    float t = fixedRandom(id, 5.0);
    // Two kinds: even ones have a honeycomb shell and twelve spines from its pentagons; odd ones a shell of
    // round pores and twenty spines between them.
    bool geodesic = index % 2 == 0;
    float ring = SongMapped > 0.0 ? 0.4 + Beat * 0.6 : Beat;
    gold = 0.0;

    if (u < 0.56)
    {
        float3 shell;
        if (geodesic)
        {
            // A honeycomb of hexagons round twelve pentagons. Each face of the icosahedron splits into four
            // triangles at its edge midpoints; the honeycomb joins their centres. Inside the face the middle
            // triangle's centre joins the three corner ones, and each corner triangle reaches out to the face's
            // edge, where the next face carries the line on (the edge is a mirror, so the two halves meet).
            uint pick = k % 180u;
            uint face = pick / 9u;
            uint arc = pick % 9u;
            uint3 f = IcoFaces[face];
            float3 a = icoCorner(f.x);
            float3 b = icoCorner(f.y);
            float3 c = icoCorner(f.z);
            float3 ab = normalize(a + b);
            float3 bc = normalize(b + c);
            float3 ca = normalize(c + a);
            float3 middle = normalize(ab + bc + ca);
            float3 nearA = normalize(a + ab + ca);
            float3 nearB = normalize(b + bc + ab);
            float3 nearC = normalize(c + ca + bc);
            float3 from = arc < 3 ? middle : (arc < 5 ? nearA : (arc < 7 ? nearB : nearC));
            float3 to;
            if (arc < 3)
            {
                to = arc == 0 ? nearA : (arc == 1 ? nearB : nearC);
            }
            else
            {
                // Out to the edge: straight across the great circle that edge lies on.
                float3 e0 = arc == 3 ? a : (arc == 4 ? c : (arc == 5 ? b : (arc == 6 ? a : (arc == 7 ? c : b))));
                float3 e1 = arc == 3 ? b : (arc == 4 ? a : (arc == 5 ? c : (arc == 6 ? b : (arc == 7 ? a : c))));
                float3 across = normalize(cross(e0, e1));
                to = normalize(from - across * dot(from, across));
            }
            shell = arcPoint(from, to, t);
            band = spectrum(0.03 + face / 20.0 * 0.55);
            glowAmount = 0.35 + band * 1.6 + ring * 0.5;
        }
        else
        {
            // Round pores: a ring round each of the twelve corners and the thirty edge midpoints. (Each midpoint
            // ring is drawn by both faces beside it, so it gets half the light from each.)
            uint pore = k % 72u;
            float3 centre;
            float radius;
            if (pore < 12u)
            {
                centre = icoCorner(pore);
                radius = 0.17;
                band = spectrum(0.03 + pore / 12.0 * 0.55);
                glowAmount = 1.0;
            }
            else
            {
                uint3 f = IcoFaces[(pore - 12u) / 3u];
                uint side = (pore - 12u) % 3u;
                centre = normalize(icoCorner(side == 0 ? f.x : (side == 1 ? f.y : f.z)) + icoCorner(side == 0 ? f.y : (side == 1 ? f.z : f.x)));
                radius = 0.21;
                band = spectrum(0.03 + (pore - 12u) / 60.0 * 0.55);
                glowAmount = 0.5;
            }
            float3 side1 = normalize(cross(centre, abs(centre.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0)));
            float3 side2 = cross(centre, side1);
            float angle = t * 2.0 * PI;
            shell = normalize(centre + (side1 * cos(angle) + side2 * sin(angle)) * radius);
            glowAmount *= 1.4 * (0.35 + band * 1.6 + ring * 0.5);
        }
        // The shell swells a little on the beat and where its band is loud.
        return shell * (1.0 + Beat * 0.05 + band * 0.06);
    }
    if (u < 0.74)
    {
        // The inner shell: a plain icosahedron, turning against the outer one and breathing with the mids.
        uint pick = k % 60u;
        uint3 f = IcoFaces[pick / 3u];
        uint side = pick % 3u;
        float3 from = icoCorner(side == 0 ? f.x : (side == 1 ? f.y : f.z));
        float3 to = icoCorner(side == 0 ? f.y : (side == 1 ? f.z : f.x));
        float3 inner = arcPoint(from, to, t);
        inner.xz = rotate(inner.xz, -Time * 0.35);
        band = Mid;
        glowAmount = 0.5 * (0.5 + Mid * 1.2);
        return inner * (0.45 + Mid * 0.06);
    }
    if (u < 0.97)
    {
        // The spines, from the inner shell out past the lattice. The bass stretches them, and each beat sends
        // a bead of light racing out to the tips, which turn gold.
        uint spines = geodesic ? 12u : 20u;
        uint spine = k % spines;
        float3 direction;
        if (geodesic)
        {
            direction = icoCorner(spine);
        }
        else
        {
            uint3 f = IcoFaces[spine];
            direction = normalize(icoCorner(f.x) + icoCorner(f.y) + icoCorner(f.z));
        }
        float reach = (geodesic ? 2.3 : 1.9) + Bass * 0.5 + DropPulse * 0.6 + BuildUp * 0.25;
        float along = sqrt(t);
        float bead = along - BeatPhase * 1.1;
        band = spectrum(0.05 + spine / (float)spines * 0.5);
        glowAmount = (0.35 + along * 0.5 + band * 0.6) * (1.0 + exp(-bead * bead * 120.0) * ring * 3.0);
        gold = along * along;
        return direction * lerp(0.45, reach, along);
    }
    // A small bright heart where the spines meet.
    band = Bass;
    glowAmount = 1.2 + Bass * 1.5;
    gold = 0.4;
    return randomInSphere(id, 6.0) * 0.12;
}

float3 cameraTarget(float3 eye)
{
    return eye + float3(sin(Time * 0.05) * 0.2, 0.06 + sin(Time * 0.04) * 0.06, 1.0);
}

Sprite Show(Particle particle, uint id)
{
    float share = shareOf(id);
    float3 eye = cameraAt(Time);
    float3 target = cameraTarget(eye);
    float zoom = 1.5;
    Sprite sprite = (Sprite)0;

    if (share < SnowShare + PlanktonShare)
    {
        float3 seen = project(particle.position, eye, target, zoom, 0.0);
        // Fade out with distance into the murk, and near the box's far wall.
        float murk = exp(-seen.z * 0.3) * smoothstep(BoxSize.z, BoxSize.z * 0.75, seen.z) * smoothstep(0.15, 0.6, seen.z);
        sprite.position = seen.xy;
        if (share < SnowShare)
        {
            // Marine snow: faint flecks catching what little light there is.
            sprite.size = 0.8 + pow(fixedRandom(id, 11.0), 3.0) * 1.6;
            sprite.colour = float3(0.55, 0.7, 0.85) * 0.6 * murk * (1.0 + Treble * 0.8);
        }
        else
        {
            // Plankton: almost invisible until a wave sets it glowing blue-green.
            float glowAmount = particle.data.z;
            float3 colour = lerp(float3(0.1, 0.5, 1.0), float3(0.2, 1.0, 0.75), fixedRandom(id, 12.0));
            sprite.size = 1.5;
            sprite.colour = colour * (0.003 + glowAmount * 3.5 * (0.5 + random(id, 13.0))) * murk;
        }
        focus(sprite, seen.z, 3.5, 0.05);
        return sprite;
    }

    // A radiolarian.
    float radShare = (1.0 - SnowShare - PlanktonShare) / Radiolarians;
    float local = (share - SnowShare - PlanktonShare) / radShare;
    int index = min((int)local, Radiolarians - 1);
    uint count = (uint)(radShare * ParticleCount);
    uint k = (uint)(frac(local) * count);

    float3 centre;
    float size;
    float hue;
    float2 turn;
    radState(index, centre, size, hue, turn);
    float glowAmount;
    float gold;
    float band;
    float3 offset = radTurn(radPoint(id, k, count, index, glowAmount, gold, band), turn) * size;

    float3 seen = project(centre + offset, eye, target, zoom, 0.0);
    float murk = exp(-seen.z * 0.18);
    float3 colour = lerp(hsv2rgb(hue, 0.8, 1.0), float3(1.0, 0.72, 0.3), gold);
    // A few sparks along the glass catch the treble.
    colour *= 1.0 + step(0.995, fixedRandom(id, 7.0)) * Treble * 5.0;
    sprite.position = seen.xy;
    sprite.size = seen.z > 0.2 ? 0.9 : 0.0;
    sprite.colour = colour * glowAmount * murk * 0.13 * (0.8 + Intensity * 0.4) * (0.32 / size);
    focus(sprite, seen.z, 3.5, 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The faintest blue from far above, fading to black below.
    float3 water = float3(0.0, 0.012, 0.03) * saturate(0.6 - p.y * 0.5);
    return water + particles(uv) + previous(uv) * 0.5;
}
