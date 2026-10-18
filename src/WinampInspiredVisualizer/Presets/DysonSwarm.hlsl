// Name: Dyson Swarm
// A blazing star wrapped in shell after shell of orbiting solar mirrors, hundreds of thousands of them glinting gold as they catch its light; the star throbs with the bass and the great rings of orbits tilt and sway with the music.
// Family: Particles
// Tags: Space, Geometric, 3D
// Added: 2026-10-17 17:12
// Particles: 393216

// Each collector rides its own circular orbit. The orbits are grouped into shells: rings of
// orbits that share a radius and roughly a tilt, like the bands of a gyroscope, so the swarm
// reads as nested hoops of light round the star.
static const int Shells = 7;
static const float StarRadius = 0.62;

// The size, tilt and spread of each shell.
static const float ShellRadius[7] = { 1.25, 1.55, 1.85, 2.2, 2.55, 2.95, 3.4 };
static const float ShellWidth[7] = { 0.05, 0.12, 0.3, 0.08, 0.5, 0.15, 0.04 };

float starPulse()
{
    return 1.0 + Bass * 0.35 + Beat * 0.15;
}

// The orientation of a shell: its tilt and the direction of that tilt drift slowly, and the
// whole shell rocks a little with the music.
float3x3 shellFrame(int shell)
{
    float tilt = fmod(shell * 1.9, 3.0) - 1.5 + 0.25 * sin(Time * 0.04 + shell * 1.3) + Bass * 0.06 * sin(shell * 2.7);
    float node = shell * 2.4 + Time * 0.02 * (1.0 + shell * 0.2);
    float st, ct, sn, cn;
    sincos(tilt, st, ct);
    sincos(node, sn, cn);
    // Tip the orbit plane over by tilt about an axis in the xz plane pointing along node.
    float3 axis = float3(cn, 0.0, sn);
    float3 up = float3(-sn * st, ct, cn * st);
    float3 across = cross(axis, up);
    return float3x3(axis, up, across);
}

int shellOf(uint id)
{
    return min((int)(fixedRandom(id, 1.0) * fixedRandom(id, 1.5) * 1.6 * Shells + fixedRandom(id, 1.7) * 2.0), Shells - 1);
}

void Spawn(inout Particle particle, uint id)
{
    particle.life = 1e6;
    particle.velocity = 0.0;
    particle.position = 0.0;
    // The angle round its orbit, then its own small tilt within the shell.
    particle.data = float4(fixedRandom(id, 2.0) * 2.0 * PI, (fixedRandom(id, 3.0) - 0.5) * 2.0, fixedRandom(id, 4.0) * 2.0 * PI, 0.0);
}

float3 orbitPosition(Particle particle, uint id, int shell)
{
    float radius = ShellRadius[shell] * (1.0 + (fixedRandom(id, 5.0) - 0.5) * 0.04);
    float spread = ShellWidth[shell] * particle.data.y;
    float angle = particle.data.x;
    // A circle in the shell's plane, tipped by this collector's own small inclination.
    float s, c;
    sincos(angle, s, c);
    float3 flat = float3(c, 0.0, s) * radius;
    float lift = sin(angle - particle.data.z) * spread * radius;
    float3 local = flat + float3(0.0, lift, 0.0);
    return mul(local, shellFrame(shell));
}

void Simulate(inout Particle particle, uint id)
{
    int shell = shellOf(id);
    float dt = min(DeltaTime, 0.05);
    // Inner orbits go round faster, as they would round a real star; the music hurries them a little.
    float speed = 0.55 / pow(ShellRadius[shell], 1.5) * (1.0 + Level * 0.6);
    particle.data.x = wrap(particle.data.x + speed * dt, 2.0 * PI);
    float3 position = orbitPosition(particle, id, shell);
    particle.velocity = (position - particle.position) / max(DeltaTime, 1e-4);
    particle.position = position;
}

float3 cameraEye()
{
    float angle = Time * 0.035;
    float height = sin(Time * 0.027) * 2.2 + 0.8;
    return float3(sin(angle) * 6.0, height, cos(angle) * 6.0);
}

static const float Zoom = 1.75;

