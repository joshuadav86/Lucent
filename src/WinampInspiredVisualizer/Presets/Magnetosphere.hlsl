// Name: Magnetosphere
// The solar wind streams in from the Sun and swerves round the Earth's invisible magnetic shield, piling up into a glowing bow wave, while stray particles spiral down the field lines to light green and red aurorae over the poles; gusts of wind arrive with the beat.
// Family: Particles
// Tags: Space, Colourful, 3D
// Particles: 262144

// The Earth sits at the origin with radius 1 and the Sun far off to the left (-x). Its magnetic
// axis is y. The wind flows along +x and is turned aside at the bow shock, a curved front that
// sits a few Earth radii upwind.

// How the particles are shared out: solar wind, then particles on field lines, then aurora.
static const float WindShare = 0.68;
static const float FieldShare = 0.2;
static const float WindStart = -11.0;
static const float WindEnd = 12.0;

float shareOf(uint id)
{
    return (id + 0.5) / ParticleCount;
}

// How far upwind the bow shock's nose sits; a gust pushes it closer.
float shockNose()
{
    return 3.4 - Bass * 0.45 - Beat * 0.3;
}

// The radius of the bow shock round the Sun-Earth line at distance x along it (a paraboloid).
float shockRadius(float x)
{
    return sqrt(max(4.6 * (x + shockNose()), 0.0));
}

float windSpeed()
{
    return 2.2 + Bass * 2.5 + Beat * 1.5;
}

// ---- Field lines ----

// A dipole field line through the equator at distance shell: r = shell * sin(colatitude)^2.
// The Sun's wind squashes the day side and stretches the night side out into a long tail.
float3 fieldPoint(float shell, float longitude, float colatitude)
{
    float s = sin(colatitude);
    float r = shell * s * s;
    float3 position = float3(s * cos(longitude), cos(colatitude), s * sin(longitude)) * r;
    float night = saturate(position.x / max(shell, 1e-3));
    position.x *= position.x < 0.0 ? 0.8 : 1.0 + night * 1.6;
    return position;
}

void Spawn(inout Particle particle, uint id)
{
    float share = shareOf(id);
    particle.velocity = 0.0;
    if (share < WindShare)
    {
        // Each wind particle keeps its distance from the Sun-Earth line far upstream (b) and its
        // angle round it, and is carried along the line.
        float b = 7.0 * pow(random(id, 1.0), 0.85);
        float angle = random(id, 2.0) * 2.0 * PI;
        float x = ParticleReset > 0.5 ? lerp(WindStart, WindEnd, random(id, 3.0)) : WindStart - random(id, 3.0) * 2.0;
        particle.data = float4(b, angle, x, 0.0);
        particle.position = float3(x, b * sin(angle), b * cos(angle));
        particle.life = 1e6;
        return;
    }
    if (share < WindShare + FieldShare)
    {
        // Captured particles slide down a field line towards one of the poles, spiralling as they go.
        float shell = lerp(2.2, 7.5, pow(random(id, 4.0), 1.5));
        float longitude = random(id, 5.0) * 2.0 * PI;
        float north = random(id, 6.0) < 0.5 ? 1.0 : -1.0;
        particle.data = float4(shell, longitude, north, PI * 0.5 + (random(id, 7.0) - 0.5) * 0.3);
        particle.position = fieldPoint(shell, longitude, particle.data.w);
        particle.life = ParticleReset > 0.5 ? random(id, 8.0) * 6.0 : 6.0;
        return;
    }
    // Aurora particles live on the two ovals round the magnetic poles.
    particle.data = float4(random(id, 9.0) * 2.0 * PI, random(id, 10.0) < 0.5 ? 1.0 : -1.0, random(id, 11.0), random(id, 12.0));
    particle.position = 0.0;
    particle.life = 1e6;
}

