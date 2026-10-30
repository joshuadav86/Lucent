// Name: Firefly Meadow
// Fireflies blink over a moonlit meadow running down to a still lake, each a bright spark with its own soft halo and its reflection trembling in the water. Beats send waves of light rippling through them, build-ups pull them into flashing together, and drops light the whole meadow at once.
// Family: Particles
// Tags: Calm, Dark, 3D
// Added: 2026-10-17 17:12
// Particles: 24576

// World y is up and the ground and water are at y = 0. The meadow runs from the camera down to the
// lake's near shore, a dozen metres off; the lake runs out to wooded hills on the far side.
// Each firefly is drawn by three particles that fly exactly the same path (their random numbers all
// come from the firefly's number): its bright spark, its halo, and its reflection in the lake.
static const uint MostFireflies = 8192;
// They fly in a wedge in front of the camera that widens with distance, the shape of what it sees.
static const float FieldNear = 1.0;
static const float FieldDepth = 26.0;
static const float FieldHeight = 2.2;
static const float3 MoonDirection = float3(-0.36, 0.16, 0.92);
static const float Zoom = 1.7;

float3 cameraEye()
{
    return float3(sin(Time * 0.021) * 3.0, 2.45 + 0.12 * sin(Time * 0.037), -1.5 + sin(Time * 0.015) * 0.8);
}

float3 cameraTarget()
{
    return cameraEye() + float3(sin(Time * 0.017 + 1.0) * 1.4 - 1.2, -1.25 + 0.1 * sin(Time * 0.027), 10.0);
}

float3 moonDirection()
{
    return normalize(MoonDirection);
}

// How far away the lake's near shore is, along the meadow at x.
float shore(float x)
{
    return 9.5 + 2.6 * (noise(float2(x * 0.11, 2.0)) - 0.5) * 2.0 + 0.8 * sin(x * 0.07 + 1.3);
}

// How many fireflies there are: the same on every detail setting above Low, because a few thousand
// distinct sparks look better than more fainter ones. Spare particles stay hidden.
uint fireflyCount()
{
    return min((uint)ParticleCount / 3u, MostFireflies);
}

// How far either side of the middle the field reaches, at distance z.
float fieldHalfWidth(float z)
{
    return 3.5 + z * 1.25;
}

// A smooth bump: 1 at x = 0, falling to nearly nothing by x = 2.
float bell(float x)
{
    return exp(-x * x);
}

// Seconds since the last beat (Beat fades as e^-5t).
float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

// Each beat a flash of light spreads out across the meadow from a new spot.
float3 waveOrigin()
{
    uint seed = (uint)BeatCount;
    float2 pick = float2(pcgHash(seed * 2u + 1u), pcgHash(seed * 2u + 2u)) / 4294967295.0;
    return float3((pick.x - 0.5) * 16.0, 1.0, 3.0 + pick.y * 16.0);
}

// How brightly a firefly is lit right now, before the beat's wave.
float blink(float4 data, uint firefly)
{
    // Its own rhythm: a quick swell of light, then a long dark pause. Some flash twice.
    float cycle = frac(Time * data.y + data.x);
    float flash = bell((cycle - 0.1) / 0.045);
    flash += step(0.72, data.z) * 0.8 * bell((cycle - 0.19) / 0.03);
    // Quiet passages have fewer of them flashing; louder ones wake the rest.
    flash *= step(fixedRandom(firefly, 11.0), 0.55 + 0.45 * max(Intensity, Level));
    // Through a build-up they fall into step with the beat, the way some fireflies really do.
    float together = bell(BeatPhase / 0.05) + bell((1.0 - BeatPhase) / 0.02);
    flash = lerp(flash, together, smoothstep(0.15, 0.85, BuildUp));
    // And when the drop lands, the whole meadow lights at once.
    return max(flash, DropPulse * DropPulse);
}

