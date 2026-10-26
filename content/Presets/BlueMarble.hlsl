// Name: Blue Marble
// The Earth from orbit, turning slowly beneath you: a thin blue sky glowing along its edge, sunset orange along the line between day and night, drifting clouds casting their shadows, the sun glinting off the oceans, and city lights twinkling on the night side with the treble while storms flicker on the beat.
// Family: Ray traced
// Tags: Space, Calm, 3D
// Added: 2026-10-17 17:15

// The sky is computed the way real sunlight scatters in air: for each pixel we step through
// the atmosphere, and at each step work out how much sunlight reaches that spot (dimmed and
// reddened by the air it crossed) and how much of it is scattered towards us: blue light
// scatters most (Rayleigh), and haze adds a bright glow around the sun (Mie). The same dimming
// gives the surface its orange light near sunset. The atmosphere is drawn about ten times
// thicker than the real one, so it reads at this distance.
static const float PlanetRadius = 1.0;
static const float AtmosphereRadius = 1.075;
static const float RayleighHeight = 0.012;
static const float MieHeight = 0.004;
static const float3 RayleighScatter = float3(5.5, 13.0, 22.4);
static const float MieScatter = 4.0;
static const float MieAsymmetry = 0.76;

float3 sunDirection()
{
    return normalize(float3(1.0, 0.3, -0.42 + sin(Time * 0.013) * 0.15));
}

float3 sunIntensity()
{
    return 8.5;
}

// ---- 3D noise ----

float hash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(hash3(i), hash3(i + float3(1, 0, 0)), f.x), lerp(hash3(i + float3(0, 1, 0)), hash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(hash3(i + float3(0, 0, 1)), hash3(i + float3(1, 0, 1)), f.x), lerp(hash3(i + float3(0, 1, 1)), hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm3(float3 p, int octaves)
{
    float total = 0.0;
    float amplitude = 0.5;
    [loop]
    for (int i = 0; i < octaves; i++)
    {
        total += noise3(p) * amplitude;
        p = p * 2.07 + float3(13.1, 7.3, 3.7);
        amplitude *= 0.5;
    }
    return total;
}

// ---- The ground and the clouds ----

// A point on the globe in the Earth's own frame (it turns slowly).
float3 earthFrame(float3 normal)
{
    float2 xz = rotate(normal.xz, Time * 0.012);
    return float3(xz.x, normal.y, xz.y);
}

float landHeight(float3 q)
{
    float3 warp = float3(fbm3(q * 1.6 + 5.0, 3), fbm3(q * 1.6 + 9.0, 3), fbm3(q * 1.6 + 1.0, 3)) - 0.5;
    return fbm3(q * 2.6 + warp * 1.4, qualitySteps(7)) - 0.49;
}

float cloudCover(float3 normal)
{
    // Clouds drift a little faster than the ground turns, curling into weather systems.
    float2 xz = rotate(normal.xz, Time * 0.017);
    float3 q = float3(xz.x, normal.y, xz.y);
    float3 swirl = float3(fbm3(q * 2.5 + 20.0, 3), fbm3(q * 2.5 + 40.0, 3), fbm3(q * 2.5 + 60.0, 2)) - 0.5;
    float c = fbm3(q * 5.5 + swirl * 2.2 + float3(0.0, 0.0, Time * 0.004), qualitySteps(7));
    // Fewer clouds in the dry belts either side of the equator.
    float belts = 0.9 - 0.25 * exp(-pow((abs(normal.y) - 0.42) * 6.0, 2.0)) + 0.1 * exp(-normal.y * normal.y * 40.0);
    return smoothstep(0.5, 0.66, c * belts + 0.08);
}

// ---- The atmosphere ----

float2 sphereSpan(float3 origin, float3 direction, float radius)
{
    float b = dot(origin, direction);
    float h = b * b - dot(origin, origin) + radius * radius;
    if (h < 0.0)
    {
        return float2(1e5, -1e5);
    }
    h = sqrt(h);
    return float2(-b - h, -b + h);
}

// Air between a point and the sun: Rayleigh (x) and haze (y) amounts.
float2 depthToSun(float3 position, float3 sun)
{
    float2 span = sphereSpan(position, sun, AtmosphereRadius);
    float stepLength = max(span.y, 0.0) / 6.0;
    float2 depth = 0.0;
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        float height = length(position + sun * ((i + 0.5) * stepLength)) - PlanetRadius;
        depth += exp(-max(height, 0.0) / float2(RayleighHeight, MieHeight)) * stepLength;
    }
    // Behind the planet there is no direct sunlight; fade it out smoothly at the planet's edge.
    float along = dot(position, sun);
    float closest = along < 0.0 ? length(position - sun * along) : length(position);
    float blocked = 1.0 - smoothstep(PlanetRadius - 0.012, PlanetRadius - 0.001, closest);
    return depth + blocked * 60.0;
}

float3 extinction(float2 depth)
{
    return exp(-(RayleighScatter * depth.x + MieScatter * 1.1 * depth.y));
}

// Light scattered towards the eye along a ray from start to end, and how much of what's behind gets through.
float3 scatterAlong(float3 origin, float3 direction, float start, float end, float3 sun, out float3 transmittance)
{
    int steps = qualitySteps(16);
    float stepLength = (end - start) / steps;
    float cosine = dot(direction, sun);
    float rayleighPhase = 3.0 / (16.0 * PI) * (1.0 + cosine * cosine);
    float g = MieAsymmetry;
    float miePhase = 3.0 / (8.0 * PI) * ((1.0 - g * g) * (1.0 + cosine * cosine)) / ((2.0 + g * g) * pow(max(1.0 + g * g - 2.0 * g * cosine, 1e-4), 1.5));

    float jitter = frac(hash(Resolution * float2(0.37, 0.71) * cosine + origin.xy) + FrameCount * 0.618);
    float2 depthToEye = 0.0;
    float3 rayleigh = 0.0;
    float3 mie = 0.0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 position = origin + direction * (start + (i + jitter) * stepLength);
        float height = length(position) - PlanetRadius;
        float2 density = exp(-max(height, 0.0) / float2(RayleighHeight, MieHeight)) * stepLength;
        depthToEye += density;
        float3 attenuation = extinction(depthToEye + depthToSun(position, sun));
        rayleigh += density.x * attenuation;
        mie += density.y * attenuation;
    }
    transmittance = extinction(depthToEye);
    return sunIntensity() * (rayleigh * RayleighScatter * rayleighPhase + mie * MieScatter * miePhase);
}

