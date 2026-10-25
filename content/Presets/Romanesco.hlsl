// Name: Romanesco
// A head of Romanesco turns slowly under a warm, low light: a cone of spiralling florets, each one a smaller cone of spiralling florets, glowing lime green and gold. The bass makes the florets swell gently and each beat brightens the light across them.
// Family: Fractal
// Tags: 3D, Calm
// Added: 2026-10-17 22:02

// Romanesco broccoli grows in Fibonacci spirals: its florets sit on the cone like the seeds of a
// sunflower, each turned from the last by the golden angle, getting larger towards the base. Each
// floret is itself a cone covered in smaller florets the same way.
//
// To find the nearest floret quickly we unroll the cone's surface into a flat sheet, using
// "log of distance from the tip" across and "angle around" down. In that sheet the florets sit
// on a perfectly regular slanted grid (a florets' size grows with distance from the tip, and the
// log undoes that), so the nearest one is a matter of rounding to the grid.

static const float ConeAngle = 0.62;           // half-angle of every cone, radians
static const float GrowthStep = 0.03;          // log-distance between one floret and the next
static const float GoldenTurn = 0.381966;      // the golden angle, in turns
static const float FloretSize = 0.34;          // floret height for its distance from the tip

// Distance to a solid cone with its tip at the origin, pointing up, and its base at y = -height.
float coneDistance(float3 p, float height)
{
    float2 c = float2(sin(ConeAngle), cos(ConeAngle));
    float2 q = height * float2(c.x / c.y, -1.0);
    float2 w = float2(length(p.xz), p.y);
    float2 a = w - q * saturate(dot(w, q) / dot(q, q));
    float2 b = w - q * float2(saturate(w.x / q.x), 1.0);
    float d = min(dot(a, a), dot(b, b));
    float s = max(-(w.x * q.y - w.y * q.x), -(w.y - q.y));
    return sqrt(d) * sign(s);
}

float smoothUnion(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// The two short steps of the floret grid (the 8 and 13 spirals you can count on a real head).
static const float2 SpiralA = float2(8.0 * GrowthStep, 2.0 * PI * (8.0 * GoldenTurn - 3.0));
static const float2 SpiralB = float2(13.0 * GrowthStep, 2.0 * PI * (13.0 * GoldenTurn - 5.0));

// The two florets nearest to p on a cone (tip at the origin, height 1), as their places on the
// unrolled sheet: x is the log of the distance from the tip, y the angle around.
void nearestTwo(float3 p, out float2 first, out float2 second)
{
    float slant = length(p.xz) * sin(ConeAngle) - p.y * cos(ConeAngle);
    float u = log(max(slant, 1e-4));
    float around = atan2(p.z, p.x);

    // Round to the slanted grid: solve for grid coordinates, then rank the four nearest corners.
    float det = SpiralA.x * SpiralB.y - SpiralA.y * SpiralB.x;
    float2 grid = float2(u * SpiralB.y - around * SpiralB.x, SpiralA.x * around - SpiralA.y * u) / det;
    float2 base = floor(grid);
    first = 0.0;
    second = 0.0;
    float firstDistance = 1e9;
    float secondDistance = 1e9;
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float2 corner = base + float2(k & 1, k >> 1);
        float2 at = corner.x * SpiralA + corner.y * SpiralB;
        float2 offset = at - float2(u, around);
        float d = dot(offset, offset);
        if (d < firstDistance)
        {
            second = first;
            secondDistance = firstDistance;
            first = at;
            firstDistance = d;
        }
        else if (d < secondDistance)
        {
            second = at;
            secondDistance = d;
        }
    }
}

