// Name: Abyssal Glow
// Sink slowly into the deep ocean through drifting marine snow, as glowing jellyfish pulse to the beat and waves of plankton flash blue-green in the dark.
// Family: Particles
// Tags: Calm, Dark, Water, 3D
// Particles: 262144

// How the particles are shared out: marine snow, then bioluminescent plankton, then jellyfish.
static const float SnowShare = 0.06;
static const float PlanktonShare = 0.6;
static const int Jellies = 5;
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

// One jellyfish: where it is, how big, its colour, and how hard its bell is squeezing now.
void jellyState(int index, out float3 centre, out float size, out float hue, out float squeeze)
{
    float salt = index * 2.17 + 0.4;
    float order[5] = { 0.1, 0.7, 0.3, 0.9, 0.5 };
    float3 home = float3((order[index] - 0.5) * 6.5, (frac(salt * 5.3) - 0.5) * 1.6 + 0.2, 2.4 + frac(salt * 7.9) * 3.0);
    float3 wander = float3(sin(Time * 0.05 + salt * 4.0) * 0.6, sin(Time * 0.07 + salt * 2.0) * 0.35, sin(Time * 0.04 + salt) * 0.5);
    centre = cameraAt(Time) + home + wander;
    size = 0.3 + frac(salt * 9.1) * 0.2;
    float hues[5] = { 0.88, 0.55, 0.76, 0.95, 0.5 };
    hue = hues[index];
    // Each jelly keeps its own slow rhythm, and every beat makes them all squeeze.
    float own = pow(saturate(sin(Time * (1.6 + frac(salt * 13.0) * 0.8) + salt) * 0.5 + 0.5), 4.0);
    squeeze = saturate(own * 0.6 + Beat * 0.7);
}

// A point on a jellyfish, in its own coordinates (y grows downward), and how brightly it glows.
float3 jellyPoint(uint id, uint k, uint n, float size, float squeeze, float salt, out float glowAmount, out float tint)
{
    float u = (k + 0.5) / max((float)n, 1.0);
    float swim = Time * 2.0 + salt;
    if (u < 0.5)
    {
        // The bell: a dome that narrows when it squeezes, rim brightest.
        float along = sqrt(u / 0.5);
        float angle = fixedRandom(id, 5.0) * 2.0 * PI;
        float polar = along * 1.75;
        float rim = 1.0 - squeeze * 0.28 * along * along;
        float3 dome = float3(sin(polar) * cos(angle) * rim, -cos(polar) * 0.75 + squeeze * 0.08, sin(polar) * sin(angle) * rim);
        // A little scalloping around the edge.
        dome.y += sin(angle * 16.0) * 0.03 * along;
        glowAmount = 0.35 + along * along * 1.1;
        tint = along * 0.06;
        return dome * size;
    }
    if (u < 0.88)
    {
        // Long fine tentacles trailing from the rim, rippling as the jelly swims.
        float t = (u - 0.5) / 0.38;
        uint tentacle = (uint)(fixedRandom(id, 6.0) * 24.0);
        float angle = (tentacle + 0.5) / 24.0 * 2.0 * PI;
        float along = fixedRandom(id, 7.0);
        float tentacleLength = size * (3.0 + frac(tentacle * 0.618) * 2.5);
        float rimRadius = size * (1.0 - squeeze * 0.28) * sin(1.75);
        float3 root = float3(cos(angle) * rimRadius, -cos(1.75) * 0.75 * size, sin(angle) * rimRadius);
        float3 sway = float3(sin(along * 7.0 - swim + tentacle), 0.0, cos(along * 6.0 - swim * 0.8 + tentacle * 1.3)) * 0.12 * size * along * 3.0;
        glowAmount = 0.5 * (1.0 - along * 0.7);
        tint = 0.1;
        return root * (1.0 - along * 0.3) + float3(0.0, along * tentacleLength, 0.0) + sway;
    }
    // Four frilly arms hanging from the middle, swaying more slowly.
    uint arm = (uint)(fixedRandom(id, 8.0) * 4.0);
    float along = fixedRandom(id, 9.0);
    float armAngle = (arm + 0.5) / 4.0 * 2.0 * PI + sin(swim * 0.3) * 0.3;
    float frill = (fixedRandom(id, 10.0) - 0.5) * 0.25 * size * (0.4 + along);
    float3 armPoint = float3(cos(armAngle) * (0.15 * size + frill), along * size * 1.8, sin(armAngle) * (0.15 * size + frill));
    armPoint.x += sin(along * 4.0 - swim * 0.6 + arm) * 0.12 * size * along;
    glowAmount = 0.5 * (1.0 - along * 0.6);
    tint = -0.05;
    return armPoint;
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

    // A jellyfish.
    float jellyShare = (1.0 - SnowShare - PlanktonShare) / Jellies;
    float local = (share - SnowShare - PlanktonShare) / jellyShare;
    int index = min((int)local, Jellies - 1);
    uint count = (uint)(jellyShare * ParticleCount);
    uint k = (uint)(frac(local) * count);

    float3 centre;
    float size;
    float hue;
    float squeeze;
    jellyState(index, centre, size, hue, squeeze);
    float glowAmount;
    float tint;
    float3 offset = jellyPoint(id, k, count, size, squeeze, index * 2.17, glowAmount, tint);
    // Each jelly leans gently as it swims.
    offset.xy = rotate(offset.xy, sin(Time * 0.2 + index) * 0.25);

    float3 seen = project(centre + offset, eye, target, zoom, 0.0);
    float murk = exp(-seen.z * 0.18);
    float3 colour = hsv2rgb(hue + tint, 0.75, 1.0);
    // The bell flashes brighter as it squeezes, and with the bass.
    float pulse = 0.6 + squeeze * 0.8 + Bass * 0.4;
    sprite.position = seen.xy;
    sprite.size = seen.z > 0.2 ? 1.0 : 0.0;
    sprite.colour = colour * glowAmount * pulse * murk * 0.2 * (0.4 / size);
    focus(sprite, seen.z, 3.5, 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The faintest blue from far above, fading to black below.
    float3 water = float3(0.0, 0.012, 0.03) * saturate(0.6 - p.y * 0.5);
    return water + particles(uv) + previous(uv) * 0.5;
}
