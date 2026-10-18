// Name: Aether Flow
// Silky threads of coloured light stream past a glowing glass sphere, parting smoothly round it and wrapping its surface before tumbling into slow swirling eddies behind it; the whole current surges with the bass and the glass glows from within.
// Family: Particles
// Tags: Calm, Colourful, 3D
// Added: 2026-10-17 17:15
// Particles: 262144

// The light flows like an ideal fluid round a ball: the textbook "flow past a sphere", which
// speeds up over the ball's shoulders and closes up smoothly behind it. On top of that, a wake
// of slow corkscrewing eddies trails behind the ball, and a gentle swirl everywhere makes the
// threads meander. Particles are released from a few hundred fixed points upstream, so each
// one traces out a thread, like smoke streams in a wind tunnel. World y is up.
static const float SphereRadius = 1.0;
static const float Inlet = -5.5;
static const float Outlet = 6.5;
static const float Seeds = 380.0;
static const float SeedSpread = 2.3;
static const float Zoom = 1.6;

float flowSpeed()
{
    return 0.75 + Bass * 1.1 + Beat * 0.35 + Level * 0.2;
}

float3 flowAt(float3 position, bool swirling)
{
    float r2 = max(dot(position, position), 1e-4);
    float r = sqrt(r2);
    float a3 = SphereRadius * SphereRadius * SphereRadius;
    // Ideal flow past a sphere, heading along +x.
    float3 velocity = float3(1.0 + a3 / (2.0 * r2 * r), 0.0, 0.0) - position * (1.5 * a3 * position.x / (r2 * r2 * r));
    if (swirling)
    {
        // The wake: a corkscrew twist round the axis behind the ball, plus slow tumbling eddies.
        float rho2 = dot(position.yz, position.yz);
        float wake = smoothstep(-0.2, 1.2, position.x) * exp(-rho2 * 0.55) * exp(-max(position.x, 0.0) * 0.12);
        float3 twist = float3(0.0, -position.z, position.y) * 0.75 * wake;
        float3 eddies = curlNoise(position * 0.7 - float3(Time * 0.12, 0.0, 0.0)) * (0.03 + wake * 0.55);
        velocity += twist + eddies;
    }
    return velocity;
}

// Where thread k starts, on a disc upstream.
float2 seedPoint(float k)
{
    float angle = frac(k * 0.618034) * 2.0 * PI;
    float radius = sqrt((k + 0.5) / Seeds) * SeedSpread;
    return float2(cos(angle), sin(angle)) * radius;
}

void Spawn(inout Particle particle, uint id)
{
    float k = floor(fixedRandom(id, 1.0) * Seeds);
    float2 seed = seedPoint(k) + (float2(random(id, 2.0), random(id, 3.0)) - 0.5) * 0.02;
    float3 position = float3(Inlet - random(id, 4.0) * 0.4, seed);
    if (ParticleReset > 0.5)
    {
        // On the first frame, carry each particle a random way downstream so the threads are
        // already flowing.
        int steps = (int)(random(id, 5.0) * 40.0);
        [loop]
        for (int i = 0; i < steps; i++)
        {
            position += flowAt(position, false) * 0.28;
            float r = length(position);
            position *= max(r, SphereRadius * 1.02) / max(r, 1e-4);
        }
    }
    particle.position = position;
    particle.velocity = 0.0;
    particle.life = 60.0;
    particle.data = float4(k, 0.0, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.04);
    float speed = flowSpeed();
    float3 position = particle.position;
    // A midpoint step keeps the threads smooth round the curve of the ball.
    float3 midway = position + flowAt(position, true) * speed * dt * 0.5;
    float3 velocity = flowAt(midway, true) * speed;
    position += velocity * dt;
    // Never inside the glass.
    float r = length(position);
    position *= max(r, SphereRadius * 1.015) / max(r, 1e-4);
    particle.velocity = velocity;
    particle.position = position;
    if (position.x > Outlet || length(position.yz) > 5.0)
    {
        particle.life = 0.0;
    }
}

float3 cameraEye()
{
    float angle = 0.5 + sin(Time * 0.035) * 0.55;
    return float3(sin(angle) * 5.4 - 0.4, 1.2 + sin(Time * 0.043) * 0.8, -cos(angle) * 5.4);
}

float3 cameraTarget()
{
    return float3(0.9, 0.0, 0.0);
}

float sphereHit(float3 origin, float3 direction, float radius, out float leave)
{
    float b = dot(origin, direction);
    float h = b * b - dot(origin, origin) + radius * radius;
    leave = -1.0;
    if (h < 0.0)
    {
        return -1.0;
    }
    h = sqrt(h);
    leave = -b + h;
    return -b - h;
}

