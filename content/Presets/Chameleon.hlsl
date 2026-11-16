// Name: Chameleon
// The skin of a chameleon, close enough to count the scales: wet, rounded beads packed tight over rolling folds,
// crowding and parting as the animal breathes with every four beats, under a lamp that circles overhead while
// the view glides across. A ripple crosses the skin on every beat, lifting the scales as it passes. The music
// plays in its colours: the stripes of the skin each listen to their own part of the music and flush, scale by
// scale, through gold and orange to crimson for the low notes and through turquoise to blue and violet for the
// high ones, each beat sends a blush running out over the folds, and every eight beats the whole animal changes
// colour, from green to sea blue, to violet, to gold, the new colour spreading out scale by scale.
// Family: Organic
// Tags: 3D, Colourful, Calm
// Added: 2026-11-16 08:43

static const float ChFine = 2.3;        // how many scales lie across one step of the ground
static const float ChTall = 0.26;       // how high a scale stands, against its width

static float ChSound;                   // how loud the music is just now, taken across all its parts
static float ChWave;                    // how far the ripple has run across the skin, as an angle
static float ChBreath;                  // how deeply the folds are drawn just now

// One part of the music, with the high notes made up to stand level with the low ones.
float chPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one.
float chLoud(float x)
{
    return saturate((chPart(x) - 0.62 * ChSound) / (0.6 * ChSound + 0.1));
}

// How loud the music is for one stripe of the skin, bass at one edge of a stripe and treble at the other, read at
// ten fixed places and blended between them.
float chLoudAt(float across)
{
    float k = saturate(across) * 9.0;
    float low = floor(k);
    return lerp(chLoud(0.08 + 0.06 * low), chLoud(0.14 + 0.06 * low), smoothstep(0.0, 1.0, k - low));
}

// The rolling folds of the skin under the bumps.
float chFold(float2 s)
{
    return ChBreath * (0.2 * sin(s.x * 0.5 + 0.7 * sin(s.y * 0.37 + Time * 0.23)) + 0.11 * sin(s.y * 0.8 - s.x * 0.33 + Time * 0.31));
}

// How far the ripple lifts the scales at one spot.
float chLift(float2 s)
{
    return 0.95 + 0.17 * sin(s.x * 1.3 + s.y * 0.75 - ChWave);
}

// Where the bump of one square of the skin sits just now, measured from a spot in the middle square, less the
// bump's own girth. Every bump wanders slowly round its own place.
float chBump(float2 c, float2 offset)
{
    float2 h = float2(hash(c), hash(c + 37.3));
    return length(offset + 0.5 + 0.34 * sin(Time * (0.4 + 0.25 * h.yx) + 6.2832 * h)) - 0.1 * hash(c + 11.7);
}

// How far a spot is from the middle of the nearest bump, the nine bumps round it running together where they
// crowd.
float chNearest(float2 s)
{
    float2 cell = floor(s);
    float2 f = s - cell;
    float sum = 0.0;
    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
            sum += exp(-14.0 * chBump(cell + float2(i, j), float2(i, j) - f));
    }

    return -log(sum) / 14.0;
}

// Which bump a spot belongs to, and how far the spot is from the seam with the next one; also how far it is from
// the middle of the nearest bump, as chNearest reckons it.
float3 chWhich(float2 s, out float blended)
{
    float2 cell = floor(s);
    float2 f = s - cell;
    float nearest = 1e3;
    float next = 1e3;
    float2 which = cell;
    float sum = 0.0;
    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float d = chBump(cell + float2(i, j), float2(i, j) - f);
            sum += exp(-14.0 * d);
            if (d < nearest)
            {
                next = nearest;
                nearest = d;
                which = cell + float2(i, j);
            }
            else if (d < next)
            {
                next = d;
            }
        }
    }

    blended = -log(sum) / 14.0;
    return float3(which, next - nearest);
}

// How high the skin stands that far from a bump's middle: rounded on top, creased where the bumps meet.
float chRise(float nearest)
{
    return saturate(1.0 - nearest * abs(nearest) / 0.4);
}

