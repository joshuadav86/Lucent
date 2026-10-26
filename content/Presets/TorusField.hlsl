// Name: Torus Field
// A breathing torus of light: streams of particles rise up through its glowing core, curl out over its nested shells and pour back down round the outside, the bass pumping the flow and each shell humming with its own band of the spectrum.
// Family: Particles
// Tags: Geometric, 3D, Calm, Colourful
// Added: 2026-10-17 01:42
// Particles: 262144

// The field is a set of nested shells, like the layers of an onion bent into a ring. Particles
// ride round each shell along a gentle helix: up through the hole in the middle, out over the
// top, down round the outside and back in underneath, while slowly circling the ring.
static const float Shells = 6.0;
static const float RingRadius = 0.62;

// Which shell a particle rides, 0 (innermost) to 1 (outermost).
float shellOf(uint id)
{
    return floor(fixedRandom(id, 1.0) * Shells) / (Shells - 1.0);
}

// The radius of the tube a shell wraps round. The outermost shell almost closes the hole,
// giving the classic apple shape of a torus field.
float tubeRadius(float shell)
{
    return lerp(0.22, 0.6, shell) * (1.0 + Bass * 0.08);
}

// How fast a shell's helix goes round the ring compared with its trips through the hole: from
// 1/phi squared on the inside to the golden ratio on the outside, slowly drifting, so the streams
// never quite close up and weave a fine mesh instead.
float windingOf(float shell)
{
    return lerp(0.38, 1.618, shell) + sin(Time * 0.05 + shell * 3.0) * 0.08;
}

float3 fieldPoint(float around, float through, float shell)
{
    float r = tubeRadius(shell);
    // A little pinch where the flow squeezes through the core makes the shape read as a vortex.
    float pinch = 1.0 - 0.18 * cos(through) * shell;
    float ring = RingRadius + r * cos(through) * pinch;
    return float3(ring * cos(around), r * sin(through) * 1.15, ring * sin(around));
}

void Spawn(inout Particle particle, uint id)
{
    float shell = shellOf(id);
    // Particles start on a handful of streamlines per shell, so the flow shows as threads.
    float stream = floor(fixedRandom(id, 2.0) * 36.0) / 36.0;
    float along = fixedRandom(id, 3.0) * 2.0 * PI;
    float around = stream * 2.0 * PI + along * windingOf(shell) * 0.45 + (fixedRandom(id, 4.0) - 0.5) * 0.05;
    particle.data = float4(around, along, shell, 0.0);
    particle.position = fieldPoint(around, along, shell);
    particle.velocity = 0.0;
    particle.life = lerp(20.0, 60.0, random(id, 5.0));
}

void Simulate(inout Particle particle, uint id)
{
    float shell = particle.data.z;
    float through = particle.data.y;
    float r = tubeRadius(shell);
    float ring = RingRadius + r * cos(through);

    // The flow quickens through the narrow core (like water through a funnel) and with the bass.
    float pump = 0.35 + Bass * 0.9 + Beat * 0.5 + Level * 0.2;
    float speed = pump * (0.5 + 0.35 * RingRadius / max(ring, 0.08)) / (0.6 + shell * 0.6);
    float dt = min(DeltaTime, 0.05);
    particle.data.y += speed * dt;
    particle.data.x += speed * dt * windingOf(shell) * 0.45;

    float3 moved = fieldPoint(particle.data.x, particle.data.y, shell);
    // A soft shimmer off the shell, stronger in the louder parts of its band.
    moved += curlNoise(moved * 2.5 + Time * 0.2) * 0.012 * (0.4 + Mid);
    particle.velocity = (moved - particle.position) / max(DeltaTime, 1e-4);
    particle.position = moved;
}

float3 cameraEye()
{
    float angle = Time * 0.06;
    float height = 1.0 + sin(Time * 0.037) * 0.4;
    return float3(sin(angle) * 2.4, -height, cos(angle) * 2.4);
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float3 seen = project(particle.position, eye, 0.0, 1.85 + Bass * 0.06, sin(Time * 0.03) * 0.15);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, 0.0, 1.85 + Bass * 0.06, sin(Time * 0.03) * 0.15);

    // Inner shells take the bass, outer ones the treble.
    float shell = particle.data.z;
    float level = spectrum(shell * 0.75 + 0.03);
    float hue = lerp(1.1, 0.55, shell) + Time * 0.012 + sin(particle.data.y) * 0.04;

    // Particles glow paler and hotter as they rise through the core (the inside of the ring),
    // and a pulse runs round each shell on every beat.
    float inside = saturate(-cos(particle.data.y));
    float pulse = pow(saturate(cos(particle.data.y - BeatCount * 1.3 + shell * 2.0) * 0.5 + 0.5), 8.0) * Beat;
    float3 colour = hsv2rgb(hue, lerp(0.85, 0.35, inside), 1.0);
    float brightness = 0.07 * (0.5 + level * 1.6) * (1.0 + inside * 0.7) + pulse * 0.25;
    colour *= brightness;

    // A few catch the treble and sparkle.
    colour *= 1.0 + step(0.996, fixedRandom(id, 6.0)) * Treble * 12.0;

    // Fade in after spawning so new ones don't pop.
    colour *= saturate(particle.age * 2.0);

    Sprite sprite = (Sprite)0;
    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye), 0.08);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A soft glow in the core of the torus, pulsing with the bass.
    float core = exp(-dot(p, p) * 6.0) * (0.03 + Bass * 0.08 + Beat * 0.05);
    return particles(uv) + previous(uv) * 0.7 + hsv2rgb(Time * 0.012 + 0.9, 0.5, core);
}