// Silky aurora colours, chosen by where a thread starts.
float3 threadColour(float k)
{
    float2 seed = seedPoint(k);
    float hue = atan2(seed.y, seed.x) / (2.0 * PI) * 0.6 + length(seed) * 0.12 + Time * 0.008;
    return hsv2rgb(0.5 + hue, 0.65, 1.0);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 flip = float3(1.0, -1.0, 1.0);
    float3 seen = project(particle.position * flip, eye * flip, target * flip, Zoom, 0.0);
    float3 seenBefore = project((particle.position - particle.velocity * 0.04) * flip, eye * flip, target * flip, Zoom, 0.0);
    if (seen.z < 0.3)
    {
        return sprite;
    }

    // Hidden behind the ball: Render shows those threads bent through the glass instead.
    float3 toParticle = particle.position - eye;
    float distance = length(toParticle);
    float leave;
    float t = sphereHit(eye, toParticle / distance, SphereRadius, leave);
    if (t > 0.0 && t < distance)
    {
        return sprite;
    }

    // Brighter where the flow quickens over the ball's shoulders, softly faded at both ends.
    float speed = length(particle.velocity) / flowSpeed();
    float ends = smoothstep(Inlet, Inlet + 3.0, particle.position.x) * smoothstep(Outlet, Outlet - 2.0, particle.position.x);
    float3 colour = threadColour(particle.data.x);
    float band = spectrum(frac(particle.data.x * 0.618) * 0.8);
    float brightness = 0.3 * (0.5 + speed * 0.55) * (0.7 + band * 0.9) * ends;

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = 0.85;
    sprite.colour = colour * brightness;
    focus(sprite, seen.z, length(eye), 0.035);
    return sprite;
}

float3 environment(float3 direction)
{
    // Deep blue gloom, with two soft studio lights for the glass to catch.
    float3 colour = lerp(float3(0.004, 0.006, 0.014), float3(0.016, 0.02, 0.04), saturate(direction.y * 0.5 + 0.5));
    colour += float3(1.0, 0.92, 0.85) * pow(saturate(dot(direction, normalize(float3(-0.4, 0.8, -0.45)))), 60.0) * 3.0;
    colour += float3(0.5, 0.65, 1.0) * pow(saturate(dot(direction, normalize(float3(0.7, 0.25, 0.6)))), 30.0) * 1.2;
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);

    float3 background = lerp(float3(0.006, 0.006, 0.016), float3(0.012, 0.01, 0.03), uv.y);
    float3 colour = background + particles(uv) + previous(uv) * 0.45;

    float leave;
    float t = sphereHit(eye, direction, SphereRadius, leave);
    if (t > 0.0)
    {
        float3 position = eye + direction * t;
        float3 normal = position / SphereRadius;
        float facing = saturate(dot(normal, -direction));
        float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);

        // Through the glass: bend the ray in and out, then look up the threads behind along it.
        float3 inside = refract(direction, normal, 1.0 / 1.45);
        float b = dot(position, inside);
        float through = max(-2.0 * b, 0.0);
        float3 exitPoint = position + inside * through;
        float3 exitDirection = refract(inside, -exitPoint / SphereRadius, 1.45);
        if (dot(exitDirection, exitDirection) < 1e-4)
        {
            exitDirection = reflect(inside, -exitPoint / SphereRadius);
        }
        float3 lookAt = exitPoint + exitDirection * 2.5;
        float3 flip = float3(1.0, -1.0, 1.0);
        float3 seen = project(lookAt * flip, eye * flip, target * flip, Zoom, 0.0);
        float2 bentUv = seen.z > 0.0 ? toUv(seen.xy) : uv;
        float3 behind = particles(bentUv) * 1.1 + environment(exitDirection) * 0.25;

        // The glass glows softly from within, most where the light passes through the most of it.
        float pulse = 0.35 + Bass * 0.6 + Beat * 0.25;
        float3 core = lerp(float3(0.3, 0.6, 1.0), float3(0.8, 0.45, 1.0), 0.5 + 0.5 * sin(Time * 0.1));
        float3 glowInside = core * (1.0 - exp(-through * 1.1)) * pulse * 0.06;

        float3 glass = behind * float3(0.85, 0.95, 1.0) * (1.0 - fresnel) + environment(reflect(direction, normal)) * fresnel + glowInside;
        // A thin bright rim where the threads hug the surface.
        glass += core * pow(1.0 - facing, 3.0) * 0.18 * (0.6 + Level);
        // Threads in front of the ball still show over it.
        colour = glass + particles(uv) + previous(uv) * 0.2;
    }
    return colour;
}
