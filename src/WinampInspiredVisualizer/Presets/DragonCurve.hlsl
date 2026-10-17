// Name: Dragon Curve
// A strip of light folds in half again and again, each fold swinging open on the beat, until it becomes the
// Heighway dragon; four dragons wind out from one point and lock together like tiles, coloured by the fold that placed them.
// Family: Fractal
// Tags: Geometric, Colourful

static const float DragonFirstLevel = 5.0;
static const float DragonBeatsPerFold = 2.0;
static const float DragonHoldBeats = 14.0;
static const int DragonStack = 24;

float2 dcMul(float2 a, float2 b)
{
    return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

// (1 - i) raised to the power n: the far end of an n-times folded strip of unit segments.
float2 dcEnd(float n)
{
    float r = pow(2.0, n * 0.5);
    float a = -n * PI * 0.25;
    return float2(cos(a), sin(a)) * r;
}

float dcSegment(float2 p, float2 a, float2 b, out float h)
{
    float2 pa = p - a;
    float2 ba = b - a;
    h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-8));
    return length(pa - ba * h);
}

// Distance from q to the folded strip, searched depth-first through the folds with an explicit stack.
// Each piece of the strip is a smaller dragon between two points; a piece is skipped when its bounding circle is
// further away than the best distance found so far. Also returns where along the strip the nearest point is (0..1 per root)
// and the depth of the fold that brought the nearest segment into place.
float dcDistance(float2 q, float level, float2 rootA[8], float2 rootB[8], float cutoff, out float along, out float foldDepth, out float root)
{
    float4 stackAB[DragonStack];
    // Per piece: folds left, where it starts along the strip, its length along the strip,
    // and a packed word: root * 64 + fold depth * 2 + (1 when it turns right instead of left).
    float4 stackInfo[DragonStack];
    int top = 0;
    [unroll]
    for (int r = 0; r < 8; r++)
    {
        stackAB[r] = float4(rootA[r], rootB[r]);
        stackInfo[r] = float4(level, 0.0, 1.0, r * 64.0);
    }
    top = 8;

    float best = cutoff;
    float segments = pow(2.0, level);
    along = 0.0;
    foldDepth = 0.0;
    root = 0.0;
    [loop]
    for (int iteration = 0; iteration < 1400 && top > 0; iteration++)
    {
        top--;
        float4 ab = stackAB[top];
        float4 info = stackInfo[top];
        float2 a = ab.xy;
        float2 b = ab.zw;
        float span = length(b - a);
        // A dragon between a and b always fits in a circle of 0.84 x their distance round their midpoint
        // (plus half a segment for the cut corners).
        if (length(q - (a + b) * 0.5) - span * 0.85 - 0.5 > best)
        {
            continue;
        }
        if (info.x < 0.5)
        {
            // A single segment of the strip. The strip is drawn through the segments' midpoints with every corner
            // cut off at 45 degrees, so the path never touches itself and its winding stays readable.
            int index = (int)round(info.y * segments);
            int k = index + 1;
            int oddPart = k / (k & (-k));
            float turn = (oddPart & 3) == 1 ? -1.0 : 1.0;
            float2 dir = b - a;
            float2 mid = (a + b) * 0.5;
            float2 nextMid = b + float2(-dir.y, dir.x) * (0.5 * turn);
            bool last = index >= (int)segments - 1;
            float h = 0.0;
            float d = dcSegment(q, mid, last ? b : nextMid, h);
            if (index == 0)
            {
                float h0 = 0.0;
                d = min(d, dcSegment(q, a, mid, h0));
            }
            if (d < best)
            {
                best = d;
                along = info.y + info.z * (0.5 + 0.5 * h);
                root = floor(info.w / 64.0);
                foldDepth = floor((info.w - root * 64.0) / 2.0);
            }
            continue;
        }
        float rootPacked = floor(info.w / 64.0) * 64.0;
        float rest = info.w - rootPacked;
        float depth = floor(rest / 2.0);
        float turnSign = rest - depth * 2.0 > 0.5 ? -1.0 : 1.0;
        // Fold: the corner c sits a quarter turn off the line from a to b; the first half turns left, the second right.
        float2 c = a + dcMul(b - a, float2(0.5, 0.5 * turnSign));
        float childLevel = info.x - 1.0;
        float4 firstInfo = float4(childLevel, info.y, info.z * 0.5, rootPacked + depth * 2.0);
        // The second half was laid in place by this fold, so it takes this fold's depth.
        float4 secondInfo = float4(childLevel, info.y + info.z * 0.5, info.z * 0.5, rootPacked + info.x * 2.0 + 1.0);
        if (top + 2 <= DragonStack)
        {
            // Push the nearer half last so it is searched first, which lets the search skip more.
            bool firstNearer = dot(q - (a + c) * 0.5, q - (a + c) * 0.5) < dot(q - (c + b) * 0.5, q - (c + b) * 0.5);
            float4 nearAB = firstNearer ? float4(a, c) : float4(c, b);
            float4 farAB = firstNearer ? float4(c, b) : float4(a, c);
            float4 nearInfo = firstNearer ? firstInfo : secondInfo;
            float4 farInfo = firstNearer ? secondInfo : firstInfo;
            stackAB[top] = farAB;
            stackInfo[top] = farInfo;
            stackAB[top + 1] = nearAB;
            stackInfo[top + 1] = nearInfo;
            top += 2;
        }
    }
    return best;
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    // ---- Which fold we are on: one fold every two beats, then a pause, then the strip starts again ----
    // The strip is folded up to 12 times on Normal detail (one fewer on Low, one more on Ultra).
    float maxLevel = (float)fractalDepth(12);
    float foldBeats = (maxLevel - DragonFirstLevel) * DragonBeatsPerFold;
    float cycle = foldBeats + DragonHoldBeats;
    float beatInCycle = fmod(BeatCount, cycle);
    float foldIndex = min(floor(beatInCycle / DragonBeatsPerFold), maxLevel - DragonFirstLevel - 1.0);
    bool folding = beatInCycle < foldBeats && fmod(beatInCycle, DragonBeatsPerFold) < 0.5;
    // Each fold swings open as the beat fades.
    float swing = folding ? smoothstep(0.0, 1.0, saturate((1.0 - Beat) * 1.3)) : 1.0;
    // The fold being animated turns a strip of 'level' folds into one of level + 1.
    float level = foldIndex + DragonFirstLevel;
    float grown = level - 1.0 + swing;

    // ---- Camera: zooms out by the square root of two per fold so the figure keeps its size ----
    float zoom = pow(2.0, (grown + 1.0) * 0.5) * (1.15 + 0.05 * sin(Time * 0.1)) * (1.0 - Bass * 0.04);
    float turn = grown * PI * 0.25 + Time * 0.03;
    float2 q = rotate(p, turn) * zoom;
    float worldPixel = px * zoom;

    // ---- The eight pieces: four dragons round the centre, each made of a fixed half and a half swinging open ----
    float2 halfEnd = dcEnd(level);
    float angle = swing * PI * 0.5;
    float2 swingStart = halfEnd + rotate(-halfEnd, angle);
    float2 rootA[8];
    float2 rootB[8];
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float quarter = k * PI * 0.5;
        rootA[k] = float2(0.0, 0.0);
        rootB[k] = rotate(halfEnd, quarter);
        rootA[k + 4] = rotate(swingStart, quarter);
        rootB[k + 4] = rotate(halfEnd, quarter);
    }

    float glowReach = worldPixel * 4.0;
    float along = 0.0;
    float foldDepth = 0.0;
    float root = 0.0;
    float d = dcDistance(q, level, rootA, rootB, glowReach, along, foldDepth, root);

    // ---- Colour: each segment takes the colour of the fold that placed it, shading along the strip ----
    float depthFraction = foldDepth / maxLevel;
    float swingingHalf = root > 3.5 ? 1.0 : 0.0;
    float strip = (swingingHalf > 0.5 ? 2.0 - along : along) * 0.5;
    // Each of the four dragons has its own hue; the fold that placed a segment shifts its shade and depth of colour.
    float copy = fmod(root, 4.0);
    float baseHue = copy < 0.5 ? 0.53 : (copy < 1.5 ? 0.76 : (copy < 2.5 ? 0.09 : 0.93));
    float hue = baseHue + depthFraction * 0.07 + strip * 0.04 + sin(Time * 0.02) * 0.03;
    float3 tint = hsv2rgb(hue, 0.45 + 0.35 * depthFraction, 1.0);
    float segmentPixels = 1.0 / max(worldPixel, 1e-5);
    float width = max(0.7, min(1.6, segmentPixels * 0.08));
    float lineCover = saturate(width + 0.5 - d / worldPixel);
    float halo = exp(-d / (worldPixel * 1.5)) * 0.25 * saturate(4.0 - d / worldPixel);
    float brightness = 0.75 + 0.35 * Level + 0.3 * Treble * depthFraction;
    // The half that is swinging open shines a little brighter while it moves.
    brightness *= 1.0 + 0.35 * swingingHalf * (1.0 - swing) + 0.15 * Beat;

    // Fade out at the end of the pause and back in as the strip begins again.
    float fade = smoothstep(cycle, cycle - 1.5, beatInCycle) * smoothstep(-0.2, 0.6, beatInCycle);
    float3 colour = tint * (lineCover * 0.9 + halo) * brightness * max(fade, 0.0);

    // A deep, faintly lit ground.
    float3 ground = float3(0.006, 0.007, 0.014) + float3(0.01, 0.012, 0.025) * exp(-dot(p, p) * 0.8);
    return ground + colour;
}
