// Name: Mobius Flow
// Neon lanes of light race round a twisting Möbius strip, crossing from one edge to the other on every lap; every few bars a tight new twist appears and unwinds its way round the loop, and the lanes glow with their own slices of the spectrum.
// Family: Particles
// Tags: Geometric, 3D, Neon
// Particles: 262144

// The strip is a ribbon round a circle, its cross-section turning as it goes. With an odd number
// of half twists the ribbon meets itself flipped over: a Möbius strip, with one side and one
// edge, so a particle that sets off on the left-hand edge comes back round on the right.
//
// To change the number of twists smoothly, the extra full turn first appears squeezed into
// a single point (where it's invisible: a full turn looks like no turn at all) and then spreads
// evenly round the loop, unwinding as it goes.
static const float Lanes = 28.0;
static const float RingRadius = 1.0;

// The sequence of half-twist counts the strip steps through, a step every 8 beats.
float twistCount(float stepIndex)
{
    float k = wrap(stepIndex, 4.0);
    return k < 0.5 ? 1.0 : (k < 1.5 ? 3.0 : (k < 2.5 ? 5.0 : 3.0));
}

// How far the current change has spread round the loop, 0 to 1, over about three beats.
float twistProgress()
{
    float beatsIn = wrap(BeatCount, 8.0) + saturate(1.0 - Beat);
    return smoothstep(0.0, 3.5, beatsIn);
}

// How far the ribbon's cross-section has turned at angle u round the ring.
float twistAngle(float u)
{
    float stepIndex = floor(BeatCount / 8.0);
    float before = twistCount(stepIndex - 1.0);
    float after = twistCount(stepIndex);
    float spread = max(twistProgress(), 1e-3);
    float extra = saturate(u / (2.0 * PI * spread));
    return before * 0.5 * u + (after - before) * PI * extra + Time * 0.15;
}

float stripWidth()
{
    return 0.34 + Mid * 0.05 + sin(Time * 0.21) * 0.03;
}

float3 stripPoint(float u, float across)
{
    float angle = twistAngle(u);
    float3 outward = float3(cos(u), 0.0, sin(u));
    float3 upward = float3(0.0, 1.0, 0.0);
    float3 direction = outward * cos(angle) + upward * sin(angle);
    // A gentle wave from the music runs round the ring.
    float swell = waveform(frac(u / (2.0 * PI))) * 0.04 * (0.3 + Bass);
    return outward * (RingRadius + swell) + direction * across * stripWidth() + upward * swell * 0.5;
}

void Spawn(inout Particle particle, uint id)
{
    float lane = floor(fixedRandom(id, 1.0) * Lanes);
    float across = (lane + 0.5) / Lanes * 2.0 - 1.0;
    float u = fixedRandom(id, 2.0) * 2.0 * PI;
    particle.data = float4(u, across, lane, 0.0);
    particle.position = stripPoint(u, across);
    particle.velocity = 0.0;
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float lane = particle.data.z;
    // Lanes run at slightly different speeds, so the strip shimmers with a fine shear.
    float speed = (0.3 + Bass * 0.8 + Beat * 0.3) * (0.85 + 0.3 * frac(lane * 0.618));
    float u = particle.data.x + speed * dt;
    float across = particle.data.y;
    // Round the seam the ribbon meets itself flipped, so the particle comes back on the other edge.
    if (u > 2.0 * PI)
    {
        u -= 2.0 * PI;
        across = -across;
    }
    particle.data.x = u;
    particle.data.y = across;

    float3 moved = stripPoint(u, across);
    particle.velocity = (moved - particle.position) / max(DeltaTime, 1e-4);
    if (length(particle.velocity) * dt > 0.15)
    {
        particle.velocity = 0.0;
    }
    particle.position = moved;
}

float3 cameraEye()
{
    float angle = Time * 0.06;
    return float3(sin(angle) * 3.0, -1.5 + sin(Time * 0.045) * 0.5, cos(angle) * 3.0);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float zoom = 2.15 + Bass * 0.04;
    float roll = sin(Time * 0.033) * 0.1;
    float3 seen = project(particle.position, eye, 0.0, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.035, eye, 0.0, zoom, roll);

    // Colour across the ribbon, cyan on one edge to magenta on the other. Because the strip has
    // one edge, the colours trade places every lap.
    float across = particle.data.y;
    float lane = particle.data.z;
    float3 cyan = float3(0.1, 0.85, 1.0);
    float3 magenta = float3(1.0, 0.15, 0.75);
    float3 colour = lerp(cyan, magenta, across * 0.5 + 0.5);
    // The middle of the ribbon runs a little paler, like the hot core of a neon tube.
    colour = lerp(colour, float3(0.75, 0.7, 1.0), pow(1.0 - abs(across), 6.0) * 0.5);

    float level = spectrum(0.04 + abs(across) * 0.7);
    // A pulse runs round the strip on each beat.
    float pulse = pow(saturate(cos(particle.data.x - BeatCount * 2.0) * 0.5 + 0.5), 20.0) * Beat;
    // The edges are drawn brighter so the ribbon's single boundary stands out.
    float edge = 1.0 + smoothstep(0.85, 1.0, abs(across)) * 1.2;
    colour *= (0.06 * (0.5 + level * 1.5) * edge + pulse * 0.15);
    colour *= 1.0 + step(0.996, fixedRandom(id, 6.0)) * Treble * 5.0;

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = seen.z > 0.1 ? 0.95 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye), 0.07);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // Trails give the lanes their neon-tube look; a dark violet glow sits behind the loop.
    float haze = exp(-dot(p, p) * 1.0) * (0.012 + Bass * 0.02);
    return particles(uv) + previous(uv) * 0.72 + float3(0.4, 0.15, 0.7) * haze;
}
