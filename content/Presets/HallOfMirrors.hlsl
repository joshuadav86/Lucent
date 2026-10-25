// Name: Hall of Mirrors
// Inside a triangle of mirrors, a chrome ring and glowing orbs repeat into an endless neon lattice, ray traced bounce after bounce; the orbs shine with the spectrum and the edges flash on the beat.
// Family: Ray traced
// Tags: 3D, Neon, Geometric, Intense
// Added: 2026-10-17 02:07

static const float RoomInradius = 2.2;
static const float RoomHeight = 3.0;
static const int OrbCount = 3;
static const float2 RingSize = float2(0.62, 0.15);   // ring radius, tube radius
static const float3 RingCentre = float3(0.0, 1.45, 0.0);

// Materials.
static const int Mirror = 0;
static const int Floor = 1;
static const int Ceiling = 2;
static const int Ring = 3;
static const int Orb = 4;
static const int Neon = 5;

struct Hit
{
    float distance;
    float3 normal;
    int material;
    int index;
};

// Outward normal of mirror wall i (the walls are upright, so these lie flat).
float2 wallNormal(int i)
{
    float angle = (90.0 + 120.0 * i) * PI / 180.0 + 0.3;
    return float2(cos(angle), sin(angle));
}

// Corner i of the triangle, where two mirrors meet.
float2 cornerOf(int i)
{
    float angle = (30.0 + 120.0 * i) * PI / 180.0 + 0.3;
    return float2(cos(angle), sin(angle)) * RoomInradius * 2.0;
}

// Bass, mids and treble: one orb each.
float orbLevel(int i)
{
    return i == 0 ? Bass : (i == 1 ? Mid : Treble);
}

float4 orbAt(int i)
{
    float k = i;
    float level = orbLevel(i);
    float angle = Time * (0.35 + 0.05 * k) * (i % 2 == 0 ? 1.0 : -1.0) + k * 2.0 * PI / OrbCount;
    float radius = 0.95 + 0.12 * sin(Time * 0.4 + k);
    float height = RingCentre.y + sin(Time * 0.6 + k * 1.7) * 0.7;
    return float4(cos(angle) * radius, height, sin(angle) * radius, 0.09 + level * 0.06);
}

float3 orbColour(int i)
{
    return hsv2rgb(i / (float)OrbCount + Time * 0.03, 0.75, 1.0);
}