void Spawn(inout Particle particle, uint id)
{
    uint firefly = id % max(fireflyCount(), 1u);
    float z = FieldNear + pow(random(firefly, 2.0), 1.3) * FieldDepth;
    float x = (random(firefly, 1.0) * 2.0 - 1.0) * fieldHalfWidth(z);
    particle.position = float3(x, 0.12 + pow(random(firefly, 3.0), 1.5) * FieldHeight, z);
    particle.velocity = 0.0;
    particle.life = lerp(40.0, 90.0, random(firefly, 4.0));
    // Its personality: when in its cycle it flashes, how often, and whether it flashes twice.
    particle.data = float4(fixedRandom(firefly, 5.0), lerp(0.17, 0.4, fixedRandom(firefly, 6.0)), fixedRandom(firefly, 12.0), 0.0);
}

void Simulate(inout Particle particle, uint id)
{
    uint count = fireflyCount();
    if (id >= count * 3u)
    {
        return;
    }

    uint firefly = id % count;
    float dt = min(DeltaTime, 0.05);
    float3 position = particle.position;
    // A lazy wandering flight, livelier with the music and a build-up.
    float3 drift = curlNoise(position * 0.2 + float3(0.0, Time * 0.035, fixedRandom(firefly, 7.0) * 3.0));
    float3 wish = drift * (0.2 + Mid * 0.2 + BuildUp * 0.3) + float3(0.1, 0.0, 0.0);
    // Each flash comes with a little upward swoop, and a build-up lifts them all.
    wish.y += blink(particle.data, firefly) * 0.45;
    float ceiling = 2.3 + BuildUp * 1.2;
    wish.y += saturate(0.35 - position.y) * 1.5 - saturate(position.y - ceiling) * 1.0;
    particle.velocity = lerp(particle.velocity, wish, saturate(dt * 1.5));
    position += particle.velocity * dt;
    // Wrap round the sides of the field so the meadow never empties.
    position.z = FieldNear + wrap(position.z - FieldNear, FieldDepth);
    float halfWidth = fieldHalfWidth(position.z);
    position.x = wrap(position.x + halfWidth, halfWidth * 2.0) - halfWidth;
    position.y = clamp(position.y, 0.06, 3.8);
    particle.position = position;
}

// Where a world point lands on screen in p units (y down), and its depth.
float3 toScreen(float3 position)
{
    float3 seen = project(position, cameraEye(), cameraTarget(), Zoom, 0.0);
    return float3(seen.x, -seen.y, seen.z);
}

Sprite Show(Particle particle, uint id)
{
    Sprite sprite = (Sprite)0;
    uint count = fireflyCount();
    if (id >= count * 3u)
    {
        return sprite;
    }

    uint firefly = id % count;
    uint role = id / count;  // 0 the spark, 1 its halo, 2 its reflection

    // The beat's wave: a ring of flashes spreading outward over the grass, fading as it goes.
    float age = sinceBeat();
    float apart = length(particle.position.xz - waveOrigin().xz);
    float wave = bell((apart - age * 7.0) / 0.9) * exp(-age * 0.9) * (0.4 + Bass * 0.9);
    // Each listens to its own part of the spectrum.
    float band = spectrum(0.05 + fixedRandom(firefly, 8.0) * 0.7);
    float flash = blink(particle.data, firefly);
    float light = flash * (0.6 + band * 1.2) + wave;
    if (light < 0.004)
    {
        return sprite;
    }

    // Most glow yellow-green; a few are warmer amber. At the height of a flash they burn nearly white.
    float3 colour = lerp(float3(0.62, 1.0, 0.16), float3(1.0, 0.68, 0.16), step(0.86, fixedRandom(firefly, 9.0)));
    colour = lerp(colour, float3(0.95, 1.0, 0.7), saturate(light - 0.6) * 0.5);
    // Fade in after spawning, and keep the same brightness on every detail setting.
    colour *= light * saturate(particle.age * 0.5) / max(ParticleWeight, 1e-3);

    float3 position = particle.position;
    if (role == 2u)
    {
        // The reflection: the firefly mirrored in the water, seen where the line to it crosses the lake.
        float3 eye = cameraEye();
        float3 mirrored = float3(position.x, -position.y, position.z);
        float3 water = eye + (mirrored - eye) * (eye.y / (eye.y + position.y));
        if (water.z < shore(water.x) + 0.3)
        {
            return sprite;
        }

        // Ripples break it into a short trembling streak.
        float tremble = sin(Time * 2.3 + water.z * 3.1 + water.x * 1.7) * (0.04 + Bass * 0.08);
        float3 seen = toScreen(mirrored + float3(tremble, 0.0, 0.0));
        if (seen.z < 1.2)
        {
            return sprite;
        }

        sprite.position = seen.xy;
        sprite.size = 2.6;
        sprite.colour = colour * 6.0 / (seen.z + 4.0) * 1.6;
        sprite.streak = float2(0.0, (0.01 + Bass * 0.02) * 10.0 / (seen.z + 4.0));
        return sprite;
    }

    float3 seen = toScreen(position);
    if (seen.z < 1.2)
    {
        return sprite;
    }

    // Further ones are smaller points of light, fading into the night mist.
    float fade = 6.0 / (seen.z + 4.0) * exp(-seen.z * 0.012);
    sprite.position = seen.xy;
    if (role == 0u)
    {
        sprite.size = 1.4 + flash * 0.5;
        sprite.colour = colour * 3.2 * fade;
        focus(sprite, seen.z, 9.0, 0.06);
    }
    else
    {
        // The halo: a wide soft glow round the spark, lighting the air around it.
        sprite.size = clamp(5.0 + 60.0 / seen.z, 6.0, 24.0);
        sprite.colour = colour * 0.12 * fade;
    }

    return sprite;
}

