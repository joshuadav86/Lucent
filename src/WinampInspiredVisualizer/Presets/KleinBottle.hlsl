// Name: Klein Bottle
// A glass-like Klein bottle drawn in streams of light: particles pour down its narrow neck, slip through its own wall and flow out round the belly back to the mouth, turning inside to outside on every lap; the bass drives the flow and beats send bright rings sliding down the bottle.
// Family: Particles
// Tags: Geometric, 3D

// A Klein bottle is a surface with only one side. Ours is the classic "bottle" shape: its neck
// bends round and passes in through the wall to join the base from inside. u runs along the
// bottle from the lip, down the neck, through the wall and round the body (0 to pi), v runs round
// its girth. At the end of a lap the surface joins its start mirrored, so a particle carries on
// with v turned over: what was the outside is now the inside.
static const float Streams = 56.0;

float3 bottlePoint(float u, float v)
{
    float cu = cos(u);
    float su = sin(u);
    float cv = cos(v);
    float sv = sin(v);
    float c2 = cu * cu;
    float c4 = c2 * c2;
    float c6 = c4 * c2;
    float x = -2.0 / 15.0 * cu * (3.0 * cv - 30.0 * su + 90.0 * c4 * su - 60.0 * c6 * su + 5.0 * cu * cv * su);
    float y = -1.0 / 15.0 * su * (3.0 * cv - 3.0 * c2 * cv - 48.0 * c4 * cv + 48.0 * c6 * cv - 60.0 * su
        + 5.0 * cu * cv * su - 5.0 * c2 * cu * cv * su - 80.0 * c4 * cu * cv * su + 80.0 * c6 * cu * cv * su);
    float z = 2.0 / 15.0 * (3.0 + 5.0 * cu * su) * sv;
    // Centre it and stand it upright (the bottle's length along y).
    // (Made a little plumper front to back than the textbook shape, so the belly reads well.)
    return float3(x - 0.15, y - 2.1, z * 1.5) * 0.55;
}

// The surface's normal, by stepping a little along u and v.
float3 bottleNormal(float u, float v)
{
    float3 du = bottlePoint(u + 0.01, v) - bottlePoint(u - 0.01, v);
    float3 dv = bottlePoint(u, v + 0.01) - bottlePoint(u, v - 0.01);
    return normalize(cross(du, dv) + 1e-6);
}

void Spawn(inout Particle particle, uint id)
{
    // Streams evenly round the girth, particles spread along them.
    float v = (floor(fixedRandom(id, 1.0) * Streams) + 0.5) / Streams * 2.0 * PI;
    v += (fixedRandom(id, 2.0) - 0.5) * 0.012;
    float u = fixedRandom(id, 3.0) * PI;
    particle.data = float4(u, v, 0.0, 0.0);
    particle.position = bottlePoint(u, v);
    particle.velocity = 0.0;
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float u = particle.data.x;
    float v = particle.data.y;

    // Move a steady distance along the surface: slow down in u where the bottle is long and
    // stretched (round the belly), so the flow looks even.
    float stretch = length(bottlePoint(u + 0.01, v) - bottlePoint(u - 0.01, v)) / 0.02;
    float speed = (0.16 + Bass * 0.5 + Beat * 0.2) * (0.85 + fixedRandom(id, 4.0) * 0.3);
    u += speed * dt / max(stretch, 0.2);
    // A slow corkscrew round the girth.
    v += dt * (0.05 + Mid * 0.1);

    // Round the end of a lap: carry on from the start with v mirrored.
    if (u > PI)
    {
        u -= PI;
        v = PI - v;
    }
    v = wrap(v, 2.0 * PI);
    particle.data.x = u;
    particle.data.y = v;

    float3 moved = bottlePoint(u, v);
    particle.velocity = (moved - particle.position) / max(DeltaTime, 1e-4);
    // Don't streak across the seam when a lap wraps round.
    if (length(particle.velocity) * dt > 0.2)
    {
        particle.velocity = 0.0;
    }
    particle.position = moved;
}

float3 cameraEye()
{
    float angle = Time * 0.07 + 0.6;
    return float3(sin(angle) * 3.6, -0.5 + sin(Time * 0.05) * 0.6, cos(angle) * 3.6);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = float3(0.0, 0.05, 0.0);
    float zoom = 2.0 + Bass * 0.04;
    float3 seen = project(particle.position, eye, target, zoom, 0.0);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, target, zoom, 0.0);

    float u = particle.data.x;
    float v = particle.data.y;

    // Like thin glass, the surface glows brightest where we see it edge on.
    float3 normal = bottleNormal(u, v);
    float3 view = normalize(particle.position - eye);
    float facing = abs(dot(normal, view));
    float rim = 0.35 + 0.9 * pow(1.0 - facing, 3.0);

    // Colour runs along the bottle: deep blue at the lip, through violet to warm rose round the
    // belly, each part brightening with its own band of the spectrum.
    float along = u / PI;
    float3 colour = lerp(float3(0.25, 0.5, 1.0), float3(0.7, 0.35, 1.0), smoothstep(0.0, 0.5, along));
    colour = lerp(colour, float3(1.0, 0.45, 0.45), smoothstep(0.45, 1.0, along));
    float level = spectrum(0.04 + along * 0.6);

    // Rings slide down the bottle on each beat.
    float ring = pow(saturate(cos((along - frac(BeatCount * 0.25)) * 2.0 * PI * 3.0) * 0.5 + 0.5), 30.0) * Beat;
    colour *= rim * 0.08 * (0.6 + level * 1.2) + ring * 0.2;
    colour *= 1.0 + step(0.996, fixedRandom(id, 6.0)) * Treble * 5.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    // Focus on the near side of the bottle so the far side melts into soft light.
    focus(sprite, seen.z, length(eye - target) - 0.4, 0.12);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 1.5) * (0.015 + Bass * 0.02);
    return particles(uv) + previous(uv) * 0.6 + float3(0.45, 0.4, 0.9) * haze;
}
