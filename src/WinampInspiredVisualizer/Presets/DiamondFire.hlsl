// Name: Diamond Fire
// A brilliant-cut diamond turns slowly above black velvet, ray traced colour by colour as light bends and bounces inside it, so it throws flashes of rainbow fire; the treble makes it sparkle.
// Family: Ray traced
// Tags: Dark, Geometric, 3D
// Added: 2026-10-17 17:06

// The classic round brilliant, girdle radius 1: 57% table, 34.5 degree crown, 40.75 degree pavilion.
static const int FacetCount = 73;
static const float GirdleHalf = 0.02;
static const float TableHeight = 0.315;
static const float VelvetHeight = -1.45;

// Each facet as an outward normal (xyz) and its distance from the centre (w). The stone never
// changes shape, so these are worked out once, ahead of time, rather than per pixel: an array
// filled in every pixel lives in each pixel's own memory and is slow to read on a Mac's GPU.
static const float4 Facets[FacetCount] =
{
    // The table, the flat top.
    float4(0.0, 1.0, 0.0, 0.315),
    // Crown: the eight big kite-shaped bezels (34.5 degrees),
    float4(0.56640624, 0.82412619, 0.0, 0.58288876),
    float4(0.40050969, 0.82412619, 0.40050969, 0.58288876),
    float4(0.0, 0.82412619, 0.56640624, 0.58288876),
    float4(-0.40050969, 0.82412619, 0.40050969, 0.58288876),
    float4(-0.56640624, 0.82412619, 0.0, 0.58288876),
    float4(-0.40050969, 0.82412619, -0.40050969, 0.58288876),
    float4(0.0, 0.82412619, -0.56640624, 0.58288876),
    float4(0.40050969, 0.82412619, -0.40050969, 0.58288876),
    // the eight small star facets round the table,
    float4(0.34268583, 0.92866464, 0.14194512, 0.52137377),
    float4(0.14194512, 0.92866464, 0.34268583, 0.52137377),
    float4(-0.14194512, 0.92866464, 0.34268583, 0.52137377),
    float4(-0.34268583, 0.92866464, 0.14194512, 0.52137377),
    float4(-0.34268583, 0.92866464, -0.14194512, 0.52137377),
    float4(-0.14194512, 0.92866464, -0.34268583, 0.52137377),
    float4(0.14194512, 0.92866464, -0.34268583, 0.52137377),
    float4(0.34268583, 0.92866464, -0.14194512, 0.52137377),
    // Pavilion: the eight long mains running down to the point (40.75 degrees).
    float4(0.65275975, -0.75756498, 0.0, 0.66791105),
    float4(0.46157085, -0.75756498, 0.46157085, 0.66791105),
    float4(0.0, -0.75756498, 0.65275975, 0.66791105),
    float4(-0.46157085, -0.75756498, 0.46157085, 0.66791105),
    float4(-0.65275975, -0.75756498, 0.0, 0.66791105),
    float4(-0.46157085, -0.75756498, -0.46157085, 0.66791105),
    float4(0.0, -0.75756498, -0.65275975, 0.66791105),
    float4(0.46157085, -0.75756498, -0.46157085, 0.66791105),
    // Crown: the sixteen upper girdle facets.
    float4(0.65405592, 0.7451744, 0.13009981, 0.68177312),
    float4(0.55448184, 0.7451744, 0.37049292, 0.68177312),
    float4(0.37049292, 0.7451744, 0.55448184, 0.68177312),
    float4(0.13009981, 0.7451744, 0.65405592, 0.68177312),
    float4(-0.13009981, 0.7451744, 0.65405592, 0.68177312),
    float4(-0.37049292, 0.7451744, 0.55448184, 0.68177312),
    float4(-0.55448184, 0.7451744, 0.37049292, 0.68177312),
    float4(-0.65405592, 0.7451744, 0.13009981, 0.68177312),
    float4(-0.65405592, 0.7451744, -0.13009981, 0.68177312),
    float4(-0.55448184, 0.7451744, -0.37049292, 0.68177312),
    float4(-0.37049292, 0.7451744, -0.55448184, 0.68177312),
    float4(-0.13009981, 0.7451744, -0.65405592, 0.68177312),
    float4(0.13009981, 0.7451744, -0.65405592, 0.68177312),
    float4(0.37049292, 0.7451744, -0.55448184, 0.68177312),
    float4(0.55448184, 0.7451744, -0.37049292, 0.68177312),
    float4(0.65405592, 0.7451744, -0.13009981, 0.68177312),
    // Pavilion: the sixteen lower girdle facets.
    float4(0.67568401, -0.72483601, 0.13440191, 0.70341817),
    float4(0.57281725, -0.72483601, 0.38274425, 0.70341817),
    float4(0.38274425, -0.72483601, 0.57281725, 0.70341817),
    float4(0.13440191, -0.72483601, 0.67568401, 0.70341817),
    float4(-0.13440191, -0.72483601, 0.67568401, 0.70341817),
    float4(-0.38274425, -0.72483601, 0.57281725, 0.70341817),
    float4(-0.57281725, -0.72483601, 0.38274425, 0.70341817),
    float4(-0.67568401, -0.72483601, 0.13440191, 0.70341817),
    float4(-0.67568401, -0.72483601, -0.13440191, 0.70341817),
    float4(-0.57281725, -0.72483601, -0.38274425, 0.70341817),
    float4(-0.38274425, -0.72483601, -0.57281725, 0.70341817),
    float4(-0.13440191, -0.72483601, -0.67568401, 0.70341817),
    float4(0.13440191, -0.72483601, -0.67568401, 0.70341817),
    float4(0.38274425, -0.72483601, -0.57281725, 0.70341817),
    float4(0.57281725, -0.72483601, -0.38274425, 0.70341817),
    float4(0.67568401, -0.72483601, -0.13440191, 0.70341817),
    // The girdle: a thin band of sixteen flats round the widest part.
    float4(1.0, 0.0, 0.0, 1.0),
    float4(0.92387953, 0.0, 0.38268343, 1.0),
    float4(0.70710678, 0.0, 0.70710678, 1.0),
    float4(0.38268343, 0.0, 0.92387953, 1.0),
    float4(0.0, 0.0, 1.0, 1.0),
    float4(-0.38268343, 0.0, 0.92387953, 1.0),
    float4(-0.70710678, 0.0, 0.70710678, 1.0),
    float4(-0.92387953, 0.0, 0.38268343, 1.0),
    float4(-1.0, 0.0, 0.0, 1.0),
    float4(-0.92387953, 0.0, -0.38268343, 1.0),
    float4(-0.70710678, 0.0, -0.70710678, 1.0),
    float4(-0.38268343, 0.0, -0.92387953, 1.0),
    float4(0.0, 0.0, -1.0, 1.0),
    float4(0.38268343, 0.0, -0.92387953, 1.0),
    float4(0.70710678, 0.0, -0.70710678, 1.0),
    float4(0.92387953, 0.0, -0.38268343, 1.0)
};

