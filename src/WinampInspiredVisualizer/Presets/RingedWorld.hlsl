// Name: Ringed World
// A banded gas giant hangs in space inside its vast rings: the rings throw thin stripes of shadow across the clouds, the planet's own shadow cuts a dark bite out of the rings, and a small moon drifts past; the cloud bands swirl and stir with the music.
// Family: Ray traced
// Tags: Space, Calm, 3D

static const float InnerRing = 1.28;
static const float OuterRing = 2.35;
static const float MoonRadius = 0.12;

// How wide one pixel is where the ray meets the rings, so ringlets finer than that fade out instead
// of shimmering. Set in Render.
static float Footprint = 0.0;
static const float MoonOrbit = 3.1;

// The planet's spin axis, which is also the rings' plane normal, and the direction to the sun.
float3 spinAxis()
{
    return normalize(float3(0.28, 1.0, 0.12));
}

float3 sunDirection()
{
    return normalize(float3(-0.34, 0.1, 0.9));
}

// Two directions in the ring plane, to measure longitude from.
void planeFrame(out float3 east, out float3 north)
{
    float3 axis = spinAxis();
    east = normalize(cross(axis, float3(0.0, 0.0, 1.0)));
    north = cross(east, axis);
}

float3 moonCentre()
{
    float3 east;
    float3 north;
    planeFrame(east, north);
    float angle = Time * 0.09 + 2.2;
    // The moon's orbit is tilted a few degrees out of the ring plane.
    return (east * cos(angle) + north * sin(angle)) * MoonOrbit + spinAxis() * sin(angle + 0.7) * 0.25;
}

float sphereHit(float3 origin, float3 direction, float3 centre, float radius)
{
    float3 oc = origin - centre;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - radius * radius;
    float h = b * b - c;
    if (h < 0.0)
    {
        return -1.0;
    }
    float t = -b - sqrt(h);
    return t > 1e-4 ? t : -1.0;
}

// How much light a sphere lets past along a ray: 0 straight behind it, with a soft penumbra.
float sphereShadow(float3 origin, float3 direction, float3 centre, float radius, float softness)
{
    float3 oc = centre - origin;
    float along = dot(oc, direction);
    if (along < 0.0)
    {
        return 1.0;
    }
    float miss = length(oc - direction * along);
    return smoothstep(radius * (1.0 - softness), radius * (1.0 + softness), miss);
}

// The rings: many ringlets of different thickness, a wide dark gap (like Cassini's) and a thin
// one near the outer edge. Each band also brightens with its own slice of the spectrum.
float ringDensity(float r)
{
    float x = (r - InnerRing) / (OuterRing - InnerRing);
    if (x < 0.0 || x > 1.0)
    {
        return 0.0;
    }
    float body = smoothstep(0.0, 0.12, x) * (0.55 + 0.45 * smoothstep(0.15, 0.45, x));
    float fine = exp(-Footprint * 160.0);
    float ringlets = 0.65 + 0.2 * sin(r * 160.0) * sin(r * 37.0) * fine + 0.25 * (noise(float2(r * 90.0, 0.5)) - 0.5) * exp(-Footprint * 60.0) + 0.15 * sin(r * 23.0);
    float gap = smoothstep(0.012, 0.004, abs(x - 0.66)) * 0.95 + smoothstep(0.006, 0.002, abs(x - 0.9)) * 0.8;
    float fade = smoothstep(1.0, 0.94, x);
    return saturate(body * ringlets * (1.0 - gap) * fade);
}

float3 ringColour(float r)
{
    float x = saturate((r - InnerRing) / (OuterRing - InnerRing));
    float3 colour = lerp(float3(0.55, 0.48, 0.4), float3(0.9, 0.82, 0.7), smoothstep(0.1, 0.5, x));
    return lerp(colour, float3(0.75, 0.72, 0.7), smoothstep(0.7, 1.0, x));
}

