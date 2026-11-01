// Name: Riemann Sphere
// A neon globe of latitude rings and great circles rests on a dark floor, lit from its north pole, so every ring
// throws a circle of light onto the floor around it: the stereographic projection, the map that turns circles into
// circles. As the globe tumbles, the circles on the floor swell, swing out to the horizon and fold back in. Each
// ring plays its own band of the spectrum, light races along every line, the bass lifts the globe and spreads the
// floor's circles wider, every beat fans rays out of the pole, and a drop spins the globe and flares it white.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-11-01 04:00
// Particles: 262144

// The world here is y-up: the floor is y = 0 and the globe (radius 1) sits on it, centred at (0, 1 + lift, 0), with
// the light at its north pole. A point P on the globe lands on the floor where the line from the pole through P
// meets it. The globe's lines are drawn in the globe's own turning frame, so as it tumbles the light from the pole
// meets them at new angles and their circles on the floor move. (Particle space is y-down, so y flips at the end.)
static const float Rings = 11.0;           // latitude rings, in the globe's own frame
static const float Circles = 6.0;          // great circles through its poles (twelve meridians)
static const float MostLatitude = 1.3;     // the rings stop short of the globe's own poles
static const float FloorReach = 5.5;       // the floor's circles fade out from here
static const float ShareGlobe = 0.4;
static const float ShareFloor = 0.9;       // the rest are the rays from the pole

// The globe's tumble: steady, plus a push on every beat and a spin on a drop. (BeatCount - Beat only moves
// forward, so the music can push it without ever pulling it back.)
float rsClock()
{
    return Time * 0.09 + (BeatCount - Beat) * 0.04;
}

// How high the globe floats off the floor: the bass and a drop lift it, which spreads its circles wider.
float rsLift()
{
    return 0.06 + Bass * 0.22 + DropPulse * 0.3 + BuildUp * 0.08;
}

float3 rsPole()
{
    return float3(0.0, 2.0 + rsLift(), 0.0);
}

// A direction in the globe's own frame turned into the world's: a slow tilt that rocks to and fro, so the globe's
// own poles swing past the light, and a turn round the vertical.
float3 rsTurn(float3 d)
{
    float clock = rsClock();
    float tilt = 1.1 * sin(clock * 0.7) + 0.35 + DropPulse * 0.6;
    float spin = clock * 1.3 + DropPulse * 1.5;
    d.yz = rotate(d.yz, tilt);
    d.xz = rotate(d.xz, spin);
    return d;
}

float rsRingLatitude(float ring)
{
    return (ring / (Rings - 1.0) * 2.0 - 1.0) * MostLatitude;
}

float rsRingBand(float ring)
{
    return spectrum(0.02 + ring / Rings * 0.6);
}

// A point on line `which` (rings first, then the great circles) at angle a, on the globe, in the world.
float3 rsGlobe(float which, float a)
{
    float3 d;
    if (which < Rings)
    {
        float latitude = rsRingLatitude(which);
        // Each ring swells a little with its band, like a breath through the globe.
        float r = 1.0 + rsRingBand(which) * 0.05;
        d = float3(cos(latitude) * cos(a), sin(latitude), cos(latitude) * sin(a)) * r;
    }
    else
    {
        float longitude = (which - Rings) / Circles * PI;
        d = float3(cos(a) * cos(longitude), sin(a), cos(a) * sin(longitude));
    }
    return float3(0.0, 1.0 + rsLift(), 0.0) + rsTurn(d);
}

// Where the light from the pole through p lands on the floor, and how much it was stretched getting there.
float3 rsFloor(float3 p, out float stretch)
{
    float3 pole = rsPole();
    stretch = pole.y / max(pole.y - p.y, 0.02);
    float3 x = pole + (p - pole) * stretch;
    x.y = 0.0;
    return x;
}

// data.x: kind (0 globe, 1 floor, 2 ray); data.y: which line; data.z: angle along it; data.w: the particle's own
// random number (for rays, how far along the ray it is).
float3 rsPosition(float4 data, out float stretch)
{
    float3 p = rsGlobe(data.y, data.z);
    stretch = 1.0;
    if (data.x > 0.5)
    {
        float3 x = rsFloor(p, stretch);
        if (data.x > 1.5)
        {
            // A ray from the pole, through the globe, down to the floor.
            x = lerp(rsPole(), x, data.w);
        }
        p = x;
    }
    return float3(p.x, -p.y, p.z);
}

