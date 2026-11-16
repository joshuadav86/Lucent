// Name: Ribbon Candy
// A sheet of striped candy fills the screen, pulled and folded over itself while still warm: glossy round strands
// of hot pink, orange, yellow, green, sky blue, and violet, looping and doubling back. The folds swing with each
// bar and a lamp circles overhead once a bar, so the shine slides along every strand in time. Each colour of
// strand listens to its own part of the music and glows from within as it sounds, every beat sends a ripple out
// through the sheet from the middle, and every eight beats each strand takes on its neighbour's colour.
// Family: Organic
// Tags: Colourful, Retro, Intense
// Added: 2026-11-16 16:22

static float RcSound;                   // how loud the music is just now, taken across all its parts
static float RcSwing;                   // how far the folds have swung: one way and back with each bar
static float RcPull;                    // how hard the sheet is pulled about: a little harder just after each beat

// One part of the music, with the high notes made up to stand level with the low ones.
float rcPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one. It listens to a broad part and answers gently, so the glow swells and never jitters.
float rcLoud(float x)
{
    float heard = (rcPart(x - 0.03) + rcPart(x) + rcPart(x + 0.03)) / 3.0;
    return smoothstep(0.0, 1.0, saturate((heard - 0.55 * RcSound) / (0.8 * RcSound + 0.12)));
}

// Which strand lies at this spot: a number that climbs by one from each strand to the next. The sheet is pushed
// sideways in waves, each wave finer than the last, and every push bends the pushes that come after it, which is
// what folds the strands over one another.
float rcStrand(float2 p)
{
    float2 q = p;
    [unroll]
    for (int i = 1; i < 6; i++)
    {
        float fold = i;
        q.x += RcPull / fold * sin(fold * 1.45 * q.y + Time * 0.23 + 1.7 * fold + RcSwing);
        q.y += RcPull / fold * cos(fold * 1.45 * q.x - Time * 0.17 + 0.9 * fold - RcSwing);
    }
    return (q.x + 0.55 * q.y) * 2.0 + 0.5 * sin(q.y * 0.9 - q.x * 0.6) + Time * 0.16;
}

// The six colours of candy, in the order the strands lie.
float3 rcCandy(float which)
{
    float k = wrap(which, 6.0);
    if (k < 0.5) return float3(1.5, 0.1, 0.62);
    if (k < 1.5) return float3(1.6, 0.5, 0.05);
    if (k < 2.5) return float3(1.4, 1.0, 0.08);
    if (k < 3.5) return float3(0.2, 1.3, 0.3);
    if (k < 4.5) return float3(0.06, 0.85, 1.5);
    return float3(0.62, 0.3, 1.6);
}

float3 Render(float2 uv, float2 p)
{
    RcSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        RcSound += rcPart(0.08 + 0.12 * part) / 6.0;
    float barTurn = 6.2832 * BarPhase;
    RcSwing = 0.3 * sin(barTurn) + 0.2 * sin(Time * 0.13);
    RcPull = 0.6 + 0.035 * BeatSwell + 0.1 * DropPulse + 0.04 * sin(Time * 0.071);

    // The sheet turns very slowly and drifts nearer and further.
    float2 at = rotate(p, 0.25 * sin(Time * 0.043)) * (1.0 + 0.1 * sin(Time * 0.061));

    // Each beat sends a ripple out from the middle that pushes the candy aside as it passes. With a steady beat
    // to follow the ripple keeps pace with it; without one it follows how far the last beat has faded.
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float far = length(p);
    float crest = far - since * 1.9 * max(Aspect, 1.0);
    float ripple = exp(-crest * crest * 11.0) * smoothstep(0.0, 0.1, since) * (1.0 - since);
    at += p / max(far, 0.05) * 0.055 * ripple;

    // Which strand, and how fast the strands change across the screen here.
    float px = pixelSize();
    float span = 1.5 * px;
    float strand = rcStrand(at);
    float2 slope = float2(rcStrand(at + float2(span, 0.0)) - strand, rcStrand(at + float2(0.0, span)) - strand) / span;
    float steep = max(length(slope), 1e-4);
    float2 across = slope / steep;
    float which = floor(strand);
    float within = frac(strand);

    // Each strand is round: its surface faces the way the strands are stacked at its two edges and straight out
    // of the screen along its middle.
    float3 facing = float3(-cos(PI * within) * across, sin(PI * within));

    // A lamp circles overhead once a bar, so the shine travels along the strands in time.
    float lampTurn = barTurn + Time * 0.37;
    float3 lamp = normalize(float3(cos(lampTurn), sin(lampTurn), 0.85));
    float lit = saturate(dot(facing, lamp));
    float shine = pow(saturate(dot(facing, normalize(lamp + float3(0.0, 0.0, 1.0)))), 46.0);

    // Every eight beats each strand takes on its neighbour's colour.
    float phrase = wrap(BeatGlide, 4096.0) / 8.0;
    float turned = smoothstep(0.0, 0.12, frac(phrase));
    float3 candy = lerp(rcCandy(which + floor(phrase) - 1.0), rcCandy(which + floor(phrase)), turned);

    // Each colour of strand listens to its own part of the music and glows from within as it sounds. The colour a
    // strand shows is the part it listens to, so the bass stays pink and the treble violet.
    float band = lerp(wrap(which + floor(phrase) - 1.0, 6.0), wrap(which + floor(phrase), 6.0), step(0.5, turned));
    float loud = rcLoud(0.07 + 0.105 * band);

    // Where the strands are squeezed thinner than a few pixels they blend into one another, and the crease
    // between two strands is dark.
    float wide = 1.0 / (steep * px);
    float fine = smoothstep(2.0, 7.0, wide);
    float crease = smoothstep(0.0, 1.6, min(within, 1.0 - within) * wide);
    float body = lerp(0.62, facing.z, fine);

    float3 colour = candy * (0.2 + 0.5 * lerp(0.6, lit, fine) + (0.12 + 0.85 * loud) * body);
    colour *= lerp(1.0, 0.25 + 0.75 * crease, fine);
    colour += float3(1.0, 0.97, 0.9) * shine * fine * (0.55 + 0.5 * loud);
    colour += candy * 0.5 * ripple * body;
    return colour * (0.86 + 0.14 * Intensity);
}
