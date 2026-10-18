// Name: Field Lines
// Glowing threads of light stream out of two warm charges and arc through space into two cool ones, tracing the electric field between them as the charges drift and circle; each charge swells with its own band of the music, bending every line towards it, and beats send bright pulses racing down the threads.
// Family: Particles
// Tags: Geometric, 3D
// Added: 2026-10-17 22:17
// Particles: 262144

// Four charges float in space: two positive (amber) and two negative (blue). A particle leaves a
// positive charge in one of a fixed set of directions and simply follows the electric field
// wherever it points, which carries it in a curving arc into a negative charge, where it's
// absorbed and starts again. Particles that set off the same way follow the same path, so
// together they draw the field lines. As the charges move and swell, the lines bend and sway.
static const float LinesPerCharge = 48.0;

// Where charge i is (0 and 1 positive, 2 and 3 negative), each on its own slow orbit.
float3 chargePosition(int i)
{
    float t = Time * 0.12;
    if (i == 0)
    {
        return float3(cos(t) * 1.0, sin(t * 0.7) * 0.35, sin(t) * 0.55);
    }
    if (i == 1)
    {
        return float3(cos(t + PI) * 1.0, sin(t * 0.7 + 2.0) * 0.35, sin(t + PI) * 0.55);
    }
    if (i == 2)
    {
        return float3(sin(t * 0.8 + 1.0) * 0.55, cos(t * 0.6) * 0.75 + 0.15, cos(t * 0.8 + 1.0) * 1.0);
    }
    return float3(sin(t * 0.8 + 1.0 + PI) * 0.55, cos(t * 0.6 + 2.5) * 0.75 - 0.15, cos(t * 0.8 + 1.0 + PI) * 1.0);
}

// Each charge's strength, from its own slice of the spectrum.
float chargeStrength(int i)
{
    float band = i == 0 ? 0.04 : (i == 1 ? 0.18 : (i == 2 ? 0.36 : 0.6));
    float strength = 1.0 + spectrum(band) * 1.2;
    return i < 2 ? strength : -strength;
}

float3 field(float3 position)
{
    float3 total = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float3 offset = position - chargePosition(i);
        float distance = max(length(offset), 0.03);
        total += chargeStrength(i) * offset / (distance * distance * distance);
    }
    return total;
}

// The kth of n directions spread evenly over a sphere (a Fibonacci lattice).
float3 latticeDirection(float k, float n)
{
    float z = 1.0 - (k + 0.5) * 2.0 / n;
    float angle = k * 2.39996323;
    float r = sqrt(saturate(1.0 - z * z));
    return float3(r * cos(angle), z, r * sin(angle));
}

float3 lineStart(uint id, out float source)
{
    source = fixedRandom(id, 1.0) < 0.5 ? 0.0 : 1.0;
    float k = floor(fixedRandom(id, 2.0) * LinesPerCharge);
    float3 direction = latticeDirection(k, LinesPerCharge);
    return chargePosition((int)source) + direction * 0.06;
}

// One step along the field, a fixed distance, using the midpoint for smooth curves.
float3 stepAlong(float3 position, float distance)
{
    float3 halfway = position + normalize(field(position) + 1e-6) * distance * 0.5;
    return position + normalize(field(halfway) + 1e-6) * distance;
}

void Spawn(inout Particle particle, uint id)
{
    float source = 0.0;
    float3 position = lineStart(id, source);
    // How far along its line it has travelled, for the colour and the pulses.
    float travelled = 0.0;
    if (ParticleReset > 0.5)
    {
        // On the first frame, spread the particles out along their lines straight away.
        int steps = (int)(random(id, 3.0) * 40.0);
        [loop]
        for (int i = 0; i < steps; i++)
        {
            position = stepAlong(position, 0.05);
        }
        travelled = steps * 0.05;
    }
    particle.position = position;
    particle.velocity = 0.0;
    particle.life = lerp(6.0, 12.0, random(id, 4.0));
    particle.data = float4(source, travelled, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float speed = (0.32 + Bass * 0.6 + Beat * 0.25) * (0.9 + fixedRandom(id, 5.0) * 0.2);
    float3 moved = stepAlong(particle.position, speed * dt);
    particle.velocity = (moved - particle.position) / max(DeltaTime, 1e-4);
    particle.position = moved;
    particle.data.y += speed * dt;

    // Absorbed by a negative charge, or lost far out in space: start again.
    float sink = min(length(moved - chargePosition(2)), length(moved - chargePosition(3)));
    if (sink < 0.05 || length(moved) > 3.5)
    {
        particle.life = 0.0;
    }
}

float3 cameraEye()
{
    float angle = Time * 0.05 + 0.4;
    return float3(sin(angle) * 3.4, -1.1 + sin(Time * 0.041) * 0.6, cos(angle) * 3.4);
}

float cameraZoom()
{
    return 1.8 + Bass * 0.04;
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 seen = project(particle.position, eye, 0.0, cameraZoom(), 0.0);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, 0.0, cameraZoom(), 0.0);

    // Warm near the positive charges, cooling to blue near the negative ones.
    float toSource = min(length(particle.position - chargePosition(0)), length(particle.position - chargePosition(1)));
    float toSink = min(length(particle.position - chargePosition(2)), length(particle.position - chargePosition(3)));
    float cool = toSource / max(toSource + toSink, 1e-3);
    float3 warm = particle.data.x < 0.5 ? float3(1.0, 0.55, 0.2) : float3(1.0, 0.75, 0.35);
    float3 colour = lerp(warm, float3(0.25, 0.55, 1.0), smoothstep(0.15, 0.85, cool));

    // Pulses run down the lines on the beat.
    float pulse = pow(saturate(cos((particle.data.y - frac(BeatCount * 0.25) * 4.0) * 6.0) * 0.5 + 0.5), 16.0) * Beat;
    colour *= 0.085 * (0.6 + Level * 0.8) + pulse * 0.16;
    // Fade in from the source and out towards the far reaches.
    colour *= saturate(particle.age * 3.0 + particle.data.y * 4.0) * saturate((3.5 - length(particle.position)) * 1.5);
    // Lines crowd together where they leave and enter the charges; thin them out there.
    colour *= smoothstep(0.03, 0.45, min(toSource, toSink)) * 0.85 + 0.15;
    colour *= 1.0 + step(0.996, fixedRandom(id, 6.0)) * Treble * 5.0;

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye), 0.035);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The charges themselves: soft glowing orbs, sized by their strength.
    float3 eye = cameraEye();
    float3 glowSum = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float3 seen = project(chargePosition(i), eye, 0.0, cameraZoom(), 0.0);
        float strength = abs(chargeStrength(i));
        float radius = 0.022 * strength * cameraZoom() / max(seen.z, 0.5) * 3.4;
        float d = length(p - seen.xy);
        float core = smoothstep(radius, radius * 0.6, d);
        float halo = exp(-d / (radius * 1.6)) * 0.12;
        float3 tint = i < 2 ? float3(1.0, 0.6, 0.25) : float3(0.3, 0.55, 1.0);
        glowSum += tint * (core * 0.55 + halo) * (0.7 + strength * 0.3) * (seen.z > 0.1 ? 1.0 : 0.0);
    }
    return particles(uv) + previous(uv) * 0.5 + glowSum;
}
