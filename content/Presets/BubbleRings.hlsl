// Name: Bubble Rings
// Two ribbons of pearly light, each given a half twist and closed into a ring, hang linked together inside a
// glowing bubble with a golden rim, on a deep twilight ground. The linked pair tumbles slowly as one, rocking with
// each bar, while each ring spins round on itself and its ribbon rolls, always clear of the other ring. Each ring listens to its own part of the music, one the low
// notes and one the high, and its pearl brightens and its colours deepen as that part sounds; each beat sends a
// bead of light running round both rings and makes the bubble breathe, and every eight beats the colours move on.
// Family: Ray traced
// Tags: Geometric, 3D, Colourful
// Added: 2026-11-16 16:34

static const float Hoop = 0.52;         // how wide each ring is, to the middle of its ribbon
static const float Wide = 0.13;         // half the width of a ribbon
static const float Thin = 0.03;        // half the thickness of a ribbon

static float BrSound;                   // how loud the music is just now, taken across all its parts
static float BrRoll;                    // how far the ribbons have rolled round their rings
static float3x3 BrTurn;                 // how the linked pair is turned
static float BrSpinA;                   // how far each ring has spun round on itself
static float BrSpinB;

// One part of the music, with the high notes made up to stand level with the low ones.
float brPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one. It listens to a broad part and answers gently, so the light swells and never jitters.
float brLoud(float x)
{
    float heard = (brPart(x - 0.03) + brPart(x) + brPart(x + 0.03)) / 3.0;
    return smoothstep(0.0, 1.0, saturate((heard - 0.55 * BrSound) / (0.8 * BrSound + 0.12)));
}

float3x3 brTurn(float yaw, float pitch, float roll)
{
    float sy, cy, sp, cp, sr, cr;
    sincos(yaw, sy, cy);
    sincos(pitch, sp, cp);
    sincos(roll, sr, cr);
    float3x3 y = float3x3(cy, 0.0, -sy, 0.0, 1.0, 0.0, sy, 0.0, cy);
    float3x3 x = float3x3(1.0, 0.0, 0.0, 0.0, cp, -sp, 0.0, sp, cp);
    float3x3 z = float3x3(cr, -sr, 0.0, sr, cr, 0.0, 0.0, 0.0, 1.0);
    return mul(z, mul(x, y));
}

// Distance to one ring lying flat round its own y axis, and how far round it the nearest spot is (0..1).
float2 brRing(float3 q, float spun)
{
    q.xz = rotate(q.xz, spun);
    float round0 = atan2(q.z, q.x);
    float2 cut = float2(length(q.xz) - Hoop, q.y);
    // Half a turn of twist all the way round, so the ribbon closes on itself with its faces swapped.
    cut = rotate(cut, 0.5 * round0 + BrRoll);
    float2 d = abs(cut) - float2(Wide, Thin);
    float band = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - 0.012;
    return float2(band * 0.85, round0 / 6.2832 + 0.5);
}

// Distance to the nearer ring; which ring it is (0 or 1); how far round it.
float3 brRings(float3 x)
{
    // The two rings lie square to each other, each passing through the other's middle, so the middles of their
    // ribbons never come nearer than a ring's radius and the ribbons never touch.
    float3 y = mul(BrTurn, x);
    float2 a = brRing(y - float3(-0.5 * Hoop, 0.0, 0.0), BrSpinA);
    float3 fromB = y - float3(0.5 * Hoop, 0.0, 0.0);
    float2 b = brRing(fromB.xzy, BrSpinB);
    return a.x < b.x ? float3(a.x, 0.0, a.y) : float3(b.x, 1.0, b.y);
}

// Pearl: soft pastel colours that slide as the surface turns toward or away from the eye.
float3 brPearl(float t)
{
    float3 c = 0.5 + 0.5 * cos(6.2832 * (t + float3(0.0, 0.33, 0.67)));
    return lerp(c, float3(1.0, 1.0, 1.0), 0.28) * 1.15;
}