void Spawn(inout Particle particle, uint id)
{
    float pick = fixedRandom(id, 1.0);
    float kind = pick < ShareGlobe ? 0.0 : (pick < ShareFloor ? 1.0 : 2.0);
    float which = min(floor(fixedRandom(id, 2.0) * (Rings + Circles)), Rings + Circles - 1.0);
    float a = fixedRandom(id, 3.0) * 2.0 * PI;
    // Rays bunch towards the pole, where they start, and thin out towards the floor.
    float own = kind > 1.5 ? pow(fixedRandom(id, 4.0), 1.5) : fixedRandom(id, 4.0);
    particle.data = float4(kind, which, a, own);
    particle.velocity = 0.0;
    float stretch;
    particle.position = rsPosition(particle.data, stretch);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    // Light races round every line, rings one way and great circles the other.
    float way = particle.data.y < Rings ? 1.0 : -1.0;
    float speed = 0.25 + Mid * 0.6 + Beat * 0.4 + DropPulse * 0.8;
    particle.data.z = wrap(particle.data.z + way * speed * dt, 2.0 * PI);
    float3 before = particle.position;
    float stretch;
    particle.position = rsPosition(particle.data, stretch);
    float3 moved = particle.position - before;
    // Far out on the floor a small step on the globe is a big jump; those get no streak.
    particle.velocity = length(moved) > 0.2 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 rsEye()
{
    float angle = Time * 0.035 + 0.5;
    float height = 2.9 + sin(Time * 0.027) * 0.6;
    return float3(sin(angle) * 6.2, -height, cos(angle) * 6.2);
}

float3 rsTarget()
{
    return float3(0.0, -0.9, 0.0);
}

float rsZoom()
{
    return 1.8 * (1.0 + BuildUp * 0.06 - DropPulse * 0.06);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = rsEye();
    float3 target = rsTarget();
    float zoom = rsZoom();
    float roll = sin(Time * 0.04) * 0.06;
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.025, eye, target, zoom, roll);

    float kind = particle.data.x;
    float which = particle.data.y;
    bool ring = which < Rings;
    float stretch;
    rsPosition(particle.data, stretch);

    // Rings run teal through blue to magenta from the globe's south to north; the great circles are warm gold.
    // Every circle on the floor keeps the colour of the line that throws it.
    float drift = sin(Time * 0.02) * 0.03;
    float band = ring ? rsRingBand(which) : Mid;
    float3 colour = ring ? hsv2rgb(frac(0.48 + which / (Rings - 1.0) * 0.42 + drift), 0.85, 1.0)
                         : hsv2rgb(0.1 + drift * 0.5, 0.6, 1.0);
    // Every beat a pulse runs up the great circles from the globe's south pole to its north.
    float at = (frac(particle.data.z / (2.0 * PI)) - BeatPhase) * 8.0;
    float pulse = ring ? 0.0 : exp(-at * at) * (SongMapped > 0.0 ? 0.4 + Beat * 0.6 : Beat);
    float glow = (ring ? 0.1 * (0.3 + band * 1.6) : 0.07 * (0.5 + Mid * 0.8)) + pulse * 0.12;
    float size = 0.9;
    if (kind < 0.5)
    {
        glow *= 1.3 + Beat * 0.25;
    }
    else if (kind < 1.5)
    {
        // The floor's circles: as bright along their length as the lines that throw them, so the big ones out
        // near the horizon don't fade to dust, then fading out before they reach it.
        float reach = length(particle.position.xz);
        glow *= 0.8 * clamp(stretch, 1.0, 4.0) * smoothstep(FloorReach, FloorReach * 0.65, reach);
        size = 1.0;
    }
    else
    {
        // The rays fan out of the pole on each beat, through every twelfth point on the lines.
        float lit = step(0.92, frac(particle.data.z * 1.7 + which * 0.37));
        float reach = length(particle.position.xz);
        colour = lerp(colour, float3(1.0, 0.95, 0.85), 0.5);
        glow = lit * 0.05 * (0.15 + Beat * 1.0 + DropPulse * 1.2 + BuildUp * 0.3)
            * (1.0 - particle.data.w * 0.6) * smoothstep(FloorReach, FloorReach * 0.6, reach);
        size = 1.3;
    }
    colour = lerp(colour, float3(1.0, 1.0, 1.0), DropPulse * 0.5);
    colour *= glow * (0.8 + Intensity * 0.4);
    // A few sparks along the lines catch the treble.
    colour *= 1.0 + step(0.995, fixedRandom(id, 6.0)) * Treble * 5.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    float2 streak = seen.xy - seenBefore.xy;
    sprite.streak = streak * min(1.0, 0.008 / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.3 ? size : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.04);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    // The light at the pole: a small warm star that flares on the beat.
    float3 pole = rsPole();
    float2 star = project(float3(pole.x, -pole.y, pole.z), rsEye(), rsTarget(), rsZoom(), sin(Time * 0.04) * 0.06).xy;
    float d = dot(p - star, p - star);
    float3 light = float3(1.0, 0.9, 0.75) * (exp(-d * 3000.0) * 0.9 + exp(-d * 120.0) * 0.06)
        * (0.7 + Beat * 0.6 + DropPulse * 1.0);
    return particles(uv) + previous(uv) * 0.3 + light;
}
