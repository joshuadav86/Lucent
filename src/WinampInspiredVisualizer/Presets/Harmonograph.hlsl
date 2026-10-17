// Name: Harmonograph
// A pendulum harmonograph draws one fine line of light that loops and slowly winds inward as its swing dies away.
// Every few bars the paper is cleared and the pendulums start again in a new ratio; the beat leaves a tremor in the line.
// Family: Minimal
// Tags: Minimal, Calm, Geometric

// Memory: one pixel at the top-left of the feedback buffer, stored far too dim to see:
// which drawing we are on, when it started (as a fraction of HgPeriod), and its seed.
static const float HgStore = 0.004;
static const float HgPeriod = 128.0;
static const float HgClear = 1.6;         // seconds spent fading the old drawing before the pen starts
static const float HgRate = 5.0;          // how fast the pendulums swing

float3 hgEncode(float3 v)
{
    return (0.3 + 0.6 * saturate(v)) * HgStore;
}

bool hgValid(float3 stored)
{
    float3 t = stored / HgStore;
    return all(t > 0.27) && all(t < 0.93);
}

float3 hgDecode(float3 stored)
{
    return (stored / HgStore - 0.3) / 0.6;
}

float hgRandom(float seed, float salt)
{
    return frac(sin(seed * 91.3458 + salt * 47.123) * 47453.5453);
}

// The pen's position at swing time u for one drawing: two pendulums move the paper, two move the pen,
// all in near-whole-number ratios and all slowly losing energy.
float2 hgPen(float u, float seed, float tremor)
{
    // A handful of pleasing ratios; a tiny detune makes the figure slowly precess.
    float pick = floor(hgRandom(seed, 1.0) * 6.0);
    float2 ratio = float2(2.0, 3.0);
    ratio = pick < 0.5 ? float2(2.0, 3.0) : ratio;
    ratio = pick > 0.5 && pick < 1.5 ? float2(3.0, 4.0) : ratio;
    ratio = pick > 1.5 && pick < 2.5 ? float2(1.0, 2.0) : ratio;
    ratio = pick > 2.5 && pick < 3.5 ? float2(3.0, 5.0) : ratio;
    ratio = pick > 3.5 && pick < 4.5 ? float2(4.0, 5.0) : ratio;
    ratio = pick > 4.5 ? float2(1.0, 3.0) : ratio;
    float detune = (hgRandom(seed, 2.0) - 0.5) * 0.02;
    float fx1 = ratio.x;
    float fy1 = ratio.y + detune;
    float fx2 = ratio.y - detune * 0.5;
    float fy2 = ratio.x + detune * 0.7;
    float px1 = hgRandom(seed, 3.0) * 6.2831853;
    float py1 = hgRandom(seed, 4.0) * 6.2831853;
    float px2 = hgRandom(seed, 5.0) * 6.2831853;
    float py2 = hgRandom(seed, 6.0) * 6.2831853;
    float mix2 = 0.25 + 0.35 * hgRandom(seed, 7.0);
    float decay = 0.014 + 0.006 * hgRandom(seed, 8.0);

    float fade1 = exp(-u * decay);
    float fade2 = exp(-u * decay * 1.3);
    float2 pen;
    pen.x = sin(u * fx1 + px1) * fade1 + sin(u * fx2 + px2) * fade2 * mix2;
    pen.y = sin(u * fy1 + py1) * fade1 + sin(u * fy2 + py2) * fade2 * mix2;
    pen /= 1.0 + mix2;
    // A slowly turning paper table, as on a rotary harmonograph.
    pen = rotate(pen, u * 0.004 + seed * 6.2831853);
    // The beat shakes the pen very slightly, leaving a fine tremor in the line.
    pen *= 1.0 + tremor * 0.006 * sin(u * 47.0);
    return pen * 0.82;
}

float hgSegment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-8));
    return length(pa - ba * h);
}

float3 Render(float2 uv, float2 p)
{
    // ---- Memory: which drawing, when it started, and its seed ----
    float3 stored = previous(float2(0.5 / Resolution.x, 0.5 / Resolution.y));
    bool known = hgValid(stored);
    float3 memory = known ? hgDecode(stored) : float3(0.0, 0.0, 0.5);
    // A new drawing every 32 beats (eight bars), or every 40 seconds if the music has no beat.
    float drawing = fmod(floor(BeatCount / 32.0), 8.0) / 8.0;
    float elapsed = frac(Time / HgPeriod - memory.y) * HgPeriod;
    bool restart = !known || abs(drawing - memory.x) > 0.01 || elapsed > 40.0;
    float start = restart ? frac(Time / HgPeriod) : memory.y;
    float seed = restart ? frac(memory.z + 0.618034) : memory.z;
    elapsed = restart ? 0.0 : elapsed;

    float2 pixel = floor(uv * Resolution);
    if (pixel.x < 0.5 && pixel.y < 0.5)
    {
        return hgEncode(float3(drawing, start, seed));
    }

    float px = pixelSize();
    // Dark paper with a hint of warmth in the middle.
    float3 paper = lerp(float3(0.006, 0.006, 0.010), float3(0.022, 0.018, 0.020), exp(-dot(p, p) * 0.7));

    // While a new drawing begins, the old one fades quickly; otherwise it barely fades at all.
    float fadeRate = elapsed < HgClear ? 3.0 : 0.015;
    float3 old = previous(uv);
    float3 colour = lerp(paper, old, exp(-DeltaTime * fadeRate));

    // ---- The pen: the part of the line drawn since the last frame ----
    float u1 = max(elapsed - HgClear, 0.0) * HgRate;
    float u0 = max(elapsed - DeltaTime - HgClear, 0.0) * HgRate;
    if (u1 > u0)
    {
        float tremor = Beat;
        float best = 1e5;
        float bestU = u0;
        float2 a = hgPen(u0, seed, tremor);
        // Enough pieces that the curve stays smooth however long the frame was.
        int steps = clamp((int)ceil((u1 - u0) * 30.0), 4, 64);
        [loop]
        for (int i = 1; i <= steps; i++)
        {
            float u = lerp(u0, u1, i / (float)steps);
            float2 b = hgPen(u, seed, tremor);
            float d = hgSegment(p, a, b);
            bestU = d < best ? u : bestU;
            best = min(best, d);
            a = b;
        }
        // Colour drifts gently along the line, from warm gold to rose, nudged by the mids.
        float shade = 0.5 + 0.5 * sin(bestU * 0.025 + seed * 6.2831853 + Mid * 0.6);
        float3 ink = lerp(float3(1.0, 0.78, 0.45), float3(0.95, 0.45, 0.55), shade);
        float strength = 0.55 + 0.35 * Level + 0.25 * Beat;
        float lineCover = saturate(1.0 - best / (px * 1.1));
        float halo = exp(-best / (px * 3.0)) * 0.12;
        float3 drawn = ink * (lineCover * strength + halo);
        // Keep the brighter of old and new ink, so crossings stay crisp and nothing builds up to a blot.
        colour = max(colour, paper + drawn);
    }
    return colour;
}
