// Name: Fable Anemone
// A sea anemone in deep water, turning slowly in the current: hundreds of soft coral tentacles with glowing tips, each
// ring of them reaching out as far as its own band of the spectrum is loud, bass at the crown. Its body swells with
// the bass and a pulse of light runs out to the tips on every beat.
// Family: Organic
// Tags: 3D, Water, Calm
// Added: 2026-11-15 17:45

// The tentacles are not separate shapes. Each ray is stepped through the shell of space between the body and
// the furthest tips; at every step the direction from the middle says which tentacle (if any) the point is
// inside, and how far along it, and the soft colour found there is laid over what has been gathered so far.
// Directions are given a tentacle by cutting each face of a cube round the body into a grid, one per square.
static const float Body = 0.4;
static const float Tips = 1.0;
static const float Across = 8.0;               // tentacles across each of the cube's six faces

float3 fnSea(float2 p)
{
    // Deep water: lighter above, shafts of light slanting down, and drifting specks.
    float3 sea = lerp(float3(0.0, 0.085, 0.13), float3(0.0, 0.016, 0.04), saturate(p.y * 0.5 + 0.5));
    float shafts = noise(float2(p.x * 2.6 + p.y * 0.9 + Time * 0.03, 3.0)) * noise(float2(p.x * 6.1 + p.y * 2.1 - Time * 0.05, 9.0));
    sea += float3(0.03, 0.2, 0.22) * shafts * saturate(0.75 - p.y * 0.5) * (0.5 + Mid * 0.6);
    float2 drift = p * 9.0 + float2(Time * 0.11, -Time * 0.07);
    float2 speck = frac(drift) - 0.5;
    float seed = hash(floor(drift) * 0.37 + 2.2);
    sea += float3(0.35, 0.6, 0.6) * step(0.86, seed) * exp(-dot(speck, speck) * 260.0) * (0.25 + 0.25 * sin(Time * 1.3 + seed * 40.0));
    return sea;
}

float3 Render(float2 uv, float2 p)
{
    float turn = Time * 0.07 + BeatGlide * 0.025;
    float3 eye = float3(sin(turn) * 2.15, 0.85 + 0.35 * sin(Time * 0.05), cos(turn) * 2.15);
    float3 ray = cameraRay(eye, float3(0.0, 0.02, 0.0), float2(p.x, -p.y), 1.95 - DropPulse * 0.12, sin(Time * 0.04) * 0.06);
    float swell = 1.0 + Bass * 0.06 + DropPulse * 0.1;
    float beatLight = SongMapped > 0.0 ? 0.35 + Beat * 0.65 : Beat;

    float3 sea = fnSea(p);
    float b = dot(eye, ray);
    float outer = Tips * swell;
    float reached = b * b - (dot(eye, eye) - outer * outer);
    if (reached <= 0.0)
    {
        return sea * (0.9 + Intensity * 0.3);
    }

    // The stretch of the ray inside the shell, stopping at the body if it hits it.
    float t0 = max(-b - sqrt(reached), 0.0);
    float t1 = -b + sqrt(reached);
    float inner = Body * swell;
    float struck = b * b - (dot(eye, eye) - inner * inner);
    bool body = struck > 0.0;
    if (body)
    {
        t1 = -b - sqrt(struck);
    }

    int steps = qualitySteps(72);
    float stride = (t1 - t0) / (float)steps;
    float t = t0 + stride * hash(uv * Resolution.xy + 0.37);
    float3 colour = 0.0;
    float clear = 1.0;                                    // how much of what's behind still shows
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 at = (eye + ray * t) / swell;
        t += stride;
        float far = length(at);
        float h = (far - Body) / (Tips - Body);           // 0 on the body, 1 at full reach
        if (h <= 0.0 || h >= 1.0)
        {
            continue;
        }

        // Tentacles lean in the current, more the further out: undo the lean to find whose root this is.
        float3 dir = at / far;
        float3 current = float3(sin(Time * 0.6 + dir.y * 2.0 + dir.z * 1.3), 0.6 * sin(Time * 0.47 + dir.x * 2.2),
                                cos(Time * 0.53 + dir.y * 1.7 + dir.x));
        dir = normalize(dir - current * h * h * 0.3);

        // Which square of the cube's faces that direction passes through.
        float3 size = abs(dir);
        float2 st;
        float face;
        if (size.x >= size.y && size.x >= size.z)
        {
            st = dir.yz / size.x;
            face = dir.x > 0.0 ? 0.0 : 1.0;
        }
        else if (size.y >= size.z)
        {
            st = dir.xz / size.y;
            face = dir.y > 0.0 ? 2.0 : 3.0;
        }
        else
        {
            st = dir.xy / size.z;
            face = dir.z > 0.0 ? 4.0 : 5.0;
        }

        float2 grid = (atan(st) * (2.0 / PI) + 0.5) * Across;      // evenly spaced by angle
        float2 square = min(floor(grid), Across - 1.0);
        float2 key = square + face * 17.0;
        float one = hash(key * 0.173 + 1.3);
        float two = hash(key * 0.291 + 7.9);
        float2 from = grid - square - 0.5 - (float2(one, two) - 0.5) * 0.3;
        float off = length(from);                                  // from the tentacle's middle, in squares

        // How long this tentacle is just now: its ring's band of the spectrum, bass at the crown.
        float band = spectrum(0.02 + (0.5 - 0.5 * dir.y) * 0.72);
        float reach = min(0.42 + 0.2 * one + band * 0.55, 1.0);
        float along = h / reach;                                   // 0 at its root, 1 at its tip
        if (along >= 1.0)
        {
            continue;
        }

        float thick = 0.36 * (1.0 - 0.7 * along) * smoothstep(1.0, 0.9, along);
        float inside = smoothstep(thick, thick * 0.5, off);
        if (inside <= 0.0)
        {
            continue;
        }

        // Coral flesh, darker at the root and in among the others, with a lit tip.
        float3 flesh = lerp(float3(0.42, 0.03, 0.1), float3(1.0, 0.36, 0.3), along);
        flesh = lerp(flesh, float3(1.0, 0.5 + 0.4 * two, 0.3 + 0.5 * two), 0.25);
        float3 tip = float3(0.25, 1.0, 0.85);
        float lit = (0.12 + 0.88 * h * h) * (0.6 + 0.4 * dir.y);
        float run = along - BeatPhase * 1.2;
        float pulse = exp(-run * run * 16.0) * beatLight;
        float3 light = flesh * lit * (0.75 + band * 0.7);
        light = lerp(light, tip * (0.4 + band * 1.3 + pulse * 0.6), smoothstep(0.66, 1.0, along) * 0.9);
        light += tip * pulse * 0.2 * along;

        float cover = inside * 0.6;
        colour += light * cover * clear;
        clear *= 1.0 - cover;
        if (clear < 0.03)
        {
            break;
        }
    }

    // Behind the tentacles: the body, glowing a little with the bass, or the sea.
    float3 behind = sea;
    if (body)
    {
        float3 on = normalize(eye + ray * t1);
        float rim = pow(saturate(1.0 + dot(on, ray)), 2.0);
        behind = float3(0.3, 0.02, 0.07) * (0.35 + Bass * 0.9 + DropPulse * 0.5) + float3(0.9, 0.3, 0.3) * rim * 0.25;
    }

    colour += behind * clear;
    return colour * (0.9 + Intensity * 0.3);
}
