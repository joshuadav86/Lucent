// Name: Plexus
// A drift through layer after layer of fine golden wire strung between glowing beads on black, each bead linked
// to its nearest neighbours by perfectly straight threads that brighten as two beads draw close and fade as they
// part. Every bead wanders its own small orbit, so the net is always shifting. Each layer listens to its own part
// of the music, bass in the nearest and treble in the furthest, and its beads flare as that part sounds; every beat
// sends a spark racing along every thread and a wave of light out through the layers, and every eight beats the
// gold turns to rose, to silver, and to champagne.
// Family: Cosmic
// Tags: Space, Minimal, Neon
// Added: 2026-11-16 16:37

static const int Layers = 4;

static float PxSound;                   // how loud the music is just now, taken across all its parts
static float PxSince;                   // how far through the beat, 0 on the beat

// One part of the music, with the high notes made up to stand level with the low ones.
float pxPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one. It listens to a broad part and answers gently, so the beads swell and never jitter.
float pxLoud(float x)
{
    float heard = (pxPart(x - 0.03) + pxPart(x) + pxPart(x + 0.03)) / 3.0;
    return smoothstep(0.0, 1.0, saturate((heard - 0.55 * PxSound) / (0.8 * PxSound + 0.12)));
}

// Where the bead of a cell is: each wanders its own small orbit about the middle of its cell.
float2 pxBead(float2 cell, float layer)
{
    float a = hash(cell + layer * 17.0);
    float b = hash(cell * 1.7 + layer * 5.0 + 3.0);
    float pace = 0.25 + 0.35 * a;
    return cell + 0.5 + 0.36 * float2(sin(Time * pace + a * 6.2832), cos(Time * pace * 1.27 + b * 6.2832));
}

// How brightly a thread from a to b lights the spot p: brighter the shorter the thread. A spark runs along it each
// beat, from whichever end the thread's own coin toss picks.
float pxThread(float2 p, float2 a, float2 b, float px, float spark)
{
    float2 ab = b - a;
    float span = length(ab);
    float along = saturate(dot(p - a, ab) / max(span * span, 1e-5));
    float d = length(p - a - ab * along);
    float strength = smoothstep(1.25, 0.5, span);
    float wire = smoothstep(1.6 * px, 0.4 * px, d) * 0.55 + exp(-d / (px * 3.0)) * 0.12;
    float flip = hash(a * 3.1 + b * 7.3);
    float runAt = (flip < 0.5 ? along : 1.0 - along) - PxSince * 1.25;
    float sparkAt = exp(-runAt * runAt * 90.0) * spark;
    return wire * strength * (1.0 + 2.6 * sparkAt);
}

// The colour of the wire: gold, rose gold, silver blue, and champagne, a new one every eight beats.
float3 pxMetal(float which)
{
    float k = wrap(which, 4.0);
    if (k < 0.5) return float3(1.5, 1.05, 0.32);
    if (k < 1.5) return float3(1.5, 0.62, 0.55);
    if (k < 2.5) return float3(0.75, 1.0, 1.5);
    return float3(1.45, 1.25, 0.85);
}

float3 Render(float2 uv, float2 p)
{
    PxSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        PxSound += pxPart(0.08 + 0.12 * part) / 6.0;
    PxSince = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float spark = smoothstep(0.0, 0.06, PxSince) * (1.0 - PxSince);

    float phrase = wrap(BeatGlide, 4096.0) / 8.0;
    float3 metal = lerp(pxMetal(floor(phrase) - 1.0), pxMetal(floor(phrase)), smoothstep(0.0, 0.15, frac(phrase)));

    float px = pixelSize();
    float3 colour = float3(0.0, 0.0, 0.0);
    float drift = wrap(Time * 0.03, 1.0);
    [loop]
    for (int i = 0; i < Layers; i++)
    {
        // Layers drift toward the viewer, fading in from far off and out as they pass.
        float depth = frac((i + 0.5) / Layers - drift);
        float shown = smoothstep(0.0, 0.3, depth) * smoothstep(1.0, 0.75, depth);
        float zoom = 0.8 + 6.0 * depth * depth;
        float2 q = rotate(p, i * 1.1 + 0.03 * sin(Time * 0.05 + i)) * zoom + float2(i * 7.3, i * 3.1);
        float lpx = px * zoom;
        // Each time a layer passes and starts again far off, it is strung afresh.
        float layer = i + 4.0 * wrap(floor((i + 0.5) / Layers - Time * 0.03), 64.0);

        // The beads of this cell and its eight neighbours.
        float2 home = floor(q);
        float2 beads[9];
        [unroll]
        for (int k = 0; k < 9; k++)
            beads[k] = pxBead(home + float2(k % 3 - 1, k / 3 - 1), layer);

        // Threads from the middle bead to each neighbour, and between neighbours side by side round the ring.
        float wires = 0.0;
        [unroll]
        for (int k = 0; k < 9; k++)
        {
            if (k != 4)
                wires += pxThread(q, beads[4], beads[k], lpx, spark);
        }
        wires += pxThread(q, beads[1], beads[3], lpx, spark) + pxThread(q, beads[1], beads[5], lpx, spark)
               + pxThread(q, beads[7], beads[3], lpx, spark) + pxThread(q, beads[7], beads[5], lpx, spark);

        // Beads glow, flaring with this layer's part of the music, each twinkling in its own time.
        float loud = pxLoud(0.07 + 0.55 * (1.0 - depth));
        float glints = 0.0;
        [unroll]
        for (int k = 0; k < 9; k++)
        {
            float2 off = (q - beads[k]) / lpx;
            float twinkle = 0.6 + 0.4 * sin(hash(beads[k] - home) * 6.2832 + Time * 1.3);
            float reach = dot(off, off) / (1.0 + 2.5 * loud);
            glints += twinkle * (0.12 / (1.0 + reach * 0.12) + 0.9 * exp(-reach * 0.06)) * (0.12 + 0.6 * loud);
        }

        // A wave of light passes out through the layers on each beat, nearest first.
        float wave = exp(-pow(depth - PxSince * 1.2, 2.0) * 25.0) * spark;
        float dim = shown * (0.25 + 0.75 * (1.0 - depth));
        colour += metal * (wires * (0.25 + 1.1 * loud + 0.7 * wave) + glints * (1.0 + 1.5 * wave)) * dim;
    }

    colour = colour / (1.0 + 0.3 * max(max(colour.r, colour.g), colour.b));
    return colour * (0.88 + 0.12 * Intensity + 0.15 * DropPulse);
}
