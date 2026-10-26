// Name: Dancing Fountains
// A grand fountain show at night: a long line of water jets leaps, sways and fans out in choreographed patterns to the music, lit from below in shifting colours, mirrored in a dark rippling lake with the city skyline glittering behind.
// Family: Ray traced
// Tags: Water, Colourful, 3D
// Added: 2026-10-17 17:12
// Particles: 393216

// The lake, the sky and the city are ray traced; the water in the air is particles. Particles come
// in identical twins: both are launched together from the same nozzle with the same random numbers,
// so they fly exactly alike, but the second of each pair is drawn upside down below the surface as
// the jet's reflection, wobbled by the ripples. The show changes routine every sixteen beats:
// jets that rise and fall with the spectrum, a travelling wave, a crossing lattice and a fan,
// with tall shooters bursting up on the beats.
static const int Nozzles = 22;
static const float LineZ = 3.0;
static const float Gravity = 3.6;
static const float Zoom = 1.55;

float3 cameraEye()
{
    return float3(sin(Time * 0.04) * 1.6, 0.75 + sin(Time * 0.029) * 0.15, -3.6 + sin(Time * 0.023) * 0.4);
}

float3 cameraTarget()
{
    return float3(sin(Time * 0.03) * 0.5, 1.6, LineZ);
}

float3 nozzleAt(float n)
{
    float x = (n / (Nozzles - 1) - 0.5) * 8.4;
    return float3(x, 0.0, LineZ + x * x * 0.035);
}

float routine()
{
    return wrap(floor(BeatCount / 16.0), 4.0);
}

// The colour of the lights under each nozzle, sweeping along the line and changing with each routine.
float3 lightColour(float n)
{
    float u = n / (Nozzles - 1);
    float hue = frac(routine() * 0.27 + u * 0.35 + Time * 0.04);
    return hsv2rgb(hue, 0.75, 1.0);
}

// How a nozzle is firing right now: its tilt to the side and its launch speed.
float2 nozzleAim(float n)
{
    float u = n / (Nozzles - 1);
    float mode = routine();
    float tilt = 0.0;
    float height = 1.5;
    if (mode < 0.5)
    {
        // Each jet's height follows its own part of the spectrum, like a giant equaliser.
        height = 0.6 + spectrum(abs(u - 0.5) * 1.6) * 4.2;
        tilt = (u - 0.5) * 0.25;
    }
    else if (mode < 1.5)
    {
        // A wave rolling along the line.
        height = 2.0 + Bass * 1.6;
        tilt = 0.5 * sin(Time * 1.7 - u * 7.0);
    }
    else if (mode < 2.5)
    {
        // Alternate jets lean left and right, weaving a lattice that slowly breathes.
        float side = fmod(n, 2.0) < 0.5 ? 1.0 : -1.0;
        height = 2.4 + Mid * 1.8;
        tilt = side * (0.35 + 0.15 * sin(Time * 0.8));
    }
    else
    {
        // A fan that leaps on every beat.
        height = 1.4 + Beat * 2.8 + Bass;
        tilt = (u - 0.5) * 1.3;
    }
    return float2(tilt, sqrt(2.0 * Gravity * height));
}


