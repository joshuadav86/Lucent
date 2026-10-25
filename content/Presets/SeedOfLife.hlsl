// Name: Seed of Life
// Streams of light draw the Flower of Life the way it's drawn by hand, one compass circle at a time, then rule Metatron's Cube across it; every line trembles like a plucked string with the music, until the figure drifts away like smoke and is drawn again.
// Family: Particles
// Tags: Geometric, Calm, Neon
// Added: 2026-10-17 01:42
// Particles: 262144

// One full drawing, hold and fade takes this long, in seconds.
static const float Cycle = 42.0;
// The radius of each circle of the flower, in p units.
static const float Radius = 0.29;

// The figure is made of 98 strokes: the 19 circles of the Flower of Life (the centre, a ring of 6
// and a ring of 12), the great circle round them, and the 78 lines of Metatron's Cube joining
// the 13 points at its heart. Circles get more particles than lines, as they're longer.
static const uint CircleSlots = 22;   // 19 circles, plus 3 slots for the great circle
static const uint LineCount = 78;

// Where circle k of the flower is centred.
float2 circleCentre(uint k)
{
    if (k == 0u)
    {
        return 0.0;
    }
    if (k < 7u)
    {
        return rotate(float2(0.0, -Radius), (k - 1u) * PI / 3.0);
    }
    // The outer ring alternates between two steps out along each direction and the gaps between.
    uint j = k - 7u;
    float angle = j * PI / 6.0;
    float distance = (j & 1u) == 0u ? 2.0 * Radius : sqrt(3.0) * Radius;
    return rotate(float2(0.0, -distance), angle);
}

// The 13 points of Metatron's Cube: the centre, and two hexagons of circle centres.
float2 metatronPoint(uint k)
{
    if (k == 0u)
    {
        return 0.0;
    }
    float distance = k < 7u ? Radius : 2.0 * Radius;
    return rotate(float2(0.0, -distance), ((k - 1u) % 6u) * PI / 3.0);
}

// The two points joined by line n of the cube (every pair of the 13).
void lineEnds(uint n, out float2 a, out float2 b)
{
    uint first = 0u;
    uint remaining = n;
    [loop]
    for (uint i = 0u; i < 12u; i++)
    {
        uint pairs = 12u - i;
        if (remaining < pairs)
        {
            first = i;
            break;
        }
        remaining -= pairs;
    }
    a = metatronPoint(first);
    b = metatronPoint(first + 1u + remaining);
}

// When stroke number s starts being drawn, and how long it takes, in seconds of the cycle.
void strokeTiming(uint stroke, out float start, out float duration)
{
    if (stroke == 0u)
    {
        start = 0.5;
        duration = 2.0;
    }
    else if (stroke < 7u)
    {
        start = 2.0 + (stroke - 1u) * 1.0;
        duration = 1.6;
    }
    else if (stroke < 19u)
    {
        start = 8.4 + (stroke - 7u) * 0.6;
        duration = 1.4;
    }
    else if (stroke == 19u)
    {
        start = 16.0;
        duration = 3.5;
    }
    else
    {
        start = 20.0 + (stroke - 20u) * 0.075;
        duration = 0.9;
    }
}

float cycleTime()
{
    return wrap(Time + 30.0, Cycle);
}

// Which stroke a particle belongs to, and how far along it (0..1).
void strokeOf(uint id, out uint stroke, out float along)
{
    along = fixedRandom(id, 1.0);
    uint slot = (uint)(fixedRandom(id, 2.0) * 100.0);
    if (slot < CircleSlots)
    {
        stroke = min(slot, 19u);
    }
    else
    {
        stroke = 20u + min(slot - CircleSlots, LineCount - 1u);
    }
}

// Where a particle sits on its stroke once it's drawn. Finished circles keep slowly flowing round,
// and every stroke trembles with the music like a plucked string.
float2 strokePoint(uint stroke, float along, float sinceDone)
{
    // Each stroke rings like a plucked string: a smooth standing wave whose height follows its
    // part of the spectrum.
    float level = spectrum(frac(stroke * 0.161) * 0.8 + 0.05);
    float harmonic = stroke < 20u ? 6.0 + (stroke % 3u) * 3.0 : 1.0 + (stroke % 3u);
    float wobble = sin(along * harmonic * 2.0 * PI) * sin(Time * (5.0 + (stroke % 5u)) + stroke) * (0.001 + level * level * 0.012);
    if (stroke < 20u)
    {
        float2 centre = stroke == 19u ? 0.0 : circleCentre(stroke);
        float radius = stroke == 19u ? 3.0 * Radius : Radius;
        float startAngle = atan2(-centre.y, -centre.x);
        float angle = startAngle + along * 2.0 * PI + max(sinceDone, 0.0) * 0.04;
        return centre + float2(cos(angle), sin(angle)) * (radius + wobble);
    }
    float2 a;
    float2 b;
    lineEnds(stroke - 20u, a, b);
    float2 across = normalize(float2(a.y - b.y, b.x - a.x) + 1e-5);
    return lerp(a, b, along) + across * wobble;
}

