// Name: Hyperboloid
// Straight neon strings stretched between two turning rings bend into a perfect curved waist, the way a hyperboloid is
// woven from nothing but straight lines. The figure dances in time: every beat twists the rings against each other
// and pulls the waist tight, every bar swings it between a vase and a funnel, and a wave runs round both rims so
// they ripple like a crown. Every string plays its own part of the music and flares as it sounds, a second set of
// strings leaning the other way glows in with the middle of the music, each beat sends pulses of light along the
// strings from both rings to cross at the waist, and the colours change every eight beats.
// Family: Minimal
// Tags: Neon, Minimal, Geometric, 3D
// Added: 2026-10-31 23:45

// A string from angle a + twist on the top ring to a - twist on the bottom ring passes closest to the axis at
// radius cos(twist) halfway down, so a ring of such strings draws a hyperboloid of one sheet. Strings leaning
// the other way (a - twist to a + twist) lie on the same surface, which is why it has two families of lines.
static const int Strings = 36;
static const float Height = 0.95;

static float HbSound;                   // how loud the music is just now, taken across all its parts
static float HbWave;                    // where the wave round the rims has got to: once round a crest per beat
static float HbFlare;                   // how much wider the top ring is than the bottom one: it swings with the bar

// One part of the music, with the high notes made up to stand level with the low ones.
float hbPart(float x)
{
    return spectrum(x) * (1.0 + 0.9 * x);
}

// How loud one part of the music is, judged against the music as a whole, so that a quiet song shows as plainly
// as a loud one.
float hbLoud(float x)
{
    return saturate((hbPart(x) - 0.62 * HbSound) / (0.6 * HbSound + 0.1));
}

// How far each ring turns against the other, in radians. It sways with the bar, and each beat and a drop pull it
// tighter.
float hbTwist()
{
    return 0.6 + sin(Time * 0.045) * 0.2 + 0.17 * sin(6.2832 * BarPhase) + 0.3 * BeatSwell + DropPulse * 0.3;
}

// The camera: how far it tilts down onto the figure (as sin, cos) and how far it zooms in.
float3 hbCamera()
{
    float s, c;
    sincos(0.32 + sin(Time * 0.031) * 0.14 + 0.05 * sin(6.2832 * BarPhase + 0.9), s, c);
    return float3(s, c, 2.35 * (1.0 + DropPulse * 0.05));
}

// Projects the point of a ring at this angle to the screen (y down), keeping how near it is in z. `side` is 1
// for the top ring and -1 for the bottom one. The rings flare against each other, and a wave runs round each rim.
float3 hbRing(float angle, float side, float height, float3 camera)
{
    float s, c;
    sincos(angle, s, c);
    float wide = 1.0 + side * HbFlare;
    float y = side * (height + 0.065 * sin(3.0 * angle - side * HbWave));
    float3 v = float3(wide * c, y * camera.y - wide * s * camera.x, y * camera.x + wide * s * camera.y);
    return float3(float2(v.x, -v.y) * camera.z / (4.2 - v.z) - float2(0.0, 0.09), v.z);
}

// Distance from p to the segment a..b, and how far along it the closest point is.
float2 hbSegment(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float along = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
    return float2(length(p - a - ab * along), along);
}

// How far round the colours the figure has moved on: a new step every eight beats.
float hbMood(float which)
{
    float k = wrap(which, 4.0);
    return k < 0.5 ? 0.0 : (k < 1.5 ? 0.13 : (k < 2.5 ? -0.12 : 0.21));
}

