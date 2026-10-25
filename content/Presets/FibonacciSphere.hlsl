// Name: Fibonacci Sphere
// A quiet globe of evenly spaced points, laid out in the golden spirals of a sunflower head, that breathes with the bass; ripples run from pole to pole with the waveform, and the music slices it into rings that lift and turn and grow fine spines, each with its own part of the spectrum.
// Family: Particles
// Tags: Minimal, Geometric, 3D
// Added: 2026-10-17 22:17
// Particles: 131072

// The points sit on a Fibonacci lattice: each one a golden angle round from the last as they
// step down from the north pole to the south, which spreads them more evenly over a sphere
// than any grid, and draws the sunflower spirals you can see where they bunch up in view.
//
// There are only a few thousand points, so each one reads as a clean dot. Every point is made of
// a short column of particles that sit on top of one another when it's quiet; when its band of
// the music gets loud the column stretches out from the sphere into a fine spine.
static const float Bands = 18.0;
static const uint Dots = 6144;

uint dotOf(uint id)
{
    return id % Dots;
}

// How far along its point's spine a particle sits, 0 (on the sphere) to 1 (the tip).
float spineOf(uint id)
{
    float perDot = max(floor(ParticleCount / Dots), 2.0);
    return min((float)(id / Dots) / (perDot - 1.0), 1.0);
}

// Where point id sits on the unit sphere, and its height from the south pole (-1) to the north (1).
float3 latticePoint(uint id, out float height)
{
    id = dotOf(id);
    height = 1.0 - (id + 0.5) * 2.0 / Dots;
    float r = sqrt(saturate(1.0 - height * height));
    // The golden angle step done in whole numbers, which stay exact however many points there are.
    float angle = (id * 1640531527u) / 4294967296.0 * 2.0 * PI;
    return float3(r * cos(angle), height, r * sin(angle));
}

// The band of latitude a height falls in, 0 at the south pole to Bands - 1 at the north.
float bandOf(float height)
{
    return min(floor((height * 0.5 + 0.5) * Bands), Bands - 1.0);
}

float3 spherePosition(uint id)
{
    float height = 0.0;
    float3 onSphere = latticePoint(id, height);
    float band = bandOf(height);

    // Each band's slice of the spectrum, mirrored so the bass sits at the equator and the treble
    // at the poles; the music lifts its band out from the sphere and turns it a little.
    float fromEquator = abs(band - (Bands - 1.0) * 0.5) / ((Bands - 1.0) * 0.5);
    float level = spectrum(0.03 + fromEquator * 0.7);
    float lift = level * level * 0.1;
    float twist = (band - (Bands - 1.0) * 0.5) * (0.04 * sin(Time * 0.13)) + level * 0.35 * (fmod(band, 2.0) - 0.5);
    onSphere.xz = rotate(onSphere.xz, twist);

    // Ripples running from pole to pole, shaped by the waveform.
    float ripple = waveform(frac(height * 0.5 + 0.5 - Time * 0.08)) * 0.035 * (0.4 + Mid);

    // The whole sphere breathes slowly, and swells a touch with the bass.
    float radius = 1.0 + sin(Time * 0.4) * 0.015 + Bass * 0.05 + Beat * 0.025 + ripple + lift;

    // The bands drift apart along the axis when the music is loud, leaving thin dark gaps.
    float gap = (band - (Bands - 1.0) * 0.5) * (0.004 + Level * 0.02);
    float spine = spineOf(id) * (level * level * 0.45 + Beat * 0.04);
    float3 position = onSphere * (radius + spine);
    position.y += gap;
    return position;
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = spherePosition(id);
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    float3 moved = spherePosition(id);
    particle.velocity = (moved - particle.position) / max(DeltaTime, 1e-4);
    particle.position = moved;
}

// The sphere turns slowly about a tilted axis; the camera stays put.
float3 turned(float3 position)
{
    position.xz = rotate(position.xz, Time * 0.07);
    position.yz = rotate(position.yz, 0.35 + sin(Time * 0.05) * 0.2);
    return position;
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = float3(0.0, 0.0, -3.4);
    float3 position = turned(particle.position);
    float3 seen = project(position, eye, 0.0, 1.95, 0.0);

    float height = 0.0;
    float3 onSphere = latticePoint(id, height);
    float band = bandOf(height);
    float fromEquator = abs(band - (Bands - 1.0) * 0.5) / ((Bands - 1.0) * 0.5);
    float level = spectrum(0.03 + fromEquator * 0.7);

    // (Screen y grows downward, so world -y is up.) The near side is bright and the far side a faint ghost, lit softly from the upper left.
    float3 normal = turned(onSphere);
    float facing = saturate(-normal.z);
    float light = 0.35 + 0.65 * saturate(dot(normal, normalize(float3(-0.5, -0.6, -0.6))));
    float brightness = lerp(0.12, 1.0, facing) * light;

    // Two colours only: a soft ivory, and a pale gold for the bands the music is lifting.
    float3 ivory = float3(0.85, 0.88, 1.0);
    float3 gold = float3(1.0, 0.72, 0.38);
    float3 colour = lerp(ivory, gold, saturate(level * level * 2.5));
    colour *= brightness * (0.17 + level * 0.08 + Beat * 0.04);
    // The spine fades towards its tip.
    colour *= 1.4 - spineOf(id) * 0.9;

    sprite.position = seen.xy;
    sprite.size = seen.z > 0.1 ? 1.6 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, 3.0, 0.035);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The faintest warm glow behind the globe, and no trails: the dots stay crisp.
    float halo = exp(-dot(p, p) * 1.6) * (0.012 + Bass * 0.02);
    return particles(uv) + previous(uv) * 0.25 + float3(0.9, 0.75, 0.6) * halo;
}
