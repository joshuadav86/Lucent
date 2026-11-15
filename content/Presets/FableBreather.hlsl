// Name: Fable Breather
// The breather surface, a ribbed blossom of constant negative curvature that comes from a wave which never settles,
// drawn in neon: rainbow ribs wind round its axis crossed by gold spines, light streams along every line, each rib
// swells with its own band of the spectrum, and the bass makes the whole shape breathe.
// Family: Particles
// Tags: Geometric, Neon, 3D
// Added: 2026-11-15 17:00
// Particles: 262144

// The breather surface: the pseudospherical surface (curvature -1 everywhere) that belongs to the breather
// solution of the sine-Gordon equation. With w = sqrt(1 - a^2) and D = a ((w cosh(a u))^2 + (a sin(w v))^2):
//     x = -u + 2 w^2 cosh(a u) sinh(a u) / D
//     y = 2 w cosh(a u) (-w cos v cos(w v) - sin v sin(w v)) / D
//     z = 2 w cosh(a u) (-w sin v cos(w v) + cos v sin(w v)) / D
// When w is a fraction the surface closes on itself. With w = (q - 1) / q it closes after q turns of v and has
// 2q - 1 petals, so stepping q through 2, 3, 4, 5, 6 gives 3, 5, 7, 9 and 11 petals.
// Lines of constant u ("ribs") wind round the axis; lines of constant v ("spines") run its length.
static const float Ribs = 22.0;
static const float Spines = 44.0;
static const float RibShare = 0.62;
static const float Tail = 3.0;                // u runs -Tail / a .. Tail / a
static const float Size = 0.62;

// How many turns the surface closes after: it rests on each whole number, then blooms into the next.
float fbTurns()
{
    float clock = Time / 22.0 + BeatGlide / 40.0;
    float at = abs(wrap(clock, 8.0) - 4.0);          // 4..0..4
    return 2.0 + floor(at) + smoothstep(0.3, 0.7, frac(at));
}

// u01 runs -1..1 along the axis, v01 0..1 round the whole surface.
float3 fbPoint(float u01, float v01)
{
    float turns = fbTurns();
    float w = 1.0 - 1.0 / turns;
    float a = sqrt(1.0 - w * w);
    float u = u01 * Tail / a;
    float v = v01 * 2.0 * PI * turns;
    float ch = cosh(a * u);
    float sh = sinh(a * u);
    float sw = sin(w * v);
    float cw = cos(w * v);
    float d = a * (w * w * ch * ch + a * a * sw * sw);
    float x = -u + 2.0 * w * w * ch * sh / d;
    float y = 2.0 * w * ch * (-w * cos(v) * cw - sin(v) * sw) / d;
    float z = 2.0 * w * ch * (-w * sin(v) * cw + cos(v) * sw) / d;
    // The surface is 2 / a across; keep it one size as it changes.
    float3 position = float3(x, y, z) * (a * Size);

    // Each part swells with its own band (the middle rib the bass, the tails the treble) and with the beat.
    // It depends only on where on the surface the point is, so crossing lines still cross.
    float band = spectrum(0.02 + abs(u01) * 0.7);
    float swell = 1.0 + band * 0.14 + Bass * 0.07 + DropPulse * 0.2;
    position.yz *= swell;
    position.yz = rotate(position.yz, Time * 0.07 + BeatGlide * 0.03);
    return position;
}

// data.x: 0 for a rib, 1 for a spine; data.y: the line's u (rib) or v (spine); data.z: how far along; data.w: speed.
float3 fbPosition(float4 data)
{
    return data.x < 0.5 ? fbPoint(data.y, data.z) : fbPoint(data.z, data.y);
}

void Spawn(inout Particle particle, uint id)
{
    bool rib = fixedRandom(id, 1.0) < RibShare;
    float count = rib ? Ribs : Spines;
    float which = min(floor(fixedRandom(id, 2.0) * count), count - 1.0);
    float place = (which + 0.5) / count;
    // Ribs crowd towards the middle, where the surface has most of its shape.
    float centred = place * 2.0 - 1.0;
    float onLine = rib ? centred * abs(centred) : place;
    float along = rib ? fixedRandom(id, 3.0) : fixedRandom(id, 3.0) * 2.0 - 1.0;
    particle.data = float4(rib ? 0.0 : 1.0, onLine, along, 0.6 + fixedRandom(id, 4.0) * 0.8);
    particle.velocity = 0.0;
    particle.position = fbPosition(particle.data);
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float speed = (0.1 + Bass * 0.35 + Beat * 0.2 + DropPulse * 0.5) * particle.data.w;
    float4 ahead = particle.data;
    if (particle.data.x < 0.5)
    {
        particle.data.z = frac(particle.data.z + speed * 0.07 * dt);
        ahead.z = particle.data.z + 0.0005;
    }
    else
    {
        // Light on the spines runs out from the middle to both tails and starts again.
        float way = particle.data.z < 0.0 ? -1.0 : 1.0;
        float along = particle.data.z + way * speed * 0.35 * dt;
        particle.data.z = abs(along) > 1.0 ? along - way : along;
        ahead.z = particle.data.z + way * 0.002;
    }

    particle.position = fbPosition(particle.data);
    // Which way its line runs here, so its streak lies along the line however the shape is moving.
    float3 heading = fbPosition(ahead) - particle.position;
    particle.velocity = heading * rsqrt(max(dot(heading, heading), 1e-12)) * speed;
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    // The camera sways between looking almost down the axis, where it's a star, and seeing it from the side.
    float angle = 0.72 + 0.5 * sin(Time * 0.045);
    float3 eye = float3(cos(angle) * 3.5, -0.7 + sin(Time * 0.031) * 0.7, sin(angle) * 3.5);
    float3 target = 0.0;
    float zoom = 2.5 * (1.0 - DropPulse * 0.08);
    float roll = sin(Time * 0.045) * 0.1;
    float3 seen = project(particle.position, eye, target, zoom, roll);
    float3 seenBefore = project(particle.position - particle.velocity * 0.05, eye, target, zoom, roll);

    bool rib = particle.data.x < 0.5;
    float out0 = abs(rib ? particle.data.y : particle.data.z);
    float band = spectrum(0.02 + out0 * 0.7);
    float run = out0 - BeatPhase;
    float pulse = exp(-run * run * 36.0) * (SongMapped > 0.0 ? 0.4 + Beat * 0.6 : Beat);

    float3 colour;
    if (rib)
    {
        colour = hsv2rgb(frac(0.48 + out0 * 0.46 + sin(Time * 0.02) * 0.05), 0.85, 1.0);
    }
    else
    {
        colour = hsv2rgb(frac(0.09 + out0 * 0.05), 0.72, 1.0);
    }

    float glow = (rib ? 0.14 : 0.075) * (0.3 + band * 1.6) + pulse * 0.16 + DropPulse * 0.07;
    colour *= glow * (0.8 + Intensity * 0.4);
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
    return particles(uv) + previous(uv) * 0.55 + float3(0.3, 0.2, 0.9) * haze;
}
