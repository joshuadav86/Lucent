// Name: Silver Nitrate
// An old reel of film, black and white and grainy, of a machine that can't exist: a scaffold of rods and
// joints turned inside out, so that every straight rod bends into a ring and they all dive towards one
// bright point. The scaffold slides through itself with the beat, the bass fattens the rods, and the lamp
// behind it burns brighter as the music grows.
// Family: Fractal
// Tags: 3D, Dark, Retro
// Added: 2026-11-15 20:59

// Distance to the scaffold. The scaffold is an ordinary square frame of rods, seen after space has been
// turned inside out around the origin.
float snMap(float3 x, float3 slide, float fat)
{
    float k = (2.3 + 0.1 * BeatSwell) / max(dot(x, x), 1e-4);
    float3 q = x * k + slide;
    float3 c = abs(q - floor(q + 0.5));
    float rods = min(length(c.yz), min(length(c.xz), length(c.xy))) - fat;
    float joints = length(c) - fat * 2.3;
    return min(rods, joints) / k;
}

float3 Render(float2 uv, float2 p)
{
    // The film runs at 24 frames a second: the picture weaves a hair in the gate, and the grain is new each frame.
    float frame = wrap(floor(Time * 24.0), 977.0);
    float2 weave = (float2(hash(float2(frame, 1.0)), hash(float2(frame, 2.0))) - 0.5) * 0.004;
    float2 film = p + weave;

    float3 slide = float3(1.0, 0.45, 0.2) * (Time * 0.1 + BeatGlide * 0.16);
    float fat = 0.065 + 0.03 * Bass;
    float around = Time * 0.08;
    float3 eye = float3(1.55 * cos(around), 0.5 * sin(Time * 0.11), 1.55 * sin(around));
    float3 ray = cameraRay(eye, float3(0.0, 0.0, 0.0) + 0.25 * float3(sin(Time * 0.05), cos(Time * 0.07), 0.0), float2(film.x, -film.y), 1.1, 0.4 * sin(Time * 0.04));

    float t = 0.05;
    bool hit = false;
    int steps = qualitySteps(80);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = snMap(eye + ray * t, slide, fat);
        if (d < 0.0015 * surfacePrecision() * (1.0 + t))
        {
            hit = true;
            break;
        }

        t += d * 0.8;
        if (t > 9.0) break;
    }

    // Behind everything, the lamp: brightest straight through the middle of the machine.
    float through = saturate(dot(ray, normalize(-eye)));
    float lampLight = 0.75 + 0.3 * Level + 0.25 * DropPulse;
    float silver = (0.1 + 1.0 * pow(through, 6.0)) * lampLight;

    if (hit)
    {
        float3 at = eye + ray * t;
        float2 e = float2(0.002, 0.0);
        float3 n = normalize(float3(
            snMap(at + e.xyy, slide, fat) - snMap(at - e.xyy, slide, fat),
            snMap(at + e.yxy, slide, fat) - snMap(at - e.yxy, slide, fat),
            snMap(at + e.yyx, slide, fat) - snMap(at - e.yyx, slide, fat)));
        float3 toLamp = normalize(-at);
        float lit = 0.08 + 0.5 * saturate(dot(n, toLamp)) + 0.35 * saturate(dot(n, normalize(float3(0.4, 0.8, -0.3))));
        float shine = pow(saturate(dot(reflect(ray, n), toLamp)), 24.0);
        float metal = lit * 0.6 + shine * 0.9;
        silver = lerp(metal, silver, 1.0 - exp(-t * 0.28));
    }

    // Grain, a scratch or two, and the dark corners of an old lens.
    silver += (hash(film * 190.0 + frame * 0.37) - 0.5) * 0.09;
    float reel = wrap(floor(Time * 3.0), 311.0);
    float scratch = hash(float2(reel, 7.0)) * 2.0 * Aspect - Aspect;
    silver += 0.1 * smoothstep(0.004, 0.0, abs(p.x - scratch)) * step(0.55, hash(float2(reel, 3.0)));
    silver *= 1.0 - dot(uv - 0.5, uv - 0.5) * 1.5;
    return max(silver, 0.0) * float3(1.0, 0.97, 0.92);
}