// The whole stone fits inside this sphere (its farthest corners, at the girdle, are about 1.02 out).
static const float StoneRadius = 1.1;

// Ray against the whole stone from outside: where it enters, and that facet's normal.
float enterDiamond(float3 origin, float3 direction, out float3 normal)
{
    float entry = -1e9;
    float leave = 1e9;
    normal = float3(0.0, 1.0, 0.0);
    [loop]
    for (int i = 0; i < FacetCount; i++)
    {
        float4 facet = Facets[i];
        float facing = dot(facet.xyz, direction);
        float t = (facet.w - dot(facet.xyz, origin)) / facing;
        if (facing < 0.0)
        {
            if (t > entry)
            {
                entry = t;
                normal = facet.xyz;
            }
        }
        else
        {
            leave = min(leave, t);
        }
    }
    return (entry < leave && entry > 0.0) ? entry : -1.0;
}

// From inside the stone: how far to the facet the ray leaves through, and that facet's outward normal.
float leaveDiamond(float3 origin, float3 direction, out float3 normal)
{
    float leave = 1e9;
    normal = float3(0.0, 1.0, 0.0);
    [loop]
    for (int i = 0; i < FacetCount; i++)
    {
        float4 facet = Facets[i];
        float facing = dot(facet.xyz, direction);
        if (facing > 1e-5)
        {
            float t = (facet.w - dot(facet.xyz, origin)) / facing;
            if (t < leave)
            {
                leave = t;
                normal = facet.xyz;
            }
        }
    }
    return leave;
}