// ---- The night sky ----

float starField(float3 direction)
{
    float2 q = float2(atan2(direction.x, direction.z), direction.y) * 200.0;
    float2 cell = floor(q);
    float2 f = frac(q) - 0.5;
    float h = hash(cell);
    float2 offset = float2(hash(cell + 7.1), hash(cell + 3.3)) - 0.5;
    float star = step(0.9, h) * exp(-dot(f - offset * 0.6, f - offset * 0.6) * 40.0);
    float twinkle = 0.6 + 0.4 * sin(Time * (2.0 + h * 5.0) + h * 40.0) * (0.4 + Treble);
    return star * twinkle * (h - 0.88) * 10.0;
}

// A faint band of the Milky Way rising from the hills, with dark lanes of dust down its middle.
float3 milkyWay(float3 direction)
{
    float3 across = normalize(float3(-0.42, -0.63, 0.42));
    float off = dot(direction, across);
    float along = dot(direction, normalize(cross(across, float3(0.0, 1.0, 0.0))));
    float2 q = float2(along * 6.0, off * 9.0);
    float band = exp(-off * off * 28.0) * (0.55 + 0.6 * fbm(q * 1.5));
    float dust = smoothstep(0.35, 0.7, fbm(q * 2.3 + 4.0)) * exp(-off * off * 90.0);
    return float3(0.09, 0.095, 0.13) * band * (1.0 - dust * 0.8);
}

float3 sky(float3 direction)
{
    float up = saturate(direction.y);
    float3 colour = lerp(float3(0.035, 0.05, 0.095), float3(0.004, 0.006, 0.02), pow(up, 0.45));
    float3 moon = moonDirection();
    float facing = saturate(dot(direction, moon));
    // The moon: a pale disc with faint grey seas, and a wide halo in the haze.
    float disc = smoothstep(0.99945, 0.99965, facing);
    if (disc > 0.0)
    {
        float3 local = direction - moon;
        colour += float3(1.0, 0.97, 0.88) * disc * (2.6 - fbm(local.xy * 380.0 + 3.0) * 1.2);
    }
    colour += float3(0.35, 0.42, 0.62) * (pow(facing, 400.0) * 0.5 + pow(facing, 40.0) * 0.08 + pow(facing, 6.0) * 0.025) * (0.9 + Level * 0.3);
    colour += milkyWay(direction) * smoothstep(0.0, 0.25, direction.y);
    // Thin clouds catching the moonlight, slowly passing.
    float2 cloudPlane = direction.xz / max(direction.y, 0.05) * 0.5 + float2(Time * 0.008, 0.0);
    float cloud = smoothstep(0.5, 0.85, fbm(cloudPlane));
    float3 cloudColour = float3(0.05, 0.06, 0.09) + float3(0.3, 0.32, 0.38) * pow(facing, 10.0);
    colour = lerp(colour, cloudColour, cloud * 0.5 * smoothstep(0.02, 0.2, direction.y));
    colour += starField(direction) * (1.0 - cloud) * smoothstep(0.01, 0.15, direction.y) * (1.0 - disc);
    return colour;
}

