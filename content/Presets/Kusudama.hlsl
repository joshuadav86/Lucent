// Name: Kusudama
// A ball of folded white paper hanging on a cord before a grey studio wall: two shells, one inside the other and
// turning the other way, creased into facets and pierced with round holes trimmed in red. It dances on its cord:
// it swings from side to side once in every four beats, twists as it turns, and refolds on every beat, its creases
// drawing in to a star and letting out to a many-sided ball. The music plays in the lamp inside it: light pours
// through the paper and out of every hole, throwing spots across the wall, bass at the foot of the ball and treble
// at its crown. The lamp swells on each beat, sends a ring of light out across the wall, and takes a new colour
// every eight beats.
// Family: Geometric
// Tags: Geometric, 3D, Minimal, Calm
// Added: 2026-11-16 05:00

static const float KdWall = 1.5;        // how far behind the ball the studio wall stands
static const float KdHung = 2.6;        // how far above the ball its cord is tied

static float3x3 KdTurn;                 // how the outer shell has turned so far
static float3x3 KdTurnIn;               // and the inner shell, which turns the other way
static float3 KdOuter;                  // which way the outer shell's facets face, within one fold of the paper
static float3 KdInner;                  // and the inner shell's
static float KdOuterReach;              // how far out the outer shell's facets lie
static float KdInnerReach;              // and the inner shell's
static float KdHole;                    // how wide the holes are
static float KdSound;                   // how loud the music is just now, taken across all its parts

