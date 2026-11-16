// Name: Gossamer
// A nebula spun from countless fine threads of glowing gas, layer behind layer, round a bright heart that wanders
// slowly about the screen: gold at the heart, through green, to teal and deep blue at the edges. The gas never
// stops flowing: it streams out of the heart and swirls round it in long curling currents, each layer at its own
// pace and the nearer ones faster. Each layer listens to its own part of the music, bass in the nearest and treble
// in the furthest, and glows brighter as it sounds; ripples of light run out from the heart in time with the beat,
// every beat sends a wash of light out through all of them, and every eight beats the nebula takes on new colours.
// Family: Cosmic
// Tags: Space, Colourful, Calm
// Added: 2026-11-16 16:31

static const int Layers = 5;

static float GoSound;                   // how loud the music is just now, taken across all its parts

// One part of the music, with the high notes made up to stand level with the low ones.
float goPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one. It listens to a broad part and answers gently, so the threads swell and never jitter.
float goLoud(float x)
{
    float heard = (goPart(x - 0.03) + goPart(x) + goPart(x + 0.03)) / 3.0;
    return smoothstep(0.0, 1.0, saturate((heard - 0.55 * GoSound) / (0.8 * GoSound + 0.12)));
}

// Threads of gas: the creases of a few layers of noise, bent about by a slower noise so they curl and branch.
float goThreads(float2 q)
{
    float2 bend = float2(noise(q * 0.6 + float2(3.1, 7.7)), noise(q * 0.6 + float2(8.3, 1.9))) - 0.5;
    q += bend * 1.6;
    float total = 0.0;
    float size = 0.55;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float crease = 1.0 - abs(2.0 * noise(q) - 1.0);
        total += size * crease * crease * crease * crease;
        q = float2(0.8 * q.x - 0.6 * q.y, 0.6 * q.x + 0.8 * q.y) * 2.11 + float2(5.3, 1.7);
        size *= 0.6;
    }
    return total;
}

// The nebula's colours, from the heart (t 0) to the edge (t 1), in one of four sets.
float3 goShade(float t, float which)
{
    float k = wrap(which, 4.0);
    float3 heart, middle, edge;
    if (k < 0.5)
    {
        heart = float3(1.5, 1.25, 0.4); middle = float3(0.35, 1.4, 0.3); edge = float3(0.05, 0.6, 1.0);
    }
    else if (k < 1.5)
    {
        heart = float3(1.5, 0.9, 1.1); middle = float3(0.9, 0.3, 1.4); edge = float3(0.15, 0.3, 1.3);
    }
    else if (k < 2.5)
    {
        heart = float3(1.5, 1.1, 0.6); middle = float3(1.4, 0.45, 0.25); edge = float3(0.6, 0.12, 0.7);
    }
    else
    {
        heart = float3(1.0, 1.4, 1.3); middle = float3(0.15, 1.2, 1.2); edge = float3(0.1, 0.35, 1.3);
    }
    return lerp(lerp(heart, middle, smoothstep(0.0, 0.45, t)), edge, smoothstep(0.35, 1.0, t));
}

float3 Render(float2 uv, float2 p)
{
    GoSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        GoSound += goPart(0.08 + 0.12 * part) / 6.0;

    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float washFade = smoothstep(0.0, 0.08, since) * (1.0 - since);

    // The heart wanders slowly about the screen.
    float2 heart = float2(0.42 * Aspect * sin(Time * 0.023 + 3.6), 0.1 + 0.25 * sin(Time * 0.031 + 1.3));
    float2 fromHeart = p - heart;
    float far = length(fromHeart);

    float phrase = wrap(BeatGlide, 4096.0) / 8.0;
    float changed = smoothstep(0.0, 0.15, frac(phrase));

    float3 colour = float3(0.004, 0.008, 0.03) + float3(0.01, 0.025, 0.07) * exp(-far * 1.2);
    // Ripples of light that run out from the heart, one crest passing each spot every beat.
    float ripple = 0.5 + 0.5 * cos(6.2832 * (far * 1.1 - BeatPhase) - wrap(Time * 0.6, 6.2832));

    [loop]
    for (int i = 0; i < Layers; i++)
    {
        float depth = (i + 0.5) / Layers;
        float shown = 1.0 - 0.4 * depth;
        float zoom = 1.0 + 2.0 * depth;

        // How the gas flows here: out of the heart and round it, each layer its own way round, stirred by slow
        // eddies. The threads are carried along the flow and, so they never stretch out of shape, each spot shows
        // two copies a while apart in their journey, handing over from one to the other.
        float way = (i & 1) == 0 ? 1.0 : -1.0;
        float2 around = float2(-fromHeart.y, fromHeart.x) * way;
        float2 eddy = float2(noise(p * 0.9 + float2(i * 3.7, 1.0)), noise(p * 0.9 + float2(5.0, i * 2.3))) - 0.5;
        float2 flow = (around * 0.9 + fromHeart * 0.45) / (far + 0.35) + eddy * 1.2;
        flow *= 1.25 - 0.6 * depth;

        float2 q = rotate(fromHeart, i * 1.3) * zoom * 1.6 + float2(i * 11.3, i * 4.7);
        float threads = 0.0;
        [unroll]
        for (int copy = 0; copy < 2; copy++)
        {
            float age = frac(Time * 0.055 + 0.5 * copy + 0.19 * i);
            float share = 1.0 - abs(2.0 * age - 1.0);
            threads += goThreads(q - rotate(flow, i * 1.3) * zoom * 1.6 * (age - 0.5) * 2.2 + copy * float2(17.0, 9.0)) * share;
        }

        // Threads are thick and bright near the heart and fray to faint wisps far from it.
        float near = exp(-far * (1.4 - 0.3 * depth));
        float place = 1.0 - (i + 0.5) / Layers;
        float loud = goLoud(0.07 + 0.55 * (1.0 - depth));
        float passing = far - since * 2.2;
        float wash = exp(-passing * passing * 5.0) * washFade;

        float bright = threads * (0.15 + 0.85 * near) * (0.3 + 1.3 * loud + 0.8 * wash) * (0.75 + 0.5 * ripple) * shown;
        float t = saturate(far * 0.5 + 0.25 * (1.0 - threads) - 0.1 * depth);
        float3 shade = lerp(goShade(t, floor(phrase) - 1.0), goShade(t, floor(phrase)), changed);
        colour += shade * bright * (0.4 + 0.6 * (1.0 - depth));
    }

    // The heart's own glow, which swells with each beat.
    float3 core = lerp(goShade(0.0, floor(phrase) - 1.0), goShade(0.0, floor(phrase)), changed);
    colour += core * (0.35 + 0.25 * BeatSwell + 0.25 * goLoud(0.1)) * exp(-far * far * 14.0);
    colour += core * 0.08 * exp(-far * 2.5);

    // A few stars, still, through the gas.
    float2 starCell = floor(p * 90.0);
    float star = step(0.994, hash(starCell)) * smoothstep(0.5, 0.0, length(frac(p * 90.0) - 0.5));
    colour += float3(0.7, 0.8, 1.0) * star * 0.5;

    colour = colour / (1.0 + 0.25 * max(max(colour.r, colour.g), colour.b));
    return colour * (0.88 + 0.12 * Intensity + 0.15 * DropPulse);
}