// p in the frame of the floret at the given place, scaled so the floret is again a cone of
// height 1. size is its height; valid is 0 for places off the cone.
float3 floretFrame(float3 p, float2 place, out float size, out float valid, out float age)
{
    // The bass makes every floret swell a little.
    float s = exp(place.x);
    float swell = 1.0 + Bass * 0.06 + Beat * 0.02;
    size = FloretSize * s * swell;
    float limit = 1.0 / cos(ConeAngle);
    valid = step(s, limit * 0.97) * step(0.035, s);
    age = s / limit;

    // Where the floret sits on the surface, and which way it points: out from the cone and up.
    float sa, ca;
    sincos(place.y, sa, ca);
    float3 seat = float3(s * sin(ConeAngle) * ca, -s * cos(ConeAngle), s * sin(ConeAngle) * sa);
    float3 normal = float3(cos(ConeAngle) * ca, sin(ConeAngle), cos(ConeAngle) * sa);
    float3 axis = normalize(normal + float3(0.0, 0.55, 0.0));
    float3 side = float3(-sa, 0.0, ca);
    float3 front = cross(side, axis);
    float3 tip = seat + axis * size * 0.72;
    float3 v = p - tip;
    return float3(dot(v, side), dot(v, axis), dot(v, front)) / size;
}

// How the head turns.
float headTurn()
{
    return Time * 0.07;
}

// Florets stand no further than this off the cone they grow on, smoothing included.
static const float FloretReach = 0.52;

// Distance to the whole head, and how deep in the floret levels the nearest surface is, following
// the florets this many levels down.
float head(float3 p, out float depthShade, int levels)
{
    float3 q = p - float3(0.0, 0.55, 0.0);
    q.xz = rotate(q.xz, headTurn());
    float cone = coneDistance(q, 1.0);
    depthShade = 0.0;
    // Well clear of the cone the florets can't be nearer than this, so they needn't be found.
    if (cone > FloretReach + 0.1)
    {
        return cone - FloretReach;
    }

    float d = cone + 0.05;
    float scale = 1.0;
    [loop]
    for (int level = 0; level < levels; level++)
    {
        // Check the two nearest florets, since a big one can reach past its neighbour's spot,
        // and carry on into whichever is closer.
        float2 first;
        float2 second;
        nearestTwo(q, first, second);
        float size, valid, age;
        float size2, valid2, age2;
        float3 local = floretFrame(q, first, size, valid, age);
        float3 local2 = floretFrame(q, second, size2, valid2, age2);
        float floret = valid > 0.5 ? (coneDistance(local, 1.0) - 0.06) * size : 1e3;
        float floret2 = valid2 > 0.5 ? (coneDistance(local2, 1.0) - 0.06) * size2 : 1e3;
        if (floret2 < floret)
        {
            floret = floret2;
            local = local2;
            size = size2;
            age = age2;
            valid = valid2;
        }
        if (valid < 0.5)
        {
            break;
        }
        floret *= scale;
        scale *= size;
        float before = d;
        d = smoothUnion(d, floret, 0.03 * scale);
        depthShade = floret < before ? level + 1.0 - age * 0.5 : depthShade;
        q = local;
        // Only follow this floret's own florets when we're close enough to see them.
        if (floret > 0.6 * scale)
        {
            break;
        }
    }
    return d;
}

float scene(float3 p)
{
    float depthShade;
    return head(p, depthShade, fractalDepth(3));
}

// The head with its finest florets left off: shadows are soft enough that they don't show.
float shadowScene(float3 p)
{
    float depthShade;
    return head(p, depthShade, max(fractalDepth(3) - 1, 1));
}

float3 sceneNormal(float3 p, float e)
{
    float2 k = float2(1.0, -1.0);
    return normalize(k.xyy * scene(p + k.xyy * e) + k.yyx * scene(p + k.yyx * e)
                   + k.yxy * scene(p + k.yxy * e) + k.xxx * scene(p + k.xxx * e));
}

float softShadow(float3 origin, float3 direction)
{
    float light = 1.0;
    float t = 0.02;
    int steps = qualitySteps(22);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = shadowScene(origin + direction * t);
        light = min(light, 9.0 * d / t);
        t += clamp(d, 0.02, 0.25);
        if (light < 0.01 || t > 3.0)
        {
            break;
        }
    }
    return saturate(light);
}