// The ring tumbles slowly; the bass pushes it round faster.
float3x3 ringRotation()
{
    float a = Time * 0.31 + Bass * 0.6;
    float b = Time * 0.23;
    float sa, ca, sb, cb;
    sincos(a, sa, ca);
    sincos(b, sb, cb);
    float3x3 spinX = float3x3(1.0, 0.0, 0.0, 0.0, ca, -sa, 0.0, sa, ca);
    float3x3 spinY = float3x3(cb, 0.0, sb, 0.0, 1.0, 0.0, -sb, 0.0, cb);
    return mul(spinY, spinX);
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
    float t = -b - sqrt(h);
    return t > 1e-3 ? t : -1.0;
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
    // Skip quickly if the ray misses the bounding sphere.
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
    // Keep the cubic well conditioned.
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

Hit traceScene(float3 origin, float3 direction, float4 orbs[OrbCount], float3x3 rotation)
{
    Hit hit;
    hit.distance = 1e5;
    hit.normal = float3(0.0, 1.0, 0.0);
    hit.material = Mirror;
    hit.index = 0;

    // Floor and ceiling.
    if (direction.y < 0.0)
    {
        hit.distance = -origin.y / direction.y;
        hit.material = Floor;
    }
    else
    {
        hit.distance = (RoomHeight - origin.y) / max(direction.y, 1e-5);
        hit.normal = float3(0.0, -1.0, 0.0);
        hit.material = Ceiling;
    }

    // The three mirrors, seen from inside.
    [unroll]
    for (int w = 0; w < 3; w++)
    {
        float2 n = wallNormal(w);
        float along = dot(n, direction.xz);
        if (along > 1e-5)
        {
            float t = (RoomInradius - dot(n, origin.xz)) / along;
            if (t > 1e-4 && t < hit.distance)
            {
                hit.distance = t;
                hit.normal = float3(-n.x, 0.0, -n.y);
                hit.material = Mirror;
            }
        }
    }

    // Neon tubes up the corners where the mirrors meet.
    [unroll]
    for (int c = 0; c < 3; c++)
    {
        float2 oc = origin.xz - cornerOf(c);
        float a = dot(direction.xz, direction.xz);
        float b = dot(oc, direction.xz);
        float h = b * b - a * (dot(oc, oc) - 0.0036);
        if (h > 0.0)
        {
            float t = (-b - sqrt(h)) / a;
            if (t > 1e-4 && t < hit.distance)
            {
                hit.distance = t;
                hit.material = Neon;
                hit.index = c;
            }
        }
    }

    [unroll]
    for (int i = 0; i < OrbCount; i++)
    {
        float t = sphereHit(origin, direction, orbs[i]);
        if (t > 0.0 && t < hit.distance)
        {
            hit.distance = t;
            hit.normal = normalize(origin + direction * t - orbs[i].xyz);
            hit.material = Orb;
            hit.index = i;
        }
    }

    float3 localOrigin = mul(origin - RingCentre, rotation);
    float3 localDirection = mul(direction, rotation);
    float t = torusHit(localOrigin, localDirection, RingSize);
    if (t > 0.0 && t < hit.distance)
    {
        hit.distance = t;
        hit.normal = mul(rotation, torusNormal(localOrigin + localDirection * t, RingSize));
        hit.material = Ring;
    }
    return hit;
}

// Light from the orbs on a surface: each orb is a small coloured lamp.
float3 orbLight(float3 position, float3 normal, float4 orbs[OrbCount])
{
    float3 light = 0.0;
    [unroll]
    for (int i = 0; i < OrbCount; i++)
    {
        float3 toOrb = orbs[i].xyz - position;
        float d2 = dot(toOrb, toOrb);
        float level = orbLevel(i);
        light += orbColour(i) * saturate(dot(normal, toOrb * rsqrt(d2))) * (0.3 + level * 1.6) / (0.3 + d2);
    }
    return light;
}

float3 neonColour(int corner)
{
    float pulse = 0.6 + Beat * 1.8 + Treble * 0.8;
    return neon(corner / 3.0 + Time * 0.02) * pulse * 2.2;
}

float3 Render(float2 uv, float2 p)
{
    float4 orbs[OrbCount];
    [unroll]
    for (int b = 0; b < OrbCount; b++)
    {
        orbs[b] = orbAt(b);
    }
    float3x3 rotation = ringRotation();

    float orbit = Time * 0.05;
    float3 eye = float3(cos(orbit) * 1.85, 1.2 + 0.4 * sin(Time * 0.13), sin(orbit) * 1.85);
    float3 target = RingCentre + float3(sin(Time * 0.07) * 0.6, sin(Time * 0.09) * 0.3, 0.0);
    float3 origin = eye;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 1.2, sin(Time * 0.05) * 0.15);

    float3 colour = 0.0;
    float3 throughput = 1.0;
    float travelled = 0.0;
    // Each bounce off a mirror shows one more room deeper into the lattice.
    int bounces = qualitySteps(9);
    [loop]
    for (int depth = 0; depth < bounces; depth++)
    {
        Hit hit = traceScene(origin, direction, orbs, rotation);
        float3 position = origin + direction * hit.distance;
        travelled += hit.distance;
        float facing = saturate(dot(hit.normal, -direction));
        // The far rooms fade into a coloured haze.
        float3 haze = neon(0.6 + Time * 0.02) * 0.02 * (1.0 + Level);
        float fog = exp(-travelled * 0.05);

        if (hit.material == Orb)
        {
            // A glowing glass bead: white hot in the middle, coloured towards its edge.
            float level = orbLevel(hit.index);
            float3 glow = orbColour(hit.index) * (1.0 + level * 3.0) * (0.35 + 0.65 * facing * facing)
                        + float3(1.0, 1.0, 1.0) * pow(facing, 6.0) * (0.5 + level * 2.0);
            colour += throughput * lerp(haze, glow, fog);
            throughput = 0.0;
            break;
        }
        if (hit.material == Neon)
        {
            colour += throughput * lerp(haze, neonColour(hit.index), fog);
            throughput = 0.0;
            break;
        }

        float3 normal = hit.normal;
        if (hit.material == Mirror)
        {
            // Silvered glass: slightly blue, and a little darker each time.
            throughput *= float3(0.86, 0.9, 0.95);
        }
        else if (hit.material == Ring)
        {
            float3 gold = float3(1.0, 0.78, 0.45);
            // Polished brass: a mirror with a warm glow of its own from the lights above and the orbs.
            colour += throughput * gold * (orbLight(position, normal, orbs) * 0.15 + (0.06 + 0.2 * saturate(normal.y)) * (0.6 + Level)) * fog;
            throughput *= lerp(gold, 1.0, pow(1.0 - facing, 5.0)) * 0.85;
        }
        else
        {
            // Floor and ceiling: dark polished tiles in a triangular pattern, lit by the orbs.
            float2 q = position.xz * 1.6;
            float2 skew = float2(q.x + q.y * 0.57735, q.y * 1.1547);
            float3 lines = abs(frac(float3(skew.x, skew.y, skew.x - skew.y) + 0.5) - 0.5);
            float pattern = hash(floor(skew) + step(frac(skew.x - skew.y), 0.5) * 7.0);
            // Thin lines blur out with distance rather than shimmering.
            float blur = 0.01 + travelled * 0.004;
            float grout = smoothstep(0.025 + blur, 0.01, min(lines.x, min(lines.y, lines.z))) * 0.02 / blur;
            float3 albedo = (hit.material == Floor ? float3(0.1, 0.1, 0.13) : float3(0.03, 0.03, 0.04)) * (0.7 + pattern * 0.3);
            float3 lit = albedo * (orbLight(position, normal, orbs) + (hit.material == Floor ? 0.5 : 0.1));
            if (hit.material == Ceiling)
            {
                // A triangular light panel in the middle of the ceiling.
                float panel = -1.0;
                [unroll]
                for (int w = 0; w < 3; w++)
                {
                    panel = max(panel, dot(wallNormal(w), position.xz) - 0.9);
                }
                // Its rim is a neon outline; the middle glows faintly.
                float rim = smoothstep(0.035, 0.0, abs(panel));
                lit += neon(0.15 + Time * 0.02) * rim * (1.2 + Beat * 1.5) + float3(0.3, 0.28, 0.45) * step(panel, 0.0) * (0.12 + Level * 0.2);
            }
            lit += neon(0.3 + length(position.xz) * 0.1 + Time * 0.05) * grout * 0.08 * (0.5 + Level);
            colour += throughput * lerp(haze, lit, fog);
            float fresnel = 0.1 + 0.9 * pow(1.0 - facing, 5.0);
            throughput *= fresnel * 0.7;
        }

        direction = reflect(direction, normal);
        origin = position + normal * 1e-3;
        if (max(throughput.x, max(throughput.y, throughput.z)) < 0.03)
        {
            break;
        }
    }
    // Whatever light is still bouncing when we stop is lost in the haze of the far rooms.
    colour += throughput * neon(0.6 + Time * 0.02) * 0.02 * (1.0 + Level);
    return colour;
}