// ---- Surroundings, in the diamond's turning frame ----

static float Turn = 0.0;
static float Tilt = 0.0;

float3 toWorld(float3 v)
{
    v.yz = rotate(v.yz, Tilt);
    v.xz = rotate(v.xz, Turn);
    return v;
}

float3 toStone(float3 v)
{
    v.xz = rotate(v.xz, -Turn);
    v.yz = rotate(v.yz, -Tilt);
    return v;
}

// Small, very bright lamps overhead and around: the sharp light a diamond needs to show fire.
// The treble makes them twinkle; the colour of the room's glow slowly turns with the music.
float3 lamps(float3 direction)
{
    float3 total = 0.0;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float angle = i * 0.785 + 0.4 + Time * 0.02;
        float rise = 0.45 + 0.4 * frac(i * 0.618);
        float3 lamp = normalize(float3(cos(angle) * cos(rise), sin(rise), sin(angle) * cos(rise)));
        float twinkle = 1.0 + Beat * 0.6 + Treble * 2.5 * (0.5 + 0.5 * sin(Time * (9.0 + i * 3.1) + i));
        float tightness = 260.0 + i * 60.0;
        total += float3(1.0, 0.97, 0.92) * 7.0 * twinkle * exp(-(1.0 - dot(direction, lamp)) * tightness);
    }
    // One big soft window light from above, breathing with the level.
    total += float3(0.8, 0.85, 1.0) * 1.3 * (0.8 + Level * 0.5) * exp(-(1.0 - direction.y) * 6.0);
    return total;
}

float velvetFolds(float2 q)
{
    return noise(q * 0.3 + float2(3.0, 1.0)) * 0.75 + noise(q * 0.7 + float2(7.0, 2.0)) * 0.25;
}

float3 velvet(float3 position, float3 direction)
{
    // Deep black-blue velvet in soft folds, with its sheen strongest where we look across it.
    float2 q = position.xz;
    float folds = velvetFolds(q);
    float3 normal = normalize(float3((velvetFolds(q + float2(0.05, 0.0)) - folds) * 14.0, 1.0, (velvetFolds(q + float2(0.0, 0.05)) - folds) * 14.0));
    float grazing = 1.0 - saturate(dot(normal, -direction));
    float3 sheenColour = hsv2rgb(0.64 + sin(Time * 0.02) * 0.06, 0.65, 1.0);
    float sheen = (pow(grazing, 3.0) * 0.22 + 0.01) * exp(-length(q) * 0.05);
    float lit = 0.25 + 0.75 * saturate(dot(normal, normalize(float3(0.3, 1.0, 0.2))));
    // A pool of light under the stone, and its shadow in the middle.
    float pool = exp(-dot(q, q) * 0.12);
    float shadow = 1.0 - 0.7 * exp(-dot(q, q) * 1.0);
    // Scattered rainbow sparks of fire the diamond throws down onto the cloth nearby.
    float2 cell = floor(q * 7.0);
    float spark = step(0.97, hash(cell + floor(Time * 2.0 + hash(cell) * 3.0) * 7.31));
    float near = exp(-dot(q, q) * 0.5) * (1.0 - exp(-dot(q, q) * 2.0));
    float3 fire = hsv2rgb(hash(cell * 1.3), 0.85, 1.0) * spark * near * (0.5 + Treble * 2.0) * smoothstep(0.3, 0.0, length(frac(q * 7.0) - 0.5));
    return (sheenColour * sheen * lit + float3(0.03, 0.03, 0.04) * pool * lit) * shadow + fire * 0.5;
}

