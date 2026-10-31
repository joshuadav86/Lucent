// Name: Epicycle Rosette
// A chain of spinning neon circles, each riding on the rim of the last, traces a glowing rosette with perfect rotational
// symmetry. The music sets the circles: each one's size follows its own band of the spectrum, so the rosette's petals
// grow, sharpen, and loop as the song moves, while a bright pen runs round the curve on the beat. The symmetry slowly
// steps from three-fold to seven-fold and back, the crown of the rosette rises and falls with the mids, and a drop
// throws every circle wide.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-10-31 19:30
// Particles: 262144

// A point turning on a circle that turns on a circle (and so on) traces sum of a_j e^(i w_j t). If every
// frequency w_j is 1 more than a multiple of n, turning t by 2pi/n turns the whole curve by 2pi/n, so the
// curve has n-fold symmetry. The six circles here turn at 1, 1-n, 1+n, 1-2n, 1+2n and 1-3n. Their sizes
// come from the spectrum, bass for the big slow circle and treble for the small fast ones.
static const int Circles = 6;
static const float CircleShare = 0.1;      // the share of particles drawing the circles themselves
static const float Layers = 7.0;           // nested rosettes, each leaning further on the fast circles

float erFrequency(int j, float n)
{
    float k = floor((j + 1) / 2);
    return 1.0 + n * k * ((j & 1) == 1 ? -1.0 : 1.0);
}

// Circle j's size in nested rosette layer (0 is the outer one, which the drawn circles show).
float erSize(int j, float layer)
{
    static const float Base[6] = { 1.0, 0.7, 0.1, 0.25, 0.05, 0.1 };
    float band = spectrum(0.03 + j * 0.11);
    return Base[j] * (1.0 + layer * 0.22 * j) * (0.8 + band * 0.45 + DropPulse * 0.4);
}

// The symmetry: the slow clock steps n through 3..7 and round again, blending into the next over the
// last part of each step. (The beat clock BeatCount - Beat only moves forward, so the music can push it.)
float2 erOrder()
{
    float clock = Time * 0.02 + (BeatCount - Beat) * 0.007;
    float stage = floor(clock);
    float blend = smoothstep(0.8, 1.0, clock - stage);
    return float2(3.0 + wrap(stage, 5.0), blend);
}

// The whole figure turns slowly; the pen runs round the curve once every two bars or so.
float erTurn()
{
    return Time * 0.05 + (BeatCount - Beat) * 0.01;
}

float erPen()
{
    return Time * 0.15 + (BeatCount - Beat) * 0.35;
}

float3 erCurveAt(float t, float n, float layer)
{
    float2 sum = 0.0;
    float total = 0.0;
    [unroll]
    for (int j = 0; j < Circles; j++)
    {
        float a = erSize(j, layer);
        float w = erFrequency(j, n) * t;
        sum += a * float2(cos(w), sin(w));
        total += a;
    }
    // The crown rises and falls n times round the curve, keeping the n-fold symmetry.
    float crown = (0.12 + Mid * 0.18 + BuildUp * 0.06) * cos(n * t + erTurn() * 3.0) + 0.05 * cos(2.0 * n * t);
    float shrink = 1.0 - layer * 0.085;
    return float3(sum / max(total, 1e-3) * 1.3 * shrink, crown * shrink);
}

float3 erToWorld(float3 flat)
{
    float2 turned = rotate(flat.xy, erTurn());
    // (Particle space has y growing downward.)
    return float3(turned.x, -flat.z, turned.y);
}

float3 erCurve(float t, float layer)
{
    float2 order = erOrder();
    float next = order.x + 1.0 > 7.5 ? 3.0 : order.x + 1.0;
    float3 flat = lerp(erCurveAt(t, order.x, layer), erCurveAt(t, next, layer), order.y);
    return erToWorld(flat);
}

// A point on circle k at angle a, with the chain posed where the pen is now.
float3 erCircle(int k, float a)
{
    float2 order = erOrder();
    float n = order.x;
    float pen = erPen();
    float2 centre = 0.0;
    float total = 0.0;
    [unroll]
    for (int j = 0; j < Circles; j++)
    {
        float size = erSize(j, 0.0);
        total += size;
        if (j < k)
        {
            float w = erFrequency(j, n) * pen;
            centre += size * float2(cos(w), sin(w));
        }
    }
    float scale = 1.3 / max(total, 1e-3);
    float2 p = (centre + erSize(k, 0.0) * float2(cos(a), sin(a))) * scale;
    return erToWorld(float3(p, 0.0));
}

