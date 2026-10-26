// Name: Black Hole
// Spacetime drawn as a glowing neon net sinking into the bottomless throat of a black hole: rings of the net slide slowly down the well and light up with the spectrum, spiral streams of glowing gas whirl down it faster and faster, a ring of trapped light circles the edge, jets fire out of the throat with the bass and every beat sends a gravitational wave rolling out across the net.
// Family: Cosmic
// Tags: Space, Neon, Geometric, 3D
// Added: 2026-10-16 23:54
// Particles: 262144

// Units: the event horizon has radius 1. The well is Flamm's paraboloid, the shape that the space
// around a black hole bends into when one dimension is drawn as height: 2 sqrt(r - 1) above the
// throat, flat far out and plunging straight down at the horizon.
static const float Outer = 12.0;
static const float Depth = 0.85;      // how deep the well is drawn
static const float Meridians = 48.0;
static const float Rings = 28.0;
static const float Streams = 96.0;

// The share of particles in each part: the net, the gas, the ring of trapped light, the jets.
static const float NetShare = 0.34;
static const float GasShare = 0.52;
static const float RingShare = 0.05;

// A gravitational wave rolling out from the throat after each beat. With a beat to follow it rides
// through the whole beat; without one it shows just as it lands.
float holeWave(float r)
{
    float front = 1.4 + BeatPhase * (Outer - 1.4);
    float strength = (SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat) * (1.0 - BeatPhase * 0.7);
    float d = r - front;
    return sin(d * 3.0) * exp(-d * d * 0.6) * strength * 0.35;
}

// The height of the well at radius r, carrying the beat's wave.
float holeWell(float r)
{
    return Depth * (2.0 * sqrt(max(r - 1.0, 0.0)) - 2.0 * sqrt(Outer - 1.0)) + holeWave(r);
}

float3 onWell(float r, float angle, float lift)
{
    return float3(cos(angle) * r, holeWell(r) + lift, sin(angle) * r);
}

// Where a ring of the net sits: rings are spaced evenly in log r and slide down into the well.
float ringRadius(float ring)
{
    float s = frac((ring + 0.5) / Rings - Time * 0.012);
    return exp(lerp(log(1.08), log(Outer), s));
}

// A particle's data: its part (0 net, 1 gas, 2 ring, 3 jet), then two numbers that place it and a speed.
// The net: x part, y which strand (meridians first, then rings), z how far along it, w speed.
// The gas: y which stream, z how far in (0 at the edge, 1 at the throat), w speed.
// The ring: y its angle. The jets: y up or down, z how far out, w its angle round the beam.
float3 holePoint(Particle particle)
{
    float part = particle.data.x;
    float strand = particle.data.y;
    float along = particle.data.z;
    float swirl = Time * 0.05;
    if (part < 0.5)
    {
        if (strand < Meridians)
        {
            // Meridians: closer together near the throat, where the well is steepest.
            float r = 1.03 + (Outer - 1.03) * along * along;
            return onWell(r, strand / Meridians * 2.0 * PI + swirl, 0.0);
        }
        float r = ringRadius(strand - Meridians);
        // The ring turns faster the deeper it is, dragged round by the hole.
        return onWell(r, along * 2.0 * PI + swirl + Time * 0.6 * pow(r, -1.5), 0.0);
    }
    if (part < 1.5)
    {
        // The gas spirals in: four arms of six fine threads each, winding tighter towards the
        // throat, and the whole whirlpool turns faster as it narrows.
        float r = Outer * pow(1.15 / Outer, along);
        float arm = floor(strand / (Streams * 0.25));
        float thread = floor(frac(strand * 0.618034) * 6.0);
        float spread = (thread / 5.0 - 0.5) * 0.45;
        float angle = arm * 0.5 * PI + spread + log(Outer / r) * 2.4 - Time * 0.35 + swirl;
        return onWell(r, angle, 0.08 + (1.0 - along) * 0.1);
    }
    if (part < 2.5)
    {
        // Light trapped in orbit, circling the lip of the throat.
        return onWell(1.5, strand - Time * 2.2, 0.06);
    }
    // The jets fire straight out of the throat along the spin axis, widening as they go.
    float height = along * 9.0;
    float width = 0.05 + height * 0.035;
    float base = holeWell(1.0);
    return float3(cos(particle.data.w) * width, base + strand * height, sin(particle.data.w) * width);
}

