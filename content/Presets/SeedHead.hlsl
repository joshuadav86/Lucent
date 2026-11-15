// Name: Seed Head
// A ball of white lace on plain dark green, like a seed head held up to look at: threads that curve through
// one another and never tangle, around a small pale heart. The ball turns slowly and its weave drifts. Bass
// thickens the threads, low, middle, and high notes each swell their own band of the ball, and it breathes
// on the beat.
// Family: Organic
// Tags: 3D, Minimal, Calm
// Added: 2026-11-15 21:00

// Distance to the lace. The threads run where two families of curved sheets cross.
float shMap(float3 x, float3 heard, float thick, float drift)
{
    float r = length(x);
    float up = x.y / max(r, 1e-4);
    float low = saturate(-up * 1.6);
    float high = saturate(up * 1.6);
    float ball = 1.08 + 0.04 * BeatSwell + 0.3 * (heard.x * low + heard.z * high + heard.y * (1.0 - low - high));

    float3 q = x * 6.6;
    float a = dot(sin(q + float3(drift, 0.0, -drift)), cos(q.yzx));
    float3 w = q.zxy * 1.37 + float3(1.7, drift * 0.6, 0.4);
    float b = dot(sin(w), cos(w.yzx));
    float threads = (length(float2(a, b)) - thick) / 12.0;

    float lace = max(threads, r - ball);
    return min(lace, r - 0.2);
}

float3 Render(float2 uv, float2 p)
{
    float3 green = float3(0.02, 0.2, 0.115);
    float3 colour = green * (1.0 + 0.5 * exp(-dot(p, p) * 1.1)) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.7);

    float3 heard = float3(spectrum(0.04), spectrum(0.2), spectrum(0.5));
    float thick = 0.3 + 0.08 * Bass;
    float drift = Time * 0.22 + BeatGlide * 0.3;
    float turn = Time * 0.13 + BeatGlide * 0.08;

    float3 eye = float3(0.0, 0.0, -4.15);
    float3 ray = normalize(float3(p.x, -p.y, 2.6));
    eye.yz = rotate(eye.yz, 0.3);
    ray.yz = rotate(ray.yz, 0.3);
    eye.xz = rotate(eye.xz, turn);
    ray.xz = rotate(ray.xz, turn);
    float3 lamp = normalize(float3(-0.5, 0.75, -0.45));
    lamp.xz = rotate(lamp.xz, turn);

    // Only look inside the ball that holds the whole seed head.
    float reach = 1.58;
    float b = dot(eye, ray);
    float h = b * b - dot(eye, eye) + reach * reach;
    if (h <= 0.0) return colour;
    h = sqrt(h);
    float t = max(-b - h, 0.0);
    float leave = -b + h;

    bool hit = false;
    int steps = qualitySteps(130);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = shMap(eye + ray * t, heard, thick, drift);
        if (d < 0.002 * surfacePrecision())
        {
            hit = true;
            break;
        }

        t += max(d * 0.7, 0.004);
        if (t > leave) break;
    }

    if (!hit) return colour;

    float3 at = eye + ray * t;
    float2 e = float2(0.004, 0.0);
    float3 n = normalize(float3(
        shMap(at + e.xyy, heard, thick, drift) - shMap(at - e.xyy, heard, thick, drift),
        shMap(at + e.yxy, heard, thick, drift) - shMap(at - e.yxy, heard, thick, drift),
        shMap(at + e.yyx, heard, thick, drift) - shMap(at - e.yyx, heard, thick, drift)));

    // Threads deep inside the ball sit in the shade of the ones outside them.
    float deep = saturate((length(at) - 0.2) / 0.95);
    float open = saturate(shMap(at + n * 0.12, heard, thick, drift) / 0.12);
    float lit = 0.4 + 0.6 * saturate(dot(n, lamp) * 0.6 + 0.4);
    float3 thread = lerp(float3(0.05, 0.4, 0.22), float3(1.1, 1.08, 1.0), deep * deep * (0.45 + 0.55 * open));
    if (length(at) < 0.22) thread = float3(1.2, 1.1, 0.55);
    return thread * lit * (0.94 + 0.08 * Level);
}
