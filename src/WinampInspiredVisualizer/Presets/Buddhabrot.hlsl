// Name: Buddhabrot
// A ghostly seated figure condenses out of the dark like a nebula, its veils of violet, teal and gold built up point by point by countless sparks tracing their escape from the Mandelbrot set. The bass deepens its outer glow while the treble kindles the finest threads.
// Family: Particles
// Tags: Space, Calm
// Particles: 458752

// The Buddhabrot is the Mandelbrot set seen from the other side. Instead of colouring the starting
// points, it follows every point that eventually escapes and marks where it travels on the way
// out. Each particle here is one of those journeys: it picks a starting point c that escapes,
// then steps along its path z -> z^2 + c one hop per frame, leaving a mark at each stop. The marks
// pile up in the trail buffer into a soft density picture. Journeys are coloured by their length,
// like the "Nebulabrot": short ones violet, middling ones teal, the longest a warm gold.
static const float MinimumHops = 14.0;

int longestJourney()
{
    return qualitySteps(1200);
}

// How many hops the journey from c takes to escape, or 0 if it never does.
float journeyLength(float2 c)
{
    // Skip the two big solid regions straight away: nothing inside them ever escapes.
    float2 q = c - float2(0.25, 0.0);
    float qq = dot(q, q);
    if (qq * (qq + q.x) < 0.25 * c.y * c.y || dot(c + float2(1.0, 0.0), c + float2(1.0, 0.0)) < 0.0625)
    {
        return 0.0;
    }
    float2 z = 0.0;
    int limit = longestJourney();
    [loop]
    for (int i = 0; i < limit; i++)
    {
        z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
        if (dot(z, z) > 4.0)
        {
            return i + 1.0;
        }
    }
    return 0.0;
}

// Finds a new journey. Mostly it nudges the last good starting point a little, which keeps the
// particles near the edge of the set where the long, beautiful journeys are; sometimes it jumps
// somewhere fresh so every part of the figure keeps being visited.
void newJourney(inout Particle particle, uint id)
{
    float2 c = particle.velocity.xy;
    float hops = 0.0;
    [loop]
    for (int attempt = 0; attempt < 4; attempt++)
    {
        float2 candidate;
        float fresh = random(id, 10.0 + attempt);
        if (fresh < 0.15 || particle.data.w < 0.5)
        {
            candidate = float2(random(id, 11.0 + attempt) * 3.0 - 2.0, random(id, 12.0 + attempt) * 3.0 - 1.5);
        }
        else
        {
            float reach = 0.002 * exp2(random(id, 13.0 + attempt) * 6.0);
            float angle = random(id, 14.0 + attempt) * 2.0 * PI;
            candidate = c + float2(cos(angle), sin(angle)) * reach;
        }
        float n = journeyLength(candidate);
        if (n >= MinimumHops)
        {
            c = candidate;
            hops = n;
            break;
        }
    }

    // The journey starts at 0, and its first hop always lands on c itself.
    particle.velocity.xy = c;
    particle.position.xy = c;
    particle.data.x = hops;              // the journey's whole length (0 = none found yet; try again next frame)
    particle.data.y = 1.0;               // hops taken so far
    particle.data.w = hops > 0.0 ? 1.0 : particle.data.w;
}

void Spawn(inout Particle particle, uint id)
{
    particle.velocity = 0.0;
    particle.data = 0.0;
    particle.life = 1e6;
    newJourney(particle, id);
    // Start somewhere along the journey so the figure doesn't appear in one synchronised flash.
    float skip = floor(random(id, 2.0) * particle.data.x * 0.9);
    [loop]
    for (int i = 0; i < 1200; i++)
    {
        if (i >= (int)skip)
        {
            break;
        }
        float2 z = particle.position.xy;
        particle.position.xy = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + particle.velocity.xy;
    }
    particle.data.y += skip;
}

void Simulate(inout Particle particle, uint id)
{
    if (particle.data.y + 1.0 >= particle.data.x)
    {
        newJourney(particle, id);
        return;
    }
    float2 z = particle.position.xy;
    particle.position.xy = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + particle.velocity.xy;
    particle.data.y += 1.0;
}

// The camera sways and breathes very slowly. These map the complex plane to the screen (the figure
// sits upright, so the real axis runs down the screen) and back.
float2 cameraShift(float t)
{
    return float2(sin(t * 0.023) * 0.05, cos(t * 0.017) * 0.04);
}

float cameraTurn(float t)
{
    return sin(t * 0.029) * 0.1;
}

float cameraScale(float t)
{
    return 0.72 * (1.0 + 0.07 * sin(t * 0.019));
}

float2 toScreen(float2 z, float t)
{
    float2 upright = float2(z.y, z.x + 0.45);
    return rotate(upright, cameraTurn(t)) * cameraScale(t) + cameraShift(t);
}

float2 fromScreen(float2 s, float t)
{
    float2 upright = rotate((s - cameraShift(t)) / cameraScale(t), -cameraTurn(t));
    return float2(upright.y - 0.45, upright.x);
}

// Violet for short journeys, teal for middling ones, warm gold for the longest.
float3 nebulaColour(float hops, out float band)
{
    float t = saturate(log2(hops / MinimumHops) / log2(1200.0 / MinimumHops));
    band = t;
    float3 violet = float3(0.42, 0.2, 1.0);
    float3 teal = float3(0.15, 0.85, 0.75);
    float3 gold = float3(1.0, 0.72, 0.38);
    return t < 0.4 ? lerp(violet, teal, t / 0.4) : lerp(teal, gold, saturate((t - 0.4) / 0.4));
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    if (particle.data.x < MinimumHops)
    {
        return sprite;
    }
    float2 z = particle.position.xy;
    // Every journey has a mirror image (start from the reflected point and the whole path
    // reflects), so half the particles draw theirs flipped.
    if ((id & 1u) == 1u)
    {
        z.y = -z.y;
    }

    float band;
    float3 colour = nebulaColour(particle.data.x, band);
    // The bass feeds the broad violet glow, the mids the teal, the treble the fine gold threads.
    float music = band < 0.4 ? 0.6 + Bass * 0.9 : (band < 0.75 ? 0.7 + Mid * 0.8 : 0.75 + Treble * 1.2);
    // Short journeys are many and cover a lot of sky; long ones are rarer but trace fine lace,
    // so they're weighted up to hold their own.
    float weight = lerp(0.012, 0.07, band * band);

    sprite.position = toScreen(z, Time);
    sprite.size = 0.8;
    sprite.colour = colour * weight * music * (1.0 + Beat * 0.2);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // Keep the slowly built-up picture lined up with the drifting camera: find where this point
    // of the plane was on screen last frame and carry its light forward.
    float2 z = fromScreen(p, Time);
    float2 before = toScreen(z, Time - DeltaTime);
    float3 kept = previous(toUv(before)) * 0.965;

    // A faint deep-space ground, and a little dust of distant stars.
    float3 ground = float3(0.004, 0.003, 0.01) + float3(0.01, 0.004, 0.02) * glow(length(p), 1.5);
    float2 cell = floor(p * 90.0);
    float star = step(0.997, hash(cell)) * hash(cell + 3.7) * 0.05 * (0.8 + 0.2 * sin(Time * 2.0 + hash(cell) * 40.0));

    return particles(uv) + kept + (ground + star) * 0.035;
}
