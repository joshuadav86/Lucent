// Name: Gyroid Dust
// A glowing sphere of dust settles onto an endless saddle-shaped surface, revealing its looping channels as it slowly turns; the dust drifts faster with the bass, the surface swells and shifts on every beat, and each layer of the sphere shines with its own part of the spectrum.
// Family: Particles
// Tags: Geometric, 3D
// Particles: 262144

// The gyroid is a surface that curves the same way everywhere, like a saddle repeated forever,
// dividing space into two interwoven labyrinths of tunnels. Here each particle drifts on a slow
// swirling breeze, but after every step it's pulled straight back onto the surface, so the dust
// can only slide along it, and together the particles paint the surface in light. Only a ball of
// it is shown; dust that wanders out of the ball is blown back in somewhere new.
static const float BallRadius = 1.5;
static const float Frequency = 3.3;

// The surface's level, which beats nudge up and down so the whole surface shifts and thickens.
float surfaceLevel()
{
    return sin(Time * 0.17) * 0.25 + Beat * 0.18;
}

float gyroid(float3 q)
{
    return dot(sin(q), cos(q.yzx)) - surfaceLevel();
}

float3 gyroidGradient(float3 q)
{
    float3 s = sin(q);
    float3 c = cos(q);
    return float3(c.x * c.y - s.z * s.x, c.y * c.z - s.x * s.y, c.z * c.x - s.y * s.z);
}

// Pulls a point straight onto the surface (two Newton steps are plenty from nearby).
float3 ontoSurface(float3 position)
{
    float3 q = position * Frequency;
    [unroll]
    for (int i = 0; i < 2; i++)
    {
        float3 g = gyroidGradient(q);
        q -= gyroid(q) * g / max(dot(g, g), 0.05);
    }
    return q / Frequency;
}

float3 surfaceNormal(float3 position)
{
    return normalize(gyroidGradient(position * Frequency) + 1e-5);
}

void Spawn(inout Particle particle, uint id)
{
    float3 start = randomInSphere(id, 1.0) * BallRadius;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        start = ontoSurface(start);
    }
    particle.position = start;
    particle.velocity = 0.0;
    // Short lives keep the dust evenly spread: the breeze slowly gathers it into drifts.
    particle.life = lerp(5.0, 12.0, random(id, 2.0));
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float3 position = particle.position;

    // A gentle swirling breeze, with only its part along the surface kept.
    float3 breeze = curlNoise(position * 0.9 + float3(0.0, Time * 0.05, 0.0)) * (0.07 + Bass * 0.2 + Beat * 0.06);
    float3 normal = surfaceNormal(position);
    breeze -= normal * dot(breeze, normal);

    float3 moved = ontoSurface(position + breeze * dt);
    particle.velocity = (moved - position) / max(DeltaTime, 1e-4);
    // A jump means the Newton step slipped to a different sheet: just keep it there quietly.
    if (length(moved - position) > 0.1)
    {
        particle.velocity = 0.0;
    }
    particle.position = moved;

    if (length(moved) > BallRadius + 0.05)
    {
        particle.life = 0.0;
    }
}

// The ball turns slowly in place; the camera drifts gently round it.
float3 turned(float3 position)
{
    position.xz = rotate(position.xz, Time * 0.06);
    position.xy = rotate(position.xy, sin(Time * 0.04) * 0.4);
    return position;
}

float3 cameraEye()
{
    return float3(sin(Time * 0.03) * 1.2, -1.2 + sin(Time * 0.05) * 0.4, -3.9);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 position = turned(particle.position);
    float3 seen = project(position, eye, 0.0, 2.4 + Bass * 0.04, 0.0);
    float3 seenBefore = project(turned(particle.position - particle.velocity * 0.04), eye, 0.0, 2.4 + Bass * 0.04, 0.0);

    // Shade the dust like a translucent skin: it glows where we see the surface edge on, which
    // traces the saddles' outlines, and is lit from above for shape.
    float3 normal = turned(surfaceNormal(particle.position));
    float3 view = normalize(position - eye);
    float facing = abs(dot(normal, view));
    float rim = 0.45 + 0.9 * pow(1.0 - facing, 2.5);
    float light = 0.6 + 0.4 * abs(dot(normal, normalize(float3(0.3, -0.8, -0.4))));

    // The two sides of the surface face the two labyrinths: teal on one, amber on the other.
    float side = dot(surfaceNormal(particle.position), normalize(particle.position + 1e-4));
    float3 colour = lerp(float3(0.15, 0.75, 0.85), float3(1.0, 0.62, 0.3), smoothstep(-0.6, 0.6, side));

    // Shells of the ball take their own part of the spectrum, bass in the core.
    float depth = length(particle.position) / BallRadius;
    float level = spectrum(0.03 + depth * 0.6);
    // A shell of light sweeps out from the centre on each beat.
    float sweep = exp(-pow((depth - (1.0 - Beat)) * 7.0, 2.0)) * Beat;
    colour *= rim * light * 0.16 * (0.6 + level * 1.2) + sweep * 0.1;
    // Soft edge to the ball, and fade in after respawning.
    colour *= smoothstep(1.0, 0.82, depth) * saturate(particle.age * 1.5);

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye) - 0.3, 0.09);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 2.0) * (0.012 + Bass * 0.015);
    return particles(uv) + previous(uv) * 0.55 + float3(0.4, 0.5, 0.6) * haze;
}
