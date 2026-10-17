// Name: Hourglass
// A glass hourglass in a polished brass frame hangs in a dark room, glowing sand pouring through its narrow waist and heaping up below in time with the bass; every few bars, on a strong downbeat, it turns itself over and starts again.
// Family: Ray traced
// Tags: Calm, 3D
// Particles: 131072

// The glass, the brass frame and the two heaps of sand are ray traced; the falling stream, the
// splashes where it lands and the grains running down the heap are particles, drawn on top.
// The sand's story is told in beats: it turns over on the first beat of every sixteen, and on
// each beat after that another pulse of sand runs through, so the top is empty just as the next
// turn comes round.

static const float BeatsPerTurn = 16.0;
static const float Gravity = 3.2;

// ---- The story of the sand ----

// How many times it has turned over.
float turns()
{
    return floor(BeatCount / BeatsPerTurn);
}

float beatInTurn()
{
    return BeatCount - turns() * BeatsPerTurn;
}

// How much of the sand has run through this time, 0..1.
float flowed()
{
    float k = beatInTurn();
    if (k < 0.5)
    {
        return 0.0;
    }
    return saturate((k - 1.0 + (1.0 - Beat)) / (BeatsPerTurn - 1.5));
}

// The hourglass's angle round the z axis: half a turn more on every turn, swung over in the
// first beat with a little overshoot.
float tilt()
{
    float progress = beatInTurn() < 0.5 ? saturate((1.0 - Beat) / 0.9) : 1.0;
    float swing = smoothstep(0.0, 1.0, progress);
    swing += sin(progress * PI) * 0.08 * progress;
    return PI * (turns() - 1.0 + swing) + sin(Time * 0.4) * 0.025;
}

// Which bulb (+1 or -1 along the glass's own axis) the sand is running out of this turn.
float sourceSide()
{
    return frac(turns() * 0.5) < 0.25 ? 1.0 : -1.0;
}

// ---- Shapes, in the glass's own frame (y along its axis) ----

float glassRadius(float y)
{
    return 0.065 + 0.5 * pow(max(sin(PI * saturate(abs(y))), 0.0), 0.85);
}

float glassShell(float3 q)
{
    float rho = length(q.xz);
    float d = abs(rho - glassRadius(q.y)) * 0.45 - 0.008;
    return max(d, abs(q.y) - 1.0);
}

float brass(float3 q)
{
    float rho = length(q.xz);
    // Two round plates, with a turned lip round each.
    float plate = max(rho - 0.74, abs(abs(q.y) - 1.1) - 0.06) - 0.02;
    float lip = length(float2(rho - 0.72, abs(q.y) - 1.1)) - 0.065;
    // Three turned posts between them.
    float angle = wrap(atan2(q.z, q.x) + PI / 3.0, 2.0 * PI / 3.0) - PI / 3.0;
    float2 across = float2(cos(angle), sin(angle)) * rho - float2(0.66, 0.0);
    float beads = 0.03 + 0.012 * pow(abs(sin(q.y * 9.0)), 4.0) + 0.02 * smoothstep(0.9, 1.05, abs(q.y));
    float post = max(length(across) - beads, abs(q.y) - 1.1);
    return min(min(plate, lip), post);
}

// The sand's surfaces in the frame where it runs from +y down to -y.
float sinkLevel()
{
    return -1.0 + 0.7 * flowed();
}

float sourceLevel()
{
    return 0.72 * pow(1.0 - flowed(), 0.85);
}

float sand(float3 q)
{
    float s = sourceSide();
    float3 f = float3(q.x, q.y * s, q.z);
    float rho = length(f.xz);
    float inside = rho - glassRadius(f.y) + 0.012;
    float amount = flowed();
    // Below: a cone-shaped heap rising where the stream lands.
    float heap = sinkLevel() + 0.16 * sqrt(amount) * saturate(1.0 - rho / 0.55);
    float below = max(f.y - heap, -f.y - 1.0);
    // Above: what's left, with a crater draining into the neck.
    float level = sourceLevel();
    float crater = level - 0.14 * min(amount * 6.0, 1.0) * saturate(1.0 - rho / 0.45) * saturate(level * 4.0);
    float above = max(f.y - crater, -f.y);
    float d = min(max(inside, below * 0.7), max(inside, above * 0.7));
    return (level > 0.003 || f.y < 0.0) ? d * 0.5 : max(d, inside) * 0.5;
}

