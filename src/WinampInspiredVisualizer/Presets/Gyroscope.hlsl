// Name: Gyroscope
// Nested chrome, gold and copper rings spin round a glowing glass heart above a black mirror, ray traced with reflections, refraction and soft shadows; each ring's light band follows the bass, mids or treble.
// Family: Ray traced
// Tags: 3D, Geometric, Dark
// Added: 2026-10-17 02:07

static const int RingCount = 3;
static const float3 Centre = float3(0.0, 1.6, 0.0);
static const float CoreRadius = 0.36;
static const float HeartRadius = 0.14;

// Materials.
static const int Studio = 0;
static const int Floor = 1;
static const int Metal = 2;
static const int Glass = 3;
static const int Heart = 4;

struct Hit
{
    float distance;
    float3 normal;
    int material;
    int ring;
    float band;   // how close the hit is to the ring's glowing band, 1 on it
};

float2 ringSize(int i)
{
    return i == 0 ? float2(1.2, 0.1) : (i == 1 ? float2(0.9, 0.09) : float2(0.62, 0.08));
}

float3 ringTint(int i)
{
    return i == 0 ? float3(0.92, 0.93, 0.96) : (i == 1 ? float3(1.0, 0.76, 0.36) : float3(0.95, 0.55, 0.42));
}

float ringLevel(int i)
{
    return i == 0 ? Bass : (i == 1 ? Mid : Treble);
}

static const float3 RingGlows[RingCount] = { float3(1.0, 0.2, 0.5), float3(0.2, 0.9, 1.0), float3(0.7, 0.4, 1.0) };

float3 ringGlow(int i)
{
    return RingGlows[i];
}

float3x3 rotationX(float a)
{
    float s, c;
    sincos(a, s, c);
    return float3x3(1.0, 0.0, 0.0, 0.0, c, -s, 0.0, s, c);
}

float3x3 rotationY(float a)
{
    float s, c;
    sincos(a, s, c);
    return float3x3(c, 0.0, s, 0.0, 1.0, 0.0, -s, 0.0, c);
}

// Each ring turns inside the one around it, like the gimbals of a real gyroscope.
// The music nudges each ring on through its own part of the spectrum.
void gimbals(out float3x3 frames[RingCount])
{
    float3x3 outer = mul(rotationY(Time * 0.21 + Bass * 0.5), rotationX(PI * 0.5));
    float3x3 middle = mul(outer, mul(rotationY(Time * 0.37 + Mid * 0.7), rotationX(PI * 0.5)));
    float3x3 inner = mul(middle, mul(rotationY(-Time * 0.53 - Treble * 0.9), rotationX(PI * 0.5)));
    frames[0] = outer;
    frames[1] = middle;
    frames[2] = inner;
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
    h = sqrt(h);
    float t = -b - h;
    if (t < 1e-3)
    {
        t = -b + h;
    }
    return t > 1e-3 ? t : -1.0;
}

float sphereShadow(float3 origin, float3 direction, float3 centre, float radius, float softness)
{
    float3 oc = origin - centre;
    float b = dot(oc, direction);
    float c = dot(oc, oc) - radius * radius;
    float h = b * b - c;
    float d = sqrt(max(0.0, radius * radius - h)) - radius;
    float t = -b - sqrt(max(h, 0.0));
    return (t < 0.0) ? 1.0 : smoothstep(0.0, 1.0, 2.5 * softness * d / t);
}

