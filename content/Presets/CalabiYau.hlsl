// Name: Calabi-Yau
// The famous quintic Calabi-Yau surface, the hidden six-dimensional shape of string theory, drawn in drifting light: twenty-five petals of one folded surface turning slowly through four dimensions, so the shape keeps curling into itself; light pours out from its waist to its frayed edges with the bass, each petal glows with its own band of the spectrum and beats send rings rippling outward.
// Family: Particles
// Tags: Geometric, 3D
// Added: 2026-10-25 01:40

// Hanson's picture of the quintic: a slice through it is the surface z1^5 + z2^5 = 1 in two
// complex dimensions, which is four real ones. It's made of 5 x 5 patches, each
//
//     z1 = e^(2 pi i k1 / 5) cosh(a + ib)^(2/5)
//     z2 = e^(2 pi i k2 / 5) (-i sinh(a + ib))^(2/5)
//
// with a from -1 to 1 and b from 0 to pi/2, and the patches meet edge to edge into one surface.
// We see it in 3D by keeping both real parts and a blend of the two imaginary parts; turning
// that blend turns the shape through the fourth dimension, so it folds and unfolds as it goes.
// Each particle keeps its patch and its b, and slides along a from the waist (a = 0) out to the
// edge, fading as it goes and starting again at the waist.
static const float Order = 5.0;
static const float Reach = 1.0;

// A complex number raised to a real power, on its principal branch. Every base here has a real
// part of at least 0, so the branch never cuts through the surface.
float2 complexPower(float2 z, float power)
{
    float radius = pow(max(length(z), 1e-6), power);
    float angle = atan2(z.y, z.x) * power;
    return float2(cos(angle), sin(angle)) * radius;
}

