// Name: Neural Bloom
// Drift through a glowing web of neurons as sparks of thought race along their branching fibres; every beat sets off a cascade of firing that ripples from cell to cell, and each cell's dendrites glow with its own part of the music.
// Family: Particles
// Tags: Neon, Dark, 3D
// Particles: 262144

// The network: 32 neurons, each sending out 6 long fibres (to a neighbour, or out into the dark),
// and each fibre sprouting 3 short twigs. Every one of those curves is a line of particles that
// stays put, glowing faintly, plus a few that race along it as signals. Nothing needs remembering
// from frame to frame, so every particle works out where it is from its id alone.
static const uint Neurons = 32;
static const uint Fibres = 6;
static const uint Twigs = 3;
static const uint SegmentsPerNeuron = 24;   // Fibres * (1 + Twigs)
static const uint Segments = 768;           // Neurons * SegmentsPerNeuron
static const float FilamentShare = 0.88;    // the rest of each curve's particles are signals
static const float NetworkRadius = 3.6;

float neuronRandom(uint neuron, float salt)
{
    return pcgHash(neuron * 2654435761u ^ pcgHash(asuint(salt))) / 4294967295.0;
}

// Where a neuron's cell body sits: spread through a ball, breathing slowly.
float3 somaOf(uint neuron)
{
    float3 direction = normalize(float3(neuronRandom(neuron, 1.0), neuronRandom(neuron, 2.0), neuronRandom(neuron, 3.0)) * 2.0 - 1.0 + 1e-3);
    float3 position = direction * NetworkRadius * pow(neuronRandom(neuron, 4.0), 0.5);
    return position + sin(Time * 0.2 + float3(1.0, 2.0, 3.0) * neuron) * 0.08;
}

// A point on a curve from a to b that bows out through control, with a gentle organic wiggle.
float3 curvePoint(float3 a, float3 control, float3 b, float t, float seed, float wiggle)
{
    float3 position = lerp(lerp(a, control, t), lerp(control, b, t), t);
    float3 bend = float3(sin(t * 5.0 + seed * 7.0), sin(t * 4.0 + seed * 3.0), sin(t * 6.0 + seed * 5.0))
                + float3(sin(t * 13.0 + seed), sin(t * 11.0 + seed * 2.0), sin(t * 15.0 + seed * 4.0)) * 0.2;
    return position + bend * wiggle * sin(t * PI);
}

// A fibre of a neuron: from its soma (a) towards a neighbour or out into space (b).
void fibreOf(uint neuron, uint fibre, out float3 a, out float3 control, out float3 b)
{
    a = somaOf(neuron);
    float salt = fibre * 10.0 + 20.0;
    if (fibre < 3u)
    {
        // The first three reach a nearby neuron, making the web.
        uint other = (neuron + 1u + (uint)(neuronRandom(neuron, salt) * 6.0) + fibre * 7u) % Neurons;
        b = somaOf(other);
    }
    else
    {
        float3 out_ = normalize(float3(neuronRandom(neuron, salt), neuronRandom(neuron, salt + 1.0), neuronRandom(neuron, salt + 2.0)) * 2.0 - 1.0 + 1e-3);
        b = a + out_ * lerp(1.0, 2.2, neuronRandom(neuron, salt + 3.0));
    }
    float3 sideways = float3(neuronRandom(neuron, salt + 4.0), neuronRandom(neuron, salt + 5.0), neuronRandom(neuron, salt + 6.0)) * 2.0 - 1.0;
    control = (a + b) * 0.5 + sideways * length(b - a) * 0.35;
}

// A point on segment s at t (0..1); also how far along the network it is from its soma (w).
float4 segmentPoint(uint segment, float t)
{
    uint neuron = segment / SegmentsPerNeuron;
    uint local = segment % SegmentsPerNeuron;
    uint fibre = local / (1u + Twigs);
    uint twig = local % (1u + Twigs);
    float3 a, control, b;
    fibreOf(neuron, fibre, a, control, b);
    float seed = segment * 0.731;
    float fibreLength = length(b - a);
    if (twig == 0u)
    {
        return float4(curvePoint(a, control, b, t, seed, 0.06 * fibreLength), t * fibreLength);
    }
    // A twig branches off part-way along its fibre and wanders off to the side.
    float from = lerp(0.12, 0.7, neuronRandom(segment, 50.0));
    float3 root = curvePoint(a, control, b, from, seed - twig * 0.731, 0.06 * fibreLength);
    float3 away = normalize(float3(neuronRandom(segment, 51.0), neuronRandom(segment, 52.0), neuronRandom(segment, 53.0)) * 2.0 - 1.0 + 1e-3);
    float3 along = normalize(b - a);
    float twigLength = lerp(0.25, 0.8, neuronRandom(segment, 54.0));
    float3 end = root + normalize(away + along * 0.6) * twigLength;
    float3 bow = (root + end) * 0.5 + away * twigLength * 0.25;
    return float4(curvePoint(root, bow, end, t, seed, 0.04), from * fibreLength + t * twigLength);
}