// One part of the music, with the high notes made up to stand level with the low ones.
float kdPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one.
float kdLoud(float x)
{
    return saturate((kdPart(x) - 0.62 * KdSound) / (0.6 * KdSound + 0.1));
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
    float3 r = kdFold(mul(KdTurnIn, p));
    float outerShell = max(abs(dot(q, KdOuter) - KdOuterReach) - 0.012, KdHole - kdFromHole(q));
    float innerShell = max(abs(dot(r, KdInner) - KdInnerReach) - 0.01, KdHole * 0.5 - kdFromHole(r));
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

// A turn about the cord by one angle, leaning over by another.
float3x3 kdTurned(float spin, float lean)
{
    float sa, ca, sb, cb;
    sincos(spin, sa, ca);
    sincos(lean, sb, cb);
    return float3x3(ca * cb, -ca * sb, -sa, sb, cb, 0.0, sa * cb, -sa * sb, ca);
}

// The lamp's colours, one for every eight beats: amber, rose, sea green, and violet.
float3 kdLampColour(float which)
{
    float k = wrap(which, 4.0);
    float3 c = k < 0.5 ? float3(1.5, 0.72, 0.22) : k < 1.5 ? float3(1.55, 0.3, 0.42) : k < 2.5 ? float3(0.25, 1.15, 0.85) : float3(0.8, 0.45, 1.6);
    return c;
}

// The same turn about the line the ball swings round, for a place or a direction.
float2 kdSwung(float2 v, float2 swing)
{
    return float2(swing.x * v.x + swing.y * v.y, swing.x * v.y - swing.y * v.x);
}

float3 Render(float2 uv, float2 p)
{
    float beats = wrap(BeatGlide, 4096.0);
    // How far through the beat: with a steady beat to follow it keeps pace with it, without one it follows how
    // far the last beat has faded. (The glide of the beat count rests between beats, so it can't serve here.)
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since) * (1.0 - since);
    float swell = BeatSwell;
    float beatTurn = 6.2832 * BeatPhase;
    float barTurn = 6.2832 * BarPhase;

    KdSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        KdSound += kdPart(0.08 + 0.12 * part) / 6.0;

    // The lamp's colour, changing over the first beat of every eight.
    float phrase = beats / 8.0;
    float3 lampColour = lerp(kdLampColour(floor(phrase) - 1.0), kdLampColour(floor(phrase)), smoothstep(0.0, 0.12, frac(phrase)));
    float3 paleLamp = lerp(lampColour, float3(1.3, 1.1, 0.9), 0.3);

    // The shells turn against each other, twisting back and forth with the bar, and refold on every beat.
    float twist = 0.3 * sin(barTurn + 1.2);
    float drawn = cos(beatTurn);
    KdTurn = kdTurned(Time * 0.24 + twist, 0.45);
    KdTurnIn = kdTurned(-Time * 0.31 - twist, 1.1 + 0.3 * sin(Time * 0.11));
    KdOuter = normalize(float3(1.0, 0.2 + 0.3 * sin(Time * 0.13) + 0.14 * drawn, 0.1 + 0.22 * sin(Time * 0.09 + 1.7) - 0.09 * drawn));
    KdInner = normalize(float3(1.0, 0.3 + 0.26 * sin(Time * 0.1 + 2.3) - 0.12 * drawn, 0.15 + 0.2 * sin(Time * 0.15 + 0.6)));
    KdOuterReach = kdReach(KdOuter, 0.86);
    KdInnerReach = kdReach(KdInner, 0.5);
    KdHole = KdOuterReach * (0.135 + 0.018 * sin(Time * 0.21) + 0.03 * swell);

    // The ball swings on its cord once in every bar, and a little by itself. Everything below is worked out as
    // the ball sees it, hanging straight, with the room swung the other way.
    float2 swing;
    sincos(0.1 * sin(barTurn) + 0.04 * sin(Time * 0.83), swing.y, swing.x);
    float3 eye = float3(0.5 * sin(Time * 0.17), 0.12 + 0.1 * sin(Time * 0.11), -3.2);
    float3 ray = cameraRay(eye, float3(0.0, 0.03, 0.0), float2(p.x, -p.y), 2.7, 0.0);
    float3 lamp = normalize(float3(-0.5, 0.62, -0.6));
    eye.y -= KdHung;
    eye.xy = kdSwung(eye.xy, swing);
    eye.y += KdHung;
    ray.xy = kdSwung(ray.xy, swing);
    lamp.xy = kdSwung(lamp.xy, swing);

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
        float glow = 0.4 + 1.2 * loud + 0.9 * swell;
        paper += lampColour * inward * glow * 0.5 / (0.15 + fromCentre * fromCentre);

        // And shines through the paper itself, strongest where a face looks squarely away from it.
        float through = saturate(dot(n, fromLamp)) * onBall * (1.0 - trim);
        paper += paleLamp * through * through * (0.2 + 1.3 * loud + 0.7 * swell) * 0.42 / (0.12 + fromCentre * fromCentre);
        colour = paper;
    }
    else
    {
        // The wall behind, paler toward the top, with the ball's soft shadow thrown down it to the right.
        float tWall = (KdWall - eye.z) / ray.z;
        float3 at = eye + ray * tWall;
        float wallHeight = KdHung + swing.x * (at.y - KdHung) + swing.y * at.x;
        float3 wall = lerp(float3(0.27, 0.27, 0.29), float3(0.4, 0.4, 0.42), smoothstep(-1.2, 1.2, wallHeight));
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
        float3 r = kdFold(mul(KdTurnIn, toWall));
        float3 onOuter = q * KdOuterReach / max(dot(q, KdOuter), 0.2);
        float3 onInner = r * KdInnerReach / max(dot(r, KdInner), 0.2);
        float spot = smoothstep(KdHole, KdHole * 0.6, kdFromHole(onOuter)) * (0.3 + 0.7 * smoothstep(KdHole * 0.5, KdHole * 0.3, kdFromHole(onInner)));
        wall += paleLamp * spot * (0.3 + 1.1 * kdLoudAt(at.y / 1.6) + 0.7 * swell) * 2.6 * toWall.z / far2;

        // Its light reaches the wall round the ball too, and each beat sends a ring of it outward.
        float out2 = length(at.xy);
        wall += lampColour * (0.1 + 0.3 * kdLoud(0.2) + 0.22 * swell) * exp(-out2 * out2 * 0.7);
        wall += paleLamp * 0.3 * flash * exp(-(out2 - 0.7 - since * 2.6) * (out2 - 0.7 - since * 2.6) * 7.0);
        colour = wall * (1.0 - 0.1 * dot(p, p) / (1.0 + Aspect * Aspect));
    }

    return colour;
}
