// Name: Porcelain
// A white china ball on royal purple, made of pyramids folded into pyramids until the surface is all points
// and hollows, with a rose light glowing in the deepest of them. The ball turns, swinging with each bar. Every
// beat twists every fold at once, so the whole shape blooms and closes in time, and sends a shell of light out
// from the heart through the hollows to the tips. The light inside rises with the low and middle of the music,
// the tips glint with the treble, and the purple behind moves on to a new shade every eight beats.
// Family: Fractal
// Tags: 3D, Geometric, Calm
// Added: 2026-11-15 21:12

static float PcSound;                   // how loud the music is just now, taken across all its parts

// One part of the music, with the high notes made up to stand level with the low ones.
float pcPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one.
float pcLoud(float x)
{
    return saturate((pcPart(x) - 0.62 * PcSound) / (0.6 * PcSound + 0.1));
}

// The purple behind the ball: royal purple, then violet blue, magenta, and plum, a new one every eight beats.
float3 pcGround(float which)
{
    float k = wrap(which, 4.0);
    if (k < 0.5) return float3(0.42, 0.1, 0.95);
    if (k < 1.5) return float3(0.2, 0.16, 1.0);
    if (k < 2.5) return float3(0.72, 0.08, 0.8);
    return float3(0.5, 0.1, 0.62);
}

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
    PcSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        PcSound += pcPart(0.08 + 0.12 * part) / 6.0;
    float swell = BeatSwell;
    float barTurn = 6.2832 * BarPhase;

    float phrase = wrap(BeatGlide, 4096.0) / 8.0;
    float3 purple = lerp(pcGround(floor(phrase) - 1.0), pcGround(floor(phrase)), smoothstep(0.0, 0.12, frac(phrase)));
    // The light behind the ball swells with each beat.
    float3 colour = purple * (0.5 + (0.5 + 0.3 * swell) * exp(-dot(p, p) * 0.8)) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.6);

    // The folds open and close with each beat and sway with the bar, and the ball swings as it turns.
    float twist = 0.2 + 0.05 * sin(Time * 0.11) + 0.05 * sin(barTurn) + 0.085 * swell + 0.05 * DropPulse;
    float spread = 1.0 + 0.08 * sin(Time * 0.07) + 0.04 * cos(6.2832 * BeatPhase);
    float turn = wrap(Time * 0.16, 6.2832) + 0.22 * sin(barTurn + 1.2);

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
    float3 china = lerp(purple * float3(0.48, 1.1, 0.58), float3(1.05, 1.02, 1.04), open * lit);

    // The rose light in the hollows rises with the low and middle of the music.
    float inner = 0.5 * (pcLoud(0.1) + pcLoud(0.34));
    china += float3(1.5, 0.3, 0.55) * deep * deep * deep * (1.0 - open) * (0.2 + 1.25 * inner + 0.4 * swell);

    // Each beat sends a shell of light out from the heart to the tips. With a steady beat to follow the shell
    // keeps pace with it; without one it follows how far the last beat has faded.
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float shell = length(at) - 0.25 - since * 1.35;
    float lightShell = exp(-shell * shell * 26.0) * smoothstep(0.0, 0.08, since) * (1.0 - since);
    china += float3(1.3, 0.4, 0.75) * lightShell * (0.7 - 0.5 * open);

    // The tips glint with the treble.
    float tips = pcLoud(0.62);
    china += float3(1.0, 1.0, 1.0) * (0.3 + 0.5 * tips) * pow(saturate(dot(reflect(ray, n), lamp)), 30.0) * open;
    china += float3(0.5, 0.42, 0.6) * tips * open * open * open * lit;
    return china * (0.94 + 0.08 * Level);
}