// The wooded hills on the far side of the lake, in two layers: how much of each covers this direction.
float2 hills(float3 direction)
{
    float around = atan2(direction.x, direction.z);
    float far = 0.04 + 0.05 * fbm(float2(around * 2.5, 7.0)) + 0.008 * noise(float2(around * 50.0, 1.0));
    float near = 0.012 + 0.035 * fbm(float2(around * 4.0, 1.0)) + 0.014 * pow(noise(float2(around * 70.0, 3.0)), 2.0)
               + 0.008 * pow(noise(float2(around * 160.0, 5.0)), 3.0);
    float edge = pixelSize() / Zoom;
    return float2(smoothstep(far + edge, far - edge, direction.y), smoothstep(near + edge, near - edge, direction.y));
}

// The sky with the hills in front of it, and the mist lying at their feet.
float3 skyline(float3 direction)
{
    float3 colour = sky(direction);
    float2 cover = hills(direction);
    float height = max(direction.y, 0.0);
    colour = lerp(colour, float3(0.018, 0.024, 0.042) + float3(0.03, 0.035, 0.05) * pow(saturate(dot(direction, moonDirection())), 8.0), cover.x);
    colour = lerp(colour, float3(0.005, 0.007, 0.012), cover.y);
    colour += float3(0.035, 0.045, 0.07) * exp(-height * 60.0) * 0.8;
    return colour;
}

// ---- The lake ----

// The slope of the water's surface at a point on the lake: a slow swell, small wind ripples, and a
// ring of ripples spreading out from where the last beat's light began.
float2 waterSlope(float2 at, float distance)
{
    float2 slope = 0.0;
    float strength = 0.012 + Bass * 0.012;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float angle = 1.3 + i * 1.7;
        float2 heading = float2(cos(angle), sin(angle));
        float frequency = 1.6 * pow(1.9, i);
        float phase = dot(at, heading) * frequency + Time * (0.8 + i * 0.5);
        slope += heading * cos(phase) * strength * pow(0.75, i);
    }

    slope += (float2(noise(at * 3.0 + Time * 0.3), noise(at * 3.0 + 9.0 - Time * 0.3)) - 0.5) * 0.04;
    float age = sinceBeat();
    float2 fromRing = at - waveOrigin().xz;
    float ringDistance = length(fromRing);
    float ring = sin((ringDistance - age * 3.0) * 9.0) * bell((ringDistance - age * 3.0) / 0.6) * exp(-age * 0.8) * (0.02 + Bass * 0.05);
    slope += fromRing / max(ringDistance, 1e-3) * ring;
    // Far off the ripples are too small to see; smoothing them there keeps the water from shimmering.
    return slope / (1.0 + distance * 0.06);
}

float3 lake(float3 eye, float3 direction, float3 at, float distance)
{
    float2 slope = waterSlope(at.xz, distance);
    float3 normal = normalize(float3(-slope.x, 1.0, -slope.y));
    float3 bounce = reflect(direction, normal);
    bounce.y = abs(bounce.y);
    float facing = saturate(-dot(direction, normal));
    float fresnel = 0.02 + 0.98 * pow(1.0 - facing, 5.0);
    float3 colour = float3(0.003, 0.006, 0.01) + skyline(bounce) * lerp(0.35, 1.0, fresnel);
    // The moon's path: a broad glow across the water, broken into glitter by the ripples.
    float glint = saturate(dot(bounce, moonDirection()));
    float glitter = smoothstep(0.55, 1.0, noise(at.xz * float2(6.0, 14.0) + float2(Time * 0.7, -Time * 0.4)));
    colour += float3(0.8, 0.82, 0.9) * (pow(glint, 120.0) * 0.6 + pow(glint, 900.0) * 3.0 * (0.3 + glitter * 2.0)) * (0.9 + Level * 0.3);
    return colour;
}

// ---- The meadow ----