// ---- Stars ----

float3 starfield(float3 direction)
{
    float2 cell = float2(atan2(direction.z, direction.x), asin(clamp(direction.y, -1.0, 1.0))) * 160.0;
    float2 id = floor(cell);
    float pick = hash(id);
    float2 spot = (float2(hash(id + 3.1), hash(id + 7.7)) - 0.5) * 0.7;
    float star = step(0.975, pick) * exp(-dot(frac(cell) - 0.5 - spot, frac(cell) - 0.5 - spot) * 70.0);
    return lerp(float3(0.7, 0.8, 1.0), float3(1.0, 0.85, 0.7), hash(id + 1.3)) * star * (0.2 + pick * 0.6);
}

// ---- Shading the surface ----

// Tiny points of light scattered over the globe, one per cell of a 3D grid (most cells empty).
float townLights(float3 q, float scale)
{
    float3 cell = floor(q * scale);
    float3 inCell = frac(q * scale) - 0.5;
    float pick = hash3(cell + 0.37);
    float3 spot = (float3(hash3(cell + 1.7), hash3(cell + 4.3), hash3(cell + 8.9)) - 0.5) * 0.6;
    float3 d = inCell - spot;
    return step(0.45, pick) * exp(-dot(d, d) * 30.0) * (0.4 + pick);
}

