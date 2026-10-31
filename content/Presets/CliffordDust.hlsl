// Name: Clifford Dust
// A veil of fine luminous dust hangs in the dark, folding into silk-like ribbons and shells that slowly melt from one shape to the next. Slow dust glows deep blue, fast dust burns pale gold, and the whole veil swells softly with the music.
// Family: Particles
// Tags: Calm, Dark
// Added: 2026-10-17 22:02
// Particles: 458752

// A Clifford attractor: each speck of dust jumps by a simple rule made of four sines and cosines.
// The jumps look random, but every speck lands on the same delicate shape. Its four numbers glide
// slowly along a path chosen so the shape always stays rich and never collapses to a few dots.
static const float Period = 150.0;

// Each speck jumps somewhere new every frame, so the mouse parts the veil where it's drawn rather than pushing each speck (see ParticleMain.hlsli).
#define MOUSE_MOVES_PLACES

float4 shapeNumbers()
{
    float t = Time * 2.0 * PI / Period;
    float4 n = float4(-1.931 + 0.265 * sin(2.0 * t + 2.055),
                       1.633 + 0.131 * sin(3.0 * t + 3.281),
                      -1.052 + 0.169 * sin(2.0 * t + 0.92),
                       1.234 + 0.300 * sin(3.0 * t + 0.281));
    // The music breathes into it, just a little.
    n.x -= Bass * 0.025;
    n.w += Mid * 0.03;
    return n;
}

float2 jump(float2 v, float4 n)
{
    return float2(sin(n.x * v.y) + n.z * cos(n.x * v.x), sin(n.y * v.x) + n.w * cos(n.y * v.y));
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = float3(random(id, 1.0) * 2.0 - 1.0, random(id, 2.0) * 2.0 - 1.0, 0.0);
    particle.velocity = 0.0;
    particle.data = 0.0;
    // A slow trickle of fresh specks keeps every part of the shape filled as it changes.
    particle.life = 4.0 + random(id, 3.0) * 12.0;
}

void Simulate(inout Particle particle, uint id)
{
    float4 n = shapeNumbers();
    float2 v = particle.position.xy;
    float2 next = jump(v, n);
    particle.velocity.xy = next - v;
    particle.position.xy = next;
}

// Deep indigo for the slowest specks, through sea-teal, to a pale warm gold for the fastest.
float3 dustColour(float speed)
{
    float t = saturate(speed / 3.6);
    float3 slow = float3(0.12, 0.16, 0.85);
    float3 middle = float3(0.1, 0.75, 0.7);
    float3 fast = float3(1.0, 0.78, 0.45);
    return t < 0.5 ? lerp(slow, middle, t * 2.0) : lerp(middle, fast, t * 2.0 - 1.0);
}

Sprite Show(Particle particle, uint id)
{
    float speed = length(particle.velocity.xy);
    // The veil turns slowly and swells a touch on each beat.
    float2 seen = rotate(particle.position.xy, PI * 0.5 + sin(Time * 0.021) * 0.35) * (0.42 + Beat * 0.006);

    float level = spectrum(0.04 + saturate(speed / 3.6) * 0.6);
    Sprite sprite = (Sprite)0;
    sprite.position = seen;
    sprite.size = 0.7;
    sprite.colour = dustColour(speed) * 0.05 * (0.75 + level * 0.7 + Beat * 0.25);
    sprite.colour *= 1.0 + step(0.998, fixedRandom(id, 4.0)) * Treble * 5.0;
    // Fresh specks wait a few jumps until they've landed on the shape.
    sprite.colour *= step(0.15, particle.age);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float3 ground = float3(0.004, 0.005, 0.012) * (1.0 - saturate(length(p) * 0.5));
    return ground + particles(uv) + previous(uv) * 0.7;
}