void Simulate(inout Particle particle, uint id)
{
    float share = shareOf(id);
    float dt = min(DeltaTime, 0.05);
    float3 before = particle.position;

    if (share < WindShare)
    {
        float b = particle.data.x;
        float angle = particle.data.y;
        // The wind slows sharply as it crosses the shock, then picks up again as it flows past.
        float x = particle.data.z;
        float edge = shockRadius(x);
        float r = sqrt(b * b + edge * edge);
        float squeeze = edge / max(r, 1e-3);
        float speed = windSpeed() * (1.0 - 0.6 * squeeze * smoothstep(-5.0, 0.0, x) * smoothstep(8.0, 2.0, x));
        x += speed * dt;
        // Behind the shock the flow is hot and turbulent.
        float3 position = float3(x, r * sin(angle), r * cos(angle));
        float turbulence = squeeze * smoothstep(-shockNose() - 0.5, 0.0, x);
        position += curlNoise(position * 0.6 + float3(-Time * 0.4, 0.0, 0.0)) * 0.12 * turbulence;
        particle.position = position;
        particle.data.z = x;
        particle.data.w = squeeze;
        if (x > WindEnd)
        {
            particle.data.z = WindStart - random(id, 13.0) * 1.5;
            particle.position.x = particle.data.z;
            particle.data.x = 7.0 * pow(random(id, 1.0), 0.85);
            particle.data.y = random(id, 2.0) * 2.0 * PI;
        }
    }
    else if (share < WindShare + FieldShare)
    {
        // Slide towards the pole, faster in a gust.
        float shell = particle.data.x;
        float target = particle.data.z > 0.0 ? asin(saturate(1.0 / sqrt(shell))) : PI - asin(saturate(1.0 / sqrt(shell)));
        float speed = (0.25 + Bass * 0.35 + Beat * 0.3) / shell;
        particle.data.w += (target - particle.data.w) * saturate(speed * 3.0 * dt) + sign(target - particle.data.w) * speed * 0.3 * dt;
        float3 onLine = fieldPoint(shell, particle.data.y, particle.data.w);
        // A tight corkscrew round the field line.
        float spin = particle.age * 9.0 + fixedRandom(id, 14.0) * 6.28;
        float3 out1 = normalize(cross(onLine, float3(0.0, 1.0, 0.0)) + 1e-4);
        float3 out2 = normalize(cross(out1, onLine) + 1e-4);
        float radius = 0.012 * length(onLine);
        particle.position = onLine + (out1 * cos(spin) + out2 * sin(spin)) * radius;
        if (length(particle.position) < 1.03)
        {
            particle.life = 0.0;
        }
    }
    particle.velocity = (particle.position - before) / max(DeltaTime, 1e-4);
}

// ---- Camera ----

float3 cameraEye()
{
    float angle = sin(Time * 0.04) * 0.7 + 0.25;
    float height = 2.6 + sin(Time * 0.03) * 1.6;
    return float3(-sin(angle) * 6.0 - 0.5, height, -cos(angle) * 8.5);
}

float3 cameraTarget()
{
    return float3(-0.6, 0.0, 0.0);
}

static const float Zoom = 1.3;

// Is this point hidden behind the Earth from the camera?
float behindEarth(float3 position, float3 eye)
{
    float3 toPoint = position - eye;
    float distance = length(toPoint);
    float3 direction = toPoint / distance;
    float b = dot(eye, direction);
    float c = dot(eye, eye) - 1.0;
    float h = b * b - c;
    if (h < 0.0)
    {
        return 0.0;
    }
    float t = -b - sqrt(h);
    return t > 0.0 && t < distance ? 1.0 : 0.0;
}

Sprite Show(Particle particle, uint id)
{
    float share = shareOf(id);
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    Sprite sprite = (Sprite)0;
    float3 position = particle.position;
    float3 colour = 0.0;
    float size = 1.0;
    float3 trail = particle.velocity * 0.07;

    if (share < WindShare)
    {
        // Warm gold far upstream, heating to pink and white as it piles into the bow shock.
        float squeeze = particle.data.w;
        float shock = smoothstep(0.55, 0.95, squeeze) * smoothstep(-shockNose() - 1.5, -shockNose() + 1.0, position.x);
        float level = spectrum(saturate(particle.data.x / 9.0) * 0.6 + 0.02);
        colour = lerp(float3(1.0, 0.8, 0.45), float3(1.0, 0.45, 0.8), saturate(squeeze * 1.3 - 0.2));
        colour = lerp(colour, float3(1.0, 0.85, 1.0), shock * 0.5);
        float fade = smoothstep(WindStart, WindStart + 3.0, position.x) * smoothstep(WindEnd, WindEnd - 3.0, position.x);
        colour *= 0.07 * (4.5 - squeeze * 3.8 + pow(squeeze, 6.0) * 4.0 + shock * 2.0) * (0.7 + level * 0.8) * fade;
        size = 0.9;
        trail *= 0.4;
    }
    else if (share < WindShare + FieldShare)
    {
        // Spiralling particles show the shape of the field: cool blues and violets.
        float near = 1.0 / particle.data.x;
        colour = lerp(float3(0.25, 0.4, 1.0), float3(0.6, 0.35, 1.0), fixedRandom(id, 15.0)) * 0.1 * (0.6 + near * 2.0) * (0.6 + Mid);
        colour *= saturate(particle.age * 1.5) * saturate(particle.life * 2.0);
        size = 1.0;
        trail *= 0.5;
    }
    else
    {
        // The aurora: a wavering curtain on an oval round each pole, green below and red on top,
        // brighter on the night side and flaring with each gust.
        float around = particle.data.x;
        float north = particle.data.y;
        float rise = particle.data.z;
        float wobble = 0.05 * sin(around * 5.0 + Time * 0.7) + 0.03 * sin(around * 13.0 - Time * 1.3);
        float colatitude = 0.36 + wobble + (particle.data.w - 0.5) * 0.04;
        float3 direction = float3(sin(colatitude) * cos(around), cos(colatitude) * north, sin(colatitude) * sin(around));
        float height = 1.02 + rise * rise * 0.2;
        position = direction * height;
        float night = 0.35 + 0.65 * saturate(direction.x * 1.5 + 0.4);
        float curtain = 0.5 + 0.5 * sin(around * 23.0 + Time * 1.5 + sin(around * 7.0 - Time) * 2.0);
        float3 green = float3(0.2, 1.0, 0.45);
        float3 red = float3(1.0, 0.2, 0.35);
        colour = lerp(green, red, smoothstep(0.4, 0.95, rise)) * (1.0 - rise * 0.6);
        colour *= 0.16 * night * (0.3 + curtain * curtain) * (0.4 + Bass * 1.2 + Beat * 1.5 + Level * 0.4);
        size = 1.2;
        trail = 0.0;
    }

    float3 seen = project(position, eye, target, Zoom, 0.0);
    float3 seenBefore = project(position - trail, eye, target, Zoom, 0.0);
    sprite.position = float2(seen.x, -seen.y);
    sprite.streak = float2(seen.x - seenBefore.x, seenBefore.y - seen.y);
    sprite.size = (seen.z > 0.3 && behindEarth(position, eye) < 0.5) ? size : 0.0;
    sprite.colour = colour;
    focus(sprite, seen.z, length(eye - target), 0.06);
    return sprite;
}