float3 surfaceColour(float3 position, float3 direction, float3 sun, float3 sunlight)
{
    float3 normal = normalize(position);
    float3 q = earthFrame(normal);
    float height = landHeight(q);
    bool land = height > 0.0;
    float latitude = abs(normal.y);
    float daylight = dot(normal, sun);

    // Ground: forests, grassland, desert and ice; oceans deep blue with turquoise shallows.
    float3 albedo;
    float roughness = 0.9;
    if (land)
    {
        float dryness = noise3(q * 4.0 + 3.0) + exp(-pow((latitude - 0.38) * 5.0, 2.0)) * 0.5;
        albedo = lerp(float3(0.035, 0.08, 0.02), float3(0.09, 0.12, 0.04), saturate(height * 8.0));
        albedo = lerp(albedo, float3(0.24, 0.17, 0.09), smoothstep(0.85, 1.15, dryness));
        albedo = lerp(albedo, float3(0.25, 0.22, 0.2), smoothstep(0.12, 0.2, height) * 0.6);
        albedo = lerp(albedo, float3(0.7, 0.75, 0.8), smoothstep(0.78, 0.86, latitude + height * 0.4));
    }
    else
    {
        albedo = lerp(float3(0.004, 0.015, 0.04), float3(0.02, 0.09, 0.1), smoothstep(-0.07, 0.0, height));
        albedo = lerp(albedo, float3(0.75, 0.8, 0.85), smoothstep(0.86, 0.92, latitude));
        roughness = 0.22;
    }

    // Clouds above, and their shadows on the ground, offset towards the sun.
    float cloud = cloudCover(normal);
    float shadow = 1.0 - cloudCover(normalize(normal + sun * 0.018)) * 0.75;

    float3 colour = albedo * sunlight * saturate(daylight) * shadow;

    // Sun glint on open water.
    if (!land)
    {
        float3 halfway = normalize(sun - direction);
        float nh = saturate(dot(normal, halfway));
        float a2 = roughness * roughness * roughness * roughness;
        float d = nh * nh * (a2 - 1.0) + 1.0;
        float specular = a2 / (PI * d * d);
        float fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(normal, -direction)), 5.0);
        colour += sunlight * specular * fresnel * saturate(daylight) * shadow * 0.25;
    }

    // City lights on the night side, clustered along coasts and in the lowlands, twinkling with the treble.
    float night = smoothstep(0.05, -0.15, daylight);
    if (land && night > 0.0)
    {
        float clusters = smoothstep(0.5, 0.85, noise3(q * 14.0) * 0.7 + noise3(q * 5.0) * 0.5 + (0.1 - abs(height - 0.05)) * 2.0 - 0.15);
        float towns = townLights(q, 90.0) * 1.6 + townLights(q * 1.7 + 3.1, 220.0);
        float twinkle = 0.75 + (noise3(q * 300.0 + Time * 3.0) - 0.5) * Treble * 1.4;
        float3 lights = float3(1.0, 0.62, 0.25) * clusters * towns * twinkle * (0.6 + Level * 0.4);
        colour += lights * night * (1.0 - cloud * 0.85) * 0.5 * smoothstep(0.85, 0.6, latitude);
    }

    // The clouds themselves: bright white by day, faintly lit by city glow at night.
    float3 cloudLit = float3(0.9, 0.92, 0.95) * lerp(sunlight, dot(sunlight, 0.333), 0.4) * saturate(daylight * 0.9 + 0.1) * smoothstep(-0.1, 0.05, daylight);
    // A storm on the night side flickers on each beat.
    float stormSeed = floor(BeatCount);
    float3 stormAt = normalize(float3(hash(float2(stormSeed, 1.0)) - 0.5, (hash(float2(stormSeed, 2.0)) - 0.5) * 0.8, hash(float2(stormSeed, 3.0)) - 0.5) + sun * -1.2);
    float flash = Beat * Beat * exp(-dot(normal - stormAt, normal - stormAt) * 600.0) * night;
    cloudLit += float3(0.7, 0.8, 1.0) * flash * 6.0 * (0.3 + cloud);
    colour = lerp(colour, cloudLit, cloud * 0.92);
    return colour;
}

float3 Render(float2 uv, float2 p)
{
    float3 sun = sunDirection();
    // A slow drifting orbit, looking towards the curve of the planet's edge.
    float orbit = Time * 0.01;
    float3 eye = float3(-0.55 + sin(orbit) * 0.08, 0.45 + sin(Time * 0.017) * 0.05, -1.1);
    float3 target = float3(0.6 + sin(Time * 0.021) * 0.1, -0.05, 0.4);
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.4, -0.32 + sin(Time * 0.015) * 0.04);

    float3 colour = starfield(direction);
    // The sun itself, with a soft glare.
    float toSun = saturate(dot(direction, sun));
    colour += float3(1.0, 0.95, 0.88) * (smoothstep(0.9997, 0.99985, toSun) * 40.0 + pow(toSun, 900.0) * 3.0 + pow(toSun, 40.0) * 0.06);

    float2 air = sphereSpan(eye, direction, AtmosphereRadius);
    if (air.y > 0.0)
    {
        float2 ground = sphereSpan(eye, direction, PlanetRadius);
        bool hitsGround = ground.x > 0.0 && ground.x < 1e4;
        float start = max(air.x, 0.0);
        float end = hitsGround ? ground.x : air.y;
        float3 transmittance;
        float3 inScatter = scatterAlong(eye, direction, start, end, sun, transmittance);
        float3 behind = colour;
        if (hitsGround)
        {
            float3 position = eye + direction * ground.x;
            float3 sunlight = 3.6 * extinction(depthToSun(position * 1.001, sun) * 0.6);
            // Light scattered down from the sky keeps the day side from going flat in shadow.
            sunlight += float3(0.05, 0.09, 0.18) * saturate(dot(normalize(position), sun) + 0.2);
            behind = surfaceColour(position, direction, sun, sunlight);
        }
        colour = behind * transmittance + inScatter * (0.9 + Level * 0.15);
    }
    // A little of the last frame smooths the sky's sampling noise.
    return lerp(colour, previous(uv), 0.4);
}