float3 hbNeon(float t, float mood)
{
    return hsv2rgb(frac(0.48 + mood + t * 0.4 + sin(Time * 0.017) * 0.05), 0.85, 1.0);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();

    HbSound = 0.0;
    [unroll]
    for (int part = 0; part < 6; part++)
        HbSound += hbPart(0.08 + 0.12 * part) / 6.0;
    HbWave = 6.2832 * BeatPhase + wrap(Time * 0.5, 6.2832);
    HbFlare = 0.11 * cos(6.2832 * BarPhase) * saturate(SongMapped * 4.0) + 0.05 * sin(Time * 0.21);

    float twist = hbTwist();
    float turn = wrap(Time * 0.07, 2.0 * PI);
    float swell = BeatSwell;
    float height = Height * (1.0 + 0.04 * swell);
    float other = saturate(0.22 + 0.75 * hbLoud(0.38) + BuildUp * 0.5 + DropPulse * 0.5);

    // The pulses each beat sends along the strings, one from each ring, crossing at the waist. With a steady
    // beat to follow they keep pace with the beat; without one they follow how far the last beat has faded.
    float since = SongMapped > 0.25 ? BeatPhase : saturate(-log(max(Beat, 0.004)) / 2.75);
    float fade = smoothstep(0.0, 0.08, since) * (1.0 - since);

    // The colours move on every eight beats.
    float phrase = wrap(BeatGlide, 4096.0) / 8.0;
    float mood = lerp(hbMood(floor(phrase) - 1.0), hbMood(floor(phrase)), smoothstep(0.0, 0.12, frac(phrase)));

    float3 camera = hbCamera();
    float3 colour = 0.0;
    // The figure stays within about 1.1 of the middle across, so pixels well outside it skip the strings.
    if (abs(p.x) < 1.25)
    {
        [loop]
        for (int i = 0; i < Strings; i++)
        {
            float a = turn + i * (2.0 * PI / Strings);
            // Bands mirror round the ring, so the music keeps the figure's symmetry.
            float loud = hbLoud(0.05 + abs(frac(i / (float)Strings * 2.0) * 2.0 - 1.0) * 0.6);
            // Each string's ends; the first family's ends are also where the beads sit.
            float3 ends[4] = { hbRing(a + twist, 1.0, height, camera), hbRing(a - twist, -1.0, height, camera),
                               hbRing(a - twist, 1.0, height, camera), hbRing(a + twist, -1.0, height, camera) };
            [unroll]
            for (int family = 0; family < 2; family++)
            {
                float3 top = ends[family * 2];
                float3 bottom = ends[family * 2 + 1];
                float2 hit = hbSegment(p, top.xy, bottom.xy);
                float d = hit.x;
                if (d > px * 40.0)
                {
                    continue;
                }
                float along = hit.y;
                // Strings at the back of the figure are dimmer, so it reads as solid.
                float near = lerp(top.z, bottom.z, along);
                float depth = 0.35 + 0.65 * smoothstep(-1.0, 1.0, near);
                // A string glows wider the louder its part of the music.
                float stroke = exp(-pow(d / (px * 1.2), 2.0)) + exp(-d / (px * 7.0)) * (0.08 + 0.13 * loud);
                float2 at = (float2(along, 1.0 - along) - since * 1.3 + 0.12) * 6.0;
                float pulse = (exp(-at.x * at.x) + exp(-at.y * at.y)) * fade * 0.8;
                float strength = (0.2 + loud * 1.5 + pulse * 1.9) * depth * (family == 0 ? 1.0 : other);
                float3 tint = hbNeon(along * 0.85 + family * 0.18, mood);
                colour += lerp(tint, float3(1.0, 0.95, 0.9), saturate(pulse * 0.5)) * stroke * strength;
            }
            // A bead of light where each string meets a ring: it flares with its string and with the beat.
            float beads = exp(-pow(length(p - ends[0].xy) / (px * 2.4), 2.0)) * (0.5 + 0.5 * smoothstep(-1.0, 1.0, ends[0].z))
                + exp(-pow(length(p - ends[1].xy) / (px * 2.4), 2.0)) * (0.5 + 0.5 * smoothstep(-1.0, 1.0, ends[1].z));
            colour += float3(0.9, 0.95, 1.0) * beads * (0.5 + 1.3 * loud + 0.7 * swell);
        }
    }

    // Where the strings bunch together at the waist they would burn out, so ease the brightest parts down.
    colour = colour / (1.0 + max(max(colour.r, colour.g), colour.b) * 0.25);
    colour *= 0.8 + Intensity * 0.4;
    colour += hbNeon(0.35, mood) * 0.06 * exp(-dot(p, p) * 2.5) * (0.4 + 0.9 * hbLoud(0.08));
    return colour + previous(uv) * 0.25;
}