// Ray against a torus lying in the xy plane round the origin, solved exactly as a quartic.
// size.x is the ring radius and size.y the tube radius.
float torusHit(float3 origin, float3 direction, float2 size)
{
    float po = 1.0;
    float outer2 = size.x * size.x;
    float inner2 = size.y * size.y;
    float m = dot(origin, origin);
    float n = dot(origin, direction);
    float bound = n * n - m + (size.x + size.y) * (size.x + size.y);
    if (bound < 0.0)
    {
        return -1.0;
    }
    float k = (m - inner2 - outer2) * 0.5;
    float k3 = n;
    float k2 = n * n + outer2 * direction.z * direction.z + k;
    float k1 = k * n + outer2 * origin.z * direction.z;
    float k0 = k * k + outer2 * origin.z * origin.z - outer2 * inner2;
    if (abs(k3 * (k3 * k3 - k2) + k1) < 0.01)
    {
        po = -1.0;
        float swap = k1;
        k1 = k3;
        k3 = swap;
        k0 = 1.0 / k0;
        k1 = k1 * k0;
        k2 = k2 * k0;
        k3 = k3 * k0;
    }
    float c2 = 2.0 * k2 - 3.0 * k3 * k3;
    float c1 = k3 * (k3 * k3 - k2) + k1;
    float c0 = k3 * (k3 * (-3.0 * k3 * k3 + 4.0 * k2) - 8.0 * k1) + 4.0 * k0;
    c2 /= 3.0;
    c1 *= 2.0;
    c0 /= 3.0;
    float q = c2 * c2 + c0;
    float r = 3.0 * c0 * c2 - c2 * c2 * c2 - c1 * c1;
    float h = r * r - q * q * q;
    float z;
    if (h < 0.0)
    {
        float sq = sqrt(q);
        z = 2.0 * sq * cos(acos(clamp(r / (sq * q), -1.0, 1.0)) / 3.0);
    }
    else
    {
        float sq = pow(sqrt(h) + abs(r), 1.0 / 3.0);
        z = sign(r) * abs(sq + q / sq);
    }
    z = c2 - z;
    float d1 = z - 3.0 * c2;
    float d2 = z * z - 3.0 * c0;
    if (abs(d1) < 1e-4)
    {
        if (d2 < 0.0)
        {
            return -1.0;
        }
        d2 = sqrt(d2);
    }
    else
    {
        if (d1 < 0.0)
        {
            return -1.0;
        }
        d1 = sqrt(d1 * 0.5);
        d2 = c1 / d1;
    }
    float result = 1e20;
    h = d1 * d1 - z + d2;
    if (h > 0.0)
    {
        h = sqrt(h);
        float t1 = -d1 - h - k3;
        float t2 = -d1 + h - k3;
        t1 = po < 0.0 ? 2.0 / t1 : t1;
        t2 = po < 0.0 ? 2.0 / t2 : t2;
        if (t1 > 0.0) result = t1;
        if (t2 > 0.0) result = min(result, t2);
    }
    h = d1 * d1 - z - d2;
    if (h > 0.0)
    {
        h = sqrt(h);
        float t1 = d1 - h - k3;
        float t2 = d1 + h - k3;
        t1 = po < 0.0 ? 2.0 / t1 : t1;
        t2 = po < 0.0 ? 2.0 / t2 : t2;
        if (t1 > 0.0) result = min(result, t1);
        if (t2 > 0.0) result = min(result, t2);
    }
    return result < 1e19 ? result : -1.0;
}

float3 torusNormal(float3 position, float2 size)
{
    return normalize(position * (dot(position, position) - size.y * size.y - size.x * size.x * float3(1.0, 1.0, -1.0)));
}