float2 complexTimes(float2 a, float2 b)
{
    return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

// How far the shape has turned through the fourth dimension.
float turn()
{
    return Time * 0.11 + sin(Time * 0.037) * 0.8 + Mid * 0.25;
}

float3 surfacePoint(float k1, float k2, float a, float b, float twist)
{
    float ca = cosh(a);
    float sa = sinh(a);
    float cb = cos(b);
    float sb = sin(b);
    // cosh(a + ib) and -i sinh(a + ib).
    float2 w1 = float2(ca * cb, sa * sb);
    float2 w2 = float2(ca * sb, -sa * cb);
    float2 z1 = complexTimes(complexPower(w1, 2.0 / Order), float2(cos(2.0 * PI * k1 / Order), sin(2.0 * PI * k1 / Order)));
    float2 z2 = complexTimes(complexPower(w2, 2.0 / Order), float2(cos(2.0 * PI * k2 / Order), sin(2.0 * PI * k2 / Order)));
    // Both real parts, and the imaginary parts blended by the turn through the fourth dimension.
    float depth = cos(twist) * z1.y + sin(twist) * z2.y;
    return float3(z1.x, z2.x, depth);
}

// The surface's normal, by stepping a little along a and b.
float3 surfaceNormal(float k1, float k2, float a, float b, float twist)
{
    float3 da = surfacePoint(k1, k2, a + 0.01, b, twist) - surfacePoint(k1, k2, a - 0.01, b, twist);
    float3 db = surfacePoint(k1, k2, a, b + 0.01, twist) - surfacePoint(k1, k2, a, b - 0.01, twist);
    return normalize(cross(da, db) + 1e-6);
}

// A particle's data: its patch (0 to 24), its b, how far out it is (0 at the waist, 1 at the
// edge) and which way it heads (-1 or 1).
float patchK1(Particle particle)
{
    return floor(particle.data.x / Order);
}

float patchK2(Particle particle)
{
    return particle.data.x - patchK1(particle) * Order;
}

float particleA(Particle particle)
{
    return particle.data.z * particle.data.w * Reach;
}

float3 particlePoint(Particle particle, float twist)
{
    return surfacePoint(patchK1(particle), patchK2(particle), particleA(particle), particle.data.y, twist);
}

void Spawn(inout Particle particle, uint id)
{
    float patch = min(floor(fixedRandom(id, 1.0) * Order * Order), Order * Order - 1.0);
    // Fine threads across each patch, so the flow reads as streams of light.
    float b = (floor(fixedRandom(id, 2.0) * 24.0) + fixedRandom(id, 3.0) * 0.25 + 0.4) / 24.0 * PI * 0.5;
    float outward = fixedRandom(id, 4.0);
    float side = fixedRandom(id, 5.0) < 0.5 ? -1.0 : 1.0;
    particle.data = float4(patch, b, outward, side);
    particle.velocity = 0.0;
    particle.position = particlePoint(particle, turn());
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float speed = (0.05 + Bass * 0.3 + Beat * 0.08 + DropPulse * 0.15) * (0.8 + fixedRandom(id, 6.0) * 0.4);
    float outward = particle.data.z + speed * dt;
    float3 before = particle.position;
    if (outward > 1.0)
    {
        outward -= 1.0;
    }
    particle.data.z = outward;
    particle.position = particlePoint(particle, turn());
    // Remember the movement for the streak, but not across the jump back to the waist.
    float3 moved = particle.position - before;
    particle.velocity = length(moved) > 0.2 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 cameraEye()
{
    float angle = Time * 0.05 + 0.4;
    return float3(sin(angle) * 3.8, 0.9 + sin(Time * 0.043) * 0.7, cos(angle) * 3.8);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = float3(0.0, 0.0, 0.0);
    // The drop swells it a little rather than flashing it.
    float swell = 1.0 + DropPulse * 0.08 + Bass * 0.03;
    float3 position = particle.position * swell;
    float zoom = 2.5;
    float3 seen = project(position, eye, target, zoom, Time * 0.02);
    float3 seenBefore = project(position - particle.velocity * 0.03, eye, target, zoom, Time * 0.02);

    float k1 = patchK1(particle);
    float k2 = patchK2(particle);
    float outward = particle.data.z;

    // Like thin glass, the surface glows brightest where we see it edge on.
    float3 normal = surfaceNormal(k1, k2, particleA(particle), particle.data.y, turn());
    float3 view = normalize(particle.position - eye);
    float facing = abs(dot(normal, view));
    float rim = 0.3 + 0.9 * pow(1.0 - facing, 3.0);

    // Each petal its own colour round a cool-to-warm wheel, and its own band of the spectrum.
    float patch = particle.data.x;
    float hue = frac(0.5 + k1 / Order * 0.55 + k2 / Order * 0.1 + Time * 0.006);
    float3 colour = hsv2rgb(hue, 0.7, 1.0);
    float level = spectrum(0.03 + patch / (Order * Order) * 0.6);

    // Rings ripple out from the waist on each beat: soft, and only a thin band at a time. With a
    // beat to follow the ring rides through the whole beat; without one it shows just as it lands.
    float ringAt = (outward - BeatPhase) * 14.0;
    float ring = exp(-ringAt * ringAt) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);
    // Bright at the waist, fading out to the frayed edge, and in from nothing after each restart.
    float fade = smoothstep(0.0, 0.08, outward) * (1.0 - smoothstep(0.6, 1.0, outward));
    colour *= (rim * 0.075 * (0.55 + level * 1.3) + ring * 0.12 * fade) * fade * (0.8 + Intensity * 0.4);
    colour *= 1.0 + step(0.997, fixedRandom(id, 7.0)) * Treble * 4.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    sprite.streak = seen.xy - seenBefore.xy;
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    // Focus on the middle of the shape so its far side melts into soft light.
    focus(sprite, seen.z, length(eye - target), 0.1);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 1.8) * (0.012 + Bass * 0.015);
    return particles(uv) + previous(uv) * 0.55 + float3(0.35, 0.45, 0.9) * haze;
}