// How much sunlight gets through the rings to a point: rays crossing a dense band are dimmed.
float ringShadow(float3 position)
{
    float3 axis = spinAxis();
    float3 sun = sunDirection();
    float facing = dot(sun, axis);
    float t = -dot(position, axis) / facing;
    if (t <= 0.0)
    {
        return 1.0;
    }
    return 1.0 - ringDensity(length(position + sun * t)) * 0.85;
}

// The cloud bands: stripes by latitude that drift at different speeds, stirred into eddies.
// The mids stir them harder; a pale storm oval drifts in the south.
float3 cloudColour(float3 normal)
{
    float3 axis = spinAxis();
    float3 east;
    float3 north;
    planeFrame(east, north);
    float latitude = asin(clamp(dot(normal, axis), -1.0, 1.0));
    float longitude = atan2(dot(normal, north), dot(normal, east));
    // Bands near the equator race ahead of those nearer the poles.
    float spin = Time * (0.05 + 0.03 * cos(latitude * 6.0));
    float2 map = float2(longitude + spin, latitude);
    float stir = 0.5 + Mid * 0.8 + Bass * 0.3;
    float2 warp = float2(fbm(map * float2(2.0, 14.0) + float2(0.0, Time * 0.03)), fbm(map * float2(3.0, 18.0) + 5.0 - float2(Time * 0.02, 0.0))) - 0.5;
    float y = latitude + warp.y * 0.05 * stir + sin(map.x * 4.0 + latitude * 30.0) * 0.006 * stir;
    float bands = sin(y * 22.0) * 0.5 + sin(y * 47.0 + 1.0) * 0.3 + sin(y * 9.0 + 2.0) * 0.4;
    float fine = fbm(float2(map.x * 6.0 + warp.x * 2.0, y * 60.0));
    float3 light = float3(0.95, 0.85, 0.68);
    float3 dark = float3(0.62, 0.42, 0.26);
    float3 colour = lerp(dark, light, saturate(bands * 0.5 + 0.5 + (fine - 0.5) * 0.6));
    // A pale-blue haze over the poles.
    colour = lerp(colour, float3(0.55, 0.62, 0.7), smoothstep(0.9, 1.3, abs(latitude)));
    // The storm, with a swirl inside it.
    float2 fromStorm = float2(wrap(map.x - Time * 0.01 + PI, 2.0 * PI) - PI - 0.6, (latitude + 0.38) * 2.2);
    float storm = exp(-dot(fromStorm, fromStorm) * 30.0);
    colour = lerp(colour, float3(0.9, 0.55, 0.35), storm * (0.6 + 0.2 * sin(atan2(fromStorm.y, fromStorm.x) * 3.0 + length(fromStorm) * 40.0 - Time)));
    return colour;
}

float3 shadePlanet(float3 position, float3 direction)
{
    float3 normal = position;
    float3 sun = sunDirection();
    float facing = saturate(dot(normal, -direction));
    float daylight = saturate(dot(normal, sun) * 1.1 + 0.05);
    // The light lands across the rings and past the moon before reaching the clouds.
    float shadow = ringShadow(position) * sphereShadow(position, sun, moonCentre(), MoonRadius, 0.25);
    float3 colour = cloudColour(normal) * daylight * shadow * (0.6 + 0.4 * pow(facing, 0.4)) * 1.7;
    // The night side is faintly lit by sunlight bouncing off the rings.
    colour += cloudColour(normal) * float3(0.02, 0.018, 0.015) * saturate(-dot(normal, spinAxis()) * sign(dot(sun, spinAxis())) + 0.3);
    // A thin bright haze at the edge of the lit side.
    float rim = pow(1.0 - facing, 4.0);
    colour += float3(0.55, 0.65, 0.9) * rim * daylight * (0.6 + Level * 0.6);
    return colour;
}

float3 shadeMoon(float3 position, float3 centre)
{
    float3 normal = normalize(position - centre);
    float craters = fbm(normal.xy * 6.0 + normal.z * 3.0);
    float3 colour = float3(0.6, 0.58, 0.55) * (0.6 + craters * 0.6);
    float lit = saturate(dot(normal, sunDirection()));
    // The planet can eclipse the moon.
    lit *= sphereShadow(position, sunDirection(), 0.0, 1.0, 0.03);
    return colour * lit * 1.6 + colour * 0.01;
}

