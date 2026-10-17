// Name: Solar Flare
// Glowing plasma streams along great magnetic arches above the boiling surface of the sun; each arch swells with its own part of the music, and on the beat one snaps in a blinding flare that hurls plasma out into space.
// Family: Particles
// Tags: Space, Fire, Intense
// Particles: 262144

static const float SunRadius = 10.0;
static const float3 SunCentre = float3(0.0, -10.0, 0.0);
static const int Loops = 11;
// Most particles flow along the loops; the rest drift off the surface as faint streamers.
static const float LoopShare = 0.82;
static const float Zoom = 1.55;

float3 cameraEye()
{
    float swing = sin(Time * 0.035) * 0.35;
    return float3(sin(swing) * 11.0, 1.4 + sin(Time * 0.05) * 0.4, -cos(swing) * 11.0);
}

float3 cameraTarget()
{
    return float3(sin(Time * 0.02) * 0.6, 1.2, 0.0);
}

// The loop that flares on the latest beat.
int flaringLoop()
{
    return (int)(pcgHash((uint)BeatCount * 7u + 3u) % (uint)Loops);
}

// One magnetic loop: where its middle stands on the surface, which way it spans, how wide and tall.
void loopShape(int index, out float3 base, out float3 across, out float3 up, out float halfWidth, out float height)
{
    float salt = index * 1.618 + 0.3;
    // Loops stand along the visible limb, spread side to side and a little front to back.
    float angle = ((index + 0.5) / Loops - 0.5) * 1.15 + sin(Time * 0.01 + salt) * 0.03;
    float depth = (frac(salt * 3.7) - 0.5) * 0.45;
    up = normalize(float3(sin(angle), cos(angle) * cos(depth), sin(depth) * 1.3 - 0.05));
    base = SunCentre + up * SunRadius;
    float turn = (frac(salt * 7.3) - 0.5) * 2.4 + Time * 0.008;
    float3 tangent = normalize(cross(up, float3(0.0, 0.0, 1.0)));
    float3 bitangent = cross(up, tangent);
    across = tangent * cos(turn) + bitangent * sin(turn);
    halfWidth = 0.7 + frac(salt * 5.1) * 1.4;
    // Each loop swells with its own band of the spectrum.
    float band = spectrum(0.05 + (index + 0.5) / Loops * 0.6);
    height = halfWidth * (1.0 + frac(salt * 9.3) * 1.2) * (0.8 + band * 0.55);
}

// A point s (0..1) of the way along a loop, leaning a little like real ones do.
float3 loopPoint(int index, float s)
{
    float3 base, across, up;
    float halfWidth, height;
    loopShape(index, base, across, up, halfWidth, height);
    float arc = PI * s;
    float3 lean = cross(up, across) * sin(arc) * height * 0.25 * sin(index * 2.3);
    return base + across * (-cos(arc) * halfWidth) + up * (sin(arc) * height - 0.05) + lean;
}

