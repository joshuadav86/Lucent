// Name: India Ink
// A billow of black ink carried across the screen by a current in clear water over a pale ground: glossy, puffed
// like a storm cloud, streaming a tail behind it, with a thin haze round it where the ink runs out and the light
// splits into petrol blues, teals, and ambers. Its five arms surge out in turn, one on each beat, and the whole of
// it swells on the beat while a lamp circles overhead so the shine slides across its folds in time. The
// haze is the music: bass on one side round to treble on the other, it spreads wide and bright where its part
// sounds, the ink's edges catch its colours, and each beat sends a band of stronger colour out through it. Its
// colours move on every eight beats.
// Family: Organic
// Tags: Water, Minimal
// Added: 2026-11-16 16:28

static float IiSound;                   // how loud the music is just now, taken across all its parts
static float2 IiHeart;                  // where the middle of the billow is
static float2 IiArms[5];                // where each of the five arms of ink reaches to
static float IiSwell;                   // how much the whole billow has swollen with the beat
static float2 IiTail;                   // the way the current carries the ink: the tail streams out behind
static float IiBoil;                    // how far the puffs have boiled: a little further on each beat

// One part of the music, with the high notes made up to stand level with the low ones.
float iiPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one. It listens to a broad part and answers gently, so the haze swells and never jitters.
float iiLoud(float x)
{
    float heard = (iiPart(x - 0.03) + iiPart(x) + iiPart(x + 0.03)) / 3.0;
    return smoothstep(0.0, 1.0, saturate((heard - 0.55 * IiSound) / (0.8 * IiSound + 0.12)));
}

// Puffs: rounded lumps with sharp creases between them, large ones carrying smaller ones.
float iiPuffs(float2 p)
{
    float total = 0.0;
    float size = 0.5;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float lump = 2.0 * noise(p) - 1.0;
        total += size * sqrt(lump * lump + 0.006);
        p = float2(0.8 * p.x - 0.6 * p.y, 0.6 * p.x + 0.8 * p.y) * 2.13 + float2(7.1, 3.3);
        size *= 0.45;
    }
    return total;
}

// How deep in the ink a spot is: above 0 inside the billow, below 0 in clear water.
float iiInk(float2 p)
{
    // The water stirs the ink about as the current carries it.
    float2 w = p + 0.12 * float2(noise(p * 1.1 + float2(Time * 0.05, Time * 0.035)) - 0.5,
                                 noise(p * 1.1 + float2(5.2 - Time * 0.04, -Time * 0.03)) - 0.5);
    float2 fromHeart = w - IiHeart;
    float mass = 0.95 * exp(-dot(fromHeart, fromHeart) * 4.0);

    // A tail streams out behind, thinning as it goes.
    [unroll]
    for (int k = 1; k < 4; k++)
    {
        float2 fromTail = w - IiHeart - IiTail * (0.32 * k) - 0.05 * k * float2(sin(Time * 0.11 + k), cos(Time * 0.09 + 2.0 * k));
        mass += (0.75 - 0.15 * k) * exp(-dot(fromTail, fromTail) * (6.0 + 2.0 * k));
    }
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        float2 fromArm = w - IiArms[i];
        mass += 0.85 * exp(-dot(fromArm, fromArm) * 6.5);
    }

    float puffs = iiPuffs(w * 1.25 - IiTail * wrap(Time * 0.05, 64.0) + float2(0.0, IiBoil));
    return mass - 0.7 + IiSwell + 0.85 * (puffs - 0.4);
}

// The colours light splits into where the ink runs thin, t climbing as the ink thins.
float3 iiSheen(float t)
{
    return saturate(0.55 + 0.6 * cos(6.2832 * (t + float3(0.02, 0.22, 0.47))));
}

