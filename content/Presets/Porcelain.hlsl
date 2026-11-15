// Name: Porcelain
// A white china ball on royal purple, made of pyramids folded into pyramids until the surface is all points
// and hollows, with a rose light glowing in the deepest of them. The ball turns slowly. The bass twists
// every fold at once, so the whole shape blooms and closes, and the light inside rises with the music.
// Family: Fractal
// Tags: 3D, Geometric, Calm
// Added: 2026-11-15 21:12

// Distance to the ball: fold space four times, the same way in every direction, then place one pyramid.
float pcMap(float3 x, float twist, float spread)
{
    float3 q = x;
    float size = 1.0;

    [unroll]
    for (int k = 0; k < 4; k++)
    {
        q = abs(q);
        q.xy = q.x < q.y ? q.yx : q.xy;
        q.xz = q.x < q.z ? q.zx : q.xz;
        q.yz = q.y < q.z ? q.zy : q.yz;
        q.xy = rotate(q.xy, twist);
        q.xz = rotate(q.xz, twist * 0.6);
        q = q * 1.9 - float3(0.9, 0.27, 0.09) * spread;
        size *= 1.9;
    }

    return (abs(q.x) + abs(q.y) + abs(q.z) - 0.95) * 0.57735 / size;
}

float3 Render(float2 uv, float2 p)
{
    float3 purple = float3(0.42, 0.1, 0.95);
    float3 colour = purple * (0.5 + 0.5 * exp(-dot(p, p) * 0.8)) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.6);

    float twist = 0.16 + 0.12 * sin(Time * 0.11) + 0.12 * Bass + 0.04 * BeatSwell;
    float spread = 1.0 + 0.08 * sin(Time * 0.07);
    float turn = Time * 0.14 + BeatGlide * 0.1;

    float3 eye = float3(0.0, 0.0, -3.0);
    float3 ray = normalize(float3(p.x, -p.y, 2.6));
    eye.yz = rotate(eye.yz, 0.35);
    ray.yz = rotate(ray.yz, 0.35);
    eye.xz = rotate(eye.xz, turn);
    ray.xz = rotate(ray.xz, turn);
    float3 lamp = normalize(float3(-0.5, 0.75, -0.45));
    lamp.xz = rotate(lamp.xz, turn);

    float reach = 1.5;
    float b = dot(eye, ray);
    float h = b * b - dot(eye, eye) + reach * reach;
    if (h <= 0.0) return colour;
    h = sqrt(h);
    float t = max(-b - h, 0.0);
    float leave = -b + h;

    bool hit = false;
    int steps = qualitySteps(110);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = pcMap(eye + ray * t, twist, spread);
        if (d < 0.0015 * surfacePrecision())
        {
            hit = true;
            break;
        }

        t += d * 0.8;
        if (t > leave) break;
    }

    if (!hit) return colour;

    float3 at = eye + ray * t;
    float2 e = float2(0.003, 0.0);
    float3 n = normalize(float3(
        pcMap(at + e.xyy, twist, spread) - pcMap(at - e.xyy, twist, spread),
        pcMap(at + e.yxy, twist, spread) - pcMap(at - e.yxy, twist, spread),
        pcMap(at + e.yyx, twist, spread) - pcMap(at - e.yyx, twist, spread)));

    // How open the surface is here: points stand in the light, hollows sit in the shade.
    float open = saturate(pcMap(at + n * 0.05, twist, spread) / 0.05) * 0.5 + saturate(pcMap(at + n * 0.2, twist, spread) / 0.2) * 0.5;
    float deep = saturate((1.05 - length(at)) / 0.6);
    float lit = 0.25 + 0.75 * saturate(dot(n, lamp) * 0.7 + 0.3);
    float3 china = lerp(float3(0.2, 0.11, 0.55), float3(1.05, 1.02, 1.04), open * lit);
    china += float3(1.5, 0.3, 0.55) * deep * deep * deep * (1.0 - open) * (0.3 + 1.0 * Mid + 0.4 * BeatSwell);
    china += float3(1.0, 1.0, 1.0) * 0.3 * pow(saturate(dot(reflect(ray, n), lamp)), 30.0) * open;
    return china * (0.94 + 0.08 * Level);
}