// Everything around the stone, seen in a direction from a point (both in the stone's frame).
float3 surroundings(float3 origin, float3 direction)
{
    float3 world = toWorld(direction);
    float3 from = toWorld(origin);
    if (world.y < 0.0)
    {
        float t = (VelvetHeight - from.y) / world.y;
        return velvet(from + world * t, world);
    }
    return float3(0.006, 0.006, 0.01) + lamps(world);
}

// One colour's journey through the stone: in at the front, bouncing off the facets inside
// (mostly trapped by total internal reflection) until it escapes. ior is that colour's refractive index.
float traceColour(float3 position, float3 direction, float3 normal, float ior, int channel, int bounces)
{
    float3 inside = refract(direction, normal, 1.0 / ior);
    float result = 0.0;
    float carried = 1.0;
    float3 origin = position;
    [loop]
    for (int b = 0; b < bounces; b++)
    {
        float3 exitNormal;
        float t = leaveDiamond(origin + inside * 1e-4, inside, exitNormal);
        origin = origin + inside * t;
        float3 outside = refract(inside, -exitNormal, ior);
        if (dot(outside, outside) > 0.0)
        {
            // Some escapes here; the rest reflects back inside.
            float cosine = saturate(dot(outside, exitNormal));
            float f0 = (ior - 1.0) / (ior + 1.0);
            f0 *= f0;
            float fresnel = f0 + (1.0 - f0) * pow(1.0 - cosine, 5.0);
            float3 seen = surroundings(origin, outside);
            float3 pick = channel == 0 ? float3(1.0, 0.0, 0.0) : (channel == 1 ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, 1.0));
            result += carried * (1.0 - fresnel) * dot(seen, pick);
            carried *= fresnel;
        }
        inside = reflect(inside, -exitNormal);
        if (carried < 0.02)
        {
            break;
        }
    }
    return result;
}

float3 Render(float2 uv, float2 p)
{
    Turn = Time * 0.22 + sin(Time * 0.13) * 0.3;
    Tilt = 0.18 + sin(Time * 0.09) * 0.12;

    float drift = sin(Time * 0.04) * 0.5;
    float3 eye = float3(sin(drift) * 3.4, 1.9 + sin(Time * 0.07) * 0.25, -cos(drift) * 3.4);
    float3 target = float3(0.0, -0.15, 0.0);
    const float zoom = 2.9;
    float3 worldDirection = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.0);

    // Work in the stone's own frame, where its facets are fixed.
    float3 origin = toStone(eye);
    float3 direction = toStone(worldDirection);

    // Most of the picture is velvet and lamps: a ray that misses the stone's bounding sphere
    // skips the facets.
    float closest = dot(origin, direction);
    if (dot(origin, origin) - closest * closest > StoneRadius * StoneRadius)
    {
        return surroundings(origin, direction);
    }

    float3 normal;
    float t = enterDiamond(origin, direction, normal);
    if (t < 0.0)
    {
        return surroundings(origin, direction);
    }

    float3 position = origin + direction * t;
    // The bright mirror flash off the outside of each facet.
    const float outerIor = 2.42;
    float f0 = (outerIor - 1.0) / (outerIor + 1.0);
    f0 *= f0;
    float fresnel = f0 + (1.0 - f0) * pow(1.0 - saturate(dot(normal, -direction)), 5.0);
    float3 colour = surroundings(position, reflect(direction, normal)) * fresnel;

    // Then light inside, each colour bent by a slightly different amount: that spread is the fire.
    // Real diamond spreads colours a little; this one is pushed further so the fire reads on screen.
    int bounces = qualitySteps(5);
    float3 inside;
    inside.r = traceColour(position, direction, normal, 2.32, 0, bounces);
    inside.g = traceColour(position, direction, normal, 2.42, 1, bounces);
    inside.b = traceColour(position, direction, normal, 2.55, 2, bounces);
    colour += inside * (1.0 - fresnel);

    return colour;
}