// ---- Camera and frame ----

float3 cameraEye()
{
    float angle = Time * 0.08 + 0.4;
    return float3(sin(angle) * 3.6, 0.45 + 0.35 * sin(Time * 0.06), cos(angle) * 3.6);
}

static const float Zoom = 2.0;

// From the world into the glass's own frame, and back.
float3 toGlass(float3 p, float angle)
{
    return float3(rotate(p.xy, -angle), p.z);
}

float3 toWorld(float3 p, float angle)
{
    return float3(rotate(p.xy, angle), p.z);
}

// ---- Particles: the falling stream, splashes and grains running down the heap ----

static const float StreamShare = 0.55;
static const float SplashShare = 0.8;

void Spawn(inout Particle particle, uint id)
{
    particle.life = 1e6;
    particle.position = 0.0;
    particle.velocity = 0.0;
    particle.data = float4(fixedRandom(id, 1.0), fixedRandom(id, 2.0), fixedRandom(id, 3.0), 0.0);
}

void Simulate(inout Particle particle, uint id)
{
}

float3 toScreen(float3 position)
{
    float3 seen = project(position, cameraEye(), 0.0, Zoom, 0.0);
    return float3(seen.x, -seen.y, seen.z);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float amount = flowed();
    // Running only while there is sand above and the glass is upright.
    if (beatInTurn() < 0.5 || sourceLevel() < 0.002)
    {
        return sprite;
    }
    float share = (id + 0.5) / ParticleCount;
    float s = sourceSide();
    float landing = sinkLevel() + 0.16 * sqrt(amount);
    float3 f;
    float3 motion;
    float glow;
    // The bass thickens the stream: more of its grains show.
    float flow = 0.35 + Bass * 0.65 + Beat * 0.3;
    if (share < StreamShare)
    {
        if (particle.data.z > flow)
        {
            return sprite;
        }
        // Falling from the neck to the top of the heap below.
        float drop = -landing;
        float fallTime = sqrt(2.0 * drop / Gravity);
        float u = frac(Time * lerp(0.8, 1.2, particle.data.x) / fallTime + particle.data.y);
        float fallen = 0.5 * Gravity * (u * fallTime) * (u * fallTime);
        float spread = 0.012 + fallen * 0.02;
        float a = particle.data.x * 40.0;
        f = float3(cos(a) * spread * particle.data.y, -fallen, sin(a) * spread * particle.data.y);
        motion = float3(0.0, -Gravity * u * fallTime, 0.0);
        glow = 0.016;
    }
    else if (share < SplashShare)
    {
        // Grains bouncing off the heap where the stream lands.
        if (particle.data.z > flow * 0.8)
        {
            return sprite;
        }
        float u = frac(Time * lerp(1.2, 2.2, particle.data.x) + particle.data.y);
        float angle = particle.data.y * 2.0 * PI * 13.0;
        float speed = lerp(0.15, 0.45, particle.data.z);
        float3 launch = float3(cos(angle) * speed, lerp(0.4, 0.9, particle.data.x), sin(angle) * speed);
        float t = u * 0.35;
        f = float3(0.0, landing, 0.0) + launch * t + float3(0.0, -0.5 * Gravity * 2.0 * t * t, 0.0);
        f.y = max(f.y, landing - 0.16 * sqrt(amount) * saturate(length(f.xz) / 0.55));
        motion = launch + float3(0.0, -Gravity * 2.0 * t, 0.0);
        glow = 0.045 * (1.0 - u);
    }
    else
    {
        // Grains running down the slope of the heap.
        float u = frac(Time * lerp(0.15, 0.35, particle.data.x) + particle.data.y);
        float angle = particle.data.z * 2.0 * PI;
        float rho = u * 0.5;
        f = float3(cos(angle) * rho, landing - 0.16 * sqrt(amount) * (rho / 0.55) + 0.006, sin(angle) * rho);
        motion = float3(cos(angle), -0.3, sin(angle)) * 0.2;
        glow = 0.03 * (1.0 - u) * saturate(amount * 10.0);
    }

    float3 glass = float3(f.x, f.y * s, f.z);
    float3 world = toWorld(glass, tilt());
    float3 seen = toScreen(world);
    if (seen.z < 0.3)
    {
        return sprite;
    }
    float3 before = toScreen(toWorld(float3(f.x, f.y * s, f.z) - float3(motion.x, motion.y * s, motion.z) * 0.02, tilt()));
    float sparkle = 1.0 + step(0.97, fixedRandom(id, 5.0)) * (2.0 + Treble * 6.0);
    sprite.position = seen.xy;
    sprite.size = 1.0;
    sprite.colour = float3(1.0, 0.62, 0.25) * glow * sparkle * (0.8 + Level * 0.5);
    sprite.streak = seen.xy - before.xy;
    focus(sprite, seen.z, length(cameraEye()), 0.03);
    return sprite;
}

