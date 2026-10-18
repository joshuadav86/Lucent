// Name: Glass and Chrome
// Mirror, gold and glass spheres bounce to the beat on a polished floor, ray traced with real reflections, refraction and soft shadows, ringed by strip lights that rise with the music.
// Family: Ray traced
// Tags: 3D, Geometric, Colourful
// Added: 2026-10-17 02:07

static const int BallCount = 7;
static const float RingRadius = 9.0;
static const float CeilingHeight = 6.0;
static const float StripCount = 28.0;

// Materials.
static const int Floor = 0;
static const int Chrome = 1;
static const int Gold = 2;
static const int Glass = 3;
static const int Lacquer = 4;
static const int Lights = 5;

struct Hit
{
    float distance;
    float3 normal;
    int material;
    int ball;
};

// Seconds since the last beat (Beat fades as e^-5t).
float sinceBeat()
{
    return -log(max(Beat, 1e-4)) / 5.0;
}

// Height of a ball thrown up on the beat: one big arc and a small rebound, landing before the next beat.
float bounce(float age, float height)
{
    const float firstArc = 0.3;
    const float secondArc = 0.09;
    float h = 0.0;
    if (age > 0.0 && age < firstArc)
    {
        float f = age / firstArc;
        h = height * 4.0 * f * (1.0 - f);
    }
    else if (age >= firstArc && age < firstArc + secondArc)
    {
        float f = (age - firstArc) / secondArc;
        h = height * 0.09 * 4.0 * f * (1.0 - f);
    }
    return h;
}

// Chrome, glass, gold, glass, lacquer, glass, chrome round the ring.
static const int BallMaterials[BallCount] = { Chrome, Glass, Gold, Glass, Lacquer, Glass, Chrome };

int ballMaterial(int i)
{
    return BallMaterials[i];
}

static const float3 BallTints[BallCount] = {
    float3(0.95, 0.95, 1.0), float3(0.55, 0.85, 1.0), float3(1.0, 0.72, 0.32), float3(1.0, 0.55, 0.85),
    float3(0.9, 0.08, 0.2), float3(0.6, 1.0, 0.7), float3(0.85, 0.9, 1.0) };

float3 ballTint(int i)
{
    return BallTints[i];
}

// Where ball i is (xyz) and its radius (w).
float4 ballAt(int i, float age)
{
    float k = i;
    float angle = k / BallCount * 2.0 * PI + Time * 0.12;
    float radius = 0.48 + 0.12 * frac(k * 0.618);
    float ring = 2.1 + 0.25 * sin(Time * 0.3 + k * 1.3);
    // A wave of bounces runs round the ring on every beat; louder bass throws them higher.
    float height = (0.55 + 0.5 * frac(k * 0.37 + 0.2)) * (0.35 + Bass * 0.9);
    float lift = bounce(age - k * 0.03, height);
    return float4(cos(angle) * ring, radius + lift, sin(angle) * ring, radius);
}

float sphereHit(float3 origin, float3 direction, float4 sphere)
{
    float3 oc = origin - sphere.xyz;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - sphere.w * sphere.w;
    float h = b * b - c;
    if (h < 0.0)
    {
        return -1.0;
    }
    h = sqrt(h);
    float t = -b - h;
    if (t < 1e-3)
    {
        t = -b + h;
    }
    return t > 1e-3 ? t : -1.0;
}

// Soft shadow from a sphere: how much of a light of angular size softness gets past it.
float sphereShadow(float3 origin, float3 direction, float4 sphere, float softness)
{
    float3 oc = origin - sphere.xyz;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - sphere.w * sphere.w;
    float h = b * b - c;
    float d = sqrt(max(0.0, sphere.w * sphere.w - h)) - sphere.w;
    float t = -b - sqrt(max(h, 0.0));
    return (t < 0.0) ? 1.0 : smoothstep(0.0, 1.0, 2.5 * softness * d / t);
}

// Sky-light blocked by a nearby sphere.
float sphereOcclusion(float3 position, float3 normal, float4 sphere)
{
    float3 r = sphere.xyz - position;
    float l = length(r);
    return saturate(dot(normal, r) * sphere.w * sphere.w / (l * l * l));
}

Hit traceScene(float3 origin, float3 direction, float4 balls[BallCount])
{
    Hit hit;
    hit.distance = 1e5;
    hit.normal = float3(0.0, 1.0, 0.0);
    hit.material = Lights;
    hit.ball = -1;

    // The floor.
    if (direction.y < 0.0)
    {
        float t = -origin.y / direction.y;
        if (t > 1e-3)
        {
            hit.distance = t;
            hit.material = Floor;
        }
    }

    // The ring of strip lights round the room, or the ceiling above it.
    float a = dot(direction.xz, direction.xz);
    float b = dot(origin.xz, direction.xz);
    float c = dot(origin.xz, origin.xz) - RingRadius * RingRadius;
    float wall = (-b + sqrt(max(b * b - a * c, 0.0))) / max(a, 1e-5);
    if (direction.y > 0.0)
    {
        wall = min(wall, (CeilingHeight - origin.y) / direction.y);
    }
    if (wall < hit.distance)
    {
        hit.distance = wall;
        hit.material = Lights;
    }

    [unroll]
    for (int i = 0; i < BallCount; i++)
    {
        float t = sphereHit(origin, direction, balls[i]);
        if (t > 0.0 && t < hit.distance)
        {
            hit.distance = t;
            hit.ball = i;
            hit.material = ballMaterial(i);
            hit.normal = (origin + direction * t - balls[i].xyz) / balls[i].w;
        }
    }
    return hit;
}