float chHeight(float2 s)
{
    return chFold(s) + ChTall / ChFine * chLift(s) * chRise(chNearest(s * ChFine));
}

// The colour the animal wears through each eight beats: green, sea blue, violet, and gold.
float3 chMood(float which)
{
    float k = wrap(which, 4.0);
    return k < 0.5 ? float3(0.2, 0.92, 0.1) : k < 1.5 ? float3(0.03, 0.62, 0.95) : k < 2.5 ? float3(0.62, 0.2, 1.15) : float3(1.15, 0.68, 0.04);
}

// The colour a bump flushes to as its part of the music rises: through gold and orange to crimson where the
// stripe is warm, through turquoise to blue and violet where it is cool.
float3 chFlush(float3 green, float warm, float flushed)
{
    float3 hot = lerp(green, float3(1.1, 1.15, 0.08), smoothstep(0.0, 0.42, flushed));
    hot = lerp(hot, float3(1.65, 0.62, 0.04), smoothstep(0.3, 0.72, flushed));
    hot = lerp(hot, float3(1.6, 0.1, 0.2), smoothstep(0.62, 1.0, flushed));
    float3 cold = lerp(green, float3(0.04, 1.1, 0.75), smoothstep(0.0, 0.42, flushed));
    cold = lerp(cold, float3(0.06, 0.65, 1.45), smoothstep(0.3, 0.72, flushed));
    cold = lerp(cold, float3(0.55, 0.25, 1.5), smoothstep(0.62, 1.0, flushed));
    return lerp(cold, hot, warm);
}

// What the wet skin mirrors: a broad soft lamp overhead and a dim room.
float3 chRoom(float3 dir)
{
    float panel = smoothstep(0.35, 0.9, dir.y) * smoothstep(1.2, 0.1, abs(dir.x - 0.2));
    return float3(0.9, 0.95, 1.0) * panel + float3(0.02, 0.035, 0.03);
}

