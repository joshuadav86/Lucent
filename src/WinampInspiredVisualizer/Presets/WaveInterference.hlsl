// Name: Wave Interference
// A wide sheet of points stretching to the horizon, rippled by rings spreading from three drifting sources; where the ripples meet they build into crests or cancel into calm, each source rising with its own part of the music and sending out a fresh ring on every beat.
// Family: Particles
// Tags: Geometric, 3D, Calm
// Added: 2026-10-17 22:17
// Particles: 262144

// The particles sit on a square grid, like the surface of a still pond seen from low down. Three
// sources, slowly wandering across it, each send out circular waves, and the surface height at
// any point is simply all three added together: the classic interference pattern, with bright
// ridges where the waves agree and smooth lanes where they cancel.
static const float Extent = 8.0;

float gridSide()
{
    return floor(sqrt(ParticleCount));
}

float2 gridPoint(uint id)
{
    float side = gridSide();
    float2 cell = float2(id % (uint)side, id / (uint)side);
    return (cell / (side - 1.0) - 0.5) * Extent;
}

float2 sourcePosition(int i)
{
    float t = Time * 0.05 + i * 2.094;
    return float2(cos(t) * 1.3 + sin(t * 1.7) * 0.3, sin(t * 0.8) * 1.2);
}

float sourceLevel(int i)
{
    return spectrum(i == 0 ? 0.04 : (i == 1 ? 0.2 : 0.45));
}

float surfaceHeight(float2 at)
{
    // Roughly how long ago the last beat was, for the ring each beat sends out.
    float sinceBeat = (1.0 - Beat) * 0.9;
    float height = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float r = length(at - sourcePosition(i));
        float wavelength = 0.55 + i * 0.07;
        float amplitude = (0.045 + sourceLevel(i) * 0.08) / sqrt(1.0 + r * 1.5);
        height += amplitude * sin(r * 2.0 * PI / wavelength - Time * (1.6 + i * 0.25));
        // The beat's ring: a single raised swell spreading outward.
        float ring = r - sinceBeat * 2.6;
        height += Beat * 0.09 * exp(-ring * ring * 18.0) / sqrt(1.0 + r);
    }
    return height * (1.0 + Bass * 0.4);
}

float3 surfacePoint(uint id)
{
    float2 at = gridPoint(id);
    return float3(at.x, surfaceHeight(at), at.y);
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = surfacePoint(id);
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    float3 moved = surfacePoint(id);
    particle.velocity = (moved - particle.position) / max(DeltaTime, 1e-4);
    particle.position = moved;
}

float3 cameraEye()
{
    float angle = Time * 0.03;
    return float3(sin(angle) * 2.6, -1.1 - sin(Time * 0.05) * 0.25, cos(angle) * 2.6);
}

float3 cameraTarget()
{
    return float3(0.0, 0.1, 0.0);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 seen = project(particle.position, eye, cameraTarget(), 1.6, 0.0);

    // Light catches the crests: work out how steep the surface is here and shade it like a calm
    // sea under a low moon, with the highest points glowing pale gold.
    float2 at = particle.position.xz;
    float e = 0.03;
    float dx = surfaceHeight(at + float2(e, 0.0)) - surfaceHeight(at - float2(e, 0.0));
    float dz = surfaceHeight(at + float2(0.0, e)) - surfaceHeight(at - float2(0.0, e));
    float3 normal = normalize(float3(-dx, 2.0 * e, -dz));
    // (World +y appears downward on screen, so the "up" of this surface faces the camera from below.)
    float3 toEye = normalize(eye - particle.position);
    float3 lightDirection = normalize(float3(0.4, -0.5, -0.8));
    float3 halfway = normalize(toEye + lightDirection);
    float sheen = pow(saturate(abs(dot(normal, halfway))), 40.0);
    float height = particle.position.y;

    float3 deep = float3(0.08, 0.3, 0.55);
    float3 crest = float3(0.95, 0.85, 0.6);
    // Remember: on screen, smaller y is higher, so crests have negative height here.
    float rise = saturate(-height * 6.0 + 0.5);
    float3 colour = lerp(deep, float3(0.3, 0.75, 0.9), rise) + crest * sheen * 1.5;

    // Distance fog so the sheet melts into the dark, and a fade at the edges of the grid.
    float distance = seen.z;
    float fog = exp(-max(distance - 2.5, 0.0) * 0.3);
    float edge = saturate((Extent * 0.5 - max(abs(at.x), abs(at.y))) * 1.2);
    colour *= 0.7 * fog * edge * (0.75 + Level * 0.5);

    sprite.position = seen.xy;
    // Any particles left over beyond the last full row of the grid stay hidden.
    sprite.size = (seen.z > 0.1 && id < (uint)(gridSide() * gridSide())) ? 0.8 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, 3.0, 0.03);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // A faint band of sky glow along the horizon.
    float horizon = exp(-abs(p.y + 0.15) * 5.0) * (0.02 + Mid * 0.02);
    return particles(uv) + previous(uv) * 0.35 + float3(0.25, 0.4, 0.6) * horizon;
}
