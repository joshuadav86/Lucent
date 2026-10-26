// Name: Pulsar Ridges
// The last few seconds of the waveform pile up into a mirrored mountain of glowing ridges that roll away into the dark, each hiding those behind it.
// Family: Music-shaped
// Tags: Dark, Neon, Retro
// Added: 2026-10-16 23:54

// Memory: a 65 x 14 pixel block in the top-left corner of the feedback buffer, stored far too dim
// to see. Columns 0-63 hold 42 lines of the waveform (three lines per pixel, in r, g and b);
// column 64 holds how hard the beat was hitting when each ridge was taken.
static const float PulsarSamples = 64.0;
static const float PulsarGroups = 14.0;
static const float PulsarLines = 42.0;
static const float PulsarRate = 11.0;       // new lines per second
static const float PulsarStore = 0.004;

float pulsarWave(float x)
{
    return waveform(x) * 0.3 + (waveform(x - 0.004) + waveform(x + 0.004)) * 0.22
         + (waveform(x - 0.009) + waveform(x + 0.009)) * 0.13;
}

// Where the waveform rises through zero, so successive ridges line up.
float pulsarTrigger()
{
    float found = 0.0;
    float before = pulsarWave(0.02);
    [loop]
    for (int i = 1; i < 40; i++)
    {
        float x = 0.02 + i * 0.012;
        float now = pulsarWave(x);
        if (before < 0.0 && now >= 0.0)
        {
            found = x;
            break;
        }
        before = now;
    }
    return found;
}

float3 pulsarEncode(float3 v)
{
    return (0.6 + 0.3 * clamp(v, -1.0, 1.0)) * PulsarStore;
}

bool pulsarValid(float3 stored)
{
    float3 t = stored / PulsarStore;
    return all(t > 0.27) && all(t < 0.93);
}

float3 pulsarDecode(float3 stored)
{
    return pulsarValid(stored) ? (stored / PulsarStore - 0.6) / 0.3 : float3(0.0, 0.0, 0.0);
}

float3 pulsarFetch(float column, float group)
{
    return previous(float2(column + 0.5, group + 0.5) / Resolution);
}

float pulsarPick(float3 v, float ridge)
{
    float c = ridge - floor(ridge / 3.0) * 3.0;
    return dot(v, float3(step(c, 0.5), step(0.5, c) * step(c, 1.5), step(1.5, c)));
}

float3 Render(float2 uv, float2 p)
{
    // ---- Memory pixels: the newest ridge takes the live waveform; older ridges move back one when a new one is due ----
    float2 pixel = floor(uv * Resolution);
    if (pixel.x < PulsarSamples + 1.0 && pixel.y < PulsarGroups)
    {
        float shift = floor(Time * PulsarRate) - floor((Time - DeltaTime) * PulsarRate);
        float3 values = 0.0;
        bool meta = pixel.x > PulsarSamples - 0.5;
        float start = pixel.y < 0.5 && !meta ? pulsarTrigger() : 0.0;
        [unroll]
        for (int c = 0; c < 3; c++)
        {
            float ridge = pixel.y * 3.0 + c;
            float source = ridge - shift;
            float live = meta ? Beat * 2.0 - 1.0 : pulsarWave(start + (pixel.x + 0.5) / PulsarSamples * 0.4);
            float kept = pulsarPick(pulsarDecode(pulsarFetch(pixel.x, floor(source / 3.0))), source);
            values[c] = source < 0.0 || ridge < 0.5 ? live : kept;
        }
        return pulsarEncode(values);
    }

    // ---- The ridges, front (newest) to back ----
    float px = pixelSize();
    const float frontY = 1.05;
    float horizonY = -1.05 + 0.08 * sin(Time * 0.11);
    const float depthStep = 0.075;
    float scroll = frac(Time * PulsarRate);
    float newestId = floor(Time * PulsarRate);

    float3 colour = 0.0;
    float hidden = 0.0;
    [loop]
    for (int g = 0; g < 14; g++)
    {
        float group = g;
        // One perspective scale per group of three ridges, so they can share their memory reads.
        float zGroup = 1.0 + (group * 3.0 + 1.0 + scroll) * depthStep;
        float x = p.x * zGroup;
        // The waveform is mirrored about the middle: symmetric peaks rise from a calm plain.
        float u = saturate(abs(x) / 1.5);
        float fs = u * (PulsarSamples - 1.0);
        float s0 = min(floor(fs), PulsarSamples - 2.0);
        float3 v0 = pulsarDecode(pulsarFetch(s0, group));
        float3 v1 = pulsarDecode(pulsarFetch(s0 + 1.0, group));
        float3 beats = pulsarDecode(pulsarFetch(PulsarSamples, group)) * 0.5 + 0.5;
        // The two samples around this pixel, as a short segment on screen.
        float u0 = s0 / (PulsarSamples - 1.0);
        float u1 = (s0 + 1.0) / (PulsarSamples - 1.0);
        float side = p.x < 0.0 ? -1.0 : 1.0;
        float xa = side * u0 * 1.5 / zGroup;
        float xb = side * u1 * 1.5 / zGroup;
        float envA = exp(-u0 * u0 * 5.0);
        float envB = exp(-u1 * u1 * 5.0);
        float envelope = exp(-u * u * 5.0);

        [unroll]
        for (int c = 0; c < 3; c++)
        {
            float ridge = group * 3.0 + c;
            float z = 1.0 + (ridge + scroll) * depthStep;
            float base = horizonY + (frontY - horizonY) / z;
            float lift = 0.62 / z;
            float ya = base - (0.1 + v0[c] * 0.8 + abs(v0[c]) * 0.4) * envA * lift;
            float yb = base - (0.1 + v1[c] * 0.8 + abs(v1[c]) * 0.4) * envB * lift;
            float2 pa = p - float2(xa, ya);
            float2 ba = float2(xb - xa, yb - ya);
            float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-8));
            float across = length(pa - ba * h);
            float d = p.y - lerp(ya, yb, saturate((p.x - xa) / (xb - xa)));
            // Far ridges crowd together; fade them before they shimmer.
            float gap = (frontY - horizonY) * depthStep / (z * z) / px;
            float fade = saturate(gap / 3.0 - 0.7) * smoothstep(PulsarLines, PulsarLines - 6.0, ridge + scroll)
                       * smoothstep(0.0, 1.0, ridge + scroll);
            // Each ridge keeps its own colour as it rolls away.
            float id = newestId - ridge;
            float3 hue = 0.5 + 0.5 * cos(2.0 * PI * (id * 0.021 + u * 0.25 + float3(0.0, 0.33, 0.67)));
            hue = hue * hue * 1.2 + 0.03;
            float core = saturate(1.2 - across / px);
            float halo = exp(-across / (px * 4.0));
            float energy = (0.6 + beats[c] * 0.9) * smoothstep(1.0, 0.25, u);
            float visible = (1.0 - hidden) * fade;
            colour += visible * energy * (lerp(hue, 1.0, 0.3) * core + hue * halo * 0.3);
            // The body under each ridge is a dark veil of its colour that hides everything behind it.
            colour += visible * step(0.0, d) * hue * 0.015 * exp(-d * 6.0);
            hidden = max(hidden, step(0.0, d));
        }
        if (hidden > 0.5)
        {
            break;
        }
    }
    return colour;
}
