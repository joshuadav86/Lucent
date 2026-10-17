// Name: Gravity Wells
// Streams of glowing dust slingshot round four wandering stars, whipping into tight orbits and figure-eights before flinging off into the dark; each star weighs its own part of the music and every beat launches new streams.
// Family: Particles
// Tags: Space, Colourful, Intense
// Particles: 262144

// Four heavy, glowing masses drift round each other. Dust pours in from three launchers circling
// at the edge, and each grain simply falls under the pull of all four masses at once, which is
// enough to make slingshots, loops and figure-eights. Each mass's pull follows its own band of
// the spectrum, so the bass star swells and grabs more dust when the bass is loud.
static const int Masses = 4;
static const int Launchers = 3;
static const float Zoom = 2.3;

float3 massAt(int index, float t)
{
    float k = index;
    float speed = 0.11 + k * 0.023;
    float radius = 0.55 + frac(k * 0.618) * 0.6;
    return float3(sin(t * speed + k * 1.7) * radius * 1.3,
                  sin(t * speed * 1.37 + k * 2.9) * 0.35,
                  cos(t * speed * 0.83 + k * 1.7) * radius);
}

float bandOf(int index)
{
    return index == 0 ? 0.04 : (index == 1 ? 0.2 : (index == 2 ? 0.45 : 0.75));
}

float massWeight(int index)
{
    float level = spectrum(bandOf(index));
    return 0.28 * (0.55 + level * 1.4) + (index == 0 ? Bass * 0.12 : 0.0);
}

static const float3 MassColours[Masses] = { float3(1.0, 0.35, 0.12), float3(1.0, 0.8, 0.3), float3(0.2, 0.85, 1.0), float3(0.7, 0.35, 1.0) };

float3 massColour(int index)
{
    return MassColours[index];
}

// The total pull at a point, and the colour of whichever masses are pulling hardest.
float3 gravity(float3 position, float t, out float3 tint)
{
    float3 pull = 0.0;
    tint = 0.0;
    float total = 1e-5;
    [unroll]
    for (int i = 0; i < Masses; i++)
    {
        float3 offset = massAt(i, t) - position;
        float r2 = dot(offset, offset) + 0.02;
        float strength = massWeight(i) / (r2 * sqrt(r2));
        pull += offset * strength;
        float weight = massWeight(i) / r2;
        tint += massColour(i) * weight;
        total += weight;
    }
    tint /= total;
    // A faint pull back towards the middle, like an unseen halo, keeps most dust bound so it
    // loops and loops instead of escaping on the first slingshot.
    return pull - position * 0.07;
}

float3 cameraEye()
{
    float angle = Time * 0.025;
    return float3(sin(angle) * 5.0, 2.3 + sin(Time * 0.027) * 0.7, cos(angle) * 5.0);
}

void Spawn(inout Particle particle, uint id)
{
    particle.life = lerp(7.0, 14.0, random(id, 4.0)) * (ParticleReset > 0.5 ? random(id, 7.0) + 0.05 : 1.0);
    particle.data = float4(1.0, 1.0, 1.0, 0.0);

    if (fixedRandom(id, 8.0) < 0.7)
    {
        // Most dust is born in a ring round one of the stars, on a circular orbit, tilted a
        // little. When the stars swing past each other the rings are torn into streams.
        int owner = min((int)(random(id, 9.0) * Masses), Masses - 1);
        float3 centre = massAt(owner, Time);
        float3 moving = (massAt(owner, Time + 0.01) - massAt(owner, Time - 0.01)) / 0.02;
        // The dust settles into a few thin rings, which read as clean orbits.
        float radius = 0.14 + floor(random(id, 10.0) * 5.0) * 0.1 + (random(id, 13.0) - 0.5) * 0.012;
        float angle = random(id, 11.0) * 2.0 * PI;
        float3 axis = normalize(float3(sin(owner * 2.3) * 0.4, 1.0, cos(owner * 1.7) * 0.4));
        float3 side = normalize(cross(axis, float3(0.0, 0.0, 1.0)));
        float3 front = cross(axis, side);
        float3 offset = (side * cos(angle) + front * sin(angle)) * radius;
        float r2 = radius * radius + 0.02;
        float speed = sqrt(massWeight(owner) * radius * radius / (r2 * sqrt(r2)) + 0.07 * radius * radius);
        particle.position = centre + offset + randomInSphere(id, 12.0) * 0.01;
        particle.velocity = moving + cross(axis, normalize(offset)) * speed;
        particle.data = float4(massColour(owner), 0.0);
        return;
    }

    // The rest pour in from launchers circling the edge, aimed roughly inwards; every beat
    // swings each launcher's aim to a new line, starting a fresh stream.
    uint launcher = id % (uint)Launchers;
    float angle = launcher * 2.094 + Time * 0.07;
    float3 origin = float3(cos(angle) * 2.6, sin(Time * 0.05 + launcher) * 0.5, sin(angle) * 2.6);
    uint beat = (uint)BeatCount;
    float aimSide = (pcgHash(beat * 3u + launcher) / 4294967295.0 - 0.5) * 1.1;
    float aimUp = (pcgHash(beat * 3u + launcher + 101u) / 4294967295.0 - 0.5) * 0.5;
    float3 inwards = -normalize(origin);
    float3 side = normalize(cross(inwards, float3(0.0, 1.0, 0.0)));
    float3 aim = normalize(inwards + side * aimSide + float3(0.0, aimUp, 0.0));
    particle.position = origin + randomInSphere(id, 1.0) * 0.012;
    particle.velocity = aim * (0.75 + random(id, 2.0) * 0.1 + Beat * 0.3) + randomInSphere(id, 3.0) * 0.01;
}