// The light the strip lights and ceiling panel give off at a point on the wall or ceiling.
float3 lightsAt(float3 position)
{
    if (position.y > CeilingHeight - 0.01)
    {
        // A big square softbox overhead, and a faint glow round it.
        float2 q = abs(position.xz);
        float panel = smoothstep(2.6, 2.5, max(q.x, q.y));
        return float3(1.0, 0.95, 0.88) * panel * 3.2 + float3(0.06, 0.05, 0.08) * exp(-length(q) * 0.3);
    }
    float around = atan2(position.z, position.x) / (2.0 * PI) + 0.5;
    float strip = floor(around * StripCount);
    float across = frac(around * StripCount);
    float band = abs(strip / StripCount * 2.0 - 1.0);
    float level = spectrum(0.04 + band * 0.8);
    // Each strip is an equaliser bar: it lights up to a height set by its part of the spectrum.
    float top = 0.3 + level * 3.2;
    float lit = smoothstep(0.3, 0.36, across) * smoothstep(0.7, 0.64, across);
    float bar = lit * smoothstep(top + 0.04, top - 0.04, position.y) * step(0.0, position.y);
    float3 colour = palette(strip / StripCount * 0.7 + Time * 0.02) * 0.9 + 0.1;
    float cap = lit * exp(-abs(position.y - top) * 12.0);
    // Light from each bar spills softly up the wall above it.
    float spill = level * 0.1 * exp(-max(position.y - top, 0.0) * 0.5) * (1.0 - abs(across - 0.5));
    return colour * (bar * (0.5 + 1.4 * level) + lit * 0.02 + cap * 0.8 + spill) + float3(0.006, 0.005, 0.012);
}

float3 floorAlbedo(float3 position)
{
    // Dark polished stone tiles with thin pale joints.
    float2 tile = floor(position.xz * 0.8);
    float check = wrap(tile.x + tile.y, 2.0);
    float vein = fbm(position.xz * 1.3 + check * 7.0);
    float3 stone = lerp(float3(0.012, 0.012, 0.016), float3(0.2, 0.19, 0.18), check) * (0.6 + vein * 0.8);
    float2 joint = abs(frac(position.xz * 0.8) - 0.5);
    float grout = smoothstep(0.48, 0.495, max(joint.x, joint.y));
    return lerp(stone, float3(0.06, 0.06, 0.06), grout);
}

static const float LightSoftness[3] = { 3.0, 10.0, 10.0 };

// Direct light at a point on a diffuse surface: the overhead softbox and two coloured lights circling the room.
float3 directLight(float3 position, float3 normal, float3 view, float shine, float4 balls[BallCount])
{
    float3 colour = 0.0;
    float circle = Time * 0.35;
    float3 lightPositions[3] = {
        float3(0.0, CeilingHeight - 0.1, 0.0),
        float3(cos(circle) * 5.0, 2.2, sin(circle) * 5.0),
        float3(cos(circle + PI) * 5.0, 2.2, sin(circle + PI) * 5.0) };
    float3 lightColours[3] = {
        float3(1.0, 0.94, 0.86) * 1.15,
        float3(1.0, 0.25, 0.7) * (0.25 + Mid * 0.9),
        float3(0.2, 0.6, 1.0) * (0.25 + Treble * 1.1) };

    [unroll]
    for (int l = 0; l < 3; l++)
    {
        float3 toLight = lightPositions[l] - position;
        float distance = length(toLight);
        float3 direction = toLight / distance;
        float diffuse = saturate(dot(normal, direction));
        float3 halfway = normalize(direction - view);
        float specular = pow(saturate(dot(normal, halfway)), shine) * shine * 0.04;
        float shadow = 1.0;
        float3 caustic = 0.0;
        [unroll]
        for (int i = 0; i < BallCount; i++)
        {
            float s = sphereShadow(position, direction, balls[i], LightSoftness[l]);
            if (ballMaterial(i) == Glass)
            {
                // Glass lets most light through and focuses it into a bright spot in its shadow.
                float3 centre = balls[i].xyz - position;
                float along = dot(centre, direction);
                float off = length(centre - direction * along) / balls[i].w;
                caustic += ballTint(i) * exp(-off * off * 18.0) * step(0.0, along) * 2.5;
                s = lerp(1.0, s, 0.55);
            }
            shadow *= s;
        }
        float falloff = l == 0 ? 1.0 : 6.0 / (1.0 + distance * distance * 0.25);
        colour += lightColours[l] * falloff * (diffuse * (shadow + caustic) + specular * shadow);
    }
    return colour;
}