float3 Render(float2 uv, float2 p)
{
    float beats = wrap(BeatGlide, 4096.0);
    // How far through the beat: with a steady beat to follow it keeps pace with it, without one it follows how
    // far the last beat has faded. (The glide of the beat count rests between beats, so it can't serve here.)
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float flash = smoothstep(0.0, 0.22, since) * (1.0 - since) * (1.0 - since);
    float swell = BeatSwell;

    ChSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        ChSound += chPart(0.08 + 0.12 * part) / 6.0;

    // The ripple runs one crest on with every beat, and the animal breathes once in every four.
    ChWave = 6.2832 * BeatPhase + wrap(Time * 0.6, 6.2832);
    ChBreath = 1.0 + 0.2 * sin(6.2832 * BarPhase);

    // The view glides over the skin, round a wide circle.
    float2 glide = 6.0 * float2(cos(Time * 0.05), sin(Time * 0.05)) + float2(0.7 * sin(Time * 0.13), 0.6 * sin(Time * 0.091));
    float3 eye = float3(glide.x, 4.2 + 0.25 * sin(Time * 0.17), glide.y - 3.0);
    float3 ray = cameraRay(eye, float3(glide.x + 0.4 * sin(Time * 0.11 + 0.6), 0.0, glide.y + 0.5), float2(p.x, -p.y), 1.8, 0.07 * sin(Time * 0.083));
    float lampTurn = Time * 0.27;
    float3 lamp = normalize(float3(0.6 * cos(lampTurn), 0.75, 0.6 * sin(lampTurn)));

    // Down to the top of the scales first, which is quick to find, and from there on down to the skin itself.
    float t = (0.55 - eye.y) / min(ray.y, -0.05);
    float crown = ChTall / ChFine * 1.12;
    [loop]
    for (int a = 0; a < 8; a++)
    {
        float3 q = eye + ray * t;
        float d = q.y - chFold(q.xz) - crown;
        if (d < 0.004)
            break;

        t += d * 0.8;
    }

    int steps = qualitySteps(14);
    [loop]
    for (int k = 0; k < steps; k++)
    {
        float3 q = eye + ray * t;
        float d = q.y - chHeight(q.xz);
        if (d < 0.0015 * t)
            break;

        t += d * 0.7;
    }

    float3 at = eye + ray * t;
    float e = 0.008;
    float nearest;
    float3 bump = chWhich(at.xz * ChFine, nearest);
    float here = chFold(at.xz) + ChTall / ChFine * chLift(at.xz) * chRise(nearest);
    float3 n = normalize(float3(here - chHeight(at.xz + float2(e, 0.0)), e, here - chHeight(at.xz + float2(0.0, e))));

    // The colour of this bump: its own green at rest, from lime to deep emerald in slow patches. The skin's
    // markings, bars and blotches across the folds, flush with their stripe's part of the music.
    float2 lies = (bump.xy + 0.5) / ChFine;
    float mine = hash(bump.xy + 5.1);
    float patch = fbm(lies * 0.45 + 3.0);

    // The colour the animal wears changes every eight beats, the new one spreading out from under the eye.
    float fromEye = length(lies - glide + float2(0.0, 2.0));
    float phrase = beats / 8.0;
    float changed = saturate((frac(phrase) * 3.2 - fromEye * 0.11 - 0.3 * mine) / 0.06);
    float3 mood = lerp(chMood(floor(phrase) - 1.0), chMood(floor(phrase)), changed);
    float3 ground = mood * lerp(0.45, 1.1, saturate(smoothstep(0.25, 0.7, patch) + 0.3 * (mine - 0.5))) + float3(0.02, 0.03, 0.02);

    float across = abs(wrap(lies.x * 0.16 + 0.5 * sin(lies.y * 0.4), 2.0) - 1.0);
    float marking = smoothstep(0.3, 0.55, fbm(lies * float2(1.1, 0.5) + float2(7.0, 1.0)) + 0.1 * (mine - 0.5));
    float ring = fromEye * 0.1 - since * 1.4;
    float blush = flash * exp(-ring * ring * 14.0);
    float flushed = (0.3 + 0.7 * marking) * saturate(1.15 * chLoudAt(across) + 0.7 * blush + 0.1 * swell - 0.12);
    float3 skin = chFlush(ground, smoothstep(0.62, 0.38, across), flushed);

    // Wet and glossy under two lamps, dark in the creases.
    float crease = smoothstep(0.62, 0.3, nearest) * (0.45 + 0.55 * smoothstep(0.0, 0.14, bump.z));
    float close = smoothstep(11.0, 6.0, t);
    float3 bounce = reflect(ray, n);
    float glancing = 1.0 - saturate(dot(n, -ray));
    float3 fill = normalize(float3(-lamp.x, 0.9, -lamp.z));
    // Round each scale's rim the wet skin shows a film of other colours.
    float3 film = 0.5 + 0.5 * cos(6.2832 * (glancing * 1.3 + mine + 0.2 * phrase + float3(0.0, 0.33, 0.67)));
    skin = lerp(skin, skin * 0.4 + film * 0.9, 0.5 * glancing * glancing);
    float3 colour = skin * (0.07 + 0.8 * saturate(dot(n, lamp)) + 0.2 * saturate(dot(n, fill)) + 0.25 * glancing * glancing) * (0.12 + 0.88 * crease)
                  + chRoom(bounce) * (0.04 + 0.96 * pow(glancing, 5.0)) * crease * 0.6
                  + float3(1.0, 1.0, 0.95) * (1.5 * close * pow(saturate(dot(bounce, lamp)), 120.0) + 0.14 * pow(saturate(dot(bounce, lamp)), 12.0)) * crease
                  + float3(0.7, 0.9, 1.0) * 0.8 * close * pow(saturate(dot(bounce, fill)), 70.0) * crease;

    // The far skin sinks into green dark.
    colour = lerp(colour, float3(0.004, 0.012, 0.012) + 0.012 * mood, smoothstep(7.0, 13.5, t));

    return colour * (1.0 - 0.2 * dot(p, p) / (1.0 + Aspect * Aspect));
}
