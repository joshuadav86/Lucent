// Name: Midnight Snowfall
// Snow drifts down a quiet street at midnight, glittering in warm pools of lamplight, with big soft flakes tumbling right past the lens; gusts swirl it on the beat and the treble makes it sparkle.
// Family: Particles
// Tags: Calm, 3D
// Added: 2026-10-17 17:06
// Particles: 262144

// y is up; the street runs along z between two rows of houses, with lamps on both pavements.
static const float LampSpacing = 11.0;
static const float LampHeight = 4.3;
static const float LampSide = 4.0;
static const float FrontX = 6.5;
static const float3 SnowBox = float3(16.0, 11.0, 30.0);
static const float Zoom = 1.5;
static const float3 LampColour = float3(1.0, 0.62, 0.3);

// We stroll slowly down the street, swaying a little.
float3 cameraEye()
{
    return float3(sin(Time * 0.05) * 1.2, 1.7 + sin(Time * 0.11) * 0.08, Time * 0.55);
}

float3 cameraTarget()
{
    float3 eye = cameraEye();
    return eye + float3(sin(Time * 0.037) * 1.6, 0.9 + sin(Time * 0.05) * 0.3, 8.0);
}

float3 lampAt(float side, float k)
{
    return float3(side * LampSide, LampHeight, k * LampSpacing + (side > 0.0 ? LampSpacing * 0.5 : 0.0));
}

// The lamps on one side nearest to z: this one and the next.
void nearestLamps(float side, float z, out float3 a, out float3 b)
{
    float offset = side > 0.0 ? LampSpacing * 0.5 : 0.0;
    float k = floor((z - offset) / LampSpacing);
    a = lampAt(side, k);
    b = lampAt(side, k + 1.0);
}

// Each lamp flickers very slightly and glows a touch brighter with the music.
float lampStrength(float3 lamp)
{
    float own = hash(float2(lamp.z, lamp.x));
    return (1.0 + Level * 0.35 + Beat * 0.12) * (0.75 + own * 0.4);
}

// Light reaching a point from the four nearest lamps, with a soft shade (the lamp hood) above each.
float3 lampLight(float3 position, float3 normal, float wrapAround)
{
    float3 total = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float side = i < 2 ? -1.0 : 1.0;
        float3 a, b;
        nearestLamps(side, position.z, a, b);
        float3 lamp = (i & 1) == 0 ? a : b;
        float3 toLamp = lamp - position;
        float distance2 = dot(toLamp, toLamp);
        float3 direction = toLamp * rsqrt(distance2);
        float facing = saturate((dot(normal, direction) + wrapAround) / (1.0 + wrapAround));
        // Lamps shine downwards; little light goes up past the hood.
        float hood = smoothstep(-0.35, 0.25, direction.y);
        total += LampColour * lampStrength(lamp) * facing * hood * 9.0 / (distance2 + 0.6);
    }
    return total;
}

float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

// ---- Snow ----

// Where a flake drifting in the box appears, kept in a box that travels with the camera.
float3 inBox(float3 position)
{
    float3 middle = cameraEye() + float3(0.0, 2.5, SnowBox.z * 0.5 - 2.0);
    float3 relative = position - middle;
    return middle + relative - SnowBox * floor(relative / SnowBox + 0.5);
}

