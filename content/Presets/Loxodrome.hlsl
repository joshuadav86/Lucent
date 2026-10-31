// Name: Loxodrome
// A lantern of light woven from loxodromes, the spirals a ship traces when it holds one compass bearing from pole to pole.
// Sixteen wind one way and sixteen the other, crossing in a lattice of diamonds with light racing along every strand, round a
// smaller sphere woven the opposite way inside it. Each latitude swells with its own band of the spectrum, the bass winds the
// strands tighter, every beat sends a ring of light from the equator to both poles, and a drop throws the lantern wide open.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-10-31 17:00
// Particles: 262144

// A loxodrome crosses every meridian at the same angle. In Mercator's map it's a straight line, so its
// longitude grows in step with the Mercator height of its latitude: longitude = start + twist * m, where
// m = asinh(tan(latitude)). Strands are spread evenly round the globe and wound both ways, so they cross
// in diamonds. Particles are spread evenly in latitude, which keeps the light even along each strand.
static const float Strands = 16.0;         // in each direction
static const float InnerStrands = 8.0;
static const float InnerShare = 0.22;      // the share of particles on the inner sphere
static const float MostLatitude = 1.46;    // just short of the poles, where the strands would all pile up

// The lattice's twist breathes slowly by itself and winds tighter with the bass and on a drop.
// (The beat clock BeatCount - Beat only moves forward, so the music can push it without jitter.)
float lxTwist(bool inner)
{
    float clock = Time * 0.05 + (BeatCount - Beat) * 0.015;
    float twist = 0.9 + sin(clock) * 0.35 + Bass * 0.35 + DropPulse * 0.6;
    return inner ? -twist * 1.4 : twist;
}

// How far the lantern swells at a latitude: equator plays the bass, the poles the treble, the same in both
// halves, plus the beat's ring running out from the equator to the poles.
float lxRadius(float latitude, bool inner)
{
    float fromEquator = abs(latitude) / MostLatitude;
    float band = spectrum(0.02 + fromEquator * 0.6);
    float at = (fromEquator - BeatPhase) * 6.0;
    float ring = exp(-at * at) * (SongMapped > 0.0 ? 0.4 + Beat * 0.6 : Beat);
    float swell = band * 0.3 + ring * 0.08 + Bass * 0.06 + DropPulse * 0.3 + BuildUp * 0.05;
    return inner ? 0.42 * (1.0 + Mid * 0.25 + ring * 0.12) : 1.0 + swell;
}

// data.x: 0 or 1 for the outer sphere's two windings, 2 for the inner sphere; data.y: which strand;
// data.z: latitude; data.w: speed.
float3 lxPosition(float4 data)
{
    bool inner = data.x > 1.5;
    float way = data.x < 0.5 ? 1.0 : -1.0;
    float count = inner ? InnerStrands : Strands;
    float latitude = data.z;
    float slope = tan(latitude);
    float mercator = log(slope + sqrt(slope * slope + 1.0));   // asinh
    float spin = Time * (inner ? -0.11 : 0.07);
    float longitude = (data.y + (data.x > 0.5 && !inner ? 0.5 : 0.0)) / count * 2.0 * PI
        + way * lxTwist(inner) * mercator + spin;
    float r = lxRadius(latitude, inner);
    // (Particle space has y growing downward; the lantern is the same both ways up.)
    return float3(cos(latitude) * cos(longitude), sin(latitude), cos(latitude) * sin(longitude)) * r;
}

void Spawn(inout Particle particle, uint id)
{
    bool inner = fixedRandom(id, 1.0) < InnerShare;
    float count = inner ? InnerStrands : Strands;
    float kind = inner ? 2.0 : (fixedRandom(id, 2.0) < 0.5 ? 0.0 : 1.0);
    float which = min(floor(fixedRandom(id, 3.0) * count), count - 1.0);
    float latitude = (fixedRandom(id, 4.0) * 2.0 - 1.0) * MostLatitude;
    particle.data = float4(kind, which, latitude, 0.6 + fixedRandom(id, 5.0) * 0.8);
    particle.velocity = 0.0;
    particle.position = lxPosition(particle.data);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    // One winding flows north and the other south, so the weave seems to turn through itself.
    float way = particle.data.x < 0.5 ? 1.0 : -1.0;
    float speed = (0.12 + Bass * 0.5 + Beat * 0.25 + DropPulse * 0.6) * particle.data.w;
    float latitude = particle.data.z + way * speed * dt;
    // Off one pole, on again at the other.
    if (abs(latitude) > MostLatitude)
    {
        latitude -= sign(latitude) * 2.0 * MostLatitude;
    }
    particle.data.z = latitude;

    float3 before = particle.position;
    particle.position = lxPosition(particle.data);
    float3 moved = particle.position - before;
    particle.velocity = length(moved) > 0.15 ? 0.0 : moved / max(DeltaTime, 1e-4);
}

float3 lxEye()
{
    float angle = Time * 0.04 + 0.6;
    return float3(sin(angle) * 3.3, -1.1 + sin(Time * 0.031) * 0.7, cos(angle) * 3.3);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = lxEye();
    float3 target = float3(0.0, 0.0, 0.0);
    float zoom = 2.0 * (1.0 - DropPulse * 0.08);
    float roll = sin(Time * 0.05) * 0.1;
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.03, eye, target, zoom, roll);

    bool inner = particle.data.x > 1.5;
    float count = inner ? InnerStrands : Strands;
    float fromEquator = abs(particle.data.z) / MostLatitude;
    // Two neon palettes: one winding teal to blue, the other magenta to gold; the inner sphere white-violet.
    float strand = particle.data.y / count;
    float drift = sin(Time * 0.02) * 0.04;
    float hue = particle.data.x < 0.5 ? 0.47 + strand * 0.16 : 0.86 + strand * 0.2;
    float3 colour = hsv2rgb(frac(hue + drift + fromEquator * 0.05), inner ? 0.35 : 0.85, 1.0);
    if (inner)
    {
        colour = hsv2rgb(frac(0.74 + drift), 0.45, 1.0);
    }

    float band = spectrum(0.02 + fromEquator * 0.6);
    float at = (fromEquator - BeatPhase) * 6.0;
    float ring = exp(-at * at) * (SongMapped > 0.0 ? 0.4 + Beat * 0.6 : Beat);
    float glow = (inner ? 0.07 : 0.09) * (0.3 + band * 1.6) + ring * 0.16 + DropPulse * 0.08;
    // Every strand converges at the poles, so dim them there to keep them from burning out.
    glow *= lerp(1.0, 0.3, smoothstep(0.7, 1.0, fromEquator));
    colour *= glow * (0.8 + Intensity * 0.4);
    // A few sparks along the strands catch the treble.
    colour *= 1.0 + step(0.996, fixedRandom(id, 6.0)) * Treble * 5.0;
    colour *= saturate(particle.age * 2.0);

    sprite.position = seen.xy;
    float2 streak = seen.xy - seenBefore.xy;
    sprite.streak = streak * min(1.0, 0.014 / max(length(streak), 1e-5));
    sprite.size = seen.z > 0.2 ? 0.9 : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.05);
    return sprite;
}

float3 Render(float2 uv, float2 p)
{
    float haze = exp(-dot(p, p) * 2.2) * (0.006 + Bass * 0.012 + DropPulse * 0.02);
    return particles(uv) + previous(uv) * 0.55 + float3(0.25, 0.3, 0.9) * haze;
}
