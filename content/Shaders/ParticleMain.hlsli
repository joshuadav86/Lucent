// Appended after a particle visual's code to turn its Spawn, Simulate and Show into the shaders
// that move the particles (CSMain) and place their dots on screen (VSParticle).

RWStructuredBuffer<Particle> ParticleState : register(u0);
StructuredBuffer<Particle> ParticlesToDraw : register(t4);

// The mouse moving over the visuals pushes the dots near it aside, with a little swirl and a tug the way
// it's going, like a hand through sparks. The push is kept on screen, apart from where the visual puts
// the particle, so it works the same for flat and 3D visuals, and it eases back to nothing over a couple
// of seconds. PointerStrength is 0 for visuals outside the Particles family.
static const float PushReach = 0.22;     // p units: about a tenth of the picture's height
static const float PushApart = 4.0;      // straight out from the mouse
static const float PushSwirl = 1.5;      // around it
static const float PushTug = 2.0;        // along with it, times its speed
static const float PushSpring = 3.5;     // back to where the visual has it
static const float PushDamping = 4.0;
static const float PushMost = 0.6;

void Push(inout Particle particle, uint id)
{
    float4 push = particle.push;
    if (PointerStrength <= 0.0 && !any(push != 0.0))
    {
        return;
    }

    float2 velocity = push.zw;
    if (PointerStrength > 0.0)
    {
        Sprite sprite = Show(particle, id);
        float2 away = sprite.position + push.xy - PointerPosition;
        float distanceSquared = dot(away, away);
        float near = exp(-distanceSquared / (PushReach * PushReach)) * PointerStrength;
        if (sprite.size > 0.0 && near > 1e-3 && all(isfinite(sprite.position)))
        {
            // Each dot gets a slightly different shove, so the crowd parts raggedly rather than as one sheet.
            float own = 0.75 + 0.5 * fixedRandom(id, 91.0);
            float2 outward = away * rsqrt(distanceSquared + 1e-4);
            velocity += (outward * PushApart + float2(-outward.y, outward.x) * PushSwirl + PointerVelocity * PushTug) * near * own * DeltaTime;
        }
    }

    velocity -= push.xy * PushSpring * DeltaTime;
    velocity *= exp(-PushDamping * DeltaTime);
    push.xy += velocity * DeltaTime;
    float pushed = length(push.xy);
    if (pushed > PushMost)
    {
        push.xy *= PushMost / pushed;
    }

    // Settled back in place: clear it, so the visual runs as it was written.
    push.zw = velocity;
    if (pushed < 1e-4 && dot(velocity, velocity) < 1e-8)
    {
        push = 0.0;
    }

    particle.push = all(isfinite(push)) ? push : 0.0;
}

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
        Push(particle, id);
    }

    ParticleState[id] = particle;
}

SpriteVertex VSParticle(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    Particle particle = ParticlesToDraw[instance];
    Sprite sprite = Show(particle, instance);
    sprite.position += particle.push.xy;

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
