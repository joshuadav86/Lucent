// Name: Wind Atlas
// A dark globe turns slowly in space, wrapped in rivers of light that trace the planet's winds: trade winds, meandering jet streams and spiralling storms, flowing faster with the mids while storms spin up on every beat.
// Family: Particles
// Tags: Calm, Colourful, 3D
// Added: 2026-10-17 17:12
// Particles: 262144

// Every particle is a little puff of air riding the winds over the surface of a unit globe. It
// lives a few seconds and is drawn as a short streak along its path, so the whole planet reads
// like an animated wind map.
static const float GlobeRadius = 1.0;
static const float Zoom = 2.25;
static const int Storms = 5;

// The camera circles the globe slowly, rising and dipping a little over the poles.
float3 cameraEye()
{
    float angle = Time * 0.035 + 1.2;
    float lift = 0.32 + 0.22 * sin(Time * 0.021);
    return float3(sin(angle) * cos(lift), sin(lift), cos(angle) * cos(lift)) * 3.3;
}

// The sun hangs off to one side so the globe has a day side, a night side and a soft terminator.
float3 sunDirection()
{
    return normalize(float3(cos(Time * 0.01) * 0.9, 0.35, sin(Time * 0.01) * 0.9));
}

// Each storm wanders west through the tropics or east through the mid-latitudes.
float3 stormCentre(int index)
{
    float k = index;
    float hemisphere = (index & 1) == 0 ? 1.0 : -1.0;
    float latitude = hemisphere * (0.3 + frac(k * 0.37) * 0.45) + sin(Time * 0.03 + k) * 0.08;
    float longitude = k * 1.256 + Time * (latitude * latitude < 0.25 ? -0.02 : 0.025) + sin(Time * 0.05 + k * 2.0) * 0.2;
    return float3(cos(latitude) * cos(longitude), sin(latitude), cos(latitude) * sin(longitude));
}

// How hard a storm is spinning: each one hums with its own slice of the spectrum, and every beat
// spins one of them up hard, taking turns.
float stormStrength(int index)
{
    float picked = ((int)BeatCount % Storms) == index ? 1.0 : 0.25;
    return 0.9 + spectrum(0.08 + index * 0.17) * 1.6 + Beat * picked * 4.0;
}

// The jet stream's latitude in one hemisphere: a ribbon of fast wind that snakes round the world.
float jetLatitude(float longitude, float t)
{
    return 0.82 + 0.13 * sin(longitude * 4.0 - t * 0.12) + 0.05 * sin(longitude * 7.0 + t * 0.2);
}

float jetSlope(float longitude, float t)
{
    return 0.52 * cos(longitude * 4.0 - t * 0.12) + 0.35 * cos(longitude * 7.0 + t * 0.2);
}

// The wind at a point on the globe, as a velocity along the surface.
float3 wind(float3 n)
{
    float latitude = asin(clamp(n.y, -1.0, 1.0));
    float longitude = atan2(n.z, n.x);
    float3 east = float3(-n.z, 0.0, n.x);
    float eastLength = length(east);
    east = eastLength > 1e-3 ? east / eastLength : float3(1.0, 0.0, 0.0);
    float3 north = cross(n, east);

    // The great belts: easterly trade winds near the equator, westerlies further out, easterlies
    // again round the poles.
    float zonal = -cos(3.0 * latitude) * 0.55;

    // The jet streams, fast and narrow, with a north-south swing that follows their meanders.
    float hemisphere = latitude >= 0.0 ? 1.0 : -1.0;
    float jetAt = jetLatitude(longitude + (hemisphere < 0.0 ? 2.0 : 0.0), Time);
    float fromJet = (abs(latitude) - jetAt) / 0.075;
    float jet = 2.4 * exp(-fromJet * fromJet);
    float meander = jet * jetSlope(longitude + (hemisphere < 0.0 ? 2.0 : 0.0), Time) * hemisphere;
    float3 v = east * (zonal + jet) + north * meander;

    // Storms: a tight spin round each eye with a gentle pull inwards, so the air spirals in.
    [unroll]
    for (int i = 0; i < Storms; i++)
    {
        float3 centre = stormCentre(i);
        float3 offset = n - centre;
        float r2 = dot(offset, offset);
        float falloff = exp(-r2 / 0.03);
        float spin = centre.y >= 0.0 ? 1.0 : -1.0;
        v += (cross(centre, n) * spin * 9.0 - offset * 0.5) * falloff * stormStrength(i);
    }

    // A little turbulence so nothing looks ruled.
    float3 swirl = curlNoise(n * 2.2 + float3(0.0, Time * 0.03, 0.0)) * 0.12;
    v += swirl - n * dot(swirl, n);
    return v;
}

float flowSpeed()
{
    return 0.07 + Mid * 0.12 + Level * 0.03;
}

void Spawn(inout Particle particle, uint id)
{
    particle.position = randomDirection(id, 1.0);
    particle.velocity = 0.0;
    particle.life = lerp(2.0, 5.0, random(id, 2.0));
    if (ParticleReset > 0.5)
    {
        particle.life *= random(id, 3.0);
    }
    particle.data = 0.0;
}

void Simulate(inout Particle particle, uint id)
{
    float dt = min(DeltaTime, 0.05);
    float3 n = normalize(particle.position);
    float3 v = wind(n) * flowSpeed();
    // Two half steps keep the paths on the curve of the globe.
    float3 middle = normalize(n + v * dt * 0.5);
    v = wind(middle) * flowSpeed();
    particle.position = normalize(n + v * dt);
    particle.velocity = v;
}