// The whole figure turns slowly and swells a touch with the bass.
float2 toScreen(float2 q)
{
    return rotate(q, Time * 0.015) * (1.0 + Bass * 0.03);
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = float3(0.0, 0.0, 0.0);
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    uint stroke;
    float along;
    strokeOf(id, stroke, along);
    float start;
    float duration;
    strokeTiming(stroke, start, duration);
    float t = cycleTime();

    // How far the pen has got along this stroke: a particle appears as the pen passes it.
    float pen = saturate((t - start) / duration);
    float drawn = along < pen ? 1.0 : 0.0;
    float2 place = toScreen(strokePoint(stroke, along, t - start - duration));

    // Near the end of the cycle the figure lets go and drifts away like smoke.
    float fadeStart = Cycle - 9.0;
    if (t > fadeStart && particle.data.x > 0.5)
    {
        float2 drift = curlNoise2(particle.position.xy * 2.2, Time * 0.1) * 0.08 + particle.position.xy * 0.05;
        particle.position.xy += drift * min(DeltaTime, 0.05) * (1.0 + Bass * 2.0);
        particle.data.y = saturate((t - fadeStart) / 8.0);
        return;
    }

    particle.position.xy = place;
    // How long ago the pen passed (for the bright, hot freshly drawn line), and whether it's drawn.
    particle.position.z = drawn > 0.5 ? t - start - along * duration : -1.0;
    particle.data.x = drawn;
    particle.data.y = 0.0;
}

Sprite Show(Particle particle, uint id)
{
    uint stroke;
    float along;
    strokeOf(id, stroke, along);
    float drawn = particle.data.x;
    float fade = particle.data.y;
    float sincePen = particle.position.z;

    // Colours by ring: a golden centre, rose and violet rings, an ice-blue great circle and
    // white-gold lines. Each ring glows with its own part of the spectrum.
    float ring = stroke == 0u ? 0.0 : stroke < 7u ? 1.0 : stroke < 19u ? 2.0 : stroke == 19u ? 3.0 : 4.0;
    float hue = ring == 0.0 ? 0.11 : ring == 1.0 ? 0.95 : ring == 2.0 ? 0.78 : ring == 3.0 ? 0.55 : 0.12;
    float saturation = ring == 4.0 ? 0.35 : 0.75;
    float level = spectrum(ring * 0.18 + 0.03);
    float3 colour = hsv2rgb(hue + Time * 0.006, saturation, 1.0);

    // A ripple of light runs outward from the centre on every beat.
    float2 q = particle.position.xy;
    float wave = exp(-abs(length(q) - (1.0 - Beat) * 1.1) * 18.0) * Beat;

    // The pen tip burns white and the line cools behind it.
    float hot = sincePen >= 0.0 ? exp(-sincePen * 6.0) : 0.0;
    colour = lerp(colour, float3(1.0, 0.95, 0.85), hot * 0.8);

    // Each stroke has the same number of particles (three times as many for the great circle),
    // so short strokes are dimmed to match the light of the long ones.
    float strokeLength = 2.0 * PI * Radius;
    if (stroke >= 20u)
    {
        float2 a;
        float2 b;
        lineEnds(stroke - 20u, a, b);
        strokeLength = length(a - b);
    }
    float density = strokeLength / (2.0 * PI * Radius);
    float brightness = 0.42 * density * (ring == 4.0 ? 0.6 : 1.0) * (0.6 + level * 1.2) * (1.0 + hot * 6.0 + wave * 2.0) * (1.0 - fade);
    brightness *= 1.0 + step(0.996, fixedRandom(id, 3.0)) * Treble * 10.0;

    Sprite sprite = (Sprite)0;
    sprite.position = q;
    sprite.size = drawn > 0.5 && fade < 1.0 ? lerp(0.8, 1.6, fade) : 0.0;
    sprite.colour = colour * brightness;
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // Trails make the drifting smoke soft; the drawn figure stays sharp.
    return particles(uv) + previous(uv) * 0.6;
}
