// Name: Physarum
// Three colonies of glowing slime mould spread across the dark, each following its own scent until they knit together into living, branching networks of rose, cyan and gold. Each colony glows with its own part of the music, rose for the bass, cyan for the middle and gold for the treble, and all of them run faster the louder it gets. On every beat the whole network veers together, one way and then the other, so its strands snake in time, and ripples of brightness run out along the strands, each in its own colour, from three places that wander. The bass scatters food there for them to race towards.
// Family: Particles
// Tags: Colourful
// Added: 2026-10-17 22:02
// Particles: 393216

// Slime mould finds its way without a brain. Each agent here is a tiny explorer that smells the
// trail just ahead of it, a little to the left and a little to the right, turns towards the
// strongest scent, steps forward and leaves a little scent of its own. The trail is simply the
// picture itself, read back from the last frame, slowly spreading and fading. Agents of the same
// colony like each other's scent and avoid the others', so the colonies weave separate networks.
static const float Colonies = 3.0;
static const float SenseReach = 0.03;    // how far ahead an agent smells, in p units
static const float SenseAngle = 0.78;    // how far to the side its left and right sensors point
static const float TurnRate = 0.5;       // how sharply it turns towards the scent, per 60th of a second
static const float Speed = 0.16;         // p units per second

float3 colonyColour(float colony)
{
    if (colony < 0.5) return float3(1.0, 0.18, 0.5);    // rose
    if (colony < 1.5) return float3(0.1, 0.7, 1.0);     // cyan
    return float3(1.0, 0.72, 0.1);                       // gold
}

// How much a colony likes the trail it smells. These weights untangle the three colours mixed in
// the picture: a colony's own scent counts +1, each of the others' -0.6, so they keep apart.
float3 scentWeights(float colony)
{
    if (colony < 0.5) return float3(0.959, -2.291, 0.908);
    if (colony < 1.5) return float3(-1.061, 0.54, 0.728);
    return float3(-0.019, 1.659, -1.76);
}

// One part of the music, with the high notes made up to stand level with the low ones.
float phPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as
// plainly as a loud one.
float phLoud(float x)
{
    float sound = (phPart(0.08) + phPart(0.26) + phPart(0.44) + phPart(0.62)) * 0.25;
    return saturate((phPart(x) - 0.62 * sound) / (0.6 * sound + 0.1));
}

// Where one of the three feeding places is. They wander slowly.
float2 foodPlace(float i)
{
    return float2(sin(Time * 0.071 + i * 2.1) * Aspect * 0.7, sin(Time * 0.053 + i * 1.7 + 0.6) * 0.7);
}

// Food: soft pools of scent, unseen, that swell with the bass. Every colony loves them.
float foodAt(float2 p)
{
    float total = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
        total += glow(length(p - foodPlace(i)), 14.0);
    return total * (Bass * Bass + Beat * 0.15);
}

// The ripple each beat sends out from the feeding places: 1 on its crest, which widens as the beat goes by and
// fades before the next. With a steady beat to follow the ripple keeps pace with it; without one it follows how
// far the last beat has faded.
float rippleAt(float2 p)
{
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float fade = smoothstep(0.0, 0.1, since) * (1.0 - since);
    float nearest = 100.0;
    [unroll]
    for (int i = 0; i < 3; i++)
        nearest = min(nearest, length(p - foodPlace(i)));
    float crest = nearest - since * 1.1;
    return fade * exp(-crest * crest * 22.0);
}

float smell(float2 at, float3 weights)
{
    return dot(previous(toUv(at)), weights) + foodAt(at) * 0.6;
}

// Wraps a position round the edges of the screen, so the networks have no walls.
float2 wrapScreen(float2 v)
{
    float2 size = float2(Aspect, 1.0) * 2.0;
    return v - size * floor(v / size + 0.5);
}

void Spawn(inout Particle particle, uint id)
{
    // Agents start scattered everywhere, heading every which way; the networks form by themselves.
    float colony = floor(fixedRandom(id, 1.0) * Colonies);
    float angle = random(id, 2.0) * 2.0 * PI;
    particle.position = float3((random(id, 3.0) * 2.0 - 1.0) * Aspect, random(id, 4.0) * 2.0 - 1.0, 0.0);
    particle.velocity = 0.0;
    particle.data = float4(angle, colony, 0.0, 0.0);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float steps = dt * 60.0;
    float heading = particle.data.x;
    float3 weights = scentWeights(particle.data.y);
    float2 at = particle.position.xy;

    // Sniff ahead, left and right.
    float ahead = smell(at + float2(cos(heading), sin(heading)) * SenseReach, weights);
    float left = smell(at + float2(cos(heading - SenseAngle), sin(heading - SenseAngle)) * SenseReach, weights);
    float right = smell(at + float2(cos(heading + SenseAngle), sin(heading + SenseAngle)) * SenseReach, weights);

    float turn = TurnRate * steps;
    float wander = (random(id, 4.0) - 0.5) * 0.25 * steps;
    if (ahead >= left && ahead >= right)
    {
        heading += wander * 0.3;
    }
    else if (ahead < left && ahead < right)
    {
        heading += (random(id, 5.0) < 0.5 ? -turn : turn);
    }
    else
    {
        heading += left > right ? -turn : turn;
    }
    heading += wander * 0.2;

    // On each beat the whole colony veers together, one way on one beat and the other way on the next, so
    // the networks snake in time with the music.
    float way = wrap(BeatCount, 2.0) < 0.5 ? 1.0 : -1.0;
    heading += way * BeatSwell * 2.6 * dt * (0.6 + 0.8 * fixedRandom(id, 8.0));

    float pace = Speed * (0.7 + Level * 1.1 + BeatSwell * 0.3) * (0.85 + fixedRandom(id, 7.0) * 0.3);
    float2 stride = float2(cos(heading), sin(heading)) * pace * dt;
    particle.position.xy = wrapScreen(at + stride);
    particle.velocity.xy = stride / max(dt, 1e-4);
    particle.data.x = wrap(heading, 2.0 * PI);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    sprite.position = particle.position.xy;
    sprite.size = 1.0;
    // Each colony glows with its own part of the music: rose for the bass, cyan for the middle, gold for the
    // treble. Where a beat's ripple is passing, the strand shines brighter in its own colour.
    float level = phLoud(0.08 + particle.data.y * 0.27);
    float ripple = rippleAt(particle.position.xy);
    sprite.colour = colonyColour(particle.data.y) * 0.022 * (0.4 + level * 1.7 + BeatSwell * 0.4 + ripple * 1.2);
    sprite.colour *= saturate(particle.age * 2.0);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The scent spreads to its neighbours a little each frame and slowly fades away.
    float2 texel = max(RenderScale, 1.0) / Resolution;
    float3 spread = previous(uv) * 0.6
                  + (previous(uv + float2(texel.x, 0.0)) + previous(uv - float2(texel.x, 0.0))
                   + previous(uv + float2(0.0, texel.y)) + previous(uv - float2(0.0, texel.y))) * 0.1;
    float3 trail = spread * 0.93;

    // A very faint floor so the empty space reads as deep, not dead.
    float3 floorTone = float3(0.0006, 0.0004, 0.0012);
    return trail + particles(uv) + floorTone;
}
