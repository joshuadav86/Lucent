// Name: Agate
// A polished cube cut from banded stone, turning on a cream table: rust, ochre, slate blue, plum, and white
// rings, with a thread of gold between each. The rings grow out from three hearts inside the stone. Bass,
// mids, and treble each feed one heart, so the bands swell and crowd across the faces as the music plays.
// Family: Ray traced
// Tags: 3D, Colourful, Geometric
// Added: 2026-11-15 21:04

float agCube(float3 x)
{
    float3 q = abs(x) - 0.78;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0) - 0.14;
}

// The stone's rings: a number that counts up band by band, outward from the three hearts.
float agRings(float3 x, float3 heard, float grow)
{
    float3 wobble = 0.09 * sin(x.yzx * 5.3 + 1.7) + 0.05 * sin(x.zxy * 11.0);
    x += wobble;
    float a = length((x - float3(0.45, 0.3, -0.35)) * float3(1.0, 1.5, 1.0)) / (1.0 + 0.8 * heard.x);
    float b = length((x - float3(-0.5, -0.25, 0.3)) * float3(1.4, 1.0, 1.0)) / (1.0 + 0.8 * heard.y);
    float c = length((x - float3(0.1, -0.5, -0.55)) * float3(1.0, 1.0, 1.6)) / (1.0 + 0.8 * heard.z);
    float k = 0.25;
    float nearest = -k * log(exp(-a / k) + exp(-b / k) + exp(-c / k));
    return nearest * 9.0 - grow;
}

float3 agStone(float pick)
{
    if (pick < 0.2) return float3(1.3, 0.38, 0.14);
    if (pick < 0.4) return float3(1.15, 1.08, 0.95);
    if (pick < 0.57) return float3(0.2, 0.45, 0.85);
    if (pick < 0.75) return float3(1.4, 0.85, 0.2);
    if (pick < 0.88) return float3(0.5, 0.14, 0.4);
    return float3(0.08, 0.5, 0.5);
}

float3 Render(float2 uv, float2 p)
{
    // The table: cream paper, with the cube's shadow under it.
    float3 colour = float3(1.02, 0.95, 0.82) * (1.03 - 0.25 * uv.y) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.7);
    float2 shadowAt = (p - float2(0.12, 0.78)) * float2(0.6, 3.0);
    colour *= 1.0 - 0.5 * exp(-dot(shadowAt, shadowAt) * 2.0);

    float3 heard = float3(Bass, Mid, Treble);
    float grow = wrap(Time * 0.12 + BeatGlide * 0.25, 60.0);
    float turn = Time * 0.19 + BeatGlide * 0.1;
    float tip = 0.55 + 0.12 * sin(Time * 0.13);

    float3 eye = float3(0.0, 0.0, -4.4);
    float3 ray = normalize(float3(p.x, -p.y, 2.6));
    eye.yz = rotate(eye.yz, tip);
    ray.yz = rotate(ray.yz, tip);
    eye.xz = rotate(eye.xz, turn);
    ray.xz = rotate(ray.xz, turn);
    float3 lamp = normalize(float3(-0.5, 0.8, -0.3));
    lamp.yz = rotate(lamp.yz, tip);
    lamp.xz = rotate(lamp.xz, turn);

    float reach = 1.62;
    float b = dot(eye, ray);
    float h = b * b - dot(eye, eye) + reach * reach;
    if (h <= 0.0) return colour;
    h = sqrt(h);
    float t = max(-b - h, 0.0);
    float leave = -b + h;

    bool hit = false;
    int steps = qualitySteps(48);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = agCube(eye + ray * t);
        if (d < 0.0015 * surfacePrecision())
        {
            hit = true;
            break;
        }

        t += d;
        if (t > leave) break;
    }

    if (!hit) return colour;

    float3 at = eye + ray * t;
    float2 e = float2(0.003, 0.0);
    float3 n = normalize(float3(
        agCube(at + e.xyy) - agCube(at - e.xyy),
        agCube(at + e.yxy) - agCube(at - e.yxy),
        agCube(at + e.yyx) - agCube(at - e.yyx)));

    float rings = agRings(at, heard, grow);
    float band = floor(rings);
    float within = rings - band;
    float3 stone = agStone(hash(float2(wrap(band, 97.0), 3.7)));
    stone *= 0.82 + 0.18 * within;

    // A thread of gold where one band meets the next, as wide on screen however the bands crowd.
    float crowd = length(float3(
        agRings(at + e.xyy, heard, grow) - rings,
        agRings(at + e.yxy, heard, grow) - rings,
        agRings(at + e.yyx, heard, grow) - rings)) / e.x;
    float fromSeam = min(within, 1.0 - within) / max(crowd, 1e-3);
    float gold = smoothstep(0.014, 0.006, fromSeam);

    float lit = 0.38 + 0.72 * saturate(dot(n, lamp)) + 0.12 * saturate(n.y);
    float shine = pow(saturate(dot(reflect(ray, n), lamp)), 60.0);
    float rim = pow(1.0 - saturate(-dot(n, ray)), 4.0);
    float3 cube = lerp(stone, float3(1.5, 1.1, 0.4) * (0.8 + 0.3 * BeatSwell), gold) * lit;
    cube += float3(1.1, 1.08, 1.0) * (shine * (0.7 + 0.6 * gold) + rim * 0.22);
    return cube * (0.94 + 0.08 * Level);
}