float3 Render(float2 uv, float2 p)
{
    IiSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        IiSound += iiPart(0.08 + 0.12 * part) / 6.0;

    float barTurn = 6.2832 * BarPhase;
    IiSwell = 0.15 * BeatSwell + 0.1 * DropPulse + 0.05 * Intensity;
    IiBoil = wrap(Time * 0.04, 64.0);

    // The current carries the billow from side to side across the whole screen, and a little up and down; the
    // tail streams out the way it has come.
    float sway = wrap(Time * 0.075, 6.2832);
    IiHeart = float2(0.6 * Aspect * sin(sway), 0.08 * sin(Time * 0.037 + 1.0));
    float2 going = float2(0.6 * Aspect * 0.075 * cos(sway), 0.08 * 0.037 * cos(Time * 0.037 + 1.0));
    IiTail = -going / max(length(going), 0.02) * smoothstep(0.0, 0.05, length(going));

    // Which arm surges out: the next one round on each beat, handed on smoothly.
    float onArm = wrap(BeatGlide, 5.0);
    [unroll]
    for (int arm = 0; arm < 5; arm++)
    {
        float apart = abs(onArm - arm);
        apart = min(apart, 5.0 - apart);
        float surge = exp(-apart * apart * 1.6) * (0.25 + 0.75 * BeatSwell);
        float bearing = arm * 1.2566 + wrap(Time * 0.03, 6.2832) + 0.12 * sin(Time * 0.07 + arm * 2.0);
        float reach = 0.58 + 0.06 * cos(barTurn - arm * 1.2566) + 0.05 * sin(Time * 0.06 + arm * 1.7) + 0.4 * surge;
        IiArms[arm] = IiHeart + float2(cos(bearing) * 1.25, sin(bearing)) * reach;
    }

    // How deep in the ink this spot is, and which way the ink thins from here.
    float span = 0.005;
    float ink = iiInk(p);
    float2 slope = float2(iiInk(p + float2(span, 0.0)) - ink, iiInk(p + float2(0.0, span)) - ink) / span;
    float steep = length(slope);
    float inside = ink / clamp(steep, 1.2, 2.4);

    // Round the billow: bass on one side, round both ways to treble on the other.
    float2 fromHeart = p - IiHeart;
    float round0 = abs(atan2(fromHeart.y, fromHeart.x)) / PI;
    float loud = iiLoud(0.06 + 0.56 * round0);

    // Each beat sends a band of stronger colour out from the heart. With a steady beat to follow it keeps pace
    // with the beat; without one it follows how far the last beat has faded.
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float passing = length(fromHeart) - 0.15 - since * 1.7;
    float band = exp(-passing * passing * 9.0) * smoothstep(0.0, 0.08, since) * (1.0 - since);

    // The pale ground, and the haze of thin ink on it: wide and bright where its part of the music sounds.
    float phrase = wrap(BeatGlide, 4096.0) / 8.0;
    float moved = floor(phrase) + smoothstep(0.0, 0.12, frac(phrase));
    float3 ground = float3(0.8, 0.81, 0.83) * (1.0 - 0.35 * dot(uv - 0.5, uv - 0.5) * 2.0);
    float outside = max(-inside, 0.0);
    float wide = 0.04 + 0.28 * loud + 0.1 * band;
    // The thin ink gathers in wisps, so the colour lies in patches round the billow rather than an even ring.
    float wisps = smoothstep(0.25, 0.75, noise(fromHeart * 3.2 + float2(Time * 0.05, -Time * 0.04)));
    float haze = exp(-outside / wide) * (0.3 + 0.7 * wisps);
    float3 sheen = iiSheen(outside * 5.5 - 0.31 * moved + 0.25 * round0 + 0.06 * sin(Time * 0.2));
    float3 colour = ground * lerp(float3(1.0, 1.0, 1.0), sheen * (0.75 + 0.5 * loud + 0.5 * band), saturate(haze * (0.6 + 0.9 * loud + 0.6 * band)));
    colour *= 1.0 - 0.55 * exp(-outside / 0.018);

    // The ink itself: black and glossy. Its folds face the way the ink thins; a lamp circling once a bar draws a
    // bright line of shine along them, and the pale room shows faintly in its sides.
    float rim = exp(-max(ink, 0.0) * 9.0);
    float3 facing = normalize(float3(-slope * (0.3 + 0.9 * rim), 1.0));
    float lampTurn = barTurn + wrap(Time * 0.31, 6.2832);
    float3 lamp = normalize(float3(cos(lampTurn) * 0.8, 0.55 + 0.45 * sin(lampTurn), 0.75));
    float3 between = normalize(lamp + float3(0.0, 0.0, 1.0));
    float shine = pow(saturate(dot(facing, between)), 60.0);
    float soft = pow(saturate(dot(facing, between)), 5.0);
    float3 black = float3(0.012, 0.012, 0.017) + float3(0.5, 0.52, 0.56) * (0.09 * soft + 0.22 * rim * rim);
    black += float3(1.0, 0.98, 0.95) * shine * (0.7 + 0.5 * band);
    black += sheen * 0.8 * rim * rim * (0.25 + 1.2 * loud + 0.8 * band);

    float cover = smoothstep(-1.5, 1.5, inside / pixelSize());
    return lerp(colour, black, cover);
}