// The colour of each neuron: a family of electric blues, violets and magentas, with a few cyan and gold.
float3 neuronColour(uint neuron)
{
    float pick = neuronRandom(neuron, 9.0);
    return pick < 0.9 ? hsv2rgb(lerp(0.5, 0.93, pick / 0.9), 0.85, 1.0) : float3(1.0, 0.7, 0.3);
}

// Seconds since the last beat (Beat fades as e^-5t).
float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

// The cascade: each beat a different neuron fires first, and the firing spreads out through
// the network from there. Returns how strongly a point this far along from a soma is lit.
float cascade(uint neuron, float along)
{
    uint origin = pcgHash((uint)BeatCount) % Neurons;
    float since = sinceBeat();
    float reach = distance(somaOf(origin), somaOf(neuron)) + along;
    float front = since * 6.5;
    return exp(-pow((front - reach) * 2.5, 2.0)) * exp(-since * 0.7) * (0.5 + Bass);
}

void Spawn(inout Particle particle, uint id)
{
    particle.life = 1e6;
    particle.position = 0.0;
    particle.velocity = 0.0;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
}

float3 cameraEye()
{
    float angle = Time * 0.05;
    return float3(sin(angle) * 4.6, sin(Time * 0.037) * 1.5, cos(angle) * 4.6);
}

static const float Zoom = 1.6;

float3 toScreen(float3 position)
{
    float3 seen = project(position, cameraEye(), 0.0, Zoom, 0.0);
    return float3(seen.x, -seen.y, seen.z);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    uint segment = id % Segments;
    uint slot = id / Segments;
    uint perSegment = (uint)ParticleCount / Segments;
    uint filaments = (uint)(perSegment * FilamentShare);
    uint neuron = segment / SegmentsPerNeuron;
    bool twig = (segment % (1u + Twigs)) != 0u;
    float3 tint = neuronColour(neuron);
    // Each neuron's dendrites glow with their own band of the spectrum.
    float band = spectrum(0.03 + (neuron + 0.5) / Neurons * 0.75);

    float t;
    float3 colour;
    float size;
    if (slot < filaments)
    {
        // A dot of the fibre itself: dim, thinner towards the tips.
        t = (slot + 0.5) / filaments;
        colour = tint * 0.024 * (0.3 + band * 1.6) * (twig ? 0.8 : 1.0) * (1.0 - t * 0.5);
        size = twig ? 0.9 : 1.1;
    }
    else
    {
        // A signal racing out along the fibre, quicker with the mids.
        float phase = fixedRandom(id, 1.0);
        float speed = lerp(0.18, 0.4, fixedRandom(id, 2.0)) * (0.7 + Mid * 0.9) * (twig ? 1.6 : 1.0);
        t = frac(Time * speed + phase);
        colour = lerp(tint, float3(1.0, 1.0, 1.0), 0.5) * 0.5 * smoothstep(0.0, 0.1, t) * smoothstep(1.0, 0.8, t) * (0.5 + band);
        size = 1.6;
    }

    float4 point_ = segmentPoint(segment, t);
    float3 seen = toScreen(point_.xyz);
    if (seen.z < 0.3)
    {
        return sprite;
    }
    // The beat's cascade of firing: a bright white-hot pulse passing along every fibre.
    float firing = cascade(neuron, point_.w);
    colour += lerp(tint, float3(1.0, 1.0, 1.0), 0.6) * firing * (slot < filaments ? 0.12 : 0.6);

    sprite.position = seen.xy;
    sprite.size = size;
    sprite.colour = colour;
    if (slot >= filaments)
    {
        // Signals leave a short streak behind them.
        float3 behind = toScreen(segmentPoint(segment, max(t - 0.03, 0.0)).xyz);
        sprite.streak = seen.xy - behind.xy;
    }
    focus(sprite, seen.z, length(cameraEye()) * 0.85, 0.08);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 direction = cameraRay(eye, 0.0, float2(p.x, -p.y), Zoom, 0.0);
    // A deep blue-violet fog with a faint glow at the heart of the network.
    float3 colour = lerp(float3(0.004, 0.003, 0.012), float3(0.02, 0.012, 0.04), exp(-dot(p, p) * 0.8));
    float toCentre = length(cross(direction, -eye));
    colour += float3(0.05, 0.02, 0.1) * exp(-toCentre * toCentre * 0.25) * (0.3 + Level * 0.4);

    // The cell bodies: soft glowing cores that flare as the cascade passes through them.
    [loop]
    for (uint neuron = 0u; neuron < Neurons; neuron++)
    {
        float3 soma = somaOf(neuron);
        float3 offset = soma - eye;
        float along = dot(offset, direction);
        if (along > 0.3)
        {
            float off = length(offset - direction * along) / along * 4.6;
            float flare = cascade(neuron, 0.0);
            float band = spectrum(0.03 + (neuron + 0.5) / Neurons * 0.75);
            float3 tint = neuronColour(neuron);
            colour += tint * (exp(-off * off * 120.0) * (0.25 + band * 0.4 + flare * 1.5) + exp(-off * 9.0) * (0.03 + flare * 0.12));
        }
    }

    colour += particles(uv);
    // Signals leave fading trails; taking the brighter of now and before stops the picture building up.
    return max(colour, previous(uv) * 0.8);
}
