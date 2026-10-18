// Name: Pendulum Wave
// A row of chrome balls on fine strings swings in the famous pendulum wave, snaking, splitting into groups and falling back into line, over a glossy floor lit by a strip of coloured lights that follows the music.
// Family: Ray traced
// Tags: Calm, Geometric, 3D
// Added: 2026-10-17 17:06

static const int Balls = 16;
static const float BallRadius = 0.21;
static const float BarHeight = 4.0;
static const float Spacing = 0.48;
// The whole pattern repeats every Cycle seconds: ball i swings Fastest - i... times in that time.
static const float Cycle = 54.0;
static const float Swings = 24.0;

struct Hit
{
    float distance;
    float3 normal;
    int material;   // 0 nothing, 1 floor, 2 chrome, 3 bar, 4 light strip
    int index;
};

float rowZ(int i)
{
    return (i - (Balls - 1) * 0.5) * Spacing;
}

// Each ball's string is shorter than the last, so it swings a little faster: one more swing per cycle.
float stringLength(int i)
{
    float ratio = Swings / (Swings + i);
    return 2.7 * ratio * ratio;
}

float swingAngle(int i)
{
    float amplitude = 0.42 + Bass * 0.08;
    return amplitude * cos(2.0 * PI * (Swings + i) * Time / Cycle);
}

float3 ballCentre(int i)
{
    float angle = swingAngle(i);
    return float3(0.0, BarHeight, rowZ(i)) + stringLength(i) * float3(sin(angle), -cos(angle), 0.0);
}

