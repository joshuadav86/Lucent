// Name: Pipework
// A flight through an endless plant room of enamelled pipes, painted the way engineers colour them: blue,
// green, yellow, and red, with a white collar at every joint. Each pipe bends a quarter turn and hands on to
// the next, so the runs never end. Every pipe swells with its own part of the music, and the beat carries
// you forward.
// Family: Fly-through
// Tags: 3D, Colourful, Geometric
// Added: 2026-11-15 21:01

static const float Repeat = 64.0;       // the plant room repeats after this many pipes each way

// One bend: a quarter ring of pipe about the point `c`, lying in the plane of q.xy.
float pwBend(float3 q, float fat, out float collar)
{
    float ring = length(q.xy) - 0.5;
    float angle = atan2(q.y, q.x + 1e-5);
    collar = smoothstep(0.86, 0.95, abs(cos(angle * 2.0)));        // at both ends of the bend
    return length(float2(ring, q.z)) - fat - 0.035 * collar;
}

// Distance to the pipes in the cube that x is in. `paint` is x: which colour, y: how much collar.
float pwMap(float3 x, float4 heard, out float2 paint)
{
    float3 cell = floor(x);
    float3 f = x - cell - 0.5;
    float3 id = wrap(cell, Repeat);
    float pick = hash(float2(id.x + id.z * 7.31, id.y * 1.37 + id.z * 3.17) + 0.5);

    // Each cube holds the same three bends, flipped one of eight ways.
    float3 flip = float3(pick < 0.5 ? 1.0 : -1.0, frac(pick * 2.0) < 0.5 ? 1.0 : -1.0, frac(pick * 4.0) < 0.5 ? 1.0 : -1.0);
    f *= flip;

    float band = frac(pick * 8.0);
    float loud = band < 0.25 ? heard.x : band < 0.5 ? heard.y : band < 0.75 ? heard.z : heard.w;
    float fat = 0.09 + 0.07 * loud;

    float collar1;
    float collar2;
    float collar3;
    float d1 = pwBend(float3(f.x - 0.5, f.y - 0.5, f.z), fat, collar1);
    float d2 = pwBend(float3(f.z - 0.5, f.x + 0.5, f.y), fat, collar2);
    float d3 = pwBend(float3(f.y + 0.5, f.z + 0.5, f.x), fat, collar3);

    float d = d1;
    paint = float2(frac(pick * 16.0), collar1);
    if (d2 < d)
    {
        d = d2;
        paint = float2(frac(pick * 32.0), collar2);
    }

    if (d3 < d)
    {
        d = d3;
        paint = float2(frac(pick * 64.0), collar3);
    }

    return d;
}

float3 pwEnamel(float pick)
{
    if (pick < 0.3) return float3(0.1, 0.45, 1.5);
    if (pick < 0.55) return float3(0.1, 1.1, 0.45);
    if (pick < 0.8) return float3(1.6, 1.15, 0.1);
    return float3(1.6, 0.16, 0.12);
}

float3 Render(float2 uv, float2 p)
{
    float4 heard = float4(spectrum(0.03), spectrum(0.12), spectrum(0.3), spectrum(0.55));

    // Fly along the line where four cubes meet: no pipe ever crosses it.
    float travel = wrap(Time * 0.4 + BeatGlide * 0.45, Repeat);
    float3 eye = float3(0.07 * sin(Time * 0.21), 0.07 * cos(Time * 0.17), travel);
    float3 ray = cameraRay(eye, eye + float3(0.35 * sin(Time * 0.09), 0.25 * sin(Time * 0.071), 1.0), float2(p.x, -p.y), 0.9, 0.5 * sin(Time * 0.05));
    ray = float3(abs(ray.x) < 1e-4 ? 1e-4 : ray.x, abs(ray.y) < 1e-4 ? 1e-4 : ray.y, abs(ray.z) < 1e-4 ? 1e-4 : ray.z);

    float3 haze = float3(0.95, 0.98, 1.02) * (0.9 + 0.1 * Intensity + 0.1 * DropPulse);
    float t = 0.0;
    float2 paint = float2(0.0, 0.0);
    bool hit = false;
    int steps = qualitySteps(110);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 x = eye + ray * t;
        float d = pwMap(x, heard, paint);
        if (d < 0.002 * surfacePrecision() * (1.0 + t))
        {
            hit = true;
            break;
        }

        // Never step past the wall of this cube: the pipes in the next one are its own.
        float3 f = x - floor(x);
        float3 wall = (step(0.0, ray) - f) / ray;
        t += min(d, min(wall.x, min(wall.y, wall.z)) + 0.003);
        if (t > 11.0) break;
    }

    if (!hit) return haze * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.35);

    float3 at = eye + ray * t;
    float2 unused;
    float2 e = float2(0.003, 0.0);
    float3 n = normalize(float3(
        pwMap(at + e.xyy, heard, unused) - pwMap(at - e.xyy, heard, unused),
        pwMap(at + e.yxy, heard, unused) - pwMap(at - e.yxy, heard, unused),
        pwMap(at + e.yyx, heard, unused) - pwMap(at - e.yyx, heard, unused)));

    float3 lamp = normalize(float3(0.4, 0.8, -0.35));
    float3 enamel = lerp(pwEnamel(paint.x), float3(1.05, 1.05, 1.0), paint.y);
    float open = saturate(pwMap(at + n * 0.14, heard, unused) / 0.14);
    float lit = 0.3 + 0.25 * open + 0.2 * n.y + 0.6 * saturate(dot(n, lamp));
    float shine = pow(saturate(dot(reflect(ray, n), lamp)), 40.0);
    float rim = pow(1.0 - saturate(-dot(n, ray)), 4.0);
    float3 colour = enamel * lit + float3(1.0, 1.0, 1.0) * (shine * 0.6 + rim * 0.25);
    colour = lerp(colour, haze, 1.0 - exp(-t * 0.19));
    return colour * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.35);
}
