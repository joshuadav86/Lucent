// Name: Lissajous Mesh
// A crisp neon cage of looping curves, woven from a family of three-dimensional Lissajous knots running side by side; light races along every strand, and every couple of bars the knot unravels and re-ties itself into a new shape on the downbeat.
// Family: Particles
// Tags: Geometric, Neon
// Particles: 262144

// A Lissajous knot is the path of a point swinging back and forth along three axes at once, at
// three different whole-number rates. Here a whole family of them runs side by side, each a
// little out of step with the next, so together they weave a ribbon-like mesh. When the rates
// change, the curves smoothly morph from one knot into the next.
static const float Strands = 24.0;
static const float Shapes = 6.0;

// The swing rates along x, y and z for each shape in the cycle.
float3 knotRates(float shape)
{
    float k = wrap(shape, Shapes);
    if (k < 0.5) return float3(3.0, 2.0, 5.0);
    if (k < 1.5) return float3(2.0, 3.0, 7.0);
    if (k < 2.5) return float3(3.0, 4.0, 5.0);
    if (k < 3.5) return float3(4.0, 3.0, 7.0);
    if (k < 4.5) return float3(5.0, 2.0, 3.0);
    return float3(3.0, 5.0, 4.0);
}

// Every 8 beats the next shape takes over, morphing across the first couple of beats.
float3 currentRates()
{
    float shape = floor(BeatCount / 8.0);
    float beatsIn = wrap(BeatCount, 8.0) + saturate(1.0 - Beat);
    float morph = smoothstep(0.0, 2.5, beatsIn);
    return lerp(knotRates(shape - 1.0), knotRates(shape), morph);
}

float3 knotPoint(float t, float strand)
{
    float3 rates = currentRates();
    // Each strand is offset a little in phase from the last; the offsets breathe slowly with
    // the mids, opening and closing the mesh.
    float offset = (strand / (Strands - 1.0) - 0.5) * (0.5 + Mid * 0.25 + sin(Time * 0.19) * 0.15);
    float3 phase = float3(0.7 + offset, 0.2 + Time * 0.03, 1.3 - offset * 0.6);
    return sin(rates * t + phase) * float3(1.0, 0.85, 0.9);
}

void Spawn(inout Particle particle, uint id)
{
    float strand = floor(fixedRandom(id, 1.0) * Strands);
    float t = fixedRandom(id, 2.0) * 2.0 * PI;
    particle.data = float4(t, strand, 0.0, 0.0);
    particle.position = knotPoint(t, strand);
    particle.velocity = 0.0;
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float speed = (0.07 + Bass * 0.18 + Beat * 0.08) * (0.9 + fixedRandom(id, 3.0) * 0.2);
    particle.data.x = wrap(particle.data.x + speed * dt, 2.0 * PI);
    float3 moved = knotPoint(particle.data.x, particle.data.y);
    particle.velocity = (moved - particle.position) / max(DeltaTime, 1e-4);
    // While the shape is morphing the curve isn't closed, so skip the streak across the gap.
    if (length(particle.velocity) * dt > 0.1)
    {
        particle.velocity = 0.0;
    }
    particle.position = moved;
}

float3 cameraEye()
{
    float angle = Time * 0.055;
    return float3(sin(angle) * 3.6, -0.8 + sin(Time * 0.037) * 0.7, cos(angle) * 3.6);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float zoom = 2.25 + Bass * 0.05;
    float roll = sin(Time * 0.029) * 0.15;
    float3 seen = project(particle.position, eye, 0.0, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.04, eye, 0.0, zoom, roll);

    // Neon across the mesh: electric blue at one edge, through violet to hot pink at the other,
    // with the centre strand a bright cyan core.
    float strand = particle.data.y / (Strands - 1.0);
    float3 colour = lerp(float3(0.15, 0.4, 1.0), float3(1.0, 0.2, 0.7), strand);
    colour = lerp(colour, float3(0.3, 1.0, 0.95), exp(-pow((strand - 0.5) * 9.0, 2.0)));

    // Each strand glows with its own band of the spectrum, and beats send a flash along them.
    float level = spectrum(0.04 + abs(strand - 0.5) * 1.2);
    float flash = pow(saturate(cos(particle.data.x * 2.0 - BeatCount * 1.9) * 0.5 + 0.5), 18.0) * Beat;
    colour *= 0.1 * (0.5 + level * 1.5) + flash * 0.18;
    // Particles bunch up where a curve turns back on itself (it moves slowly there), which
    // would make hot spots; dim them by how slowly the curve moves at that point.
    float3 rates = currentRates();
    float offset = (particle.data.y / (Strands - 1.0) - 0.5) * (0.5 + Mid * 0.25 + sin(Time * 0.19) * 0.15);
    float3 phase = float3(0.7 + offset, 0.2 + Time * 0.03, 1.3 - offset * 0.6);
    float pace = length(rates * cos(rates * particle.data.x + phase) * float3(1.0, 0.85, 0.9));
    colour *= 0.06 + 0.94 * smoothstep(0.8, 4.0, pace);
    colour *= 1.0 + step(0.996, fixedRandom(id, 6.0)) * Treble * 5.0;

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = seen.z > 0.1 ? 0.85 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye), 0.04);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 1.4) * (0.01 + Bass * 0.015);
    return particles(uv) + previous(uv) * 0.6 + float3(0.35, 0.2, 0.7) * haze;
}