// Calm air is deep blue, steady winds teal, the jets and storm walls burn gold and rose.
float3 windColour(float speed, float hue)
{
    float s = saturate(speed);
    float3 calm = float3(0.12, 0.22, 0.9);
    float3 steady = float3(0.1, 0.8, 0.85);
    float3 brisk = float3(1.0, 0.82, 0.35);
    float3 fierce = float3(1.0, 0.35, 0.6);
    float3 colour = lerp(calm, steady, smoothstep(0.0, 0.3, s));
    colour = lerp(colour, brisk, smoothstep(0.3, 0.65, s));
    colour = lerp(colour, fierce, smoothstep(0.65, 1.0, s));
    return lerp(colour, colour.gbr, hue * 0.15);
}

Sprite Show(Particle particle, uint id)
{
    float3 eye = cameraEye();
    float3 n = normalize(particle.position);
    float3 position = n * GlobeRadius * 1.012;
    float3 seen = project(position, eye, 0.0, Zoom, 0.0);
    float3 seenBefore = project(normalize(n - particle.velocity * 0.45) * GlobeRadius * 1.012, eye, 0.0, Zoom, 0.0);

    // Only the near side of the globe shows, fading softly into the limb.
    float facing = dot(n, normalize(eye - position));
    float visible = smoothstep(-0.02, 0.18, facing);

    float speed = length(particle.velocity) / flowSpeed() / 2.2;
    float fade = saturate(particle.age * 1.5) * saturate(particle.life * 1.2);
    float3 colour = windColour(speed, fixedRandom(id, 5.0));
    // The day side glows a touch brighter than the night side.
    float daylight = 0.65 + 0.35 * saturate(dot(n, sunDirection()) + 0.3);

    Sprite sprite = (Sprite)0;
    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seen.x - seenBefore.x, seenBefore.y - seen.y);
    sprite.size = visible > 0.0 && seen.z > 0.1 ? 0.85 : 0.0;
    sprite.colour = colour * (0.035 + pow(saturate(speed), 1.5) * 1.0) * fade * visible * daylight * (1.0 + Treble * 0.3);
    return sprite;
}

float sphereHit(float3 origin, float3 direction, float radius)
{
    float b = dot(origin, direction);
    float c = dot(origin, origin) - radius * radius;
    float h = b * b - c;
    return h < 0.0 ? -1.0 : -b - sqrt(h);
}

// Made-up continents: blotches of land from layered noise on the sphere.
float landAt(float3 n)
{
    float shape = noise3(n * 1.6 + float3(3.1, 0.0, 1.7)) * 0.7 + noise3(n * 3.7) * 0.25 + noise3(n * 9.0) * 0.08;
    return shape - 0.12 + abs(n.y) * 0.1;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 direction = cameraRay(eye, 0.0, float2(p.x, -p.y), Zoom, 0.0);
    float3 sun = sunDirection();

    // Deep space: a faint blue haze and a scatter of stars.
    float3 colour = float3(0.004, 0.005, 0.012) + float3(0.01, 0.012, 0.03) * (1.0 - abs(direction.y));
    float3 cell = floor(direction * 260.0);
    float star = hash3d(cell);
    colour += step(0.999, star) * (0.1 + pow(frac(star * 911.0), 3.0) * 0.8) * float3(0.8, 0.85, 1.0);

    float t = sphereHit(eye, direction, GlobeRadius);
    // How close the ray passes to the globe, for the glow of the atmosphere round its edge.
    float closest = length(eye - direction * dot(eye, direction));

    if (t > 0.0)
    {
        float3 position = eye + direction * t;
        float3 n = normalize(position);
        float day = saturate(dot(n, sun) * 1.4 + 0.15);
        float land = landAt(n);
        float isLand = smoothstep(0.0, 0.03, land);
        float coast = exp(-abs(land) * 140.0);

        // Midnight ocean with a sun glint, dusky land with a faint lit coastline.
        float3 ocean = float3(0.006, 0.014, 0.035);
        float3 ground = float3(0.022, 0.024, 0.028) + float3(0.012, 0.014, 0.01) * noise3(n * 14.0);
        float3 surface = lerp(ocean, ground, isLand) * (0.25 + day * 1.4);
        surface += float3(0.15, 0.35, 0.6) * coast * (0.08 + day * 0.12);
        float3 halfway = normalize(sun - direction);
        surface += float3(1.0, 0.8, 0.6) * pow(saturate(dot(n, halfway)), 600.0) * (1.0 - isLand) * 0.08 * day;

        // City lights twinkle on the night side of the land.
        float night = saturate(-dot(n, sun) * 3.0);
        float cities = smoothstep(0.75, 0.95, noise3(n * 40.0) * 0.5 + 0.5) * smoothstep(0.03, 0.1, land);
        surface += float3(1.0, 0.65, 0.3) * cities * night * 0.35;

        // The atmosphere seen edge-on glows blue on the day side, violet in the dusk.
        float rim = pow(1.0 - saturate(dot(n, -direction)), 3.0);
        surface += lerp(float3(0.25, 0.1, 0.5), float3(0.25, 0.55, 1.0), day) * pow(rim, 1.5) * (0.15 + day * 0.35);
        colour = surface;
    }

    // The thin halo of air just outside the globe.
    float halo = exp(-max(closest - GlobeRadius, 0.0) * 22.0) * step(t, 0.0);
    float sunSide = saturate(dot(normalize(eye - direction * dot(eye, direction) + 1e-4), sun) * 0.7 + 0.5);
    colour += lerp(float3(0.2, 0.08, 0.45), float3(0.3, 0.6, 1.2), sunSide) * halo * (0.2 + Bass * 0.2);

    return colour + particles(uv) + previous(uv) * 0.35;
}
