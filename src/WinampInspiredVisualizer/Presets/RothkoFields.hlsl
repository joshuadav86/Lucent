// Name: Rothko Fields
// Two soft-edged fields of colour float on a deep painted ground, their brushed edges breathing in and out with the bass
// and their hues drifting slowly with the music, like a large quiet canvas seen in low gallery light.
// Family: Minimal
// Tags: Minimal, Calm

float rfSquare(float x)
{
    return x * x;
}

// Brushed texture: noise stretched sideways, the way a wide brush drags paint across the canvas.
float rfBrush(float2 p, float seed)
{
    float2 q = float2(p.x * 1.2, p.y * 4.5) + seed;
    return fbm(q) * 0.8 + noise(float2(p.x * 5.0, p.y * 18.0) + seed) * 0.2;
}

// A soft rectangle of paint: 1 inside, feathering to 0 across ragged, scumbled edges.
float rfField(float2 p, float2 centre, float2 halfSize, float softness, float seed)
{
    float2 d = abs(p - centre) - halfSize;
    float box = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
    // The edge wanders with the brush texture, so it never reads as a hard shape.
    float ragged = (fbm(p * float2(2.0, 3.0) + seed + Time * 0.015) - 0.5) * softness * 1.3;
    return 1.0 - smoothstep(-softness, softness, box + ragged);
}

// Three related hues for the canvas, which drift through a few classic Rothko moods over time.
void rfMood(float t, out float3 ground, out float3 upper, out float3 lower)
{
    float3 groundA = float3(0.20, 0.035, 0.030);
    float3 upperA  = float3(0.90, 0.36, 0.10);
    float3 lowerA  = float3(0.55, 0.07, 0.06);

    float3 groundB = float3(0.035, 0.05, 0.11);
    float3 upperB  = float3(0.18, 0.32, 0.55);
    float3 lowerB  = float3(0.06, 0.10, 0.22);

    float3 groundC = float3(0.11, 0.035, 0.09);
    float3 upperC  = float3(0.80, 0.55, 0.30);
    float3 lowerC  = float3(0.32, 0.08, 0.20);

    // Smoothly visit A, B, C and back again.
    float s = frac(t);
    float wa = saturate(1.0 - abs(s * 3.0 - 0.0) ) + saturate(1.0 - abs(s * 3.0 - 3.0));
    float wb = saturate(1.0 - abs(s * 3.0 - 1.0));
    float wc = saturate(1.0 - abs(s * 3.0 - 2.0));
    float3 w = float3(wa, wb, wc);
    w = w * w * (3.0 - 2.0 * w);
    w /= max(w.x + w.y + w.z, 1e-3);
    ground = groundA * w.x + groundB * w.y + groundC * w.z;
    upper = upperA * w.x + upperB * w.y + upperC * w.z;
    lower = lowerA * w.x + lowerB * w.y + lowerC * w.z;
}

float3 Render(float2 uv, float2 p)
{
    float3 ground;
    float3 upper;
    float3 lower;
    // The mood drifts with time and leans a little with the mids, so the music tints the canvas.
    rfMood(Time * 0.006 + Mid * 0.03, ground, upper, lower);

    // The canvas: a tall frame of fields, centred, with a margin of ground all round.
    float breathe = Bass * 0.018 + Beat * 0.006;
    float sway = sin(Time * 0.11) * 0.01;
    float2 halfUpper = float2(min(Aspect * 0.62, 0.95) + breathe, 0.40 + breathe);
    float2 halfLower = float2(min(Aspect * 0.62, 0.95) + breathe, 0.27 + breathe * 0.8);
    float fieldUpper = rfField(p, float2(sway, -0.30), halfUpper, 0.08 + Bass * 0.025, 3.0);
    float fieldLower = rfField(p, float2(-sway, 0.46), halfLower, 0.075 + Bass * 0.025, 11.0);

    // Paint layers: the ground, then each field scumbled on top with its own brushed texture.
    float groundTexture = rfBrush(p + float2(0.0, Time * 0.004), 1.0);
    float3 colour = ground * (0.75 + 0.5 * groundTexture);

    float upperTexture = rfBrush(p * 1.1 + float2(Time * 0.01, 0.0), 7.0);
    float3 upperPaint = upper * (0.72 + 0.45 * upperTexture);
    // The fields glow softly from within, a little brighter as the music swells.
    upperPaint *= 0.8 + Level * 0.35 + Beat * 0.06;
    upperPaint *= 0.88 + 0.25 * exp(-rfSquare((p.y + 0.3) / 0.35) - rfSquare(rfSquare(p.x / halfUpper.x)));
    colour = lerp(colour, upperPaint, fieldUpper * (0.85 + 0.15 * upperTexture));

    float lowerTexture = rfBrush(p * 1.2 - float2(Time * 0.008, 0.0), 19.0);
    float3 lowerPaint = lower * (0.7 + 0.45 * lowerTexture) * (0.85 + Bass * 0.3);
    lowerPaint *= 0.9 + 0.3 * exp(-rfSquare((p.y - 0.46) / 0.25) - rfSquare(rfSquare(p.x / halfLower.x)));
    colour = lerp(colour, lowerPaint, fieldLower * (0.85 + 0.15 * lowerTexture));

    // Warm gallery light from above and a gentle vignette.
    float light = 1.05 - 0.25 * smoothstep(-0.9, 1.0, p.y) - 0.15 * length(p * float2(0.5, 0.7));
    colour *= light;

    // Fine canvas weave catching the light.
    float weave = sin(uv.x * Resolution.x * 0.9) * sin(uv.y * Resolution.y * 0.9);
    colour *= 1.0 + weave * 0.015;
    return colour;
}
