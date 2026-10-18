// Name: Light Painting
// A long-exposure photograph come to life: sparklers and coloured light wands sweep spirals, glowing orbs and looping ribbons through the dark, leaving trails that slowly fade; every beat starts a fresh stroke and the colours follow the music.
// Family: Particles
// Tags: Neon, Colourful, Dark
// Added: 2026-10-17 17:12
// Particles: 327680

// Five lights are being waved about in a dark room. Most particles are the light they leave
// behind: each one is dropped where its wand is at the moment it's born and stays there, glowing
// and slowly fading, so together they draw the wand's path like a camera shutter left open.
// The rest are sparks thrown off the two sparklers.
static const int Wands = 5;
static const float TrailShare = 0.9;
static const float TrailLife = 5.5;
static const float Zoom = 1.7;

float3 cameraEye()
{
    return float3(sin(Time * 0.045) * 1.1, 0.25 + sin(Time * 0.031) * 0.25, -4.2);
}

float3 cameraTarget()
{
    return float3(sin(Time * 0.03) * 0.2, 0.0, 0.0);
}

bool isSparkler(int wand)
{
    return wand == 0 || wand == 3;
}

// Which stroke a wand is on. The beats take turns: each one starts a new stroke on the next wand.
float strokeOf(int wand)
{
    return floor((BeatCount + 5.0 - wand) / Wands);
}

float strokeRandom(int wand, float stroke, float salt)
{
    return pcgHash(pcgHash((uint)(stroke + 1000.0) * 7u + (uint)wand) ^ asuint(salt)) / 4294967295.0;
}

// Where a wand's light is at time t while drawing a given stroke: a spiral, an orb, a ribbon or a
// rosette, each with its own place, size and tilt in the room.
float3 wandAt(int wand, float stroke, float t)
{
    float shape = floor(strokeRandom(wand, stroke, 1.0) * 4.0);
    float3 centre = float3((strokeRandom(wand, stroke, 2.0) - 0.5) * 2.6 + (wand - 2.0) * 0.6,
                           (strokeRandom(wand, stroke, 3.0) - 0.5) * 1.6,
                           (strokeRandom(wand, stroke, 4.0) - 0.5) * 1.4);
    float size = 0.5 + strokeRandom(wand, stroke, 5.0) * 0.45;
    float u = t * (1.6 + strokeRandom(wand, stroke, 6.0) * 1.2) * (strokeRandom(wand, stroke, 7.0) < 0.5 ? 1.0 : -1.0);
    float3 local;
    if (shape < 1.0)
    {
        // A breathing spiral, coiling in and out.
        float r = 0.15 + 0.85 * (0.5 + 0.5 * sin(u * 0.33));
        local = float3(cos(u * 2.2) * r, sin(u * 2.2) * r, sin(u * 0.7) * 0.4);
    }
    else if (shape < 2.0)
    {
        // An orb: the light sweeps round and round a ball, like a spinning wand on a string.
        float around = u * 1.1;
        float over = u * 3.3;
        local = float3(sin(over) * cos(around), cos(over), sin(over) * sin(around));
    }
    else if (shape < 3.0)
    {
        // A looping ribbon, a figure of eight drawn wide.
        local = float3(sin(u * 1.2) * 1.6, sin(u * 2.4) * 0.55, cos(u * 1.2) * 0.5);
    }
    else
    {
        // A rosette of petals.
        float r = cos(u * 2.5);
        local = float3(cos(u) * r, sin(u) * r, sin(u * 2.5) * 0.25);
    }
    float tilt = (strokeRandom(wand, stroke, 8.0) - 0.5) * 2.0;
    local.xz = rotate(local.xz, tilt);
    local.xy = rotate(local.xy, tilt * 0.6);
    return centre + local * size;
}

// Each wand's colour drifts round the colour wheel and brightens with its own part of the spectrum.
float3 wandColour(int wand)
{
    if (isSparkler(wand))
    {
        return float3(1.0, 0.72, 0.38) * 0.6;
    }
    float hue = frac(wand * 0.29 + Time * 0.015 + spectrum(0.1 + wand * 0.2) * 0.25);
    return hsv2rgb(hue, 0.85, 1.0);
}

float wandLevel(int wand)
{
    return 0.7 + spectrum(0.06 + wand * 0.21) * 2.2;
}

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

