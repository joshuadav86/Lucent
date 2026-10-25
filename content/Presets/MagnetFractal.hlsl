// Name: Magnet Fractal
// A fractal born from a physics model of magnetism: a fat, rounded island ringed with froth and chains of glowing bubbles. The view drifts slowly down into the froth and back out, its filaments glowing brighter with the music.
// Family: Fractal
// Tags: Colourful
// Added: 2026-10-17 21:50

float2 magnetMultiply(float2 a, float2 b)
{
    return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

float2 magnetDivide(float2 a, float2 b)
{
    return float2(a.x * b.x + a.y * b.y, a.y * b.x - a.x * b.y) / max(dot(b, b), 1.0e-30);
}

float3 magnetGradient(float t)
{
    // Copper, violet, ice blue and sea green, cycling.
    float3 c = 0.5 + 0.5 * cos(2.0 * PI * (t + float3(0.05, 0.25, 0.45)));
    return c * c * float3(1.0, 0.95, 1.05);
}

// Where the camera is at time t: centre, half the view height, and turn.
// A slow breath: out to the whole island and down into the froth near its tip, over about a minute and a half.
float3 magnetView(float t, out float turn)
{
    float dive = 0.5 - 0.5 * cos(t * 2.0 * PI / 90.0);
    float halfHeight = 2.4 * exp(-dive * 3.6);   // from 2.4 down to about 0.065
    float2 overview = float2(1.2, 0.0);
    float2 target = float2(1.315, -0.035);
    float2 centre = lerp(target, overview, saturate((halfHeight - 0.065) / (2.4 - 0.065)));
    turn = 0.25 * sin(t * 0.021);
    return float3(centre, halfHeight);
}

float3 Render(float2 uv, float2 p)
{
    float turn;
    float3 view = magnetView(Time, turn);
    float2 centre = view.xy;
    float halfHeight = view.z;
    // Each frame looks through a slightly different spot inside the pixel; blending frames then smooths
    // the froth, which is far finer than a pixel, into soft glowing texture.
    float2 jitter = float2(hash(uv * 371.0 + frac(Time * 7.31)), hash(uv * 529.0 + frac(Time * 3.17))) - 0.5;
    float2 c = centre + rotate(float2(p.x, -p.y) + jitter * pixelSize(), turn) * halfHeight;

    // z -> ((z^2 + c - 1) / (2z + c - 2))^2, starting at 0. Points either run off to infinity or settle on 1.
    float2 z = 0.0;
    float n = 0.0;
    int outcome = 0;   // 1 = escaped, 2 = settled on 1
    float trap = 1.0e3;
    int maxIterations = qualitySteps(220);
    [loop]
    for (int i = 0; i < maxIterations; i++)
    {
        float2 top = magnetMultiply(z, z) + c - float2(1.0, 0.0);
        float2 bottom = 2.0 * z + c - float2(2.0, 0.0);
        float2 g = magnetDivide(top, bottom);
        z = magnetMultiply(g, g);
        trap = min(trap, abs(length(z) - 1.0));
        if (dot(z, z) > 1.0e4)
        {
            outcome = 1;
            break;
        }
        float2 offset = z - float2(1.0, 0.0);
        if (dot(offset, offset) < 1.0e-6)
        {
            outcome = 2;
            break;
        }
        n += 1.0;
    }

    float hue = Time * 0.01 + (BeatCount - Beat) * 0.01;
    float3 colour = 0.0;
    if (outcome == 1)
    {
        float smoothN = n + 1.0 - log2(log(dot(z, z)) * 0.5 / log(100.0));
        // Escaping froth: warm, brighter the longer it lingered. The hue changes only slowly with the
        // count, so the froth reads as texture rather than confetti.
        float glowAmount = 1.0 - exp(-smoothN * 0.08);
        float3 tint = magnetGradient(hue + 0.08 * log2(1.0 + smoothN) + smoothN * 0.02);
        float bands = 0.75 + 0.25 * cos(smoothN * 1.2 - Time * 0.8);   // equipotential ripples flowing inward
        colour = tint * (0.035 + 0.06 * bands * bands + 0.1 * glowAmount * bands + glowAmount * glowAmount * glowAmount * glowAmount * (0.7 + Mid * 0.6));
    }
    else if (outcome == 2)
    {
        // Settled on 1. It closes in very fast, so the step count is smoothed by how close the last step landed.
        float closeness = log(max(length(z - float2(1.0, 0.0)), 1.0e-12));
        float smoothN = n + 1.0 - log2(max(closeness / log(1.0e-3), 1.0e-6));
        float glowAmount = 1.0 - exp(-smoothN * 0.06);
        float3 tint = magnetGradient(hue + 0.5 + 0.08 * log2(1.0 + smoothN));
        float bands = 0.75 + 0.25 * cos(smoothN * 1.1 - Time * 0.7);
        colour = tint * (0.03 + 0.05 * bands * bands + 0.1 * glowAmount * bands + glowAmount * glowAmount * glowAmount * glowAmount * (0.75 + Bass * 0.7));
    }
    else
    {
        // Undecided: the heart of the island, a dark mirror with faint rings.
        // Shaded by where on its final cycle the orbit ended, so the island has soft sheen instead of a hole.
        float sheen = 0.5 + 0.5 * cos(atan2(z.y, z.x) * 2.0 + Time * 0.2);
        colour = magnetGradient(hue + 0.25 + 0.15 * sheen) * (0.015 + 0.03 * sheen + 0.08 * exp(-trap * 14.0) * (0.6 + Level));
    }

    // Beats flare the froth gently.
    colour *= 1.0 + Beat * 0.25 + Treble * 0.2;

    // Find where this point was on screen last frame and blend with it.
    float lastTurn;
    float3 lastView = magnetView(Time - DeltaTime, lastTurn);
    float2 cCentre = centre + rotate(float2(p.x, -p.y), turn) * halfHeight;
    float2 lastP = rotate((cCentre - lastView.xy) / lastView.z, -lastTurn);
    float3 history = previous(toUv(float2(lastP.x, -lastP.y)));
    return lerp(colour, history, 0.82);
}