void Spawn(inout Particle particle, uint id)
{
    float share = (id + 0.5) / ParticleCount;
    particle.velocity = 0.0;
    particle.life = 1e6;
    if (share < LoopShare)
    {
        // data: how far along the loop, whether it's been flung free, how long until it falls back.
        particle.data = float4(random(id, 1.0), 0.0, 0.0, 0.0);
        int loop = (int)(id % (uint)Loops);
        particle.position = loopPoint(loop, particle.data.x);
        return;
    }
    // A streamer: rises slowly from just above the surface, fading as it goes.
    float3 direction = normalize(float3(random(id, 2.0) * 2.4 - 1.2, 1.0, random(id, 3.0) * 1.4 - 0.7));
    particle.position = SunCentre + direction * (SunRadius + 0.05);
    particle.velocity = direction * (0.2 + random(id, 4.0) * 0.4);
    particle.life = 3.0 + random(id, 5.0) * 6.0;
    if (ParticleReset > 0.5)
    {
        float head = random(id, 6.0) * particle.life;
        particle.life -= head;
        particle.position += particle.velocity * head;
    }
    particle.data = float4(0.0, 2.0, particle.life, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    int loop = (int)(id % (uint)Loops);
    float3 fromCentre = particle.position - SunCentre;
    float3 outward = normalize(fromCentre);

    if (particle.data.y > 1.5)
    {
        // Streamers drift out on the solar wind, twisting gently.
        particle.velocity += (curlNoise(particle.position * 0.4 + Time * 0.05) * 0.15 + outward * 0.08) * dt;
        particle.position += particle.velocity * dt;
        return;
    }

    if (particle.data.y > 0.5)
    {
        // Flung free by a flare: plasma flies out, slowed by the sun's pull and twisted by the field,
        // and after a while rejoins its loop.
        particle.velocity += (-outward * 1.4 + curlNoise(particle.position * 0.5) * 0.8) * dt;
        particle.position += particle.velocity * dt;
        particle.data.z -= dt;
        if (particle.data.z < 0.0 || length(fromCentre) < SunRadius)
        {
            particle.data.y = 0.0;
            particle.data.x = random(id, 7.0);
        }
        return;
    }

    // On the beat a few particles of the flaring loop are torn off and hurled outward.
    if (Beat > 0.9 && loop == flaringLoop() && random(id, 8.0) < 0.35 * (0.4 + Bass))
    {
        float burst = 3.0 + random(id, 9.0) * 5.0 * (0.5 + Bass);
        particle.velocity = normalize(outward * 1.6 + randomDirection(id, 10.0)) * burst;
        particle.data.y = 1.0;
        particle.data.z = 1.5 + random(id, 11.0) * 2.0;
        return;
    }

    // Plasma pours along the loop from one foot to the other, faster with the music.
    float speed = (0.05 + fixedRandom(id, 12.0) * 0.06) * (0.7 + Level * 0.8 + Bass * 0.5);
    particle.data.x = frac(particle.data.x + speed * dt);
    // Each particle keeps its own place across the loop's thickness, so loops look like twisted ropes.
    float3 tube = (float3(fixedRandom(id, 13.0), fixedRandom(id, 14.0), fixedRandom(id, 15.0)) - 0.5);
    float twist = particle.data.x * 12.0 + Time * 0.6;
    tube.xy = rotate(tube.xy, twist);
    float3 next = loopPoint(loop, particle.data.x) + tube * (0.18 + 0.12 * sin(particle.data.x * PI));
    particle.velocity = (next - particle.position) / max(dt, 1e-3);
    // A loop that has just been re-shaped jumps; don't streak across the sky when that happens.
    if (dot(particle.velocity, particle.velocity) > 400.0)
    {
        particle.velocity = 0.0;
    }
    particle.position = next;
}

// Whether a point is hidden behind the sun from the eye.
bool behindSun(float3 eye, float3 position)
{
    float3 toPoint = position - eye;
    float distance = length(toPoint);
    float3 direction = toPoint / distance;
    float3 oc = eye - SunCentre;
    float b = dot(oc, direction);
    float h = b * b - dot(oc, oc) + SunRadius * SunRadius;
    return h > 0.0 && (-b - sqrt(h)) < distance - 0.02;
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 seen = project(particle.position, eye, target, Zoom, 0.0);
    if (seen.z < 0.3 || behindSun(eye, particle.position))
    {
        return sprite;
    }
    float3 before = project(particle.position - particle.velocity * 0.04, eye, target, Zoom, 0.0);
    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seen.x - before.x, before.y - seen.y);
    sprite.size = 1.2;

    float height = length(particle.position - SunCentre) - SunRadius;
    if (particle.data.y > 1.5)
    {
        float fade = saturate(particle.life / max(particle.data.z, 1e-3)) * smoothstep(0.0, 0.5, particle.age);
        sprite.colour = float3(1.0, 0.55, 0.25) * 0.05 * fade * exp(-height * 0.5);
        sprite.size = 2.0;
        return sprite;
    }

    int loop = (int)(id % (uint)Loops);
    // Hottest near the feet where plasma rains down, golden in the arch; the flaring loop blazes white.
    float flare = loop == flaringLoop() ? Beat : 0.0;
    float3 colour = lerp(float3(1.0, 0.75, 0.35), float3(1.0, 0.45, 0.12), saturate(height * 0.6));
    colour = lerp(colour, float3(1.0, 0.95, 0.85), flare * 0.7);
    float strength = 0.11 * (1.0 + flare * 3.0) * (0.8 + Mid * 0.6);
    if (particle.data.y > 0.5)
    {
        // Ejected plasma cools to deep red as it flies.
        float cooling = saturate(particle.data.z / 3.0);
        colour = lerp(float3(1.0, 0.25, 0.08), float3(1.0, 0.8, 0.5), cooling);
        strength = 0.25 * cooling + 0.05;
    }
    sprite.colour = colour * strength;
    focus(sprite, seen.z, length(eye - target), 0.03);
    return sprite;
}