float3 trace(float3 origin, float3 direction, float4 balls[BallCount])
{
    float3 colour = 0.0;
    float3 throughput = 1.0;
    int bounces = qualitySteps(5);
    [loop]
    for (int depth = 0; depth < bounces; depth++)
    {
        Hit hit = traceScene(origin, direction, balls);
        float3 position = origin + direction * hit.distance;

        if (hit.material == Lights)
        {
            colour += throughput * lightsAt(position);
            break;
        }

        float3 normal = hit.normal;
        float facing = saturate(dot(normal, -direction));

        if (hit.material == Floor)
        {
            float3 albedo = floorAlbedo(position);
            float occlusion = 1.0;
            [unroll]
            for (int i = 0; i < BallCount; i++)
            {
                occlusion *= 1.0 - sphereOcclusion(position, normal, balls[i]);
            }
            float fresnel = 0.06 + 0.94 * pow(1.0 - facing, 5.0);
            float3 lit = albedo * (directLight(position, normal, direction, 60.0, balls) + 0.04 * occlusion);
            colour += throughput * lit * (1.0 - fresnel);
            // Polished stone: keep following the reflection, dimmed, and fading into the distance.
            throughput *= fresnel * 0.9 + 0.12 * (1.0 - fresnel);
            throughput *= exp(-hit.distance * 0.03);
            direction = reflect(direction, normal);
            origin = position + normal * 1e-3;
        }
        else if (hit.material == Glass)
        {
            float3 tint = ballTint(hit.ball);
            bool inside = dot(direction, normal) > 0.0;
            float3 n = inside ? -normal : normal;
            float eta = inside ? 1.5 : 1.0 / 1.5;
            float cosine = saturate(dot(n, -direction));
            float fresnel = 0.04 + 0.96 * pow(1.0 - cosine, 5.0);
            float3 refracted = refract(direction, n, eta);
            if (inside)
            {
                // Coloured glass absorbs more the further light travels through it.
                throughput *= pow(tint, hit.distance * 0.7);
            }
            if (dot(refracted, refracted) < 0.5)
            {
                direction = reflect(direction, n);
                origin = position + n * 1e-3;
            }
            else
            {
                // The faint mirror image on the outside of the glass: one more ray, straight to whatever it sees.
                if (!inside)
                {
                    float3 mirrored = reflect(direction, n);
                    Hit glance = traceScene(position + n * 1e-3, mirrored, balls);
                    float3 seen = glance.material == Lights
                        ? lightsAt(position + mirrored * glance.distance)
                        : (glance.material == Floor ? float3(0.03, 0.03, 0.04) : ballTint(max(glance.ball, 0)) * 0.15);
                    colour += throughput * fresnel * seen;
                    throughput *= 1.0 - fresnel;
                }
                direction = refracted;
                origin = position - n * 1e-3;
            }
        }
        else
        {
            // Metals and lacquer: a mirror tinted by the metal, with a rim brightened by the treble.
            float3 tint = ballTint(hit.ball);
            if (hit.material == Lacquer)
            {
                float fresnel = 0.05 + 0.95 * pow(1.0 - facing, 5.0);
                float3 lit = tint * (directLight(position, normal, direction, 120.0, balls) + 0.03);
                colour += throughput * lit * (1.0 - fresnel);
                throughput *= fresnel + 0.06;
            }
            else
            {
                float3 edge = lerp(tint, 1.0, pow(1.0 - facing, 5.0));
                throughput *= edge * (hit.material == Gold ? 0.95 : 0.9);
            }
            direction = reflect(direction, normal);
            origin = position + normal * 1e-3;
        }

        if (max(throughput.x, max(throughput.y, throughput.z)) < 0.02)
        {
            break;
        }
    }
    return colour;
}

// Where the two rays per pixel go on High and Ultra, in pixels.
static const float2 SampleOffsets[2] = { float2(0.25, -0.25), float2(-0.25, 0.25) };

float3 Render(float2 uv, float2 p)
{
    float age = sinceBeat();
    float4 balls[BallCount];
    [unroll]
    for (int b = 0; b < BallCount; b++)
    {
        balls[b] = ballAt(b, age);
    }

    float orbit = Time * 0.07;
    float3 eye = float3(sin(orbit) * 5.4, 1.5 + 0.5 * sin(Time * 0.11), cos(orbit) * 5.4);
    float3 target = float3(0.0, 0.7, 0.0);
    const float zoom = 1.6;

    // On High and Ultra, two rays per pixel smooth the edges of the spheres.
    int samples = Quality > 1.5 ? 2 : 1;
    float3 colour = 0.0;
    [loop]
    for (int s = 0; s < samples; s++)
    {
        float2 jitter = samples > 1 ? SampleOffsets[s] * pixelSize() : 0.0;
        colour += trace(eye, cameraRay(eye, target, float2(p.x, -p.y) + jitter, zoom, 0.0), balls);
    }
    colour /= samples;

    // A flash of light round the room on each beat.
    colour *= 1.0 + Beat * 0.15;
    return colour;
}
