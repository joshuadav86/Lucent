// Name: Levy Curve
// Four hair-thin lines of light fold themselves, step by step, into the lace of the Lévy C curve: every straight piece bends into a corner, then every corner bends again, until the square blossoms into a glowing tapestry. Then it slowly unfolds and begins again, the music running along the line as it goes.
// Family: Fractal
// Tags: Geometric, Minimal
// Added: 2026-10-17 22:02

// The Lévy C curve is made by one simple move: replace a straight line with two lines half as
// long (well, 1/sqrt 2) meeting at a right angle, like a little roof. Do the same to each of
// those, and again, and again. Here the fold of the newest level opens gradually, so the curve
// grows smoothly from one level to the next. Four curves stand on the sides of a square.
//
// Drawing it: for each pixel we search the tree of folds for the nearest piece of line,
// skipping any branch whose whole curve (it always fits in a circle a little larger than its
// span) is too far away to matter.

static const int StackSize = 26;
static const float Cycle = 56.0;      // seconds to fold up, hold, and unfold

// How folded the curve is: 0 is the bare square, the maximum is a dense lace.
float foldLevel()
{
    float most = (float)fractalDepth(13);
    float t = frac(Time / Cycle);
    // Fold up through the first part, hold, then unfold more quickly.
    float up = smoothstep(0.0, 0.55, t);
    float down = smoothstep(0.75, 1.0, t);
    return most * (up - down) + 0.001;
}

// The corner of the roof built on the line from a to b, opened by amount (0 flat, 1 a right angle).
float2 roofCorner(float2 a, float2 b, float amount)
{
    float2 d = b - a;
    float2 apex = a + float2(d.x - d.y, d.x + d.y) * 0.5;
    return lerp((a + b) * 0.5, apex, amount);
}

float segmentDistance(float2 p, float2 a, float2 b, out float along)
{
    float2 pa = p - a;
    float2 ba = b - a;
    along = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-12));
    return length(pa - ba * along);
}

// The four sides of the square, turning slowly.
float2 squareCorner(int k, float size, float spin)
{
    return rotate(float2(-size, -size), spin + k * PI * 0.5);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float folds = foldLevel();
    int leafLevel = (int)ceil(folds);
    float opening = 1.0 - (leafLevel - folds);       // how far the newest fold has opened
    float spin = Time * 0.03 + sin(Time * 0.11) * 0.08;
    float size = 0.3 * (1.0 + Bass * 0.03);

    float2 q = p;

    float4 segments[StackSize];
    float2 info[StackSize];      // level, and where along the whole curve the piece starts (0..4)
    int top = 0;
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        segments[top] = float4(squareCorner(k + 1, size, spin), squareCorner(k, size, spin));
        info[top] = float2(0.0, k);
        top++;
    }

    float reach = 10.0 * px;      // anything further than this from the curve is left dark
    float best = reach;
    float bestAlong = 0.0;
    int visits = 0;
    int budget = qualitySteps(600);
    [loop]
    while (top > 0 && visits < budget)
    {
        visits++;
        top--;
        float4 s = segments[top];
        float2 a = s.xy;
        float2 b = s.zw;
        float level = info[top].x;
        float start = info[top].y;
        float span = exp2(-level);

        if (level >= leafLevel)
        {
            float along;
            float d = segmentDistance(q, a, b, along);
            if (d < best)
            {
                best = d;
                bestAlong = start + along * span;
            }
            continue;
        }

        // The whole curve that grows from this piece fits in this circle; skip it if too far.
        float2 d = b - a;
        float2 centre = a + float2(d.x * 0.5 - d.y * 0.375, d.x * 0.375 + d.y * 0.5);
        if (length(q - centre) - length(d) * 1.2 > best)
        {
            continue;
        }

        float amount = level + 1.0 >= leafLevel ? opening : 1.0;
        float2 m = roofCorner(a, b, amount);
        if (top + 2 <= StackSize)
        {
            // Visit the nearer half first, so the search tightens sooner.
            bool firstNearer = dot(q - (a + m) * 0.5, q - (a + m) * 0.5) < dot(q - (m + b) * 0.5, q - (m + b) * 0.5);
            float4 first = float4(a, m);
            float4 second = float4(m, b);
            segments[top] = firstNearer ? second : first;
            info[top] = float2(level + 1.0, firstNearer ? start + span * 0.5 : start);
            top++;
            segments[top] = firstNearer ? first : second;
            info[top] = float2(level + 1.0, firstNearer ? start : start + span * 0.5);
            top++;
        }
    }

    // The line: crisp and hair-thin, with a soft glow round it. Colour runs along the curve, so
    // each fold keeps its own hue, and the music travels along it.
    float t = bestAlong / 4.0;
    // Each side shades from warm gold to cool teal along its length, the hues drifting slowly.
    float side = frac(t * 4.0);
    float3 warm = hsv2rgb(0.09 + 0.04 * sin(Time * 0.05), 0.75, 1.0);
    float3 cool = hsv2rgb(0.5 + 0.06 * sin(Time * 0.04 + 1.0), 0.8, 1.0);
    float3 tint = lerp(warm, cool, smoothstep(0.0, 1.0, 0.5 - 0.5 * cos(2.0 * PI * side)));
    float level = spectrum(frac(t * 4.0) * 0.7 + 0.02);
    float pulse = exp(-abs(frac(t - Time * 0.05) - 0.5) * 30.0) * Beat;
    float thin = smoothstep(px * 1.3, px * 0.2, best);
    float halo = exp(-best / (px * 3.0)) * (best < reach ? 1.0 : 0.0);
    // Dense lace is drawn a little dimmer per line so the folded curve doesn't blow out.
    float density = lerp(1.0, 0.45, saturate((folds - 6.0) / 7.0));
    float3 colour = tint * (thin * 0.75 + halo * 0.22) * density * (0.75 + level * 0.8 + pulse * 1.2);

    // A dark ground with the faintest warmth in the middle.
    float3 ground = float3(0.006, 0.006, 0.012) + float3(0.012, 0.008, 0.016) * glow(length(p), 2.0);
    return ground + colour + previous(uv) * 0.25;
}