// ---- The sun ----

// Cells of hot rising gas: distance to the nearest cell centre, giving bright cells and dark lanes.
float cells(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float nearest = 1.0;
    [loop]
    for (int z = -1; z <= 1; z++)
    {
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                float3 cell = float3(x, y, z);
                float3 jitter = float3(hash3d(i + cell), hash3d(i + cell + 17.1), hash3d(i + cell + 31.7));
                // Centres wander a little so the granules churn.
                jitter = 0.5 + 0.4 * sin(Time * 0.4 + jitter * 6.2832);
                float3 offset = cell + jitter - f;
                nearest = min(nearest, dot(offset, offset));
            }
        }
    }
    return sqrt(nearest);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);

    float3 oc = eye - SunCentre;
    float b = dot(oc, direction);
    float closest = sqrt(max(dot(oc, oc) - b * b, 0.0));
    float h = SunRadius * SunRadius - closest * closest;
    float flare = Beat * Beat;

    // Deep space with a few faint stars.
    float3 colour = float3(0.002, 0.001, 0.004);
    float2 starCell = floor(direction.xy / max(direction.z + 1.5, 0.1) * 260.0);
    colour += pow(hash(starCell), 4000.0) * 0.25;

    // Above the limb: the thin pink chromosphere, a fringe of spicules, and the faint corona beyond.
    float altitude = closest - SunRadius;
    if (b < 0.0 && altitude > 0.0)
    {
        float3 limbPoint = eye + direction * -b;
        float3 rimDirection = normalize(limbPoint - SunCentre);
        float spicules = 0.6 + 0.4 * noise(float2(atan2(rimDirection.x, rimDirection.y) * 300.0, Time * 0.3));
        colour += float3(1.0, 0.25, 0.2) * 2.5 * exp(-altitude / (0.06 * spicules));
        colour += float3(1.0, 0.55, 0.3) * 0.35 * exp(-altitude * 1.6) * (1.0 + Level * 0.4 + flare * 0.5);
        colour += float3(0.9, 0.6, 0.45) * 0.06 * exp(-altitude * 0.3);
    }

    if (h > 0.0 && b < 0.0)
    {
        float t = -b - sqrt(h);
        float3 surface = eye + direction * t;
        float3 normal = (surface - SunCentre) / SunRadius;
        float facing = saturate(dot(normal, -direction));
        // Granulation: small bright cells with dark lanes, on larger slowly boiling patches.
        float3 q = normal * 55.0 + float3(0.0, Time * 0.05, 0.0);
        float granule = cells(q);
        float boil = noise3(normal * 6.0 + Time * 0.08) * 0.5 + 0.5;
        float brightness = (1.05 - granule * 0.55) * (0.75 + 0.35 * boil);
        // Limb darkening: the edge of the sun is cooler, redder and dimmer than the middle.
        float limb = pow(facing, 0.45);
        float3 hot = lerp(float3(0.9, 0.22, 0.03), float3(1.0, 0.6, 0.22), limb);
        float3 sun = hot * brightness * (0.25 + 0.75 * limb) * (1.0 + Bass * 0.25);

        // Bright footpoints under the flaring loop flash on the beat.
        float3 a = loopPoint(flaringLoop(), 0.0);
        float3 c = loopPoint(flaringLoop(), 1.0);
        float near = exp(-dot(surface - a, surface - a) * 2.5) + exp(-dot(surface - c, surface - c) * 2.5);
        sun += float3(1.0, 0.9, 0.75) * near * flare * 6.0;
        // Hot active regions beneath every loop glow a little.
        sun *= 1.0 + 0.25 * smoothstep(0.55, 0.8, noise3(normal * 9.0));
        colour = sun;
    }

    // A soft glow off the bright disc; the particles' loops, with a whisper of trail.
    colour += float3(1.0, 0.5, 0.2) * 0.08 * exp(-max(altitude, 0.0) * 0.9);
    return colour + particles(uv) + previous(uv) * 0.3;
}