float3 background(float3 direction, float2 p)
{
    // A warm, dim studio: darker above, a soft amber glow low behind the head.
    float3 sky = lerp(float3(0.02, 0.025, 0.02), float3(0.004, 0.006, 0.008), saturate(direction.y * 1.5 + 0.3));
    sky += float3(0.09, 0.06, 0.02) * exp(-dot(p - float2(0.0, 0.25), p - float2(0.0, 0.25)) * 1.6) * (0.7 + Level * 0.3);
    return sky;
}

float3 Render(float2 uv, float2 p)
{
    float orbit = Time * 0.045;
    float height = 1.15 + sin(Time * 0.06) * 0.35;
    float3 eye = float3(sin(orbit) * 3.5, height, cos(orbit) * 3.5);
    float3 target = float3(0.0, 0.05, 0.0);
    float3 direction = cameraRay(eye, target, float2(p.x, -p.y), 3.5, sin(Time * 0.05) * 0.04);

    float3 colour = background(direction, p);

    // Only march inside the sphere that holds the head.
    float3 oc = eye - float3(0.0, 0.1, 0.0);
    float b = dot(oc, direction);
    float h = b * b - (dot(oc, oc) - 1.45 * 1.45);
    if (h > 0.0)
    {
        float t = max(-b - sqrt(h), 0.0);
        float far = -b + sqrt(h);
        bool hit = false;
        float closeEnough = 0.0007 * surfacePrecision();
        int steps = qualitySteps(96);
        [loop]
        for (int i = 0; i < steps; i++)
        {
            float d = scene(eye + direction * t);
            if (d < closeEnough * t)
            {
                hit = true;
                break;
            }
            t += d * 0.7;
            if (t > far)
            {
                break;
            }
        }

        if (hit)
        {
            float3 at = eye + direction * t;
            float depthShade;
            head(at, depthShade, fractalDepth(3));
            float3 n = sceneNormal(at, 0.0015);

            // Low warm key light, cool sky fill, and a soft rim from behind.
            float3 keyDirection = normalize(float3(-0.6, 0.75, 0.45));
            float shadow = softShadow(at + n * 0.004, keyDirection);
            float wrapLight = saturate((dot(n, keyDirection) + 0.25) / 1.25);
            float fill = 0.5 + 0.5 * n.y;
            float rim = pow(1.0 - saturate(dot(n, -direction)), 3.0);

            // Ambient occlusion: how open the surface is, sampled a little way out.
            float occlusion = 0.0;
            [unroll]
            for (int k = 1; k <= 3; k++)
            {
                float reach = 0.025 * k;
                occlusion += (reach - scene(at + n * reach)) / reach;
            }
            occlusion = saturate(1.0 - occlusion * 0.28);

            // Lime green, deeper in the crevices and paling to gold at the floret tips.
            float tipness = saturate(depthShade * 0.45);
            float3 albedo = lerp(float3(0.13, 0.3, 0.06), float3(0.5, 0.68, 0.2), tipness);
            albedo = lerp(albedo, float3(0.85, 0.75, 0.3), saturate(rim * 0.6 + tipness * 0.2 - 0.15));

            float3 keyColour = float3(1.0, 0.82, 0.55) * (2.1 + Beat * 0.5 + Bass * 0.3);
            // The underside sinks into shadow, as if nestled in its leaves.
            occlusion *= smoothstep(-0.75, -0.1, at.y);
            float3 lit = albedo * (keyColour * wrapLight * shadow + float3(0.2, 0.26, 0.32) * fill * 0.8) * occlusion;
            // A waxy sheen, and light glowing through the edges of the florets.
            float3 halfway = normalize(keyDirection - direction);
            lit += float3(1.0, 0.9, 0.7) * pow(saturate(dot(n, halfway)), 40.0) * 0.25 * shadow * (0.6 + Treble);
            lit += float3(0.7, 0.85, 0.3) * rim * 0.22 * occlusion;

            colour = lit;
        }
    }

    // A little temporal smoothing calms the finest florets.
    return lerp(colour, previous(uv), 0.25);
}