Hit traceScene(float3 origin, float3 direction, float3x3 frames[RingCount], bool insideGlass)
{
    Hit hit;
    hit.distance = 1e5;
    hit.normal = float3(0.0, 1.0, 0.0);
    hit.material = Studio;
    hit.ring = 0;
    hit.band = 0.0;

    if (insideGlass)
    {
        // Inside the glass ball the only things to meet are the glowing heart and the ball's own surface.
        float t = sphereHit(origin, direction, Centre, HeartRadius);
        if (t > 0.0)
        {
            hit.distance = t;
            hit.normal = normalize(origin + direction * t - Centre);
            hit.material = Heart;
            return hit;
        }
        t = sphereHit(origin, direction, Centre, CoreRadius);
        hit.distance = max(t, 1e-3);
        hit.normal = normalize(origin + direction * hit.distance - Centre);
        hit.material = Glass;
        return hit;
    }

    if (direction.y < 0.0)
    {
        hit.distance = -origin.y / direction.y;
        hit.material = Floor;
    }

    float t = sphereHit(origin, direction, Centre, CoreRadius);
    if (t > 0.0 && t < hit.distance)
    {
        hit.distance = t;
        hit.normal = normalize(origin + direction * t - Centre);
        hit.material = Glass;
    }

    [unroll]
    for (int i = 0; i < RingCount; i++)
    {
        float3 localOrigin = mul(origin - Centre, frames[i]);
        float3 localDirection = mul(direction, frames[i]);
        float2 size = ringSize(i);
        t = torusHit(localOrigin, localDirection, size);
        if (t > 0.0 && t < hit.distance)
        {
            float3 local = localOrigin + localDirection * t;
            hit.distance = t;
            hit.normal = mul(frames[i], torusNormal(local, size));
            hit.material = Metal;
            hit.ring = i;
            // The glowing band runs round the outside edge of each ring.
            float outward = length(local.xy) - size.x;
            hit.band = smoothstep(0.5, 0.95, outward / size.y) * smoothstep(0.012, 0.0, abs(local.z) - size.y * 0.12);
        }
    }
    return hit;
}

// The dark studio: a big soft light overhead and two tall coloured strips either side.
float3 studio(float3 direction)
{
    float3 colour = float3(0.006, 0.006, 0.01) + float3(0.03, 0.025, 0.05) * saturate(direction.y);
    float overhead = smoothstep(0.7, 0.76, direction.y);
    colour += float3(1.0, 0.96, 0.9) * overhead * 2.5;
    // Big dim panels round the walls, so the metal always has something to reflect.
    float around = atan2(direction.z, direction.x);
    float panels = smoothstep(0.3, 0.25, abs(frac(around / (2.0 * PI) * 6.0) - 0.5)) * smoothstep(0.05, 0.15, direction.y) * smoothstep(0.55, 0.45, direction.y);
    colour += float3(0.16, 0.15, 0.2) * panels;
    float stripA = smoothstep(0.035, 0.015, abs(wrap(around - 0.6, 2.0 * PI) - PI)) * smoothstep(0.1, 0.2, direction.y) * smoothstep(0.65, 0.55, direction.y);
    float stripB = smoothstep(0.035, 0.015, abs(wrap(around + 2.4, 2.0 * PI) - PI)) * smoothstep(0.1, 0.2, direction.y) * smoothstep(0.65, 0.55, direction.y);
    colour += float3(1.0, 0.3, 0.6) * stripA * (1.2 + Mid * 3.0) + float3(0.3, 0.7, 1.0) * stripB * (1.2 + Treble * 3.0);
    // A thin horizon line of light, far away.
    colour += float3(0.4, 0.45, 0.7) * exp(-abs(direction.y) * 60.0) * 0.4;
    return colour;
}

float3 heartColour()
{
    return lerp(float3(1.0, 0.45, 0.15), float3(1.0, 0.85, 0.55), Beat) * (2.0 + Bass * 5.0 + Beat * 3.0);
}

// Light reaching the floor: the overhead panel, with soft shadows from the glass ball and the rings
// (the rings are tested exactly, so their shadows spin across the floor), plus the heart's warm glow.
float3 floorLight(float3 position, float3x3 frames[RingCount])
{
    float3 up = normalize(float3(0.15, 1.0, 0.1));
    float shadow = sphereShadow(position, up, Centre, CoreRadius, 6.0) * 0.5 + 0.5;
    // Two shadow rays to opposite edges of the light panel give the ring shadows soft edges.
    float rings = 0.0;
    [unroll]
    for (int edge = 0; edge < 2; edge++)
    {
        float3 toLight = normalize(up + (edge == 0 ? float3(0.07, 0.0, 0.05) : float3(-0.07, 0.0, -0.05)));
        float lit = 1.0;
        [unroll]
        for (int i = 0; i < RingCount; i++)
        {
            float t = torusHit(mul(position - Centre, frames[i]), mul(toLight, frames[i]), ringSize(i));
            lit *= t > 0.0 ? 0.3 : 1.0;
        }
        rings += lit * 0.5;
    }
    shadow *= rings;
    float3 toHeart = Centre - position;
    float3 warm = heartColour() * 0.05 / dot(toHeart, toHeart);
    return float3(1.0, 0.96, 0.9) * 0.55 * shadow + warm;
}