float3 toScreen(float3 position)
{
    float3 seen = project(position, cameraEye(), 0.0, Zoom, 0.0);
    return float3(seen.x, -seen.y, seen.z);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 position = particle.position;
    float3 eye = cameraEye();
    float3 seen = toScreen(position);
    if (seen.z < 0.3)
    {
        return sprite;
    }
    // Hidden behind the star?
    float3 toPanel = position - eye;
    float reach = length(toPanel);
    float3 ray = toPanel / reach;
    float b = dot(-eye, ray);
    float closest = length(-eye - ray * b);
    if (closest < StarRadius * starPulse() && b < reach)
    {
        return sprite;
    }

    int shell = shellOf(id);
    // Each collector faces the star, wobbling a little as it tracks it.
    float3 toStar = -position / max(length(position), 1e-3);
    float3 wobble = float3(sin(particle.data.z + Time * 0.7), cos(particle.data.z * 1.7 + Time * 0.5), sin(particle.data.z * 2.3 + Time * 0.6)) * 0.35;
    float3 normal = normalize(toStar + wobble);
    float3 toEye = -ray;
    float sunlight = 1.0 / dot(position, position) * starPulse();

    // The mirrored face: a broad sheen when we see it, and a sharp glint when it bounces the
    // star straight at us. The back of each collector glows a dull red with waste heat.
    float facing = dot(normal, toEye);
    float glint = pow(saturate(dot(reflect(-toStar, normal), toEye)), 40.0 + Treble * 60.0);
    float3 gold = lerp(float3(1.0, 0.72, 0.35), float3(0.55, 0.65, 1.0), step(0.8, fixedRandom(id, 6.0)));
    float3 colour = gold * (saturate(facing) * 0.14 + 0.02 + glint * 1.6) * sunlight;
    colour += float3(0.6, 0.12, 0.04) * saturate(-facing) * 0.035 * sunlight;
    // Each shell hums with its own part of the spectrum.
    colour *= 0.6 + spectrum(0.04 + shell * 0.1) * 1.0;

    sprite.position = seen.xy;
    sprite.size = 0.9 + glint * 1.2;
    sprite.colour = colour * 2.0;
    float3 before = toScreen(position - particle.velocity * 0.05);
    sprite.streak = seen.xy - before.xy;
    focus(sprite, seen.z, length(eye), 0.04);
    return sprite;
}

// ---- The star ----

float3 starSurface(float3 normal)
{
    // Boiling granules of plasma, darker and redder towards the limb.
    float3 q = normal * 6.0 + float3(0.0, Time * 0.05, 0.0);
    float cells = noise3(q + noise3(q * 1.7 + Time * 0.1) * 0.8) * 0.5 + 0.5;
    float fine = noise3(normal * 22.0 - Time * 0.2) * 0.5 + 0.5;
    float heat = 0.55 + cells * 0.35 + fine * 0.15;
    return lerp(float3(1.0, 0.35, 0.08), float3(1.0, 0.85, 0.6), heat * heat) * heat * heat;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 direction = cameraRay(eye, 0.0, float2(p.x, -p.y), Zoom, 0.0);
    float pulse = starPulse();
    float radius = StarRadius * pulse;

    // Deep space with faint stars.
    float3 colour = float3(0.002, 0.002, 0.006);
    float2 sky = float2(atan2(direction.x, direction.z), asin(clamp(direction.y, -1.0, 1.0))) * 120.0;
    float h = hash(floor(sky));
    float2 f = frac(sky) - 0.5;
    colour += step(0.975, h) * exp(-dot(f, f) * 30.0) * (h - 0.975) * 40.0 * float3(0.8, 0.85, 1.0);

    float b = dot(-eye, direction);
    float closest = length(-eye - direction * b);
    if (closest < radius && b > 0.0)
    {
        float t = b - sqrt(radius * radius - closest * closest);
        float3 normal = normalize(eye + direction * t);
        float limb = saturate(dot(normal, -direction));
        colour = starSurface(normal) * (0.3 + 0.7 * pow(limb, 0.6)) * 1.5 * pulse;
    }
    else
    {
        // The corona: a glow round the star with fine streamers flaring with the bass.
        float above = (closest - radius) / radius;
        float3 side = normalize(-eye - direction * b);
        float angle = atan2(dot(side, normalize(cross(eye, float3(0.0, 1.0, 0.0)))), side.y);
        float streamers = 0.6 + 0.4 * noise(float2(angle * 6.0, Time * 0.3)) + 0.3 * noise(float2(angle * 20.0, Time * 0.5));
        colour += float3(1.0, 0.6, 0.25) * (exp(-above * 6.0 / streamers) * 1.2 + exp(-above * 1.2) * 0.12) * pulse * step(0.0, b);
    }

    return colour + particles(uv);
}