// ---- The Earth and the sky ----

static const float3 SunDirection = float3(-1.0, 0.12, 0.05);

float3 earth(float3 normal, float3 view)
{
    float3 sunward = normalize(SunDirection);
    // Turning slowly: continents from noise, oceans deep blue, swirls of cloud.
    float3 turned = normal;
    turned.xz = rotate(turned.xz, Time * 0.05);
    float2 map = float2(atan2(turned.z, turned.x), asin(clamp(turned.y, -1.0, 1.0))) * float2(1.6, 2.0);
    float land = smoothstep(0.55, 0.58, fbm(map * 1.3 + 3.0));
    float ice = smoothstep(0.82, 0.9, abs(turned.y));
    float clouds = smoothstep(0.5, 0.75, fbm(map * 2.5 + float2(Time * 0.02, 0.0)));
    float3 surface = lerp(float3(0.01, 0.035, 0.1), float3(0.09, 0.08, 0.04), land);
    surface = lerp(surface, 0.8, max(ice, clouds * 0.85));
    float daylight = saturate(dot(normal, sunward) * 1.2 + 0.1);
    float3 colour = surface * daylight * 1.6;
    // City lights on the night side.
    float cities = step(0.8, noise(map * 30.0)) * land * (1.0 - ice) * smoothstep(0.1, -0.15, dot(normal, sunward));
    colour += float3(1.0, 0.65, 0.3) * cities * 0.3;
    // A thin blue atmosphere round the edge.
    float rim = pow(1.0 - saturate(dot(normal, -view)), 3.0);
    colour += float3(0.25, 0.5, 1.0) * rim * (0.2 + daylight * 1.2);
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float3 eye = cameraEye();
    float3 target = cameraTarget();
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), Zoom, 0.0);

    // Stars, and the Sun's glare off to the left.
    float2 cell = float2(atan2(direction.z, direction.x) * 140.0, asin(direction.y) * 140.0);
    float2 id = floor(cell);
    float star = step(0.995, hash(id)) * smoothstep(0.3, 0.0, length(frac(cell) - 0.5)) * (0.1 + hash(id + 7.0) * 0.35);
    float3 colour = float3(0.002, 0.003, 0.008) + star;
    float sunward = saturate(dot(direction, normalize(SunDirection)));
    colour += float3(1.0, 0.75, 0.45) * (pow(sunward, 40.0) * 0.4 + pow(sunward, 6.0) * 0.06) * (1.0 + Bass * 0.5);

    // The Earth.
    float b = dot(eye, direction);
    float c = dot(eye, eye) - 1.0;
    float h = b * b - c;
    if (h > 0.0)
    {
        float t = -b - sqrt(h);
        float3 normal = normalize(eye + direction * t);
        colour = earth(normal, direction);
    }
    // The atmosphere's glow just beyond the edge.
    float closest = length(eye - direction * b);
    colour += float3(0.2, 0.45, 1.0) * exp(-max(closest - 1.0, 0.0) * 25.0) * (h > 0.0 ? 0.0 : 0.25);

    return colour + particles(uv) + previous(uv) * 0.55;
}
