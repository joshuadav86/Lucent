// Name: Julia Dust
// A Julia set drawn in fine glowing dust on black, melting slowly from shape to shape: smooth loops curl into spirals, sprout rabbit ears and seahorse tails, then shatter into clouds of sparks and gather back again. The bass and each beat shake it apart a little, and every stretch of the outline glows with its own band of the music.
// Family: Particles
// Tags: Calm, Dark
// Added: 2026-10-19 00:30
// Particles: 393216

// Drawn backwards: instead of testing every pixel, each speck runs the Julia rule z -> z*z + c in
// reverse, z -> +-sqrt(z - c), picking the sign at random each frame. Every speck is pulled onto the
// edge of the set within a few frames, wherever it starts, and keeps hopping around it.
static const float Period = 240.0;
static const float Scale = 0.5;

// c follows the edge of the Mandelbrot set's big heart shape, now a little inside it (a smooth loop
// with spirals), now a little outside (bulbs like the rabbit and the basilica, or a cloud of dust).
float2 juliaNumber()
{
    float angle = Time * 2.0 * PI / Period + 1.3;
    float2 edge = float2(0.5 * cos(angle) - 0.25 * cos(2.0 * angle), 0.5 * sin(angle) - 0.25 * sin(2.0 * angle));
    float reach = 1.0 + 0.06 * sin(Time * 2.0 * PI / 37.0) + Bass * 0.03 + Beat * 0.015;
    return edge * reach;
}

// The square root of a complex number, on the side with a positive real part.
float2 complexSqrt(float2 z)
{
    float size = length(z);
    float re = sqrt(max((size + z.x) * 0.5, 0.0));
    float im = sqrt(max((size - z.x) * 0.5, 0.0));
    return float2(re, z.y < 0.0 ? -im : im);
}

// Each speck jumps somewhere new every frame, so the mouse parts them where they're drawn rather than pushing each one (see ParticleMain.hlsli).
#define MOUSE_MOVES_PLACES

void Spawn(inout Particle particle, uint id)
{
    float angle = random(id, 1.0) * 2.0 * PI;
    float radius = 1.8 * sqrt(random(id, 2.0));
    particle.position = float3(cos(angle) * radius, sin(angle) * radius, 0.0);
    particle.velocity = 0.0;
    particle.data = float4(random(id, 3.0), 0.0, 0.0, 0.0);
    // A slow trickle of fresh specks keeps every part of the set filled as it changes.
    particle.life = 6.0 + random(id, 4.0) * 14.0;
}

void Simulate(inout Particle particle, uint id)
{
    float2 root = complexSqrt(particle.position.xy - juliaNumber());
    float side = random(id, 5.0) < 0.5 ? 0.0 : 1.0;
    float2 next = side > 0.5 ? root : -root;
    particle.velocity.xy = next - particle.position.xy;
    particle.position.xy = next;
    // The signs picked so far, newest first, as a number from 0 to 1: specks with the same recent
    // picks sit side by side, so this runs smoothly round the outline and gives each stretch its colour.
    particle.data.x = particle.data.x * 0.5 + side * 0.5;
}

Sprite Show(Particle particle, uint id)
{
    float along = particle.data.x;
    float2 seen = rotate(particle.position.xy, Time * 0.015 + sin(Time * 0.05) * 0.4) * Scale * (1.0 + Beat * 0.012);

    // Ice blue round to violet, drifting slowly round the colour wheel, and a little paler where it's loud.
    float level = spectrum(0.03 + along * 0.55);
    float3 tint = hsv2rgb(0.53 + along * 0.28 + Time * 0.004, 0.6 - level * 0.25, 1.0);

    Sprite sprite = (Sprite)0;
    sprite.position = seen;
    sprite.size = 0.75;
    sprite.colour = tint * 0.06 * (0.6 + level * 0.9 + Beat * 0.2);
    sprite.colour *= 1.0 + step(0.997, fixedRandom(id, 6.0)) * Treble * 6.0;
    // Fresh specks wait a few hops until they've landed on the set.
    sprite.colour *= step(0.15, particle.age);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float3 ground = float3(0.004, 0.004, 0.01) * (1.0 - saturate(length(p) * 0.5));
    return ground + particles(uv) + previous(uv) * 0.6;
}