void Spawn(inout Particle particle, uint id)
{
    if (shareOf(id) < TrailShare)
    {
        // A dab of light where the wand is. Spread a little within the frame so the line is smooth;
        // on the first frame the trails are back-filled as if the shutter had been open a while.
        int wand = (int)(id % (uint)Wands);
        float back = ParticleReset > 0.5 ? random(id, 1.0) * TrailLife : random(id, 1.0) * DeltaTime;
        float stroke = strokeOf(wand);
        particle.position = wandAt(wand, stroke, Time - back) + randomInSphere(id, 2.0) * (isSparkler(wand) ? 0.012 : 0.006);
        particle.velocity = (wandAt(wand, stroke, Time - back + 0.01) - wandAt(wand, stroke, Time - back)) * 100.0;
        particle.life = TrailLife - back;
        particle.data = float4(wandColour(wand) * wandLevel(wand) * (1.0 + Beat * 0.4), wand);
        return;
    }

    // A spark from one of the sparklers, flung out in a random direction.
    int sparkler = random(id, 3.0) < 0.5 ? 0 : 3;
    float t = Time - random(id, 4.0) * DeltaTime;
    particle.position = wandAt(sparkler, strokeOf(sparkler), t);
    particle.velocity = randomDirection(id, 5.0) * lerp(0.3, 1.7, pow(random(id, 6.0), 2.0)) * (0.8 + Treble * 0.8);
    particle.life = lerp(0.15, 0.8, random(id, 7.0)) * (ParticleReset > 0.5 ? random(id, 8.0) : 1.0);
    particle.data = float4(1.0, 0.75, 0.4, -1.0);
}

void Simulate(inout Particle particle, uint id)
{
    if (particle.data.w >= 0.0)
    {
        return;
    }
    // Sparks arc down under gravity and slow in the air.
    float dt = min(DeltaTime, 0.05);
    particle.velocity += float3(0.0, -2.2, 0.0) * dt;
    particle.velocity *= exp(-dt * 1.8);
    particle.position += particle.velocity * dt;
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 seen = project(particle.position, eye, target, Zoom, 0.0);
    Sprite sprite = (Sprite)0;
    sprite.position = float2(seen.x, -seen.y);

    if (particle.data.w >= 0.0)
    {
        // A trail: bright and white-hot where the wand has just been, its colour richer as it fades.
        float fade = exp(-particle.age * 0.55) * saturate(particle.life * 1.5);
        float fresh = exp(-particle.age * 6.0);
        float3 colour = lerp(particle.data.rgb, max(particle.data.rgb, 1.5), fresh * 0.6);
        sprite.size = seen.z > 0.1 ? 1.6 : 0.0;
        sprite.colour = colour * fade * 0.14;
        focus(sprite, seen.z, length(target - eye), 0.03);
        return sprite;
    }

    // A spark: a white streak cooling to orange as it dies.
    float3 seenBefore = project(particle.position - particle.velocity * 0.035, eye, target, Zoom, 0.0);
    float heat = saturate(particle.life * 2.0);
    sprite.streak = float2(seen.x - seenBefore.x, seenBefore.y - seen.y);
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = lerp(float3(1.0, 0.35, 0.08), float3(1.0, 0.9, 0.7), heat) * (0.1 + heat * 0.3) * saturate(particle.age * 20.0);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();

    // A dark room with a little cool light falling on the floor far below.
    float3 colour = float3(0.003, 0.004, 0.008) + float3(0.006, 0.008, 0.016) * saturate(p.y + 0.3);

    // Each wand lights the haze around it, and the sparklers flicker.
    [unroll]
    for (int wand = 0; wand < Wands; wand++)
    {
        float3 head = wandAt(wand, strokeOf(wand), Time);
        float3 seen = project(head, eye, target, Zoom, 0.0);
        float distance = length(p - float2(seen.x, -seen.y));
        float flicker = isSparkler(wand) ? 0.8 + 0.4 * hash(float2(Time * 37.0, wand)) : 1.0;
        float strength = wandLevel(wand) * flicker / max(seen.z, 0.5);
        colour += wandColour(wand) * strength * (exp(-distance * 40.0) * 0.6 + exp(-distance * 6.0) * 0.025);
    }

    return colour + particles(uv) + previous(uv) * 0.45;
}
