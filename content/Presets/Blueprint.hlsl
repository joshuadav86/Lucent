// Name: Blueprint
// A draughtsman's drawing of a steel nut, in white line on blue: outlined, shaded with hatching that follows
// each face, set inside a dashed circle with centre lines through it. The nut turns with the beat, so the
// hatching redraws itself as the faces swing into shade. The rule along the bottom measures the music, bass
// at the left, and the lines thicken with the bass.
// Family: Minimal
// Tags: 3D, Retro, Geometric
// Added: 2026-11-15 21:11

// A six-sided nut with a round hole.
float bpNut(float3 x)
{
    float3 q = abs(x);
    float six = max(q.x * 0.8660254 + q.z * 0.5, q.z) - 0.8;
    float d = max(six, q.y - 0.27);
    return max(d, 0.47 - length(x.xz)) - 0.015;
}

// One set of ruled lines across the page at `angle`: 1 on a line. `weight` is how much of the page they cover.
float bpHatch(float2 p, float angle, float weight)
{
    float across = dot(p, float2(cos(angle), sin(angle))) * 30.0;
    float fromLine = abs(frac(across) - 0.5);
    float halfWide = 0.5 * saturate(weight);
    return smoothstep(halfWide + 0.07, halfWide - 0.07, fromLine) * step(0.02, weight);
}

float3 Render(float2 uv, float2 p)
{
    float turn = Time * 0.22 + BeatGlide * 0.3;
    float tip = 1.08 + 0.3 * sin(turn * 0.37);
    float lean = 0.35 * sin(turn * 0.53);
    float heavy = 1.0 + 0.5 * Bass;
    float px = pixelSize();

    // The sheet: blue, with a faint squared grid.
    float3 colour = float3(0.03, 0.2, 1.0) * (0.96 + 0.04 * noise(p * 150.0)) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.7);
    float2 fine = abs(frac(p * 5.0 + 0.5) - 0.5) / 5.0;
    float2 bold = abs(frac(p + 0.5) - 0.5);
    float ruled = 0.1 * smoothstep(px * 1.5, 0.0, min(fine.x, fine.y)) + 0.14 * smoothstep(px * 2.0, 0.0, min(bold.x, bold.y));

    // Look at the nut. The camera turns round it, so turn what it sees back to the page afterwards.
    float3 eye = float3(0.0, 0.0, -3.4);
    float3 ray = normalize(float3(p.x, -p.y - 0.1, 2.4));
    eye.yz = rotate(eye.yz, tip);
    ray.yz = rotate(ray.yz, tip);
    eye.xy = rotate(eye.xy, lean);
    ray.xy = rotate(ray.xy, lean);
    eye.xz = rotate(eye.xz, turn);
    ray.xz = rotate(ray.xz, turn);

    float solid = 0.0;
    float close = 1.0;
    float t = 0.0;
    float b = dot(eye, ray);
    float h = b * b - dot(eye, eye) + 1.1;
    if (h > 0.0)
    {
        t = max(-b - sqrt(h), 0.0);
        float leave = -b + sqrt(h);
        int steps = qualitySteps(56);

        [loop]
        for (int i = 0; i < steps; i++)
        {
            float d = bpNut(eye + ray * t);
            close = min(close, d);
            if (d < 0.002)
            {
                solid = 1.0;
                break;
            }

            t += d;
            if (t > leave) break;
        }
    }

    float ink = 0.0;
    if (solid > 0.5)
    {
        float3 at = eye + ray * t;
        float2 e = float2(0.004, 0.0);
        float3 n = normalize(float3(
            bpNut(at + e.xyy) - bpNut(at - e.xyy),
            bpNut(at + e.yxy) - bpNut(at - e.yxy),
            bpNut(at + e.yyx) - bpNut(at - e.yyx)));
        n.xz = rotate(n.xz, -turn);
        n.xy = rotate(n.xy, -lean);
        n.yz = rotate(n.yz, -tip);

        // Hatching: none on the faces that look at the lamp, close lines on the ones that look away, and a
        // second set across the first where it is darkest. The lines lie along each face.
        float shade = 1.0 - saturate(dot(n, normalize(float3(-0.55, 0.6, -0.6))) * 0.85 + 0.15);
        float lie = atan2(n.y, n.x + 1e-5) + 0.6;
        ink = bpHatch(p, lie, shade * 0.5 * heavy);
        ink = max(ink, bpHatch(p, lie + 1.2, (shade - 0.55) * 0.9 * heavy));
    }
    else
    {
        // The outline, the dashed circle round the nut, the centre lines, and the nut's shadow in loose hatching.
        ink = smoothstep(0.02 * heavy, 0.011 * heavy, close);
        float2 q = p + float2(0.0, 0.1);
        float r = length(q);
        float round1 = atan2(q.y, q.x + 1e-5);
        float circle = smoothstep(px * 2.5, px, abs(r - 0.84 - 0.03 * BeatSwell)) * step(0.35, frac(round1 * 9.0 + turn * 0.4));
        float dashes = step(0.3, frac(max(abs(q.x), abs(q.y)) * 6.0)) * step(r, 1.1);
        float centre = smoothstep(px * 2.0, px * 0.8, min(abs(q.x), abs(q.y))) * dashes;
        ink = max(ink, max(circle, centre) * 0.8);
    }

    // The rule along the bottom: one tick for each part of the music.
    float2 ruleAt = float2(p.x / Aspect * 0.5 + 0.5, 0.93 - p.y);
    float tick = floor(ruleAt.x * 48.0);
    float fromTick = abs(frac(ruleAt.x * 48.0) - 0.5);
    float tall = 0.02 + 0.2 * spectrum(0.01 + 0.62 * (tick + 0.5) / 48.0);
    float rule = smoothstep(0.2, 0.12, fromTick) * step(0.0, ruleAt.y) * step(ruleAt.y, tall);
    rule = max(rule, smoothstep(px * 2.0, px, abs(ruleAt.y)));
    ink = max(ink, rule * 0.9);

    colour += float3(0.25, 0.45, 0.6) * ruled * (1.0 - ink);
    colour = lerp(colour, float3(1.08, 1.12, 1.16), ink);
    return colour * (0.94 + 0.08 * Level);
}