void Spawn(inout Particle particle, uint id)
{
    float3 corner = cameraEye() + float3(-SnowBox.x * 0.5, 2.5 - SnowBox.y * 0.5, -2.0);
    particle.position = corner + float3(random(id, 1.0), random(id, 2.0), random(id, 3.0)) * SnowBox;
    particle.velocity = 0.0;
    particle.life = 1e6;
    particle.data = float4(0.4 + fixedRandom(id, 4.0) * 0.6, 0.0, 0.0, 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    // Flakes fall slowly, the bigger ones a little faster, wandering on the still air. Each beat sends a
    // gust down the street that swirls them up and along, and the bass keeps the wind up.
    float size = particle.data.x;
    float gust = Beat * Beat * (0.6 + Bass) + Bass * 0.4;
    float3 swirl = curlNoise(particle.position * 0.28 + float3(Time * 0.15, 0.0, -Time * 0.1)) * (0.3 + gust * 1.1);
    float3 wind = float3(0.3 + gust * 0.8, gust * 0.25, 0.1);
    float3 wanted = float3(0.0, -(0.55 + size * 0.6), 0.0) + swirl + wind;
    particle.velocity = lerp(particle.velocity, wanted, 1.0 - exp(-dt * 2.5));
    float3 moved = particle.position + particle.velocity * dt;
    // Flakes that reach the ground start again at the top of the box.
    if (moved.y < 0.0)
    {
        moved.y += SnowBox.y;
    }
    particle.position = inBox(moved);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 seen = project(particle.position, eye, target, Zoom, 0.0);
    if (seen.z < 0.15 || particle.position.y < 0.0)
    {
        return sprite;
    }
    // Each flake is lit by the lamps around it (seen from any side), with a faint blue moonlight,
    // and fades into the misty distance.
    float3 light = lampLight(particle.position, float3(0.0, 1.0, 0.0), 1.0) + float3(0.06, 0.08, 0.13);
    float mist = exp(-seen.z * 0.045) * smoothstep(SnowBox.z - 2.0, SnowBox.z * 0.7, seen.z);
    // Now and then a flake catches the light just so and glints, more with the treble.
    float glint = 1.0 + step(0.985, random(id, 5.0)) * Treble * 6.0;
    float3 ahead = project(particle.position + particle.velocity * 0.03, eye, target, Zoom, 0.0);
    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(ahead.x - seen.x, seen.y - ahead.y);
    sprite.size = 0.9 + particle.data.x * 1.2;
    // Flakes right by the lens catch the most light from the lamps behind us.
    float near = 1.0 + 4.0 * smoothstep(3.0, 0.6, seen.z);
    sprite.colour = light * 0.2 * particle.data.x * mist * glint * near;
    // Focused down the street, so flakes right by the lens turn into big soft discs.
    focus(sprite, seen.z, 7.0, 0.07);
    return sprite;
}

// ---- The street ----

float capsuleDistance(float3 origin, float3 direction, float3 a, float3 b, out float along)
{
    // Closest approach between the ray and an upright post, and how far down the ray that is.
    float3 ba = b - a;
    float3 oa = origin - a;
    float d = dot(direction, ba);
    float denominator = dot(ba, ba) - d * d;
    float s = saturate((dot(oa, ba) - dot(oa, direction) * d) / max(denominator, 1e-6));
    float3 onPost = a + ba * s;
    along = max(dot(onPost - origin, direction), 0.0);
    return length(origin + direction * along - onPost);
}

// The glow of the lamps through the falling snow and mist: light scattered towards us all along the ray,
// worked out exactly for each lamp, so each one sits in its own soft halo.
float3 lampHaze(float3 origin, float3 direction, float maxDistance)
{
    float3 total = 0.0;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float side = (i & 1) == 0 ? -1.0 : 1.0;
        float3 a, b;
        nearestLamps(side, origin.z + 2.0 + (i >> 1) * LampSpacing - LampSpacing, a, b);
        float3 lamp = a;
        float along = dot(lamp - origin, direction);
        float off = length(origin + direction * along - lamp) + 0.05;
        float scattered = (atan((maxDistance - along) / off) + atan(along / off)) / off;
        total += LampColour * lampStrength(lamp) * scattered * exp(-max(along, 0.0) * 0.03);
        // The bulb itself, a hot bright point inside its glass.
        total += float3(1.0, 0.85, 0.6) * 40.0 * exp(-off * off / 0.008) * step(along, maxDistance);
    }
    return total * 0.035;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);

    // Night sky over the rooftops: dark blue, lit faintly orange from the town below.
    float3 colour = lerp(float3(0.04, 0.035, 0.05), float3(0.008, 0.012, 0.03), saturate(direction.y * 2.5));
    float distance = 1e3;

    // The house fronts on each side, with lit windows and the lamps' light falling on them.
    float side = direction.x > 0.0 ? 1.0 : -1.0;
    float toFront = (side * FrontX - eye.x) / direction.x;
    if (abs(direction.x) > 1e-4 && toFront > 0.0)
    {
        float3 wall = eye + direction * toFront;
        float roof = 7.5 + 3.0 * hash(float2(floor(wall.z / 9.0), side));
        if (wall.y < roof)
        {
            distance = toFront;
            float3 normal = float3(-side, 0.0, 0.0);
            float2 grid = float2(wall.z / 2.2, (wall.y - 0.8) / 2.6);
            float2 cell = floor(grid);
            float2 inCell = frac(grid);
            float pane = step(0.25, inCell.x) * step(inCell.x, 0.75) * step(0.2, inCell.y) * step(inCell.y, 0.8) * step(0.0, cell.y);
            float lit = step(0.6, hash(cell + side * 13.0));
            // Glazing bars split each window into four panes.
            pane *= step(0.035, abs(inCell.x - 0.5)) * step(0.03, abs(inCell.y - 0.5));
            // Warm curtains glowing from inside, a few cooler television-blue rooms.
            float3 room = hash(cell * 1.7 + 3.0) > 0.85 ? float3(0.25, 0.35, 0.6) : float3(1.0, 0.68, 0.4);
            float3 plaster = float3(0.11, 0.09, 0.08) * (0.7 + 0.3 * noise(wall.zy * 4.0));
            colour = plaster * (lampLight(wall, normal, 0.0) + float3(0.03, 0.035, 0.05)) + pane * (lit * room * 0.3 * (0.6 + 0.4 * inCell.y) + 0.004);
            // A line of snow along each window sill and the top of the wall.
            float sill = step(0.0, cell.y) * smoothstep(0.03, 0.0, abs(inCell.y - 0.2)) * step(0.22, inCell.x) * step(inCell.x, 0.78);
            colour = lerp(colour, float3(0.6, 0.62, 0.7) * (lampLight(wall, float3(0.0, 1.0, 0.0), 0.0) * 0.3 + 0.03), sill);
        }
    }

    // The snowy road and pavements, glowing in pools under each lamp.
    if (direction.y < 0.0)
    {
        float t = -eye.y / direction.y;
        if (t < distance)
        {
            distance = t;
            float3 ground = eye + direction * t;
            float drifts = 0.85 + 0.15 * fbm(ground.xz * 0.6);
            float3 snow = float3(0.75, 0.78, 0.85) * drifts;
            colour = snow * (lampLight(ground, float3(0.0, 1.0, 0.0), 0.0) * 0.45 + float3(0.03, 0.04, 0.07));
            // Wheel ruts down the middle, a touch darker.
            colour *= 1.0 - 0.25 * smoothstep(0.4, 0.0, abs(abs(ground.x) - 0.9));
            // Snow crystals glitter as we move.
            float2 cell = floor(ground.xz * 40.0);
            float glitter = step(0.996, hash(cell + floor(Time * 2.0) * 0.37)) * (0.3 + Treble * 2.0);
            colour += glitter * lampLight(ground, float3(0.0, 1.0, 0.0), 0.0) * 0.12;
        }
    }

    // Lamp posts: thin dark iron.
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        float postSide = (i & 1) == 0 ? -1.0 : 1.0;
        float3 a, b;
        nearestLamps(postSide, eye.z + 1.0 + (i >> 1) * LampSpacing, a, b);
        float along;
        float off = capsuleDistance(eye, direction, float3(a.x, 0.0, a.z), a + float3(0.0, 0.25, 0.0), along);
        float width = 0.07 + pixelSize() / Zoom * along * 0.5;
        if (off < width && along < distance)
        {
            distance = along;
            colour = float3(0.01, 0.01, 0.012) + LampColour * 0.02;
        }
    }

    // Distant mist, then the glow of the lamps through the snow.
    float mist = 1.0 - exp(-distance * 0.035);
    colour = lerp(colour, float3(0.05, 0.05, 0.075), mist);
    colour += lampHaze(eye, direction, distance);

    return colour + particles(uv) + previous(uv) * 0.15;
}
