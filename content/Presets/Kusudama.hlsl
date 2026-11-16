// Name: Kusudama
// A ball of folded white paper hanging on a cord before a grey studio wall: two shells, one inside the other,
// creased into facets and pierced with round holes trimmed in red. It turns on its cord and never stops refolding,
// its creases swinging slowly from a many-sided ball out into a star and back. The music plays in the lamp inside
// it: warm light shines through the paper and out of every hole, throwing spots across the wall, bass at the foot
// of the ball and treble at its crown, and each beat sends a soft ring of that light out across the wall.
// Family: Geometric
// Tags: Geometric, 3D, Minimal, Calm
// Added: 2026-11-16 05:00

static const float KdWall = 1.5;        // how far behind the ball the studio wall stands

static float3x3 KdTurn;                 // how the ball has turned so far
static float3 KdOuter;                  // which way the outer shell's facets face, within one fold of the paper
static float3 KdInner;                  // and the inner shell's
static float KdOuterReach;              // how far out the outer shell's facets lie
static float KdInnerReach;              // and the inner shell's
static float KdHole;                    // how wide the holes are

float kdLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// How loud the music is for a spot at this height, bass at the foot and treble at the crown, read at ten fixed
// places and blended between them.
float kdLoudAt(float height)
{
    float k = saturate(0.5 + 0.5 * height) * 9.0;
    float low = floor(k);
    return lerp(kdLoud(0.08 + 0.06 * low), kdLoud(0.14 + 0.06 * low), smoothstep(0.0, 1.0, k - low));
}

// Fold a spot into the one wedge of the ball that all forty-eight wedges mirror.
float3 kdFold(float3 p)
{
    p = abs(p);
    if (p.x < p.y)
        p.xy = p.yx;
    if (p.x < p.z)
        p.xz = p.zx;
    if (p.y < p.z)
        p.yz = p.zy;
    return p;
}

// How far a spot in the wedge is from the middle of its hole.
float kdFromHole(float3 q)
{
    float3 axis = float3(0.8259, 0.5207, 0.2157);
    return length(q - axis * dot(q, axis));
}

// How far out a shell's facets must lie for its farthest corner to stand at the given distance.
float kdReach(float3 facing, float corner)
{
    return corner * min(facing.x, min((facing.x + facing.y) * 0.7071, (facing.x + facing.y + facing.z) * 0.5774));
}

// How far a spot is outside the paper.
float kdPaper(float3 p)
{
    float3 q = kdFold(mul(KdTurn, p));
    float pierced = KdHole - kdFromHole(q);
    float outerShell = max(abs(dot(q, KdOuter) - KdOuterReach) - 0.012, pierced);
    float innerShell = max(abs(dot(q, KdInner) - KdInnerReach) - 0.01, KdHole * 0.5 - kdFromHole(q));
    float cord = max(length(p.xz) - 0.007, 0.2 - p.y);
    return min(min(outerShell, innerShell) * 0.8, cord);
}

// How far a spot is outside the ball taken as a solid, for the shadow it throws.
float kdSolid(float3 p)
{
    return (dot(kdFold(mul(KdTurn, p)), KdOuter) - KdOuterReach) * 0.8;
}