void Spawn(inout Particle particle, uint id)
{
    // Both twins compute everything from the pair's number, so they're born identical.
    uint pair = id >> 1;
    float n = (float)(pair % (uint)Nozzles);
    float role = fixedRandom(pair, 1.0);
    float2 aim = nozzleAim(n);
    float spread = 0.012;
    float speed = aim.y;
    float kind = 0.0;
    if (role > 0.9)
    {
        // A shooter: only fires on a beat, from every third nozzle, straight up and very high.
        bool firing = Beat > 0.6 && fmod(n, 3.0) < 0.5;
        speed = firing ? sqrt(2.0 * Gravity * (4.0 + Bass * 1.5)) * (0.75 + 0.25 * random(pair, 10.0)) : 0.0;
        aim.x = 0.0;
        spread = 0.035;
        kind = firing ? 2.0 : 3.0;
    }
    else if (role > 0.65)
    {
        // Spray: torn from the jet into a mist that hangs and drifts.
        spread = 0.09;
        kind = 1.0;
    }

    float3 direction = normalize(float3(sin(aim.x), cos(aim.x), 0.0) + (float3(random(pair, 2.0), random(pair, 3.0), random(pair, 4.0)) - 0.5) * spread * 2.0);
    particle.velocity = direction * speed * (1.0 - random(pair, 5.0) * 0.06);
    particle.position = nozzleAt(n) + float3(0.0, 0.02, 0.0);
    float flight = 2.0 * max(particle.velocity.y, 0.1) / Gravity;
    particle.life = kind > 2.5 ? 0.05 + random(pair, 6.0) * 0.1 : flight * 1.3 + 0.2;
    particle.data = float4(n, kind, 0.0, 0.0);

    // Spread the launches across the frame so each jet is a smooth stream, not a string of clumps.
    float early = random(pair, 8.0) * min(DeltaTime, 0.05);
    particle.position += particle.velocity * early;
    particle.velocity.y -= Gravity * early;

    if (ParticleReset > 0.5 && kind < 2.5)
    {
        // On the first frame, start the jets already flowing.
        float age = random(pair, 7.0) * flight;
        particle.position += particle.velocity * age + float3(0.0, -0.5 * Gravity * age * age, 0.0);
        particle.velocity.y -= Gravity * age;
        particle.life = max(flight - age, 0.05);
    }
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float drag = particle.data.y > 0.5 && particle.data.y < 1.5 ? 1.6 : 0.15;
    particle.velocity.y -= Gravity * dt;
    particle.velocity *= exp(-drag * dt);
    // A light breeze carries the mist.
    particle.velocity.x += (particle.data.y > 0.5 && particle.data.y < 1.5 ? 0.25 : 0.03) * dt;
    particle.position += particle.velocity * dt;
    if (particle.position.y < 0.0)
    {
        particle.life = 0.0;
    }
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float kind = particle.data.y;
    if (kind > 2.5)
    {
        return sprite;
    }
    bool mirror = (id & 1u) == 1u;
    float n = particle.data.x;
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 position = particle.position;
    float3 velocity = particle.velocity;
    float height = position.y;
    if (mirror)
    {
        // The reflection: upside down under the surface, wobbled by the ripples.
        position.y = -position.y;
        velocity.y = -velocity.y;
        position.x += sin(position.z * 9.0 + Time * 2.3 + height * 3.0) * 0.03 * (0.3 + height);
        position.y += sin(position.x * 7.0 + Time * 1.7) * 0.02 * height;
    }
    float3 seen = project(position, eye, target, Zoom, 0.0);
    float3 seenBefore = project(position - velocity * 0.03, eye, target, Zoom, 0.0);

    // Lit from below: the jets glow in their light's colour, brightest near the water, with a
    // white sparkle in the droplets.
    float3 light = lightColour(n);
    float lit = exp(-height * 0.35) * (1.0 + Beat * 0.4) + 0.15;
    float3 colour = lerp(light, 1.0, 0.3 + 0.2 * fixedRandom(id >> 1, 9.0)) * lit;
    float brightness = kind < 0.5 ? 0.2 : (kind < 1.5 ? 0.05 : 0.2);
    float size = kind < 0.5 ? 1.0 : (kind < 1.5 ? 2.8 : 1.1);
    float fade = saturate(particle.age * 12.0) * saturate(particle.life * 4.0 + 0.3);
    if (mirror)
    {
        // Water reflects more at a glance; up close we see into it, so the reflection is dimmer.
        brightness *= 0.32;
        size *= 1.4;
    }

    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seen.x - seenBefore.x, seenBefore.y - seen.y);
    sprite.size = seen.z > 0.1 ? size : 0.0;
    sprite.colour = colour * brightness * fade;
    focus(sprite, seen.z, length(target - eye), 0.02);
    return sprite;
}