float3 trace(float3 origin, float3 direction, float3x3 frames[RingCount])
{
    float3 colour = 0.0;
    float3 throughput = 1.0;
    bool insideGlass = false;
    int bounces = qualitySteps(6);
    [loop]
    for (int depth = 0; depth < bounces; depth++)
    {
        Hit hit = traceScene(origin, direction, frames, insideGlass);
        float3 position = origin + direction * hit.distance;
        float3 normal = hit.normal;
        float facing = saturate(dot(normal, -direction));

        if (hit.material == Studio)
        {
            // Seen directly, the studio lights stay dim so they don't upstage the gyroscope.
            colour += throughput * studio(direction) * (depth == 0 ? 0.25 : 1.0);
            throughput = 0.0;
            break;
        }
        if (hit.material == Heart)
        {
            colour += throughput * heartColour() * (0.5 + 0.5 * facing);
            throughput = 0.0;
            break;
        }
        if (hit.material == Floor)
        {
            // Black mirror floor with faint rings etched into it.
            float rings = smoothstep(0.03, 0.0, abs(frac(length(position.xz) * 1.5) - 0.5) - 0.47);
            float3 albedo = float3(0.05, 0.05, 0.06) + rings * 0.04;
            float fresnel = 0.06 + 0.94 * pow(1.0 - facing, 5.0);
            colour += throughput * albedo * floorLight(position, frames);
            throughput *= (fresnel + 0.2) * exp(-max(length(position.xz) - 4.0, 0.0) * 0.4);
            direction = reflect(direction, normal);
            origin = position + normal * 1e-3;
        }
        else if (hit.material == Metal)
        {
            float level = ringLevel(hit.ring);
            colour += throughput * ringGlow(hit.ring) * hit.band * (0.3 + level * 3.0);
            throughput *= lerp(ringTint(hit.ring), 1.0, pow(1.0 - facing, 5.0)) * (1.0 - hit.band * 0.8);
            direction = reflect(direction, normal);
            origin = position + normal * 1e-3;
        }
        else
        {
            // The glass ball: refract in and out, adding the faint mirror image on the outside.
            float3 n = insideGlass ? -normal : normal;
            float eta = insideGlass ? 1.5 : 1.0 / 1.5;
            float cosine = saturate(dot(n, -direction));
            float fresnel = 0.04 + 0.96 * pow(1.0 - cosine, 5.0);
            float3 refracted = refract(direction, n, eta);
            if (dot(refracted, refracted) < 0.5)
            {
                direction = reflect(direction, n);
                origin = position + n * 1e-3;
            }
            else
            {
                if (!insideGlass)
                {
                    colour += throughput * fresnel * studio(reflect(direction, n));
                    throughput *= 1.0 - fresnel;
                }
                direction = refracted;
                origin = position - n * 1e-3;
                insideGlass = !insideGlass;
            }
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
    float3x3 frames[RingCount];
    gimbals(frames);

    float orbit = Time * 0.06;
    float3 eye = float3(sin(orbit) * 4.3, 2.0 + 0.5 * sin(Time * 0.1), cos(orbit) * 4.3);
    float3 target = Centre - float3(0.0, 0.2, 0.0);
    const float zoom = 1.9;

    int samples = Quality > 1.5 ? 2 : 1;
    float3 colour = 0.0;
    [loop]
    for (int s = 0; s < samples; s++)
    {
        float2 jitter = samples > 1 ? SampleOffsets[s] * pixelSize() : 0.0;
        colour += trace(eye, cameraRay(eye, target, float2(p.x, -p.y) + jitter, zoom, 0.0), frames);
    }
    colour /= samples;

    // The heart's glow in the air around it.
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.0);
    float3 toHeart = Centre - eye;
    float off = length(toHeart - direction * dot(toHeart, direction));
    colour += heartColour() * exp(-off * 6.0) * 0.04;
    return colour;
}