float3 Render(float2 uv, float2 p)
{
    BrSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        BrSound += brPart(0.08 + 0.12 * part) / 6.0;

    float barTurn = 6.2832 * BarPhase;
    float spin = wrap(Time * 0.21, 6.2832);
    BrRoll = wrap(Time * 0.35, 6.2832) + 0.35 * sin(6.2832 * BeatPhase);
    BrTurn = brTurn(spin + 0.25 * sin(barTurn), 0.6 + 0.35 * sin(Time * 0.13), 0.2 * sin(barTurn + 1.0));
    BrSpinA = wrap(Time * 0.37, 6.2832);
    BrSpinB = -wrap(Time * 0.29, 6.2832);

    float phrase = wrap(BeatGlide, 4096.0) / 8.0;
    float moved = floor(phrase) + smoothstep(0.0, 0.15, frac(phrase));
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float fade = smoothstep(0.0, 0.08, since) * (1.0 - since);
    float swell = BeatSwell;

    // Twilight ground, a little lighter behind the bubble.
    float3 colour = lerp(float3(0.1, 0.07, 0.2), float3(0.03, 0.04, 0.1), smoothstep(0.0, 1.4, length(p)));
    colour = lerp(colour, colour.bgr * float3(1.0, 0.8, 1.3), 0.35 + 0.35 * sin(6.2832 * moved / 4.0));

    float3 eye = float3(0.0, 0.0, -2.6);
    float3 ray = normalize(float3(p.x, -p.y, 2.0));
    float bubble = 1.0 + 0.025 * cos(6.2832 * BeatPhase) + 0.03 * swell;

    float b = dot(eye, ray);
    float h = b * b - dot(eye, eye) + bubble * bubble;
    if (h <= 0.0)
    {
        // A faint golden glow round the outside of the bubble.
        float off = length(p) - bubble * 0.8;
        colour += float3(1.2, 0.8, 0.25) * exp(-max(off, 0.0) * 18.0) * (0.12 + 0.12 * swell);
        return colour;
    }
    h = sqrt(h);
    float enter = -b - h;
    float leave = -b + h;

    // The bubble's skin: clear in the middle, glowing gold at the rim, with a little pink haze inside.
    float3 skin = normalize(eye + ray * enter);
    float edge = 1.0 - abs(dot(skin, ray));
    float3 rimColour = lerp(float3(1.5, 1.1, 0.25), float3(1.4, 0.4, 0.9), 0.5 + 0.5 * sin(atan2(skin.y, skin.x) * 2.0 + Time * 0.3));
    float3 shell = rimColour * (pow(edge, 7.0) * (1.2 + 0.8 * swell) + pow(edge, 2.0) * 0.12);
    float3 inside = colour * 0.85 + float3(0.35, 0.12, 0.45) * (0.12 + 0.1 * swell);

    float t = enter;
    float3 found = float3(1e3, 0.0, 0.0);
    int steps = qualitySteps(90);
    [loop]
    for (int i = 0; i < steps; i++)
    {
        float3 r = brRings(eye + ray * t);
        if (r.x < 0.0012 * surfacePrecision())
        {
            found = r;
            break;
        }
        t += r.x;
        if (t > leave) break;
    }

    if (found.x < 1e2)
    {
        float3 x = eye + ray * t;
        float2 e = float2(0.0015, -0.0015);
        float3 n = normalize(e.xyy * brRings(x + e.xyy).x + e.yyx * brRings(x + e.yyx).x
                           + e.yxy * brRings(x + e.yxy).x + e.xxx * brRings(x + e.xxx).x);
        if (dot(n, ray) > 0.0) n = -n;
        float facing = saturate(-dot(n, ray));
        float rim = 1.0 - facing;

        // Each ring listens to its own part of the music: the first the low notes, the second the high.
        float loud = found.y < 0.5 ? brLoud(0.12) : brLoud(0.5);
        // The bead of light each beat sends round the rings.
        float apart = abs(frac(found.z - since) - 0.5) * 2.0;
        float bead = exp(-(1.0 - apart) * (1.0 - apart) * 40.0) * fade;

        float3 pearl = brPearl(0.6 * rim + found.z + 0.25 * found.y + 0.21 * moved);
        float3 lamp = normalize(float3(-0.5, 0.7, -0.6));
        float lit = 0.45 + 0.55 * saturate(dot(n, lamp));
        float3 deep = lerp(pearl, pearl * pearl * 1.3, loud);
        float3 shown = deep * lit * (0.5 + 0.55 * loud) + pearl * rim * rim * (0.6 + 0.9 * loud);
        shown += float3(1.0, 0.95, 0.85) * pow(saturate(dot(reflect(ray, n), lamp)), 40.0) * 0.6;
        shown += lerp(pearl, float3(1.0, 1.0, 1.0), 0.4) * bead * 1.1;
        inside = shown;
    }

    return inside * (1.0 - 0.5 * pow(edge, 7.0)) + shell;
}