// ---- Ray tracing ----

static const int Nothing = 0;
static const int Brass = 1;
static const int Sand = 2;
static const int GlassWall = 3;

float sceneDistance(float3 q, out int material)
{
    float g = glassShell(q);
    float b = brass(q);
    float n = sand(q);
    material = GlassWall;
    float d = g;
    if (b < d)
    {
        d = b;
        material = Brass;
    }
    if (n < d)
    {
        d = n;
        material = Sand;
    }
    return d;
}

float3 normalOf(float3 q, int material)
{
    const float2 e = float2(0.0015, -0.0015);
    float3 n;
    if (material == Brass)
    {
        n = e.xyy * brass(q + e.xyy) + e.yyx * brass(q + e.yyx) + e.yxy * brass(q + e.yxy) + e.xxx * brass(q + e.xxx);
    }
    else if (material == Sand)
    {
        n = e.xyy * sand(q + e.xyy) + e.yyx * sand(q + e.yyx) + e.yxy * sand(q + e.yxy) + e.xxx * sand(q + e.xxx);
    }
    else
    {
        // The glass: the side of the shell facing the ray's side.
        float rho = length(q.xz);
        float slope = (glassRadius(q.y + 0.002) - glassRadius(q.y - 0.002)) / 0.004;
        n = float3(q.x / max(rho, 1e-4), -slope, q.z / max(rho, 1e-4));
    }
    return normalize(n);
}

static const float3 KeyLight = float3(-0.55, 0.65, 0.5);

// The dark room: a warm lamp glow, a pair of soft panels for the glass and brass to mirror, and a
// polished dark table far below.
float3 room(float3 direction)
{
    float3 colour = lerp(float3(0.03, 0.02, 0.015), float3(0.006, 0.006, 0.01), saturate(direction.y * 0.5 + 0.5));
    colour += float3(1.0, 0.75, 0.45) * (pow(saturate(dot(direction, normalize(KeyLight))), 24.0) * 3.0 + pow(saturate(dot(direction, normalize(KeyLight))), 3.0) * 0.08);
    // Two tall soft panels, warm and cool, for the brass and glass to mirror.
    float around = atan2(direction.z, direction.x);
    float2 panel = abs(float2(wrap(around - 2.4 + PI, 2.0 * PI) - PI, direction.y - 0.2));
    colour += float3(0.6, 0.7, 0.9) * smoothstep(0.35, 0.28, panel.x) * smoothstep(0.7, 0.6, panel.y) * 1.2;
    float2 other = abs(float2(wrap(around + 0.6 + PI, 2.0 * PI) - PI, direction.y - 0.1));
    colour += float3(1.0, 0.7, 0.4) * smoothstep(0.25, 0.2, other.x) * smoothstep(0.7, 0.6, other.y) * 0.8;
    colour += float3(1.0, 0.5, 0.2) * exp(-abs(direction.y + 0.15) * 12.0) * 0.03 * (0.5 + Level);
    return colour;
}