// data.x: 0 on the curve, 1 + k on circle k; data.y: where along the curve, or the angle round the circle;
// data.z: the curve's layer; data.w: speed.
float3 erPosition(float4 data)
{
    return data.x < 0.5 ? erCurve(data.y, data.z) : erCircle((int)(data.x - 0.5), data.y);
}

void Spawn(inout Particle particle, uint id)
{
    bool circle = fixedRandom(id, 1.0) < CircleShare;
    float kind = circle ? 1.0 + min(floor(fixedRandom(id, 2.0) * Circles), Circles - 1.0) : 0.0;
    particle.data = float4(kind, fixedRandom(id, 3.0) * 2.0 * PI, min(floor(fixedRandom(id, 5.0) * Layers), Layers - 1.0), 0.6 + fixedRandom(id, 4.0) * 0.8);
    particle.velocity = 0.0;
    particle.position = erPosition(particle.data);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float speed = (0.05 + Bass * 0.15 + Beat * 0.08 + DropPulse * 0.25) * particle.data.w;
    // Light drifts along the curve; on the circles it spins.
    particle.data.y = wrap(particle.data.y + speed * (particle.data.x < 0.5 ? 1.0 : 6.0) * dt, 2.0 * PI);

    float3 before = particle.position;
    particle.position = erPosition(particle.data);
    float3 moved = particle.position - before;
    particle.velocity = dot(moved, moved) > 0.0225 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 erEye()
{
    float angle = Time * 0.03 + 0.8;
    return float3(sin(angle) * 1.6, -4.2 + sin(Time * 0.025) * 0.5, cos(angle) * 1.6);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = erEye();
    float3 target = float3(0.0, 0.0, 0.0);
    float zoom = 3.0 * (1.0 - DropPulse * 0.08);
    float roll = sin(Time * 0.04) * 0.08;
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, target, zoom, roll);

    float n = erOrder().x;
    float3 colour;
    float glow;
    if (particle.data.x < 0.5)
    {
        // Each layer has its own neon colour, teal outside through blue and violet to magenta inside, and
        // each petal shades the same way, so the colour keeps the symmetry too.
        float petal = frac(particle.data.y * n / (2.0 * PI));
        float layer = particle.data.z / (Layers - 1.0);
        colour = hsv2rgb(frac(0.47 + layer * 0.45 + petal * 0.06 + sin(Time * 0.02) * 0.05), 0.85, 1.0);
        float band = spectrum(0.02 + petal * 0.5);
        glow = 0.1 * (0.35 + band * 1.4) + DropPulse * 0.06;
        // The pen: a bright head on the curve with a fading tail behind it.
        float behind = wrap(erPen() - particle.data.y, 2.0 * PI);
        float head = exp(-behind * 2.5) * (0.6 + Beat * 0.8);
        glow += head * 0.5;
        colour = lerp(colour, float3(1.0, 0.95, 0.9), saturate(head * 0.5));
        // Every petal passes through the middle, so dim it there to keep it from burning out.
        glow *= lerp(0.3, 1.0, smoothstep(0.05, 0.4, length(particle.position.xz)));
    }
    else
    {
        // The circles: faint violet-white rings, the small fast ones brighter so they read.
        float k = particle.data.x - 1.0;
        colour = hsv2rgb(frac(0.75 + k * 0.03), 0.4, 1.0);
        glow = 0.05 + k * 0.012 + Beat * 0.03;
    }
    colour *= glow * (0.8 + Intensity * 0.4);
    // A few sparks along the curve catch the treble.
    colour *= 1.0 + step(0.996, fixedRandom(id, 6.0)) * Treble * 5.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    float2 streak = seen.xy - seenBefore.xy;
    sprite.streak = streak * min(1.0, 0.014 / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.2 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 2.2) * (0.006 + Bass * 0.012 + DropPulse * 0.02);
    return particles(uv) + previous(uv) * 0.4 + float3(0.3, 0.5, 0.9) * haze;
}
