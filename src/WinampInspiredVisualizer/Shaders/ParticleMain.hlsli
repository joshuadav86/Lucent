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

    // Dots smaller than about a pixel would flicker as they cross pixel edges, so they're drawn a
    // little bigger and dimmer, keeping the same total brightness.
    float radius = clamp(sprite.size, 0.75, 32.0);
    float shrink = saturate(sprite.size / radius);
    float2 corner = float2(vertex & 1, vertex >> 1) * 2.0 - 1.0;
    float2 p = sprite.position + corner * radius * 2.0 * pixelSize();

    SpriteVertex output;
    output.position = float4(p.x / Aspect, -p.y, 0.5, 1.0);
    output.corner = corner;
    output.colour = max(sprite.colour, 0.0) * shrink * shrink * ParticleWeight;
    if (!(sprite.size > 0.0) || !all(isfinite(float3(sprite.position, sprite.size))))
    {
        // Hidden: every corner at the same point off the picture, so nothing is drawn.
        output.position = float4(-2.0, -2.0, 0.5, 1.0);
    }

    return output;
}