float3 shadeBrass(float3 q, float3 normal, float3 direction, float angle)
{
    float3 worldNormal = toWorld(normal, angle);
    float3 worldView = toWorld(direction, angle);
    float3 mirrored = reflect(worldView, worldNormal);
    float facing = saturate(dot(-worldView, worldNormal));
    float3 gold = float3(1.0, 0.72, 0.38);
    float3 edge = lerp(gold, 1.0, pow(1.0 - facing, 5.0));
    float3 colour = edge * room(mirrored) * 1.3;
    colour += gold * pow(saturate(dot(mirrored, normalize(KeyLight))), 60.0) * 3.0;
    colour += gold * 0.08 * saturate(dot(worldNormal, normalize(KeyLight)) * 0.7 + 0.3);
    // The glowing sand lights the inside of the frame.
    colour += float3(1.0, 0.5, 0.15) * 0.12 * (0.4 + Bass * 0.6) * saturate(-dot(normal, normalize(float3(q.x, 0.0, q.z) + 1e-4)) * 0.5 + 0.5) * exp(-length(q.xz) * 1.5);
    return colour;
}

float3 shadeSand(float3 q, float3 normal, float3 direction, float angle)
{
    float3 worldNormal = toWorld(normal, angle);
    float grain = noise(q.xz * 60.0 + q.y * 40.0);
    float3 colour = float3(1.0, 0.6, 0.25) * (0.25 + 0.75 * saturate(dot(worldNormal, normalize(KeyLight)))) * 0.35;
    // The sand glows from within, brightest where it's freshly fallen, and pulses with the bass.
    float fresh = exp(-length(q.xz) * 5.0);
    colour += float3(1.0, 0.5, 0.15) * (0.25 + Bass * 0.5 + fresh * 0.6) * (0.8 + grain * 0.4);
    colour += float3(1.0, 0.9, 0.7) * step(0.93, grain) * (0.5 + Treble * 2.0);
    return colour;
}

float3 trace(float3 origin, float3 direction)
{
    float angle = tilt();
    float3 q0 = toGlass(origin, angle);
    float3 d0 = toGlass(direction, angle);

    float3 colour = 0.0;
    float3 throughput = 1.0;
    // Skip straight to the hourglass's bounding sphere.
    float b = dot(q0, d0);
    float h = b * b - dot(q0, q0) + 1.6 * 1.6;
    float t = h > 0.0 ? max(-b - sqrt(h), 0.0) : 1e4;
    float limit = h > 0.0 ? -b + sqrt(h) : 0.0;
    int steps = qualitySteps(110);
    int crossings = 0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        if (t > limit)
        {
            break;
        }
        float3 q = q0 + d0 * t;
        int material;
        float d = sceneDistance(q, material);
        if (d < 0.0012)
        {
            float3 normal = normalOf(q, material);
            if (material == GlassWall)
            {
                if (dot(normal, d0) > 0.0)
                {
                    normal = -normal;
                }
                // The glass: a mirror sheen at grazing angles, and a slight tint passing through.
                float facing = saturate(dot(-d0, normal));
                float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
                float3 mirrored = toWorld(reflect(d0, normal), angle);
                colour += throughput * fresnel * (room(mirrored) + float3(1.0, 0.85, 0.6) * pow(saturate(dot(mirrored, normalize(KeyLight))), 200.0) * 6.0);
                throughput *= (1.0 - fresnel) * float3(0.95, 0.97, 0.96);
                t += 0.03;
                crossings++;
                if (crossings > 5)
                {
                    break;
                }
                continue;
            }
            if (material == Brass)
            {
                colour += throughput * shadeBrass(q, normal, d0, angle);
            }
            else
            {
                colour += throughput * shadeSand(q, normal, d0, angle);
            }
            return colour;
        }
        t += max(d, 0.002);
    }
    // Seen directly, the room stays dark: the soft panels only show in the glass and the brass.
    float3 backdrop = lerp(float3(0.03, 0.02, 0.015), float3(0.006, 0.006, 0.01), saturate(direction.y * 0.5 + 0.5))
                    + float3(1.0, 0.75, 0.45) * pow(saturate(dot(direction, normalize(KeyLight))), 3.0) * 0.06;
    return colour + throughput * backdrop;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 direction = cameraRay(eye, float3(0.0, -0.05, 0.0), float2(p.x, -p.y), Zoom, 0.0);
    float3 colour = trace(eye, direction);
    // A warm glow in the air round the hourglass, from the sand.
    float3 toCentre = -eye;
    float along = dot(toCentre, direction);
    float off = length(toCentre - direction * along);
    colour += float3(1.0, 0.5, 0.2) * exp(-off * off * 3.0) * 0.03 * (0.5 + Bass);
    return colour + particles(uv) * 0.9;
}
