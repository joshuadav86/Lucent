// Name: Sonic Dandelion
// A glowing seed head of four thousand filaments, each one a band of the spectrum, stretching, swaying and bursting outward with the music as it turns in the dark.
// Family: Particles
// Tags: Colourful
// Added: 2026-10-17 00:03
// Particles: 262144

// Each filament is a line of dots from the surface of the head out to its tip.
static const uint Dots = 64;

// Directions spread evenly over a sphere (a Fibonacci spiral).
float3 sphereDirection(uint index, uint count)
{
    float z = 1.0 - (index + 0.5) * 2.0 / count;
    float angle = index * 2.39996323;
    float r = sqrt(max(1.0 - z * z, 0.0));
    return float3(r * cos(angle), z, r * sin(angle));
}

uint filamentCount()
{
    return max((uint)ParticleCount / Dots, 1u);
}

float3 restingPlace(uint id)
{
    uint filament = id / Dots;
    float along = (id % Dots) / (float)(Dots - 1);
    float3 direction = sphereDirection(filament, filamentCount());

    // Bass at the equator, treble at the poles, so a kick swells a ring around the middle.
    float band = abs(direction.y);
    float level = spectrum(band * 0.85 + 0.02);
    float reach = 0.12 + level * level * 0.9 + Beat * 0.12;
    float radius = 0.42 + Bass * 0.06;

    // Filaments sway in a slow breeze, more towards their tips.
    float3 sway = curlNoise(direction * 1.7 + Time * 0.15) * 0.06 * along * (0.6 + Mid);
    float3 position = direction * (radius + reach * along) + sway * (radius + reach);

    // The head turns slowly, tilting back and forth.
    float spin = Time * 0.12;
    position.xz = rotate(position.xz, spin);
    position.yz = rotate(position.yz, sin(Time * 0.07) * 0.5);
    return position;
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = restingPlace(id);
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    // Ease towards where the music wants each dot, so the filaments move smoothly, not jitter.
    float3 target = restingPlace(id);
    float3 eased = lerp(particle.position, target, 1.0 - exp(-DeltaTime * 14.0));
    particle.velocity = (eased - particle.position) / max(DeltaTime, 1e-4);
    particle.position = eased;
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = float3(sin(Time * 0.05) * 0.4, -0.25, -2.6);
    float3 seen = project(particle.position, eye, 0.0, 1.9 + Bass * 0.08, 0.0);

    uint filament = id / Dots;
    float along = (id % Dots) / (float)(Dots - 1);
    float3 direction = sphereDirection(filament, filamentCount());
    float hue = abs(direction.y) * 0.55 + Time * 0.015 + 0.55;

    // Deep colour at the root, brightening to a pale glowing seed at the tip.
    float3 colour = hsv2rgb(hue, lerp(0.95, 0.35, along * along), 1.0);
    float tip = (id % Dots) == Dots - 1 ? 4.0 : 1.0;
    float brightness = (0.12 + 0.35 * along) * tip * (1.0 + Beat * 0.5);
    colour *= brightness * (1.0 + step(0.995, fixedRandom(id, 2.0)) * Treble * 6.0);

    Sprite sprite = (Sprite)0;
    sprite.position = seen.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye), 0.12);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A soft glow at the heart of the seed head, pulsing with the bass.
    float heart = exp(-dot(p, p) * 9.0) * (0.05 + Bass * 0.12);
    return particles(uv) + previous(uv) * 0.55 + hsv2rgb(Time * 0.015 + 0.6, 0.6, heart);
}
