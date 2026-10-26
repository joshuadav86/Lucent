// Appended after a particle visual's code to turn its Spawn, Simulate and Show into the shaders
// that move the particles (CSMain) and place their dots on screen (VSParticle).

RWStructuredBuffer<Particle> ParticleState : register(u0);
StructuredBuffer<Particle> ParticlesToDraw : register(t4);

[numthreads(256, 1, 1)]
void CSMain(uint3 thread : SV_DispatchThreadID)
{
    uint id = thread.x;
    if (id >= (uint)ParticleCount)
    {
        return;
    }

    Particle particle = ParticleState[id];
    particle.life -= DeltaTime;
    particle.age += DeltaTime;

    // A particle whose time is up (or whose numbers went bad) starts again. On a visual's first
    // frame every particle spawns at once; ParticleReset is 1 then, so Spawn can spread them out.
    if (ParticleReset > 0.5 || !(particle.life > 0.0) || !all(isfinite(particle.position)))
    {
        Particle fresh = (Particle)0;
        Spawn(fresh, id);
        particle = fresh;
    }
    else
    {
        Simulate(particle, id);
    }

    ParticleState[id] = particle;
}

SpriteVertex VSParticle(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    Sprite sprite = Show(ParticlesToDraw[instance], instance);

    // Sizes are in pixels of a 1080-line screen and scale with the window, so the picture looks the
    // same at every resolution, just sharper. Dots smaller than about a real pixel would flicker as
    // they cross pixel edges, so they're drawn a little bigger and dimmer, keeping the same light.
    float pixels = sprite.size * spritePixel() / pixelSize();
    float radius = clamp(pixels, 0.75, 32.0 * spritePixel() / pixelSize());
    float shrink = saturate(pixels / radius);
    float2 corner = float2(vertex & 1, vertex >> 1) * 2.0 - 1.0;

    // A streak is the same dot drawn out along a line: a longer square, with the light spread along it.
    float square = radius * 2.0 * pixelSize();
    float streakLength = length(sprite.streak);
    float2 along = streakLength > 1e-6 ? sprite.streak / streakLength : float2(1.0, 0.0);
    float reach = min(streakLength * 0.5 / square, 64.0);
    corner.x *= 1.0 + reach;
    float2 p = sprite.position + (along * corner.x + float2(-along.y, along.x) * corner.y) * square;

    SpriteVertex output;
    output.position = float4(p.x / Aspect, -p.y, 0.5, 1.0);
    output.corner = corner;
    output.reach = reach;
    output.colour = max(sprite.colour, 0.0) * shrink * shrink * ParticleWeight / (1.0 + reach * 3.19);
    if (!(sprite.size > 0.0) || !all(isfinite(float4(sprite.position, sprite.size, reach))))
    {
        // Hidden: every corner at the same point off the picture, so nothing is drawn.
        output.position = float4(-2.0, -2.0, 0.5, 1.0);
    }

    return output;
}
