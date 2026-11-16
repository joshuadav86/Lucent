// Name: Lava Lamp
// Inside a lava lamp: glossy blobs of glowing wax pull free of the mound at the foot of the lamp, stretch as they
// rise through violet liquid, hang near the top, and sink again, melting into each other where they meet. The lamp
// shines through the wax, red at its thin edges and gold in its thick hearts. Each blob glows with its own part of
// the music, bass at the left and treble at the right, and each beat sends a band of lamplight climbing the liquid.
// Family: Organic
// Tags: Calm, Colourful, Retro, 3D
// Added: 2026-10-17 01:51

static const int LlBlobs = 8;

static float4 LlAt[LlBlobs];            // where each blob is, and how large
static float LlTall[LlBlobs];           // how far each blob is stretched upward
static float LlLoud[LlBlobs];           // how loud each blob's part of the music is

float llLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

float llSmoothMin(float a, float b, float k)
{
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * 0.25;
}

// The mound of wax resting at the foot of the lamp, its top slowly heaving.
float llMound(float3 p)
{
    float3 radii = float3(1.7 * Aspect, 0.72, 1.5);
    float3 q = p - float3(0.0, -1.5 + 0.07 * sin(p.x * 1.9 + Time * 0.23) + 0.05 * sin(p.x * 3.3 - Time * 0.17), 0.3);
    return (length(q / radii) - 1.0) * radii.y;
}

float llBlob(float3 p, int n)
{
    float3 q = p - LlAt[n].xyz;
    float tall = LlTall[n];
    float narrow = 1.0 / sqrt(tall);
    float3 radii = LlAt[n].w * float3(narrow, tall, narrow);
    return (length(q / radii) - 1.0) * radii.x;
}

// How far a spot is outside the wax (less than nothing inside it).
float llWax(float3 p)
{
    float d = llMound(p);
    [unroll]
    for (int n = 0; n < LlBlobs; n++)
    {
        d = llSmoothMin(d, llBlob(p, n), 0.3);
    }

    return d;
}

float3 llNormal(float3 p)
{
    float2 h = float2(0.004, 0.0);
    return normalize(float3(llWax(p + h.xyy) - llWax(p - h.xyy),
                            llWax(p + h.yxy) - llWax(p - h.yxy),
                            llWax(p + h.yyx) - llWax(p - h.yyx)));
}

// How loud the music is for the wax at one spot: each blob's own part, blended where blobs have melted together,
// the mound taking the deepest bass.
float llLoudAt(float3 p)
{
    float wFoot = exp(-7.0 * max(llMound(p), 0.0));
    float sum = wFoot * llLoud(0.08);
    float weight = wFoot + 1e-4;
    [unroll]
    for (int n = 0; n < LlBlobs; n++)
    {
        float w = exp(-7.0 * max(llBlob(p, n), 0.0));
        sum += w * LlLoud[n];
        weight += w;
    }

    return sum / weight;
}

// The lamp's glass as the wet wax mirrors it: two soft windows of light, one high on the left and one to the right.
float3 llRoom(float3 dir)
{
    float left = saturate(dot(dir, normalize(float3(-0.6, 0.55, -0.58))));
    float right = saturate(dot(dir, normalize(float3(0.75, 0.2, -0.63))));
    return float3(2.2, 2.0, 2.1) * (smoothstep(0.95, 0.975, left) + 0.2 * pow(left, 8.0))
         + float3(1.3, 0.8, 1.6) * (0.6 * smoothstep(0.96, 0.98, right) + 0.2 * pow(right, 6.0));
}