// The light strip under the bar: one segment above each ball, coloured round the rainbow along the row,
// each as bright as its own band of the spectrum.
float3 stripColour(int i)
{
    float band = spectrum(0.03 + i / (float)Balls * 0.7);
    float hue = i / (float)Balls * 0.8 + Time * 0.01;
    return hsv2rgb(hue, 0.75, 1.0) * (0.25 + band * 2.2 + Beat * 0.15);
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

// Ray against a horizontal rod along z.
float rodHit(float3 origin, float3 direction, float y, float x, float radius, float halfLength, out float3 normal)
{
    float2 o = float2(origin.x - x, origin.y - y);
    float2 d = direction.xy;
    float a = dot(d, d);
    float b = dot(o, d);
    float c = dot(o, o) - radius * radius;
    float h = b * b - a * c;
    normal = float3(0.0, 1.0, 0.0);
    if (h < 0.0 || a < 1e-8)
    {
        return -1.0;
    }
    float t = (-b - sqrt(h)) / a;
    float z = origin.z + direction.z * t;
    if (t < 1e-3 || abs(z) > halfLength)
    {
        return -1.0;
    }
    float2 n = normalize(o + d * t);
    normal = float3(n.x, n.y, 0.0);
    return t;
}

Hit traceScene(float3 origin, float3 direction, float4 spheres[Balls])
{
    Hit hit;
    hit.distance = 1e4;
    hit.normal = float3(0.0, 1.0, 0.0);
    hit.material = 0;
    hit.index = 0;
    if (direction.y < 0.0)
    {
        float t = -origin.y / direction.y;
        if (t > 1e-3)
        {
            hit.distance = t;
            hit.material = 1;
        }
    }
    [unroll]
    for (int i = 0; i < Balls; i++)
    {
        float t = sphereHit(origin, direction, spheres[i]);
        if (t > 0.0 && t < hit.distance)
        {
            hit.distance = t;
            hit.normal = (origin + direction * t - spheres[i].xyz) / BallRadius;
            hit.material = 2;
            hit.index = i;
        }
    }
    float3 rodNormal;
    float halfLength = (Balls * 0.5 + 0.6) * Spacing;
    float t = rodHit(origin, direction, BarHeight + 0.06, 0.0, 0.07, halfLength, rodNormal);
    if (t > 0.0 && t < hit.distance)
    {
        hit.distance = t;
        hit.normal = rodNormal;
        float z = origin.z + direction.z * t;
        // The underside of the bar is the light strip.
        hit.material = rodNormal.y < -0.6 ? 4 : 3;
        hit.index = clamp((int)floor(z / Spacing + Balls * 0.5), 0, Balls - 1);
    }
    return hit;
}

// The studio: a dark backdrop with a soft overhead glow and a band of warm and cool light at the horizon.
float3 backdrop(float3 direction)
{
    float3 colour = float3(0.008, 0.009, 0.013);
    float band = exp(-abs(direction.y - 0.08) * 4.0);
    colour += float3(0.07, 0.09, 0.15) * band * saturate(0.6 + direction.x);
    colour += float3(0.12, 0.07, 0.04) * band * saturate(-0.2 - direction.x);
    // The big overhead softbox, seen in reflections.
    float2 box = abs(float2(direction.x, direction.z) / max(direction.y, 1e-3)) - float2(0.35, 0.9);
    colour += float3(1.0, 0.97, 0.92) * 2.2 * smoothstep(0.03, -0.03, max(box.x, box.y)) * step(0.0, direction.y);
    return colour;
}

static const float3 KeyLight = float3(1.5, 7.0, -2.0);

// Light on a diffuse point: the overhead key with soft ball shadows, plus a pool of coloured light
// from each strip segment above (no shadows needed: they're right overhead).
float3 directLight(float3 position, float3 normal, float4 spheres[Balls])
{
    float3 toKey = KeyLight - position;
    float distance = length(toKey);
    float3 direction = toKey / distance;
    float shadow = 1.0;
    [unroll]
    for (int i = 0; i < Balls; i++)
    {
        shadow *= sphereShadow(position, direction, spheres[i], 5.0);
    }
    float3 colour = float3(1.0, 0.97, 0.92) * 1.6 * saturate(dot(normal, direction)) * shadow * 50.0 / (distance * distance);
    [unroll]
    for (int j = 0; j < Balls; j++)
    {
        float3 strip = float3(0.0, BarHeight, rowZ(j)) - position;
        float d2 = dot(strip, strip);
        colour += stripColour(j) * saturate(dot(normal, strip) * rsqrt(d2)) * 2.4 / d2;
    }
    return colour;
}

float3 trace(float3 origin, float3 direction, float4 spheres[Balls])
{
    float3 colour = 0.0;
    float3 throughput = 1.0;
    int bounces = qualitySteps(3);
    [loop]
    for (int depth = 0; depth < bounces; depth++)
    {
        Hit hit = traceScene(origin, direction, spheres);
        float3 position = origin + direction * hit.distance;
        float facing = saturate(dot(hit.normal, -direction));
        if (hit.material == 0)
        {
            colour += throughput * backdrop(direction);
            break;
        }
        if (hit.material == 4)
        {
            // Each segment glows, with dark gaps between them.
            float z = position.z / Spacing + Balls * 0.5;
            float gap = smoothstep(0.5, 0.38, abs(frac(z) - 0.5));
            colour += throughput * stripColour(hit.index) * 1.1 * gap;
            break;
        }
        float3 normal = hit.normal;
        if (hit.material == 2)
        {
            // Polished chrome, tinted ever so slightly warm.
            throughput *= lerp(float3(0.86, 0.85, 0.83), 1.0, pow(1.0 - facing, 5.0));
        }
        else if (hit.material == 3)
        {
            // Brushed black steel bar.
            colour += throughput * float3(0.03, 0.03, 0.035) * directLight(position, normal, spheres) * 0.3;
            throughput *= 0.15 + 0.85 * pow(1.0 - facing, 5.0);
        }
        else
        {
            // The floor: dark glossy lacquer that fades into the studio in the distance.
            float fog = smoothstep(6.0, 18.0, length(position.xz));
            float fresnel = 0.05 + 0.95 * pow(1.0 - facing, 5.0);
            float3 surface = float3(0.09, 0.09, 0.1) * (directLight(position, normal, spheres) + 0.02) * (1.0 - fresnel);
            colour += throughput * lerp(surface, backdrop(direction), fog);
            throughput *= (fresnel * 0.9 + 0.08) * (1.0 - fog);
        }
        direction = reflect(direction, normal);
        origin = position + normal * 1e-3;
        if (max(throughput.x, max(throughput.y, throughput.z)) < 0.02)
        {
            break;
        }
    }
    return colour;
}

// Distance between a ray and a line segment, and how far along the ray that closest point is.
float2 raySegment(float3 origin, float3 direction, float3 a, float3 b)
{
    float3 ba = b - a;
    float3 oa = origin - a;
    float d = dot(direction, ba);
    float denominator = dot(ba, ba) - d * d;
    float s = saturate((dot(oa, ba) - dot(oa, direction) * d) / max(denominator, 1e-6));
    float3 onSegment = a + ba * s;
    float t = max(dot(onSegment - origin, direction), 0.0);
    return float2(length(origin + direction * t - onSegment), t);
}

float3 Render(float2 uv, float2 p)
{
    float4 spheres[Balls];
    [unroll]
    for (int b = 0; b < Balls; b++)
    {
        spheres[b] = float4(ballCentre(b), BallRadius);
    }

    float orbit = 0.95 + sin(Time * 0.04) * 0.4;
    float3 eye = float3(sin(orbit) * 6.0, 2.3 + sin(Time * 0.06) * 0.4, -cos(orbit) * 6.0);
    float3 target = float3(0.0, 2.35, 0.3);
    const float zoom = 1.6;
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), zoom, 0.0);

    float3 colour = trace(eye, direction, spheres);

    // The fine strings: each ball hangs in a V from two points on the bar, so it can only swing one way.
    Hit first = traceScene(eye, direction, spheres);
    float pixelAngle = pixelSize() / zoom;
    [loop]
    for (int w = 0; w < Balls * 2; w++)
    {
        int ball = w / 2;
        float3 top = float3(0.0, BarHeight, rowZ(ball) + ((w & 1) == 0 ? -0.16 : 0.16));
        float3 bottom = spheres[ball].xyz;
        float2 closest = raySegment(eye, direction, top, bottom);
        float width = max(0.003, pixelAngle * closest.y * 0.6);
        float coverage = saturate(0.003 / width) * smoothstep(width, 0.0, closest.x) * step(closest.y, first.distance);
        colour = lerp(colour, float3(0.35, 0.36, 0.4) + stripColour(ball) * 0.1, coverage * 0.8);
    }
    return colour;
}