float3 meadow(float3 at, float distance, float2 uv)
{
    float grass = fbm(at.xz * float2(1.6, 0.8));
    // Breezes passing over the grass, catching the moonlight as they go.
    float breeze = smoothstep(0.4, 1.0, sin(at.x * 0.6 + at.z * 0.9 - Time * (0.8 + Mid * 0.8)) * 0.5 + 0.5) * fbm(at.xz * 0.3 + Time * 0.05);
    float tips = noise(at.xz * float2(9.0, 3.0));
    float3 colour = float3(0.016, 0.03, 0.028) * (0.4 + grass) * (0.7 + tips * 0.6) + float3(0.03, 0.042, 0.065) * breeze * 0.7;
    // Moonlight on the grass, brighter towards the moon.
    colour += float3(0.015, 0.02, 0.03) * pow(saturate(1.0 - abs(at.x * 0.05 - moonDirection().x * 2.0)), 2.0) * 0.5;
    // Tiny pale flowers scattered through the grass.
    float2 cell = floor(at.xz * 4.0);
    float2 inCell = frac(at.xz * 4.0) - 0.5;
    float2 spot = float2(hash(cell + 1.7), hash(cell + 8.3)) - 0.5;
    float flower = step(0.82, hash(cell)) * exp(-dot(inCell - spot * 0.7, inCell - spot * 0.7) * 300.0) * saturate(1.5 - distance * 0.12);
    colour += float3(0.07, 0.075, 0.09) * flower;
    // The shore: a band of dark mud where the grass gives way to the lake.
    float toShore = shore(at.x) - at.z;
    colour *= lerp(0.45, 1.0, saturate(toShore * 3.0));
    // Fireflies light the grass beneath them: the glow just above this point on screen.
    float3 above = 0.0;
    float reach = 0.05 / max(distance * 0.12, 0.3);
    [unroll]
    for (int i = 1; i <= 4; i++)
    {
        above += particles(uv - float2(0.0, reach * i * 0.25));
    }

    colour += above * float3(0.4, 0.55, 0.25) * 0.1;
    float mist = 1.0 - exp(-distance * 0.05);
    return lerp(colour, float3(0.03, 0.04, 0.065), mist);
}

// The reeds along the near shore, standing against the bright water: how much they cover this pixel
// (x), and how much of their edge catches the moon (y). Found where the ray reaches the shore line.
float2 reeds(float3 eye, float3 direction)
{
    if (direction.z <= 0.0)
    {
        return 0.0;
    }

    float t = (shore(eye.x) - eye.z) / direction.z;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        t = (shore(eye.x + direction.x * t) - eye.z) / direction.z;
    }

    float3 at = eye + direction * t;
    float edge = t * pixelSize() / Zoom * 1.2;
    float clump = smoothstep(0.25, 0.6, fbm(float2(at.x * 0.35, 4.0)));
    float cellWidth = 0.09;
    float cell = floor(at.x / cellWidth);
    float cover = 0.0;
    float rim = 0.0;
    [unroll]
    for (int c = -2; c <= 2; c++)
    {
        float index = cell + c;
        float2 seed = float2(index, 41.0);
        float tallest = clump * lerp(0.3, 1.25, hash(seed));
        float along = at.y / max(tallest, 1e-3);
        if (along > 0.0 && along < 1.0 && tallest > 0.05)
        {
            float lean = (hash(seed + 3.1) - 0.5) * 0.35 * tallest;
            float sway = (sin(Time * 0.8 + index * 0.7) * 0.03 + Bass * 0.03 * sin(Time * 2.1 + index)) * tallest;
            float centre = (index + hash(seed + 5.3)) * cellWidth + (lean + sway) * along * along;
            float halfWidth = 0.009 * pow(1.0 - along, 0.6);
            // A few carry a cattail's dark seed head near the top.
            halfWidth += step(0.7, hash(seed + 9.9)) * 0.016 * smoothstep(0.62, 0.65, along) * smoothstep(0.86, 0.83, along);
            float d = abs(at.x - centre) - halfWidth;
            float blade = smoothstep(edge, -edge, d);
            cover = max(cover, blade);
            rim = max(rim, blade * step(at.x, centre) * smoothstep(-edge * 3.0, 0.0, d) * along);
        }
    }

    return float2(cover, rim);
}