float3 Render(float2 uv, float2 p)
{
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.1, since) * (1.0 - since) * (1.0 - since);

    // Where the blobs are now. Each rises and sinks at its own pace, dwelling at the top and the foot, and is
    // drawn out longest while it moves fastest.
    [unroll]
    for (int n = 0; n < LlBlobs; n++)
    {
        float h1 = hash(float2(n, 3.7));
        float h2 = hash(float2(n, 9.1));
        float h3 = hash(float2(n, 15.3));
        float phase = Time * (0.085 + 0.1 * h3) + 6.2832 * h1;
        float across = ((n + 0.5) / LlBlobs * 2.0 - 1.0 + 0.16 * (h1 - 0.5)) * Aspect * 0.9;
        LlAt[n] = float4(across + 0.07 * sin(Time * 0.07 + n * 2.0), 0.05 + 0.92 * sin(phase), (h2 - 0.5) * 1.3, 0.21 + 0.13 * h2 + 0.02 * sin(Time * 0.3 + n));
        LlTall[n] = 1.0 + 0.45 * abs(cos(phase));
        LlLoud[n] = llLoud(0.08 + 0.6 * (n + 0.5) / LlBlobs);
    }

    float3 eye = float3(0.0, 0.0, -4.0);
    float3 ray = normalize(float3(p.x, -p.y, 4.0));
    float height = -p.y;

    // The liquid: violet above, rose near the lamp at the foot, with a band of lamplight climbing it on each beat.
    float3 liquid = lerp(float3(0.62, 0.04, 0.4), float3(0.035, 0.0, 0.075), smoothstep(-1.0, 0.75, height));
    float front = -1.0 + since * 2.3;
    liquid += float3(0.3, 0.05, 0.22) * flash * exp(-(height - front) * (height - front) * 9.0);

    // Motes drift up through the liquid and glint with the treble.
    float2 grid = float2(p.x * 9.0, (height - Time * 0.03) * 9.0);
    float2 box = floor(grid);
    float2 mote = frac(grid) - 0.5 - 0.3 * (float2(hash(wrap(box, 256.0) + 1.3), hash(wrap(box, 256.0) + 7.9)) - 0.5);
    float glint = exp(-dot(mote, mote) * 900.0) * step(0.55, hash(wrap(box, 256.0) + 4.4));
    liquid += float3(0.9, 0.5, 0.8) * glint * (0.25 + 0.75 * llLoud(0.62)) * smoothstep(1.0, -0.4, height);

    float t = 2.6;
    float nearest = 1e3;
    float struck = 0.0;
    int steps = qualitySteps(64);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float d = llWax(eye + ray * t);
        nearest = min(nearest, d);
        if (d < 0.003)
        {
            struck = 1.0;
            break;
        }

        t += d * 0.9;
        if (t > 6.5)
            break;
    }

    // The wax glows through the liquid round it.
    float3 colour = liquid + float3(1.1, 0.12, 0.06) * exp(-max(nearest, 0.0) * 9.0) * 0.55;
    if (struck > 0.5)
    {
        float3 at = eye + ray * t;
        float3 n = llNormal(at);

        // How much wax the lamp's light has to cross to reach the eye here.
        float thick = 0.02;
        [loop]
        for (int k = 0; k < 10; k++)
        {
            float inside = -llWax(at + ray * thick);
            if (inside < 0.002)
                break;
            thick += max(inside, 0.02);
        }

        float loud = llLoudAt(at);
        float rising = flash * exp(-(at.y - front) * (at.y - front) * 7.0);
        float heat = (1.0 - exp(-thick * 3.2)) * (0.66 + 0.5 * loud + 0.25 * rising) * (0.85 + 0.2 * saturate(-n.y)) * lerp(1.12, 0.8, saturate(0.5 + 0.5 * at.y));
        float3 wax = lerp(float3(0.42, 0.01, 0.02), float3(1.45, 0.12, 0.03), smoothstep(0.0, 0.3, heat));
        wax = lerp(wax, float3(1.75, 0.6, 0.04), smoothstep(0.25, 0.6, heat));
        wax = lerp(wax, float3(1.9, 1.4, 0.3), smoothstep(0.55, 1.0, heat));

        float facing = saturate(-dot(ray, n));
        float mirror = 0.1 + 0.55 * pow(1.0 - facing, 4.0);
        wax = wax * (1.0 - 0.5 * mirror) + mirror * (llRoom(reflect(ray, n)) + liquid * 0.7);

        // Wax deeper in the lamp is seen through more of the liquid.
        colour = lerp(wax, liquid, saturate((t - 3.2) * 0.17)) + liquid * 0.12;
    }

    return colour;
}
