// Name: DNA Helix
// A great double helix of flowing light twists slowly through the dark, its base pairs glowing like an equaliser along its length; on every beat a stretch of it unzips and zips back up, while loose nucleotides drift around it.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Particles: 294912

// The helix is built along its own axis (local y), then tilted across the screen. Two backbone
// strands wind round the axis with a wide and a narrow groove between them, like real DNA. The
// rungs between them are the base pairs, each lit by its own slice of the spectrum, bass at one
// end and treble at the other. Backbone particles stream along the strands; the rest drift free.
static const float HalfLength = 3.4;
static const float Radius = 0.5;
static const float PairSpacing = 0.115;
static const float Groove = 0.75 * 3.14159265;   // the angle between the two strands
static const float BackboneShare = 0.4;
static const float RungShare = 0.95;
static const float Zoom = 1.7;

float pairCount()
{
    return floor(2.0 * HalfLength / PairSpacing);
}

// The helix turns ten base pairs to a full twist, and slowly spins as a whole.
float twistAt(float s)
{
    return s * (2.0 * PI / (10.0 * PairSpacing)) + Time * 0.35;
}

// How far the helix is unzipped at a point along it: every beat opens a bubble somewhere new,
// which closes again as the beat fades; a smaller bubble breathes with the bass.
float openness(float s)
{
    float centre = (hash(float2(BeatCount, 3.7)) - 0.5) * HalfLength * 1.4;
    float fromCentre = (s - centre) / 0.8;
    float opened = pow(Beat, 0.6) * exp(-fromCentre * fromCentre);
    float wander = sin(Time * 0.11) * HalfLength * 0.6;
    float fromWander = (s - wander) / 0.5;
    opened += Bass * 0.35 * exp(-fromWander * fromWander);
    return saturate(opened);
}

// A point on one strand. Unzipped strands bulge outwards and swing apart.
float3 strandPoint(float s, float strand, float opened)
{
    float angle = twistAt(s) + strand * Groove - opened * 0.6 * (strand * 2.0 - 1.0);
    float r = Radius * (1.0 + opened * 1.1);
    return float3(cos(angle) * r, s, sin(angle) * r);
}

float3 toWorld(float3 local)
{
    local.xy = rotate(local.xy, -0.55);
    local.xz = rotate(local.xz, sin(Time * 0.04) * 0.5);
    return local;
}

float3 cameraEye()
{
    float angle = Time * 0.03;
    return float3(sin(angle) * 1.6, 0.4 + sin(Time * 0.023) * 0.4, -4.6);
}

// The four bases glow in their own colours; each pairs only with its partner.
float3 baseColour(float base)
{
    return base < 1.0 ? float3(0.1, 0.85, 1.0) : (base < 2.0 ? float3(1.0, 0.18, 0.62) : (base < 3.0 ? float3(1.0, 0.7, 0.15) : float3(0.45, 1.0, 0.25)));
}

float partnerOf(float base)
{
    return base < 1.0 ? 1.0 : (base < 2.0 ? 0.0 : (base < 3.0 ? 3.0 : 2.0));
}

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

void Spawn(inout Particle particle, uint id)
{
    // Only the loose nucleotides move on their own; they start anywhere round the helix.
    particle.position = float3((random(id, 1.0) - 0.5) * 4.0, (random(id, 2.0) - 0.5) * 2.0 * HalfLength, (random(id, 3.0) - 0.5) * 4.0);
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    if (shareOf(id) < RungShare)
    {
        return;
    }
    // Loose nucleotides tumble on a slow current, kept within a tube round the helix.
    float dt = min(DeltaTime, 0.05);
    float3 current = curlNoise(particle.position * 0.45 + float3(0.0, Time * 0.05, 0.0)) * (0.05 + Mid * 0.08);
    float3 position = particle.position + (current + float3(0.0, 0.04, 0.0)) * dt;
    float across = length(position.xz);
    if (across > 2.4)
    {
        position.xz *= 2.4 / across;
    }
    position.y = wrap(position.y + HalfLength, 2.0 * HalfLength) - HalfLength;
    particle.velocity = current;
    particle.position = position;
}

