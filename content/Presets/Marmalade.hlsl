// Name: Marmalade
// A blob of orange jelly floating in front of a pale blue wall, clear enough to see the light come through
// it: thin at the edges where it glows yellow, deep amber where it is thick. Six lobes push out of it, one
// for each part of the music from bass to treble, and the whole blob wobbles on the beat.
// Family: Ray traced
// Tags: 3D, Colourful, Calm
// Added: 2026-11-15 21:02

// How loud the music is at x (0 deep bass, 1 high treble), stretched so that ordinary music swings it most of
// the way between 0 and 1 instead of sitting near the top.
float mmLoud(float x)
{
    return saturate((spectrum(x) - 0.56 + 0.5 * x) / 0.4);
}

float mmBlend(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// Distance to the jelly: a heart and six lobes, melted together.
float mmMap(float3 x, float3 low, float3 high, float wobble)
{
    x.y *= 1.0 + 0.1 * sin(wobble);
    x.xz *= 1.0 - 0.05 * sin(wobble);
    float d = length(x) - 0.62;
    float heard[6] = { low.x, low.y, low.z, high.x, high.y, high.z };

    [unroll]
    for (int k = 0; k < 6; k++)
    {
        float f = (float)k;
        float loud = heard[k];
        float3 way = normalize(float3(sin(f * 2.4 + 0.5), cos(f * 1.7 + 1.0) * 0.8, cos(f * 2.4 + 0.5)));
        way.xy = rotate(way.xy, 0.4 * sin(Time * 0.31 + f));
        float out1 = 0.3 + 0.85 * loud + 0.1 * sin(Time * 0.5 + f * 1.9);
        d = mmBlend(d, length(x - way * out1) - (0.25 + 0.17 * loud), 0.34);
    }

    return d;
}

float3 Render(float2 uv, float2 p)
{
    float3 wall = float3(0.5, 0.8, 1.12) * (1.02 - 0.3 * uv.y) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.6);

    float3 low = float3(mmLoud(0.08), mmLoud(0.14), mmLoud(0.2));
    float3 high = float3(mmLoud(0.34), mmLoud(0.5), mmLoud(0.66));
    float wobble = BeatGlide * PI;
    float turn = Time * 0.17 + BeatGlide * 0.1;

    float3 eye = float3(0.0, 0.0, -3.5);
    float3 ray = normalize(float3(p.x, -p.y, 2.5));
    eye.yz = rotate(eye.yz, 0.25);
    ray.yz = rotate(ray.yz, 0.25);
    eye.xz = rotate(eye.xz, turn);
    ray.xz = rotate(ray.xz, turn);
    float3 lamp = normalize(float3(-0.5, 0.8, -0.35));
    lamp.xz = rotate(lamp.xz, turn);

    // A soft shadow on the wall behind, down and to the right of the blob.
    float2 shadowAt = (p - float2(0.3, 0.34)) * float2(0.7, 0.85);
    wall *= 1.0 - 0.28 * exp(-dot(shadowAt, shadowAt) * 1.5);

    float reach = 2.2;
    float b = dot(eye, ray);
    float h = b * b - dot(eye, eye) + reach * reach;
    if (h <= 0.0) return wall;
    h = sqrt(h);
    float t = max(-b - h, 0.0);
    float leave = -b + h;

    bool hit = false;
    int steps = qualitySteps(64);

    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = mmMap(eye + ray * t, low, high, wobble);
        if (d < 0.002 * surfacePrecision())
        {
            hit = true;
            break;
        }

        t += d * 0.9;
        if (t > leave) break;
    }

    if (!hit) return wall;

    float3 at = eye + ray * t;
    float2 e = float2(0.003, 0.0);
    float3 n = normalize(float3(
        mmMap(at + e.xyy, low, high, wobble) - mmMap(at - e.xyy, low, high, wobble),
        mmMap(at + e.yxy, low, high, wobble) - mmMap(at - e.yxy, low, high, wobble),
        mmMap(at + e.yyx, low, high, wobble) - mmMap(at - e.yyx, low, high, wobble)));

    // How much jelly the light has to cross: walk on through the blob to the far side.
    float3 inside = normalize(ray - n * 0.25);
    float thickness = 0.0;
    float walked = 0.03;
    [loop]
    for (int k = 0; k < 20; k++)
    {
        float d = -mmMap(at + inside * walked, low, high, wobble);
        if (d < 0.004) break;
        walked += max(d, 0.03);
    }

    thickness = walked;

    // Light from the lamp side comes through thin jelly as yellow, thick jelly as deep amber.
    float3 through = exp(-thickness * float3(0.16, 0.85, 3.0));
    float lampSide = 0.6 + 0.4 * saturate(dot(n, lamp) * 0.5 + 0.5);
    float3 jelly = float3(1.75, 1.3, 0.9) * through * lampSide;

    float facing = saturate(-dot(n, ray));
    float edge = pow(1.0 - facing, 3.0);
    float shine = pow(saturate(dot(reflect(ray, n), lamp)), 90.0);
    float window = pow(saturate(dot(reflect(ray, n), normalize(float3(lamp.x, 0.25, lamp.z)))), 14.0);
    float3 colour = jelly + float3(0.75, 0.9, 1.05) * edge * 0.45 + float3(1.2, 1.2, 1.15) * (shine + 0.18 * window);
    return colour * (0.94 + 0.08 * Level);
}