float3 kdNormal(float3 p)
{
    float2 h = float2(0.003, 0.0);
    return normalize(float3(kdPaper(p + h.xyy) - kdPaper(p - h.xyy),
                            kdPaper(p + h.yxy) - kdPaper(p - h.yxy),
                            kdPaper(p + h.yyx) - kdPaper(p - h.yyx)));
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since) * (1.0 - since);

    float sa, ca, sb, cb;
    sincos(Time * 0.17, sa, ca);
    sincos(0.45, sb, cb);
    KdTurn = float3x3(ca * cb, -ca * sb, -sa, sb, cb, 0.0, sa * cb, -sa * sb, ca);
    KdOuter = normalize(float3(1.0, 0.2 + 0.36 * sin(Time * 0.13), 0.1 + 0.26 * sin(Time * 0.09 + 1.7)));
    KdInner = normalize(float3(1.0, 0.3 + 0.3 * sin(Time * 0.1 + 2.3), 0.15 + 0.2 * sin(Time * 0.15 + 0.6)));
    KdOuterReach = kdReach(KdOuter, 0.86);
    KdInnerReach = kdReach(KdInner, 0.5);
    KdHole = KdOuterReach * (0.13 + 0.018 * sin(Time * 0.21));

    float3 eye = float3(0.3 * sin(Time * 0.07), 0.12, -3.2);
    float3 ray = cameraRay(eye, float3(0.0, 0.03, 0.0), float2(p.x, -p.y), 2.7, 0.0);
    float3 lamp = normalize(float3(-0.5, 0.62, -0.6));

    // Walk the rays that pass near the ball or its cord.
    float t = 1e4;
    float walk = 1.8;
    int steps = qualitySteps(100);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = kdPaper(eye + ray * walk);
        if (d < 0.0015)
        {
            t = walk;
            break;
        }

        walk += d;
        if (walk > 5.2)
            break;
    }

    float3 colour;
    if (t < 1e3)
    {
        float3 at = eye + ray * t;
        float3 n = kdNormal(at);
        float3 q = kdFold(mul(KdTurn, at));
        float fromCentre = length(at);

        // How open each spot is to the room's light.
        float open = 1.0;
        [unroll]
        for (int k = 1; k <= 4; k++)
        {
            float reach = 0.045 * k;
            open -= (reach - kdPaper(at + n * reach) / 0.8) * 0.9 / k;
        }

        open = saturate(open);
        float lit = saturate(dot(n, lamp));
        float3 paper = float3(0.56, 0.55, 0.52) * (0.3 + 0.7 * lit) * (0.35 + 0.65 * open) + float3(0.07, 0.08, 0.1) * open;

        // The holes' red trim.
        float onBall = 1.0 - step(0.012, 0.02 - length(at.xz)) * step(0.2, at.y);
        float outerFace = step(abs(dot(q, KdOuter) - KdOuterReach), 0.03);
        float trim = smoothstep(0.03, 0.016, (kdFromHole(q) - KdHole) / KdOuterReach) * onBall * outerFace;
        paper = lerp(paper, float3(0.62, 0.05, 0.05) * (0.45 + 0.55 * lit), trim);

        // The lamp inside warms every face that looks at it.
        float loud = kdLoudAt(at.y / 0.86);
        float3 fromLamp = at / max(fromCentre, 1e-3);
        float inward = saturate(dot(n, -fromLamp)) * onBall;
        float glow = 0.1 + 1.1 * loud + 0.5 * flash;
        paper += float3(1.5, 0.7, 0.22) * inward * glow * 0.5 / (0.15 + fromCentre * fromCentre);

        // And shines through the paper itself, strongest where a face looks squarely away from it.
        float through = saturate(dot(n, fromLamp)) * onBall * (1.0 - trim);
        paper += float3(1.25, 0.62, 0.24) * through * through * (0.03 + 1.3 * loud + 0.35 * flash) * 0.42 / (0.12 + fromCentre * fromCentre);
        colour = paper;
    }
    else
    {
        // The wall behind, paler toward the top, with the ball's soft shadow thrown down it to the right.
        float tWall = (KdWall - eye.z) / ray.z;
        float3 at = eye + ray * tWall;
        float3 wall = lerp(float3(0.27, 0.27, 0.29), float3(0.4, 0.4, 0.42), smoothstep(-1.2, 1.2, at.y));
        float shade = 1.0;
        float along = 0.5;
        [loop]
        for (int s = 0; s < 20; s++)
        {
            float d = kdSolid(at + lamp * along);
            shade = min(shade, 4.0 * d / along);
            along += clamp(d, 0.04, 0.3);
            if (shade < 0.02 || along > 4.5)
                break;
        }

        wall *= 0.5 + 0.5 * saturate(shade);

        // The lamp throws a spot of light on the wall through every hole that faces it.
        float far2 = dot(at, at);
        float3 toWall = at / sqrt(far2);
        float3 q = kdFold(mul(KdTurn, toWall));
        float3 onOuter = q * KdOuterReach / max(dot(q, KdOuter), 0.2);
        float3 onInner = q * KdInnerReach / max(dot(q, KdInner), 0.2);
        float spot = smoothstep(KdHole, KdHole * 0.6, kdFromHole(onOuter)) * smoothstep(KdHole * 0.5, KdHole * 0.3, kdFromHole(onInner));
        wall += float3(1.5, 0.78, 0.3) * spot * (0.04 + 1.1 * kdLoudAt(at.y / 1.6) + 0.3 * flash) * 2.6 * toWall.z / far2;

        // Its light reaches the wall round the ball too, and each beat sends a ring of it outward.
        float out2 = length(at.xy);
        wall += float3(1.4, 0.65, 0.2) * (0.03 + 0.24 * kdLoud(0.2)) * exp(-out2 * out2 * 0.7);
        wall += float3(1.3, 0.75, 0.35) * 0.08 * flash * exp(-(out2 - 0.7 - since * 2.6) * (out2 - 0.7 - since * 2.6) * 7.0);
        colour = wall * (1.0 - 0.1 * dot(p, p) / (1.0 + Aspect * Aspect));
    }

    return colour;
}
