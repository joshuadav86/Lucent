// Name: Golden Angle
// A living seed head grows in the dark: each new seed turns by the golden angle and pushes the older ones out over a slowly spinning dome, weaving the Fibonacci spirals, until the oldest drift away from the rim as glowing pollen on the music.
// Family: Particles
// Tags: Geometric, 3D, Calm, Colourful
// Particles: 262144

// The head always has this many seeds, so its spirals look the same at every Detail setting.
static const uint Seeds = 2048;
// How long a seed takes to travel from the heart of the head to its rim, in seconds.
static const float Journey = 45.0;
// The golden angle as a fraction of a turn (1 - 1/phi), in 32-bit fixed point, so seed numbers
// in the millions still turn by exactly the right amount.
static const uint GoldenTurn = 1640531527u;

// Every seed is itself a tiny floret of particles packed the same golden way: 128 of them on
// Normal detail, fewer or more on the other settings.
uint floretCount()
{
    return max((uint)ParticleCount / Seeds, 1u);
}

// The head's shape: a dome that rises in the middle (y grows downward, so up is negative).
float3 onDome(float radius, float angle, float lift)
{
    float height = (0.3 + lift) * cos(saturate(radius) * PI * 0.5);
    return float3(cos(angle) * radius, -height, sin(angle) * radius);
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = 0.0;
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    uint seeds = Seeds;
    uint florets = floretCount();
    uint rank = min(id / florets, seeds - 1u);
    uint floret = id % florets;

    // New seeds are born at the heart at a steady rate. Each particle stands for "the seed that is
    // rank-th youngest", so as one is born they all shuffle outward by one place.
    float births = Time * seeds / Journey + seeds;
    uint seed = (uint)floor(births) - rank;
    float age = (rank + frac(births)) / seeds;  // 0 newborn at the heart, 1 at the rim

    // Seeds spaced by the golden angle and pushed out as the square root of their age pack
    // perfectly, with no gaps and no lines, and show the famous spirals.
    float turn = (seed * GoldenTurn) / 4294967296.0;
    float angle = turn * 2.0 * PI + Time * 0.05;
    float radius = sqrt(age);

    // Each part of the head breathes with its own band of the spectrum; a beat ripples out.
    float level = spectrum(age * 0.8 + 0.02);
    float ripple = exp(-abs(age - (1.0 - Beat)) * 14.0) * Beat;
    float3 centre = onDome(radius, angle, Bass * 0.06 + ripple * 0.04);

    // The seed's own floret, a little golden disc that swells when its band is loud.
    float floretTurn = (floret * GoldenTurn) / 4294967296.0;
    float size = (0.005 + 0.013 * sqrt(age)) * (0.7 + level * 0.9 + ripple * 0.6);
    float2 offset = float2(cos(floretTurn * 2.0 * PI), sin(floretTurn * 2.0 * PI)) * sqrt((floret + 0.5) / florets) * size;
    float3 position = centre + float3(offset.x, 0.0, offset.y);

    // The oldest seeds let go of the rim and float away like pollen on swirling air.
    float drift = saturate((age - 0.86) / 0.14);
    float3 wander = curlNoise(centre * 2.0 + float3(0.0, Time * 0.08, fixedRandom(id, 1.0) * 0.3));
    position += (wander * 0.35 + float3(centre.x, -0.9, centre.z) * 0.5) * drift * drift * (1.0 + Mid * 0.5);
    position += (float3(fixedRandom(id, 2.0), fixedRandom(id, 3.0), fixedRandom(id, 4.0)) - 0.5) * drift * 0.12;

    particle.position = position;
    particle.data = float4(age, drift, level, ripple);
    // The seed's number for Show's colours, cut down to a size a float holds exactly; 510510 is
    // 13 x 21 x 34 x 55, so its spirals keep their colours.
    particle.velocity.x = (float)(seed % 510510u);
}

// The colour of a seed when the spirals of one Fibonacci family are lit (0..3: 13, 21, 34, 55).
float3 familyColour(uint seed, float family)
{
    uint size = family < 0.5 ? 13u : family < 1.5 ? 21u : family < 2.5 ? 34u : 55u;
    // Stepping across the spirals moves the seed number on by the next Fibonacci number; multiplying
    // by its inverse (modulo size) turns that step into 1, so the colours sweep smoothly round.
    uint inverse = family < 0.5 ? 5u : family < 1.5 ? 13u : family < 2.5 ? 13u : 34u;
    float hue = ((seed * inverse) % size) / (float)size;
    // Each spiral also carries its own band of the spectrum, like an equaliser wound into the head.
    return hsv2rgb(hue + Time * 0.01, 0.75, 0.35 + spectrum(hue * 0.85 + 0.03) * 1.5);
}

float3 cameraEye()
{
    float angle = Time * 0.03;
    float height = 1.75 + sin(Time * 0.041) * 0.25;
    return float3(sin(angle) * 1.1, -height, cos(angle) * 1.1);
}

Sprite Show(Particle particle, uint id)
{
    float age = particle.data.x;
    float drift = particle.data.y;
    float level = particle.data.z;
    float ripple = particle.data.w;

    float3 eye = cameraEye();
    float3 target = float3(0.0, 0.12, 0.0);
    float3 seen = project(particle.position, eye, target, 1.55 + Bass * 0.05, 0.0);

    // Seeds a Fibonacci number apart sit next to each other along one of the spirals, so colouring
    // seeds by their number modulo 13, 21, 34 or 55 gives each spiral of that family its own colour.
    // The head slowly passes from one family to the next, nudged along by the beats.
    uint seed = (uint)particle.velocity.x;
    float phase = Time * 0.05 + BeatCount * 0.015;
    float family = wrap(floor(phase), 4.0);
    float blend = smoothstep(0.65, 1.0, frac(phase));
    float3 colour = lerp(familyColour(seed, family), familyColour(seed, wrap(family + 1.0, 4.0)), blend);
    colour = lerp(colour, float3(1.0, 0.85, 0.6), drift * 0.5);
    uint floret = id % floretCount();
    float heart = floret * 8u < floretCount() ? 1.8 : 1.0;  // the middle of each floret is brightest
    float brightness = 0.14 * heart * (0.6 + level * 0.6 + ripple * 2.0) * saturate(age * 80.0) * (1.0 - drift * 0.6);
    brightness *= 1.0 + step(0.995, fixedRandom(id, 5.0)) * Treble * 8.0;

    Sprite sprite = (Sprite)0;
    sprite.position = seen.xy;
    sprite.size = seen.z > 0.1 && drift < 0.999 ? 0.9 : 0.0;
    sprite.colour = colour * brightness * (1.0 - drift * drift);
    focus(sprite, seen.z, length(eye - target), 0.08);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    return particles(uv) + previous(uv) * 0.45;
}
