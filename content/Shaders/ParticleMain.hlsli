// Appended after a particle visual's code to turn its Spawn, Simulate and Show into the shaders
// that move the particles (CSMain) and place their dots on screen (VSParticle).

RWStructuredBuffer<Particle> ParticleState : register(u0);
StructuredBuffer<Particle> ParticlesToDraw : register(t4);

// The mouse moving over the visuals pushes the dots near it aside, with a little swirl and a tug the way
// it's going, like a hand through sparks. Holding the button down grabs a handful instead: they gather
// into a swirling ball under the mouse and ride along with it, and letting go throws them on with the
// mouse's speed. The push is kept on screen, apart from where the visual puts the particle, so it works
// the same for flat and 3D visuals, the visual keeps moving its particles to the music underneath, and
// it eases back to nothing over a couple of seconds. It reaches every particle visual (anything with
// Simulate), whatever its family.
static const float PushReach = 0.22;     // p units: about a tenth of the picture's height
static const float PushApart = 3.0;      // straight out from the mouse
static const float PushSwirl = 1.2;      // around it
static const float PushTug = 1.6;        // along with it, times its speed
static const float PushSpring = 2.0;     // back to where the visual has it
static const float PushDamping = 2.5;    // low enough that a throw carries
static const float PushMost = 1.6;
static const float GrabReach = 0.3;      // how far the hand reaches for dots
static const float GrabShare = 0.4;      // the share of dots it can pick up, so it holds a handful, not a blinding lump
static const float GrabBall = 0.12;      // the radius of the ball they're held in
static const float GrabPull = 60.0;      // how firmly they're held
static const float GrabFollow = 9.0;     // how closely they match the mouse's speed, which is what they're thrown with
static const float GrabSwirl = 1.5;      // how fast they circle in the hand

void Push(inout Particle particle, uint id)
{
    float4 push = particle.push;
    if (PointerStrength <= 0.0 && PointerGrab <= 0.0 && !any(push != 0.0))
    {
        return;
    }

    float2 velocity = push.zw;
    if (PointerStrength > 0.0 || PointerGrab > 0.0)
    {
        Sprite sprite = Show(particle, id);
        float2 away = sprite.position + push.xy - PointerPosition;
        float distanceSquared = dot(away, away);
        float2 outward = away * rsqrt(distanceSquared + 1e-4);
        bool shown = sprite.size > 0.0 && all(isfinite(sprite.position));
        bool grabbable = fixedRandom(id, 57.0) < GrabShare;

        float near = exp(-distanceSquared / (PushReach * PushReach)) * PointerStrength * (1.0 - PointerGrab);
        if (shown && near > 1e-3)
        {
            // Each dot gets a slightly different shove, so the crowd parts raggedly rather than as one sheet.
            float own = 0.75 + 0.5 * fixedRandom(id, 91.0);
            velocity += (outward * PushApart + float2(-outward.y, outward.x) * PushSwirl + PointerVelocity * PushTug) * near * own * DeltaTime;
        }

        float held = exp(-distanceSquared / (GrabReach * GrabReach)) * PointerGrab;
        if (shown && grabbable && held > 1e-3)
        {
            // Pulled to its own distance from the mouse, so the handful is a ball rather than a point,
            // circling it, and moving with it.
            float radius = GrabBall * sqrt(fixedRandom(id, 23.0));
            float distance = sqrt(distanceSquared);
            float2 pull = -outward * (distance - radius) * GrabPull;
            float2 circle = float2(-outward.y, outward.x) * GrabSwirl * (fixedRandom(id, 41.0) < 0.5 ? -1.0 : 1.0);
            velocity += (pull + (PointerVelocity + circle - velocity) * GrabFollow) * held * DeltaTime;
        }
    }

    velocity -= push.xy * PushSpring * (1.0 - PointerGrab) * DeltaTime;
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
        // A dot the mouse has moved keeps its push through being born again (the visual's own dots
        // often live under a second), so it carries on smoothly instead of jumping back into place.
        fresh.push = ParticleReset > 0.5 ? 0.0 : particle.push;
        particle = fresh;
        Push(particle, id);
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
    // Moved by the mouse: drawn where it's been pushed, blurred along the way it's going so the motion reads.
    sprite.position += particle.push.xy;
    sprite.streak += particle.push.zw * 0.03;

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