// One layer of grass blades in front of the camera, drawn as silhouettes on the screen.
// Returns how much of the pixel the blades cover (x) and how much of their edge faces the moon (y).
float2 grassLayer(float2 p, float layer, float shift, float bottom, float tallest, float width)
{
    float2 q = float2(p.x + shift, p.y);
    float cellWidth = width * 3.2;
    float cell = floor(q.x / cellWidth);
    float cover = 0.0;
    float rim = 0.0;
    float edge = pixelSize() * 1.2;
    [unroll]
    for (int c = -2; c <= 2; c++)
    {
        [unroll]
        for (int k = 0; k < 2; k++)
        {
            float index = cell + c;
            float2 seed = float2(index * 2.0 + k, layer * 17.0);
            float h = tallest * lerp(0.35, 1.0, hash(seed));
            float baseX = (index + hash(seed + 5.3)) * cellWidth;
            float along = (bottom - q.y) / h;
            if (along > 0.0 && along < 1.0)
            {
                // Each blade arcs over, swaying in the breeze and with the bass.
                float lean = (hash(seed + 9.1) - 0.5) * 0.8 * h;
                float sway = (sin(Time * 0.9 + baseX * 3.0 + layer) * 0.04 + Bass * 0.03 * sin(Time * 2.3 + baseX * 7.0)) * h;
                float centre = baseX + (lean + sway) * along * along;
                float halfWidth = width * pow(1.0 - along, 0.8);
                float d = abs(q.x - centre) - halfWidth;
                float blade = smoothstep(edge, -edge, d);
                cover = max(cover, blade);
                // The edge on the moon's side picks up a cool rim of light.
                float side = (q.x < centre + lean * 0.1) ? 1.0 : 0.0;
                rim = max(rim, blade * side * smoothstep(-edge * 3.0, 0.0, d) * along);
            }
        }
    }
    return float2(cover, rim);
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 direction = cameraRay(eye, cameraTarget(), float2(p.x, -p.y), Zoom, 0.0);

    float3 colour;
    if (direction.y >= 0.0)
    {
        colour = skyline(direction);
    }
    else
    {
        float distance = -eye.y / direction.y;
        float3 at = eye + direction * distance;
        if (at.z > shore(at.x))
        {
            colour = lake(eye, direction, at, distance);
            // A soft band of mist over the far water, where it meets the hills.
            colour = lerp(colour, float3(0.03, 0.04, 0.065), (1.0 - exp(-distance * 0.012)) * 0.6);
            float2 standing = reeds(eye, direction);
            colour = lerp(colour, float3(0.003, 0.005, 0.006) + float3(0.08, 0.1, 0.15) * standing.y, standing.x);
        }
        else
        {
            colour = meadow(at, distance, uv);
        }
    }

    colour += particles(uv);

    // Grass in the foreground, in three layers sliding past at different speeds as the camera drifts.
    float3 grassColour = float3(0.002, 0.004, 0.004);
    float3 rimColour = float3(0.1, 0.13, 0.2);
    // The fireflies just beside a blade light it softly green.
    float2 px = float2(pixelSize() * 8.0 / Aspect, pixelSize() * 8.0) * 0.5;
    float3 nearby = particles(uv + float2(px.x, 0.0)) + particles(uv - float2(px.x, 0.0)) + particles(uv + float2(0.0, px.y)) + particles(uv - float2(0.0, px.y));
    [unroll]
    for (int layer = 0; layer < 3; layer++)
    {
        float depth = 2.0 - layer;
        float2 blades = grassLayer(p, depth, eye.x * (1.4 - depth * 0.35) + depth * 13.0, 1.05 + depth * 0.03, lerp(0.42, 0.2, depth * 0.5), lerp(0.011, 0.006, depth * 0.5));
        float3 layerColour = grassColour * (1.0 + depth) + rimColour * blades.y * (1.0 - depth * 0.25) + nearby * 0.2;
        colour = lerp(colour, layerColour, blades.x);
    }

    return colour;
}