void Simulate(inout Particle particle, uint id)
{
    // Leapfrog steps keep the orbits steady even on close passes.
    float dt = min(DeltaTime, 0.05) / 4.0;
    float t = Time - min(DeltaTime, 0.05);
    float3 tint = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        particle.velocity += gravity(particle.position, t, tint) * dt * 0.5;
        particle.position += particle.velocity * dt;
        t += dt;
        particle.velocity += gravity(particle.position, t, tint) * dt * 0.5;
    }
    // Keep speeds sane, and let the colour follow whichever star it's falling round.
    float speed = length(particle.velocity);
    particle.velocity *= min(1.0, 4.0 / max(speed, 1e-4));
    particle.data.rgb = lerp(particle.data.rgb, tint, saturate(DeltaTime * 2.5));

    // Dust that strays too far or falls right into a star is gone.
    float nearest = 10.0;
    [unroll]
    for (int j = 0; j < Masses; j++)
    {
        nearest = min(nearest, length(particle.position - massAt(j, Time)));
    }
    if (dot(particle.position, particle.position) > 14.0 || nearest < 0.025)
    {
        particle.life = 0.0;
    }
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float3 seen = project(particle.position, eye, 0.0, Zoom, 0.0);
    float3 seenBefore = project(particle.position - particle.velocity * 0.14, eye, 0.0, Zoom, 0.0);
    float speed = length(particle.velocity);
    float fade = saturate(particle.age * 0.7) * saturate(particle.life * 0.5);
    // Fast grains whipping round a star run white-hot.
    float3 colour = lerp(particle.data.rgb, 1.0, saturate(speed * 0.35 - 0.2));

    Sprite sprite = (Sprite)0;
    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seen.x - seenBefore.x, seenBefore.y - seen.y);
    sprite.size = seen.z > 0.1 ? 0.8 : 0.0;
    sprite.colour = colour * (0.06 + speed * 0.06) * fade * (1.0 + Beat * 0.3);
    focus(sprite, seen.z, length(eye), 0.006);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 direction = cameraRay(eye, 0.0, float2(p.x, -p.y), Zoom, 0.0);

    // Faint stars far behind.
    float3 colour = float3(0.003, 0.003, 0.008);
    float star = hash3d(floor(direction * 300.0));
    colour += step(0.9985, star) * pow(frac(star * 733.0), 2.0) * float3(0.6, 0.7, 1.0) * 0.25;

    // The masses themselves: a white-hot core in a halo of their own colour, swelling with their band.
    [unroll]
    for (int i = 0; i < Masses; i++)
    {
        float3 centre = massAt(i, Time);
        float3 seen = project(centre, eye, 0.0, Zoom, 0.0);
        float distance = length(p - float2(seen.x, -seen.y)) * seen.z;
        float level = spectrum(bandOf(i));
        float size = 0.035 + level * 0.025;
        colour += lerp(massColour(i), 1.0, 0.6) * exp(-distance / size * 1.6) * (2.5 + level * 4.0);
        colour += massColour(i) * exp(-distance * 9.0) * (0.12 + level * 0.25);
    }

    return colour + particles(uv) + previous(uv) * 0.7;
}