// The city across the lake: blocks of towers against the glow of the night sky, their windows lit.
float3 skyline(float3 direction, out bool building)
{
    float around = atan2(direction.x, direction.z);
    float tower = floor(around * 40.0);
    float tall = 0.02 + pow(hash(float2(tower, 4.1)), 2.5) * 0.12 + hash(float2(floor(around * 9.0), 1.3)) * 0.02;
    float y = direction.y;
    building = y < tall && y > 0.0;
    if (!building)
    {
        return 0.0;
    }
    // Windows: a grid of small lit panes, some warm, some cool, many dark.
    float2 pane = float2(around * 40.0 * 9.0, y * 420.0);
    float2 cell = floor(pane);
    float on = step(0.62, hash(cell + tower * 13.0));
    float2 inPane = frac(pane);
    float window = on * step(0.25, inPane.x) * step(inPane.x, 0.75) * step(0.3, inPane.y) * step(inPane.y, 0.75);
    float3 tint = hash(cell * 1.3 + 7.0) < 0.7 ? float3(1.0, 0.75, 0.45) : float3(0.6, 0.8, 1.0);
    // A few towers wear a band of coloured light round the top that pulses with the treble.
    float crown = step(0.75, hash(float2(tower, 9.9))) * smoothstep(0.006, 0.0, abs(y - tall + 0.008));
    float3 crownColour = hsv2rgb(hash(float2(tower, 2.2)), 0.7, 1.0) * (1.0 + Treble * 2.0);
    float3 wall = float3(0.012, 0.013, 0.02);
    return wall + tint * window * 0.3 * (0.6 + 0.4 * hash(cell + 3.0)) + crownColour * crown * 0.6;
}

float3 sky(float3 direction)
{
    float y = direction.y;
    // Deep blue night, glowing orange-violet over the city.
    float3 colour = lerp(float3(0.06, 0.035, 0.06), float3(0.005, 0.008, 0.025), smoothstep(-0.02, 0.4, y));
    colour += float3(0.12, 0.05, 0.04) * exp(-max(y, 0.0) * 14.0);
    bool building;
    float3 city = skyline(direction, building);
    return building ? city : colour;
}

// The lake's surface: little waves, and rings spreading round each nozzle where the jets fall back.
float lakeHeight(float2 q)
{
    float h = sin(dot(q, float2(0.7, 0.7)) * 3.0 + Time * 1.2) * 0.006;
    h += sin(dot(q, float2(-0.6, 0.8)) * 5.0 + Time * 1.7) * 0.004;
    h += (noise(q * 4.0 + Time * 0.5) - 0.5) * 0.008;
    float d = abs(q.y - LineZ - q.x * q.x * 0.035);
    h += sin(d * 18.0 - Time * 5.0) * 0.006 * exp(-d * 1.5) * (0.6 + Level);
    return h;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);
    float3 colour;

    if (direction.y >= 0.0)
    {
        colour = sky(direction);
    }
    else
    {
        // The dark lake: a rippled mirror of the sky and city.
        float t = -eye.y / direction.y;
        float3 position = eye + direction * t;
        const float e = 0.01;
        float h = lakeHeight(position.xz);
        float detail = exp(-t * 0.05);
        float3 normal = normalize(float3(-(lakeHeight(position.xz + float2(e, 0.0)) - h) * detail, e, -(lakeHeight(position.xz + float2(0.0, e)) - h) * detail));
        float3 reflected = reflect(direction, normal);
        reflected.y = abs(reflected.y);
        float fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(normal, -direction)), 5.0);
        colour = sky(reflected) * (0.1 + fresnel * 0.9) + float3(0.003, 0.006, 0.01);

        // The coloured lights under the water glow along the line of nozzles.
        [loop]
        for (int i = 0; i < Nozzles; i++)
        {
            float3 nozzle = nozzleAt(i);
            float d = length((position.xz - nozzle.xz) * float2(1.0, 1.6));
            colour += lightColour(i) * (exp(-d * 9.0) * 0.6 + exp(-d * 2.0) * 0.03) * (0.6 + Beat * 0.6 + spectrum((float)i / Nozzles) * 0.8);
        }
        // Mist over the far water.
        colour = lerp(colour, float3(0.04, 0.025, 0.04), 1.0 - exp(-t * 0.02));
    }

    // Glow in the misty air over the fountains.
    float3 toLine = float3(0.0, 1.2, LineZ) - eye;
    float off = length(toLine - direction * dot(toLine, direction));
    colour += lightColour(Nozzles * 0.5) * exp(-off * 0.8) * 0.02 * (1.0 + Bass);

    return colour + particles(uv) + previous(uv) * 0.25;
}