float3 starfield(float3 direction)
{
    float2 cell = float2(atan2(direction.z, direction.x) * 160.0, asin(clamp(direction.y, -1.0, 1.0)) * 160.0);
    float2 id = floor(cell);
    float star = step(0.992, hash(id)) * smoothstep(0.3, 0.0, length(frac(cell) - 0.5)) * (0.2 + hash(id + 3.0) * 0.6);
    float haze = fbm(direction.xz * 3.0 + direction.y * 2.0);
    float3 nebula = float3(0.02, 0.012, 0.03) * smoothstep(0.4, 0.8, haze) + float3(0.006, 0.008, 0.016);
    return nebula + star * lerp(float3(1.0, 0.85, 0.7), float3(0.7, 0.8, 1.0), hash(id + 5.0));
}

float3 Render(float2 uv, float2 p)
{
    // Swinging slowly to and fro round the planet, staying on its sunlit side.
    float orbit = 0.5 + sin(Time * 0.021) * 0.65;
    float3 eye = float3(sin(orbit) * 5.2, 0.9 + sin(Time * 0.04) * 0.45, cos(orbit) * 5.2);
    float3 target = float3(0.1, -0.05, 0.0);
    const float zoom = 1.85;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), zoom, -0.18 + sin(Time * 0.03) * 0.05);

    float3 colour = starfield(direction);
    float nearest = 1e5;

    float tPlanet = sphereHit(eye, direction, 0.0, 1.0);
    float3 moon = moonCentre();
    float tMoon = sphereHit(eye, direction, moon, MoonRadius);
    if (tPlanet > 0.0)
    {
        nearest = tPlanet;
        // The ring shadow is the rings stretched across the clouds, so blur it to match.
        Footprint = pixelSize() / zoom * tPlanet / max(abs(dot(sunDirection(), spinAxis())), 0.05) * 0.5;
        colour = shadePlanet(eye + direction * tPlanet, direction);
    }
    if (tMoon > 0.0 && tMoon < nearest)
    {
        nearest = tMoon;
        colour = shadeMoon(eye + direction * tMoon, moon);
    }

    // The glow of the atmosphere just beyond the planet's edge, on its sunlit side.
    float b = dot(eye, direction);
    float3 closestPoint = eye - direction * b;
    float height = length(closestPoint) - 1.0;
    if (tPlanet < 0.0 && b < 0.0)
    {
        float lit = saturate(dot(normalize(closestPoint), sunDirection()) + 0.3);
        colour += float3(0.5, 0.65, 1.0) * exp(-max(height, 0.0) * 40.0) * lit * 0.5;
    }

    // The rings, laid over whatever is behind them.
    float3 axis = spinAxis();
    float along = dot(direction, axis);
    float tRing = abs(along) > 1e-5 ? -dot(eye, axis) / along : -1.0;
    if (tRing > 0.0 && tRing < nearest)
    {
        float3 position = eye + direction * tRing;
        Footprint = pixelSize() / zoom * tRing / max(abs(along), 0.05);
        float r = length(position);
        float density = ringDensity(r);
        if (density > 0.0)
        {
            float3 sun = sunDirection();
            float x = saturate((r - InnerRing) / (OuterRing - InnerRing));
            // Lit face or back-lit face: the rings glow softly when the sun is behind them.
            bool sameSide = sign(dot(sun, axis)) == sign(dot(-direction, axis));
            float light = sameSide ? 0.5 + abs(dot(sun, axis)) * 1.6 : (1.0 - density) * 0.9 + 0.05;
            light += pow(saturate(dot(direction, sun)), 6.0) * 0.6;
            float shadow = sphereShadow(position, sun, 0.0, 1.0, 0.02);
            float music = 0.85 + spectrum(x * 0.7 + 0.03) * 0.4 + Beat * 0.08;
            float3 ring = ringColour(r) * light * (0.04 + 0.96 * shadow) * music * 1.1;
            colour = lerp(colour, ring, density * 0.92);
        }
    }
    return colour;
}
