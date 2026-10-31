// Appended after a particle visual's code to turn its Spawn, Simulate and Show into the shaders
// that move the particles (CSMain) and place their dots on screen (VSParticle).

RWStructuredBuffer<Particle> ParticleState : register(u0);
StructuredBuffer<Particle> ParticlesToDraw : register(t4);

// The mouse moving over the visuals pushes the dots near it aside, with a little swirl and a tug the way
// it's going, like a hand through sparks. The push is kept on screen, apart from where the visual puts the
// particle, so it works the same for flat and 3D visuals, the visual keeps moving its particles to the music
// underneath, and it eases back to nothing over a couple of seconds.
//
// Holding the button down lifts the patch of dots under it, all of it, into a ball that turns slowly and
// rides with the mouse; letting go throws the ball on with the mouse's speed, and the dots settle back home
// as it fades. The ball is worked out afresh each frame from where the visual draws its dots (Lift), so the
// whole handful moves as one and stays as crisp as the visual is. (Grabbing dots one by one, each with its
// own pull and swirl, scattered them into a haze when thrown.)
//
// Pushing dots one by one needs dots that keep their place from one frame to the next. A visual whose dots
// don't (dust that jumps to a new spot on its shape every frame, like an attractor's or a fractal's, sparks
// that live only moments, or a fast flow) says so with
//     #define MOUSE_MOVES_PLACES
// above its Spawn, and then the dust just parts around the moving mouse, worked out afresh each frame too
// (Part); pushing jumping dots one by one only smeared their push across the picture.
//
// It reaches every particle visual (anything with Simulate), whatever its family.
static const float PushReach = 0.22;     // p units: about a tenth of the picture's height
static const float PushApart = 3.0;      // straight out from the mouse
static const float PushSwirl = 1.2;      // around it
static const float PushTug = 1.6;        // along with it, times its speed
static const float PushSpring = 2.0;     // back to where the visual has it
static const float PushDamping = 2.5;
static const float PushMost = 1.6;
static const float PushJump = 0.25;      // a dot the visual moves further than this in a frame has jumped, and drops its push
static const float PushStreak = 0.012;   // seconds of a pushed dot's motion drawn as a streak, so it reads as moving
static const float PushStreakMost = 0.02; // and no longer than this (p units), so a quick flick stays a dot, not a smear
static const float PartReach = 0.6;      // MOUSE_MOVES_PLACES: how far the dust parts, as a share of PushReach
static const float GrabReach = 0.3;      // the radius of the patch the button lifts
static const float BallSize = 0.15;      // the radius of the ball it's packed into
static const float BallLight = 0.75;     // the packed patch is dimmed a little, so the ball glows rather than blinds

#ifdef MOUSE_MOVES_PLACES
// Where a dot the visual draws at p shows once the moving mouse has parted the dust around it: a smooth,
// steady bend with a slight turn, so it stays crisp.
float2 Part(float2 p)
{
    if (PointerStrength <= 0.0)
    {
        return p;
    }

    float2 away = p - PointerPosition;
    float distanceSquared = dot(away, away);
    float near = exp(-distanceSquared / (PushReach * PushReach)) * PointerStrength * (1.0 - PointerGrab);
    float2 outward = away * rsqrt(distanceSquared + 1e-6);
    return p + (outward + float2(-outward.y, outward.x) * 0.3) * PushReach * PartReach * near;
}
#endif

// Where dot id, drawn at p, shows while a handful is held or thrown (see above), and how much to scale its
// light by. Each dot is either lifted whole or left where it is, so none is left stretched between the gap
// and the ball; how far out from the middle a dot still gets lifted varies dot to dot, so the gap (and the
// ball) thin out softly at the edge instead of being cut out with a hard rim.
float2 Lift(float2 p, uint id, inout float light)
{
    if (BallHeld <= 0.0)
    {
        return p;
    }

    float2 from = p - BallFrom;
    float reach = GrabReach * lerp(0.7, 1.0, fixedRandom(id, 61.0));
    if (dot(from, from) >= reach * reach)
    {
        return p;
    }

    float2 held = BallPosition + rotate(from, BallTurn) * (BallSize / GrabReach);
    light *= lerp(1.0, BallLight, BallHeld);
    return lerp(p, held, BallHeld);
}

void Push(inout Particle particle, uint id)
{
#ifdef MOUSE_MOVES_PLACES
    // This visual's dust is parted where it's drawn (Part), not pushed dot by dot.
    particle.push = 0.0;
#else
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
        float2 outward = away * rsqrt(distanceSquared + 1e-4);
        bool shown = sprite.size > 0.0 && all(isfinite(sprite.position));
        // Holding the button lifts a handful instead (Lift), so the push rests meanwhile.
        float near = exp(-distanceSquared / (PushReach * PushReach)) * PointerStrength * (1.0 - PointerGrab);
        if (shown && near > 1e-3)
        {
            // Each dot gets a slightly different shove, so the crowd parts raggedly rather than as one sheet.
            float own = 0.75 + 0.5 * fixedRandom(id, 91.0);
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
#endif
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
        // Born again somewhere new, it starts where the visual puts it; a push from its last life would
        // only drop it somewhere odd. (Visuals whose dots live only moments use MOUSE_MOVES_PLACES.)
        particle = fresh;
        Push(particle, id);
    }
    else
    {
#ifdef MOUSE_MOVES_PLACES
        Simulate(particle, id);
#else
        // A pushed dot the visual sends somewhere else entirely (wrapping round an edge, or recycled from near
        // to far) drops its push: carried over there it would only scatter the push across the picture.
        bool pushed = any(particle.push != 0.0);
        float2 before = pushed ? Show(particle, id).position : 0.0;
        Simulate(particle, id);
        if (pushed && !(distance(Show(particle, id).position, before) < PushJump))
        {
            particle.push = 0.0;
        }
#endif
        Push(particle, id);
    }

    ParticleState[id] = particle;
}

SpriteVertex VSParticle(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    Particle particle = ParticlesToDraw[instance];
    Sprite sprite = Show(particle, instance);
    // Moved by the mouse: pushed dots are drawn where they've been pushed, drawn out a touch along the way
    // they're going so the motion reads, but never so far it smears; then a held or thrown handful is lifted.
#ifdef MOUSE_MOVES_PLACES
    sprite.position = Part(sprite.position);
#else
    sprite.position += particle.push.xy;
    float2 pushStreak = particle.push.zw * PushStreak;
    float pushStreakLength = length(pushStreak);
    sprite.streak += pushStreakLength > PushStreakMost ? pushStreak * (PushStreakMost / pushStreakLength) : pushStreak;
#endif
    float light = 1.0;
    sprite.position = Lift(sprite.position, instance, light);
    sprite.colour *= light;

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
