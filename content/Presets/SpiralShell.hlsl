// Name: Spiral Shell
// A seashell of light: the logarithmic spiral that shells, horns and galaxies grow by, winding from a fine point out to a wide mouth,
// drawn as a neon wireframe of rings and spiral ridges with light flowing along every line. Each ring plays its own band
// of the spectrum, the shell breathes with the bass, and every beat sends a wave of light from its tip down to its mouth.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-10-31 03:30
// Particles: 262144

// The shell is a tube wound round a cone. Along the spiral, t runs from the tip (0) to the mouth
// (Turns whole turns), and everything grows by the same factor each turn, which is what keeps
// the shell the same shape at every size. Round the tube, v runs once round its circle.
static const float Turns = 5.0;
static const float Rings = 72.0;          // circles of light round the tube along the spiral
static const float Ridges = 14.0;         // spiral lines running the length of the shell
static const float RingShare = 0.62;

float ssGrowth(float along)
{
    return exp(2.6 * (along - 1.0));
}

// The shell's surface: along (0 tip to 1 mouth) and v (once round the tube).
float3 ssPoint(float along, float v)
{
    float s = ssGrowth(along);
    float t = along * Turns * 2.0 * PI + Time * 0.12;
    float tube = 0.5 * (1.0 + Bass * 0.12 + DropPulse * 0.1);
    float width = 1.0 + tube * cos(v);
    // (Particle space has y growing downward, so the shell hangs tip up with its mouth below.)
    return float3(width * cos(t), 1.7 + tube * sin(v), width * sin(t)) * s - float3(0.0, 0.85, 0.0);
}

// A particle's line: data.x 0 for a ring, 1 for a ridge; data.y which one; data.z where it is
// along that line (0 to 1); data.w its speed.
float3 ssPosition(float4 data)
{
    if (data.x < 0.5)
    {
        return ssPoint((data.y + 0.5) / Rings, data.z * 2.0 * PI);
    }
    return ssPoint(data.z, data.y / Ridges * 2.0 * PI);
}

void Spawn(inout Particle particle, uint id)
{
    bool ring = fixedRandom(id, 1.0) < RingShare;
    float count = ring ? Rings : Ridges;
    float which = min(floor(fixedRandom(id, 2.0) * count), count - 1.0);
    // Ridges carry more light towards the wide mouth, where they're longest.
    float along = ring ? fixedRandom(id, 3.0) : sqrt(fixedRandom(id, 3.0));
    particle.data = float4(ring ? 0.0 : 1.0, which, along, 0.6 + fixedRandom(id, 4.0) * 0.8);
    particle.velocity = 0.0;
    particle.position = ssPosition(particle.data);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float speed = (0.04 + Bass * 0.2 + Beat * 0.08 + DropPulse * 0.15) * particle.data.w;
    if (particle.data.x > 0.5)
    {
        // Along the ridges light flows from the tip to the mouth, then starts again at the tip.
        particle.data.z = particle.data.z + speed * dt * 0.6;
        if (particle.data.z > 1.0)
        {
            particle.data.z = sqrt(fixedRandom(id, 5.0 + floor(Time))) * 0.3;
        }
    }
    else
    {
        particle.data.z = frac(particle.data.z + speed * dt);
    }
    float3 before = particle.position;
    particle.position = ssPosition(particle.data);
    float3 moved = particle.position - before;
    particle.velocity = length(moved) > 0.15 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 cameraEye()
{
    float angle = Time * 0.035 + 0.8;
    return float3(sin(angle) * 3.4, -0.9 + sin(Time * 0.027) * 0.5, cos(angle) * 3.4);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = float3(0.0, 0.25, 0.0);
    float zoom = 1.9 * (1.0 + DropPulse * 0.04);
    float roll = sin(Time * 0.04) * 0.08;
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, target, zoom, roll);

    bool ring = particle.data.x < 0.5;
    float along = ring ? (particle.data.y + 0.5) / Rings : particle.data.z;
    // Neon from the tip to the mouth: gold at the fine point, through magenta and violet to teal.
    float3 colour = hsv2rgb(frac(0.12 - along * 0.65 + sin(Time * 0.02) * 0.03), ring ? 0.85 : 0.6, 1.0);
    // Each ring plays its own band: treble at the tip, bass at the mouth.
    float level = spectrum(0.03 + (1.0 - along) * 0.65);
    // A wave of light runs from the tip to the mouth over each beat (with no beat to follow, just as it lands).
    float at = (along - BeatPhase) * 8.0;
    float pulse = exp(-at * at) * (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat);
    float glow = (ring ? 0.08 : 0.1) * (0.35 + level * 1.5) + pulse * 0.14;
    // The fine tip packs many lines into little space; keep it from burning out.
    colour *= glow * lerp(0.35, 1.0, along) * (0.8 + Intensity * 0.4);
    colour *= 1.0 + step(0.997, fixedRandom(id, 6.0)) * Treble * 4.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    float2 streak = seen.xy - seenBefore.xy;
    sprite.streak = streak * min(1.0, 0.012 / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.2 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 2.5) * (0.008 + Bass * 0.012);
    return particles(uv) + previous(uv) * 0.5 + float3(0.4, 0.25, 0.9) * haze;
}