void Spawn(inout Particle particle, uint id)
{
    float pick = fixedRandom(id, 1.0);
    float part = pick < NetShare ? 0.0 : pick < NetShare + GasShare ? 1.0 : pick < NetShare + GasShare + RingShare ? 2.0 : 3.0;
    float a = fixedRandom(id, 2.0);
    float b = random(id, 3.0);
    float speed = 0.8 + fixedRandom(id, 4.0) * 0.4;
    if (part < 0.5)
    {
        particle.data = float4(0.0, min(floor(a * (Meridians + Rings)), Meridians + Rings - 1.0), b, speed);
    }
    else if (part < 1.5)
    {
        particle.data = float4(1.0, min(floor(a * Streams), Streams - 1.0), b, speed);
    }
    else if (part < 2.5)
    {
        particle.data = float4(2.0, a * 2.0 * PI, 0.0, speed);
    }
    else
    {
        particle.data = float4(3.0, a < 0.5 ? -1.0 : 1.0, b, fixedRandom(id, 5.0) * 2.0 * PI);
    }
    particle.velocity = 0.0;
    particle.position = holePoint(particle);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float part = particle.data.x;
    if (part < 0.5)
    {
        // Light creeps down the meridians into the throat.
        if (particle.data.y < Meridians)
        {
            particle.data.z = frac(particle.data.z - dt * 0.02 * (1.0 + Bass) * particle.data.w);
        }
    }
    else if (part < 1.5)
    {
        particle.data.z = frac(particle.data.z + dt * (0.03 + Bass * 0.08 + DropPulse * 0.08) * particle.data.w);
    }
    else if (part > 2.5)
    {
        float speed = 1.0 / (particle.data.w + 1.0) + 0.25;
        particle.data.z = frac(particle.data.z + dt * (0.12 + Bass * 0.5 + Beat * 0.25) * speed);
    }
    float3 before = particle.position;
    particle.position = holePoint(particle);
    // Remember the movement for the streak, but not across a jump back to the start.
    float3 moved = particle.position - before;
    particle.velocity = length(moved) > 0.3 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 cameraEye()
{
    float angle = Time * 0.045;
    float elevation = 0.32 + 0.12 * sin(Time * 0.037);
    float range = 14.0 + sin(Time * 0.029) * 1.5;
    return range * float3(cos(angle) * cos(elevation), sin(elevation), sin(angle) * cos(elevation));
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = float3(0.0, -3.0, 0.0);
    float zoom = 1.9 * (1.0 + DropPulse * 0.04);
    float3 seen = project(particle.position, eye, target, zoom, sin(Time * 0.06) * 0.06);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, target, zoom, sin(Time * 0.06) * 0.06);

    float part = particle.data.x;
    float r = length(particle.position.xz);
    float3 colour;
    if (part < 0.5)
    {
        // The net: teal far out, deepening to blue and violet down the well. Each ring glows with
        // its own band of the spectrum, bass at the throat and treble at the rim.
        float depth = saturate(log(r) / log(Outer));
        colour = hsv2rgb(0.5 + (1.0 - depth) * 0.22, 0.85, 1.0);
        bool ring = particle.data.y >= Meridians;
        float level = spectrum(0.03 + depth * 0.6);
        float fadeOut = 1.0 - smoothstep(0.75, 1.0, depth);
        float wave = abs(holeWave(r)) * 2.0;
        colour *= (ring ? 0.07 + level * 0.14 : 0.045) * fadeOut + wave * 0.06;
    }
    else if (part < 1.5)
    {
        // The gas heats as it falls: magenta at the edge, violet, then blue-white near the throat.
        // Gas swinging towards us glows brighter, gas swinging away dimmer.
        float fall = particle.data.z;
        float3 hot = lerp(hsv2rgb(0.88, 0.85, 1.0), hsv2rgb(0.74, 0.8, 1.0), smoothstep(0.0, 0.6, fall));
        hot = lerp(hot, float3(0.75, 0.9, 1.0), smoothstep(0.65, 1.0, fall));
        float3 view = normalize(particle.position - eye);
        float3 orbit = normalize(float3(-particle.position.z, 0.0, particle.position.x));
        float beaming = 1.0 + 0.6 * dot(orbit, view);
        float fade = smoothstep(0.0, 0.1, fall) * (1.0 - smoothstep(0.93, 1.0, fall));
        colour = hot * (0.045 + fall * 0.06) * beaming * fade * (0.7 + Bass * 0.6 + Level * 0.3);
    }
    else if (part < 2.5)
    {
        colour = float3(0.8, 0.95, 1.0) * (0.14 + Treble * 0.25) * (0.6 + 0.4 * sin(particle.data.y * 9.0 + Time * 3.0));
    }
    else
    {
        float along = particle.data.z;
        colour = float3(0.35, 0.7, 1.0) * (0.05 + Bass * 0.08 + Beat * 0.04) * (1.0 - along) * smoothstep(0.0, 0.05, along);
    }
    colour *= 0.8 + Intensity * 0.4;
    colour *= 1.0 + step(0.997, fixedRandom(id, 6.0)) * Treble * 4.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    float2 streak = seen.xy - seenBefore.xy;
    sprite.streak = streak * min(1.0, 0.03 / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.1 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.08);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    return particles(uv) + previous(uv) * 0.5;
}
