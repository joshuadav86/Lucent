// Name: Nova Fractal
// Newton's method for finding roots, deliberately knocked off balance: ornate chains of jewelled lace and scrolling paisley grow along the seams between calm pools of colour. Its parameters drift slowly so the ornaments unfurl and reshape, swelling with the bass.
// Family: Fractal
// Tags: Colourful
// Added: 2026-10-17 21:50

float2 novaMultiply(float2 a, float2 b)
{
    return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

float2 novaDivide(float2 a, float2 b)
{
    return float2(a.x * b.x + a.y * b.y, a.y * b.x - a.x * b.y) / max(dot(b, b), 1.0e-30);
}

float3 novaGradient(float t)
{
    // Garnet, gold, jade and sapphire.
    float3 c = 0.5 + 0.5 * cos(2.0 * PI * (t + float3(0.0, 0.15, 0.4)));
    return c * c * 1.1;
}

// Where the camera is at time t: (centre, half the view height) and its turn.
float3 novaView(float t, out float turn)
{
    turn = t * 0.012;
    float halfHeight = 1.25 * exp(-0.5 * (0.5 - 0.5 * cos(t * 2.0 * PI / 70.0)));
    return float3(0.06 * sin(t * 0.017), 0.04 * sin(t * 0.023), halfHeight);
}

float3 Render(float2 uv, float2 p)
{
    float turn;
    float3 view = novaView(Time, turn);

    // The relaxation R and the nudge c wander on slow loops; the bass swells R a touch.
    float drift = Time * 0.03;
    float2 relax = float2(1.0, 0.0) + (0.28 + 0.04 * Bass) * float2(cos(drift * 1.3), sin(drift * 0.9));
    float2 nudge = float2(-0.22 + 0.12 * sin(drift * 0.7), 0.08 * sin(drift * 1.1 + 1.0));

    // A slightly different spot inside the pixel each frame, smoothed out by blending frames below.
    float2 jitter = float2(hash(uv * 411.0 + frac(Time * 5.13)), hash(uv * 263.0 + frac(Time * 2.71))) - 0.5;
    float2 z = view.xy + rotate(float2(p.x, -p.y) + jitter * pixelSize(), turn) * view.z;

    // z -> z - R (z^3 - 1) / (3 z^2) + c, until it stops moving.
    float n = 0.0;
    float lastStep = 1.0;
    float stepSize = 1.0;
    float trap = 1.0e3;
    float stretch = 0.0;       // log of how much the map has stretched a tiny neighbourhood so far
    float mostStretch = 0.0;   // ... at its largest: huge near the seams, where the lace is
    bool settled = false;
    int maxIterations = qualitySteps(90);
    [loop]
    for (int i = 0; i < maxIterations; i++)
    {
        float2 z2 = novaMultiply(z, z);
        float2 z3 = novaMultiply(z2, z);
        // The map's slope, 1 - R (1/3 + 2 / (3 z^3)).
        float2 slope = float2(1.0, 0.0) - novaMultiply(relax, float2(1.0 / 3.0, 0.0) + novaDivide(float2(2.0 / 3.0, 0.0), z3));
        stretch += 0.5 * log(max(dot(slope, slope), 1.0e-30));
        mostStretch = max(mostStretch, stretch);
        float2 next = z - novaMultiply(relax, novaDivide(z3 - float2(1.0, 0.0), 3.0 * z2)) + nudge;
        lastStep = stepSize;
        stepSize = length(next - z);
        z = next;
        trap = min(trap, abs(length(z) - 1.0));
        if (stepSize < 1.0e-4)
        {
            settled = true;
            break;
        }
        n += 1.0;
    }

    float hue = Time * 0.008;
    float3 colour = 0.0;
    if (settled)
    {
        // Smooth the step count by how far the last step fell below the threshold.
        float fraction = saturate(log(1.0e-4 / max(lastStep, 1.0e-12)) / log(max(stepSize, 1.0e-12) / max(lastStep, 1.0e-12)));
        float smoothN = n + fraction;
        // Which pool it settled into sets the base colour; the count adds bands and lace near the seams.
        float pool = atan2(z.y, z.x) / (2.0 * PI);
        float3 tint = novaGradient(hue + pool * 0.6 + smoothN * 0.025);
        float bands = 0.6 + 0.4 * cos(smoothN * 1.4 - Time * 0.9);
        // Roughly how many pixels this point is from a seam: a pixel's worth of stretch means it is on one.
        float seamPx = exp(-(mostStretch + log(pixelSize() * view.z)));
        float core = exp(-seamPx * 0.5);
        float halo = exp(-seamPx * 0.1);
        float pace = 1.0 - exp(-smoothN * 0.04);
        colour = tint * (0.03 + 0.14 * bands * pace + 0.14 * halo * (0.7 + Mid * 0.6))
               + lerp(novaGradient(hue + pool * 0.6 + 0.3), float3(1.0, 0.95, 0.85), 0.15) * core * (0.9 + Bass * 0.5);
        colour += novaGradient(hue + 0.5) * exp(-trap * 30.0) * 0.08 * (0.5 + Treble);
    }
    else
    {
        // Never settled: the heart of the lace.
        colour = novaGradient(hue + 0.15) * (0.9 + Bass * 0.5);
    }
    colour *= 1.0 + Beat * 0.15;

    // Blend with where this point was last frame.
    float lastTurn;
    float3 lastView = novaView(Time - DeltaTime, lastTurn);
    float2 here = view.xy + rotate(float2(p.x, -p.y), turn) * view.z;
    float2 lastP = rotate((here - lastView.xy) / lastView.z, -lastTurn);
    float3 history = previous(toUv(float2(lastP.x, -lastP.y)));
    return lerp(colour, history, 0.75);
}
