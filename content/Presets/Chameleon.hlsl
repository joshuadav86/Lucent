// Name: Chameleon
// The skin of a chameleon, close enough to count the scales: wet, rounded, bright green beads packed tight over
// slow folds, crowding and parting as the animal breathes, under a lamp that circles overhead while the view
// glides across. The music plays in its colours: the skin's markings each listen to their own part of the music
// and flush, scale by scale, from green through gold and orange to crimson for the low notes and through
// turquoise to blue and violet for the high ones, and each beat sends a blush running out over the folds.
// Family: Organic
// Tags: 3D, Colourful, Calm
// Added: 2026-11-16 08:43

static const float ChFine = 2.3;        // how many scales lie across one step of the ground
static const float ChTall = 0.26;       // how high a scale stands, against its width

float chLoud(float x)
{
    return saturate((spectrum(x) - 0.42 + 0.5 * x) / 0.7);
}

// How loud the music is for one stripe of the skin, bass at one edge of a stripe and treble at the other, read at
// ten fixed places and blended between them.
float chLoudAt(float across)
{
    float k = saturate(across) * 9.0;
    float low = floor(k);
    return lerp(chLoud(0.08 + 0.06 * low), chLoud(0.14 + 0.06 * low), smoothstep(0.0, 1.0, k - low));
}

// The slow folds of the skin under the bumps.
float chFold(float2 s)
{
    return 0.2 * sin(s.x * 0.5 + 0.7 * sin(s.y * 0.37 + Time * 0.05)) + 0.11 * sin(s.y * 0.8 - s.x * 0.33 + Time * 0.07);
}

// Where the bump of one square of the skin sits just now, measured from a spot in the middle square, less the
// bump's own girth. Every bump wanders slowly round its own place.
float chBump(float2 c, float2 offset)
{
    float2 h = float2(hash(c), hash(c + 37.3));
    return length(offset + 0.5 + 0.34 * sin(Time * (0.14 + 0.1 * h.yx) + 6.2832 * h)) - 0.1 * hash(c + 11.7);
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

// Which bump a spot belongs to, and how far the spot is from the seam with the next one.
float3 chWhich(float2 s)
{
    float2 cell = floor(s);
    float2 f = s - cell;
    float nearest = 1e3;
    float next = 1e3;
    float2 which = cell;
    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float d = chBump(cell + float2(i, j), float2(i, j) - f);
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

    return float3(which, next - nearest);
}

// How high the skin stands that far from a bump's middle: rounded on top, creased where the bumps meet.
float chRise(float nearest)
{
    return saturate(1.0 - nearest * abs(nearest) / 0.4);
}

float chHeight(float2 s)
{
    return chFold(s) + ChTall / ChFine * chRise(chNearest(s * ChFine));
}

// The colour a bump flushes to as its part of the music rises: green through gold and orange to crimson where the
// stripe is warm, green through turquoise to blue and violet where it is cool.
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
    float since = frac(BeatGlide);
    float flash = smoothstep(0.0, 0.22, since) * (1.0 - since) * (1.0 - since);

    // The view glides slowly over the skin, round a wide circle.
    float2 glide = 6.0 * float2(cos(Time * 0.02), sin(Time * 0.02)) + float2(0.5 * sin(Time * 0.043), 0.4 * sin(Time * 0.031));
    float3 eye = float3(glide.x, 4.2, glide.y - 3.0);
    float3 ray = cameraRay(eye, float3(glide.x + 0.3 * sin(Time * 0.043 + 0.6), 0.0, glide.y + 0.5), float2(p.x, -p.y), 1.8, 0.04 * sin(Time * 0.037));
    float lampTurn = Time * 0.09;
    float3 lamp = normalize(float3(0.6 * cos(lampTurn), 0.75, 0.6 * sin(lampTurn)));

    // Down to the skin.
    float t = (0.55 - eye.y) / min(ray.y, -0.05);
    int steps = qualitySteps(40);
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
    float here = chHeight(at.xz);
    float3 n = normalize(float3(here - chHeight(at.xz + float2(e, 0.0)), e, here - chHeight(at.xz + float2(0.0, e))));

    // The colour of this bump: its own green at rest, from lime to deep emerald in slow patches. The skin's
    // markings, bars and blotches across the folds, flush with their stripe's part of the music.
    float nearest = chNearest(at.xz * ChFine);
    float3 bump = chWhich(at.xz * ChFine);
    float2 lies = (bump.xy + 0.5) / ChFine;
    float mine = hash(bump.xy + 5.1);
    float patch = fbm(lies * 0.45 + 3.0);
    float3 green = lerp(float3(0.03, 0.62, 0.17), float3(0.42, 1.08, 0.06), saturate(smoothstep(0.25, 0.7, patch) + 0.3 * (mine - 0.5)));
    float across = abs(wrap(lies.x * 0.16 + 0.5 * sin(lies.y * 0.4), 2.0) - 1.0);
    float marking = smoothstep(0.37, 0.55, fbm(lies * float2(1.1, 0.5) + float2(7.0, 1.0)) + 0.1 * (mine - 0.5));
    float ring = length(lies - glide + float2(0.0, 2.0)) * 0.1 - since * 1.4;
    float flushed = (0.08 + 0.72 * marking) * smoothstep(0.2, 1.3, chLoudAt(across) + 0.3 * flash * exp(-ring * ring * 14.0));
    float3 skin = chFlush(green, smoothstep(0.62, 0.38, across), flushed);

    // Wet and glossy under two lamps, dark in the creases.
    float crease = smoothstep(0.62, 0.3, nearest) * (0.45 + 0.55 * smoothstep(0.0, 0.14, bump.z));
    float close = smoothstep(11.0, 6.0, t);
    float3 bounce = reflect(ray, n);
    float glancing = 1.0 - saturate(dot(n, -ray));
    float3 fill = normalize(float3(-lamp.x, 0.9, -lamp.z));
    float3 colour = skin * (0.07 + 0.8 * saturate(dot(n, lamp)) + 0.2 * saturate(dot(n, fill)) + 0.25 * glancing * glancing) * (0.12 + 0.88 * crease)
                  + chRoom(bounce) * (0.04 + 0.96 * pow(glancing, 5.0)) * crease * 0.6
                  + float3(1.0, 1.0, 0.95) * (1.5 * close * pow(saturate(dot(bounce, lamp)), 120.0) + 0.14 * pow(saturate(dot(bounce, lamp)), 12.0)) * crease
                  + float3(0.7, 0.9, 1.0) * 0.8 * close * pow(saturate(dot(bounce, fill)), 70.0) * crease;

    // The far skin sinks into green dark.
    colour = lerp(colour, float3(0.004, 0.02, 0.012), smoothstep(7.0, 13.5, t));

    return colour * (1.0 - 0.2 * dot(p, p) / (1.0 + Aspect * Aspect));
}