Sprite Show(Particle particle, uint id)
{
    float share = shareOf(id);
    float3 local;
    float3 colour;
    float size = 1.0;

    if (share < BackboneShare)
    {
        // The backbones: particles stream along each strand, through a chain of brighter beads
        // (the sugars and phosphates) that stay put on the strand.
        float strand = fixedRandom(id, 1.0) < 0.5 ? 0.0 : 1.0;
        float flow = (strand * 2.0 - 1.0) * Time * 0.16;
        float s = wrap(fixedRandom(id, 2.0) * 2.0 * HalfLength + flow, 2.0 * HalfLength) - HalfLength;
        float opened = openness(s);
        local = strandPoint(s, strand, opened) + randomDirection(id, 3.0) * 0.028 * fixedRandom(id, 4.0);
        float bead = pow(0.5 + 0.5 * cos(s * 2.0 * PI / PairSpacing), 6.0);
        colour = (strand < 0.5 ? float3(0.25, 0.45, 1.0) : float3(0.7, 0.3, 1.0)) * (0.05 + bead * 0.1) * (1.0 + Level * 0.4);
    }
    else if (share < RungShare)
    {
        // The rungs: half a rung belongs to each strand, in its base's colour. They pulse with
        // their own part of the spectrum, and split down the middle where the helix unzips.
        float count = pairCount();
        float k = floor(fixedRandom(id, 5.0) * count);
        float s = (k + 0.5) * PairSpacing - HalfLength;
        float opened = openness(s);
        float3 a = strandPoint(s, 0.0, opened);
        float3 b = strandPoint(s, 1.0, opened);
        float f = fixedRandom(id, 6.0);
        float gap = opened * 0.7;
        float along = f < 0.5 ? f * (1.0 - gap) : 1.0 - (1.0 - f) * (1.0 - gap);
        local = lerp(a, b, along) + randomDirection(id, 7.0) * 0.018;
        float base = floor(hash(float2(k, 9.1)) * 4.0);
        float mine = f < 0.5 ? base : partnerOf(base);
        float level = spectrum(k / count);
        // The hydrogen bonds in the middle glow softly white while the pair is joined.
        float fromMiddle = f - 0.5;
        float middle = exp(-fromMiddle * fromMiddle * 400.0) * (1.0 - opened);
        colour = lerp(baseColour(mine), float3(0.9, 0.95, 1.0), middle) * (0.04 + level * level * 0.28 + Beat * 0.03 * (1.0 - opened));
        // Freshly broken ends flare.
        colour *= 1.0 + opened * exp(-fromMiddle * fromMiddle * 156.0) * 4.0;
    }
    else
    {
        // A loose nucleotide: a bright dot in one of the four base colours.
        local = particle.position;
        float base = floor(fixedRandom(id, 8.0) * 4.0);
        colour = baseColour(base) * (0.1 + Treble * 0.15) * (0.3 + fixedRandom(id, 9.0)) * saturate(2.4 - length(local.xz) * 0.9) * saturate((HalfLength - abs(local.y)) * 0.8);
        size = 2.2;
    }

    float3 eye = cameraEye();
    float3 seen = project(toWorld(local), eye, 0.0, Zoom, 0.0);
    Sprite sprite = (Sprite)0;
    sprite.position = float2(seen.x, -seen.y);
    sprite.size = seen.z > 0.1 ? size : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye), 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A deep blue-violet backdrop with a soft glow behind the helix.
    float3 colour = float3(0.004, 0.004, 0.012) + float3(0.012, 0.006, 0.03) * exp(-dot(p, p) * 0.8) * (1.0 + Bass * 0.5);
    return colour + particles(uv) + previous(uv) * 0.3;
}
