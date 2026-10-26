// Name: Quantum Orbitals
// The cloud of a single electron around a hydrogen atom, drawn as a swarm of points that settles into the true shapes of its orbitals, round, dumbbell, clover and flower, and melts from one to the next every few bars; the two halves of each wave glow in opposite colours.
// Family: Particles
// Tags: Geometric, Space, 3D
// Added: 2026-10-17 17:02
// Particles: 262144

// Each particle takes a little random walk every frame, keeping a step more often when it leads
// somewhere the electron is more likely to be (the Metropolis method). Left to wander like this,
// the swarm spreads out exactly like the electron's probability cloud, and when the orbital
// changes the swarm simply flows into the new shape.

static const int OrbitalCount = 10;

// Which orbital this particle is following. Each one switches a little after its neighbours,
// so the change ripples through the swarm instead of happening all at once.
int orbitalFor(uint id)
{
    float bars = (BeatCount - fixedRandom(id, 1.0) * 2.0) / 8.0;
    return (int)wrap(floor(bars), (float)OrbitalCount);
}

// The orbital's wave at a point, with r measured in Bohr radii. Only the shape matters here, so
// the usual constants in front are left out. Real orbitals are positive in some lobes and
// negative in others; that sign is what the two colours show.
float wave(int orbital, float3 q)
{
    float r = length(q);
    float x = q.x;
    float y = q.y;
    float z = q.z;
    if (orbital == 0) return exp(-r);                                         // 1s
    if (orbital == 1) return z * exp(-r * 0.5);                               // 2p
    if (orbital == 2) return (3.0 * z * z - r * r) * exp(-r / 3.0);           // 3d z squared
    if (orbital == 3) return x * y * exp(-r / 3.0);                           // 3d xy (four-leaf clover)
    if (orbital == 4) return z * (6.0 - r) * exp(-r / 3.0);                   // 3p (nested lobes)
    if (orbital == 5) return z * (5.0 * z * z - 3.0 * r * r) * exp(-r * 0.25); // 4f z cubed
    if (orbital == 6) return x * y * z * exp(-r * 0.25);                      // 4f xyz
    if (orbital == 7) return x * (x * x - 3.0 * y * y) * exp(-r * 0.25);      // 4f six-petal flower
    if (orbital == 8) return (3.0 * z * z - r * r) * (12.0 - r) * exp(-r * 0.25); // 4d z squared (with rings)
    return (27.0 - 18.0 * r + 2.0 * r * r) * exp(-r / 3.0);                   // 3s (nested shells)
}

// How many Bohr radii one world unit holds for each orbital, so every cloud fills the same space.
float scaleOf(int orbital)
{
    if (orbital == 0) return 2.8;
    if (orbital == 1) return 8.0;
    if (orbital == 4) return 17.0;
    if (orbital <= 4) return 15.0;
    if (orbital == 9) return 16.0;
    if (orbital == 8) return 25.0;
    return 22.0;
}

// The highest density each orbital reaches, so brightness can be measured against it.
float peakDensity(int orbital)
{
    float peaks[OrbitalCount] = { 1.0, 0.541, 94.9, 5.93, 17.2, 29590.0, 273.8, 7399.0, 10060.0, 729.0 };
    return peaks[clamp(orbital, 0, OrbitalCount - 1)];
}

float density(int orbital, float3 position)
{
    float w = wave(orbital, position * scaleOf(orbital));
    return w * w;
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = randomInSphere(id, 1.0) * 0.9;
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = float4(0.0, 0.0, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    int orbital = orbitalFor(id);
    float3 position = particle.position;
    float here = density(orbital, position) + 1e-30;
    // The bass makes the electron restless: longer steps, a livelier shimmer.
    float stride = 0.045 + Bass * 0.03 + Beat * 0.02;
    [unroll]
    for (int k = 0; k < 3; k++)
    {
        float3 tried = position + randomInSphere(id, 2.0 + k * 7.0) * stride;
        float there = density(orbital, tried);
        if (there > here * random(id, 3.0 + k * 7.0))
        {
            position = tried;
            here = there + 1e-30;
        }
    }
    // Keep strays from wandering off into the far, empty tail.
    if (dot(position, position) > 2.5)
    {
        position *= 0.9;
    }
    particle.velocity = (position - particle.position) / max(DeltaTime, 1e-4);
    particle.position = position;
    particle.data.x = sign(wave(orbital, position * scaleOf(orbital)));
    particle.data.y = saturate(here / peakDensity(orbital));
}

float3 cameraEye()
{
    float angle = Time * 0.07;
    float lift = 0.6 + sin(Time * 0.043) * 0.5;
    return float3(sin(angle) * 3.2, lift, cos(angle) * 3.2);
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    // The whole atom also tips slowly, so each orbital is seen from many sides.
    float3 position = particle.position;
    position.yz = rotate(position.yz, sin(Time * 0.05) * 0.5);
    float3 seen = project(position, eye, 0.0, 2.75, 0.0);

    // Positive lobes glow warm amber-pink, negative ones cool cyan-violet, drifting slowly.
    float drift = sin(Time * 0.03) * 0.05;
    float3 positive = hsv2rgb(0.02 + drift, 0.85, 1.0);
    float3 negative = hsv2rgb(0.58 + drift, 0.85, 1.0);
    float3 colour = particle.data.x >= 0.0 ? positive : negative;

    // Shells further out answer higher notes, and every beat sends a ripple out from the nucleus.
    float r = length(particle.position);
    float level = spectrum(saturate(r * 0.7) * 0.7 + 0.02);
    float ripple = exp(-abs(r - (1.0 - Beat) * 1.4) * 8.0) * Beat;
    // A little whiter in the densest places, which picks out the shapes of the lobes.
    float dense = pow(particle.data.y, 0.28);
    colour = lerp(colour, 1.0, ripple * 0.4 + dense * dense * dense * 0.4);
    colour *= 0.07 * (0.1 + dense * 2.2) * (0.6 + level * 1.4 + ripple * 1.5);
    colour *= 1.0 + step(0.997, random(id, 9.0)) * Treble * 10.0;

    Sprite sprite = (Sprite)0;
    sprite.position = float2(seen.x, -seen.y);
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye), 0.03);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The tiny nucleus at the centre, a hot white point with a soft halo that beats.
    float d = length(p);
    float3 nucleus = float3(1.0, 0.85, 0.7) * (exp(-d * 30.0) * 0.45 + exp(-d * 10.0) * 0.06) * (1.0 + Bass * 0.8 + Beat * 0.6);
    // Faint deep-blue space behind.
    float3 backdrop = float3(0.004, 0.006, 0.016) * (1.4 - d * 0.5) + float3(0.02, 0.01, 0.03) * exp(-d * 1.5) * (0.5 + Level);
    return backdrop + nucleus + particles(uv) + previous(uv) * 0.45;
}
