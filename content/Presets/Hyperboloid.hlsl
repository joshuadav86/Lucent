// Name: Hyperboloid
// Straight neon strings stretched between two turning rings bend into a perfect curved waist, the way a hyperboloid is
// woven from nothing but straight lines. The bass twists the rings against each other, pulling the waist tight towards a
// double cone and letting it fall back to an open drum. A second set of strings leaning the other way glows in with the
// mids and the build-ups to close the lattice, every string plays its own band of the spectrum, and each beat sends a
// pulse of light running down from the top ring.
// Family: Minimal
// Tags: Neon, Minimal, Geometric, 3D
// Added: 2026-10-31 23:45

// A string from angle a + twist on the top ring to a - twist on the bottom ring passes closest to the axis at
// radius cos(twist) halfway down, so a ring of such strings draws a hyperboloid of one sheet. Strings leaning
// the other way (a - twist to a + twist) lie on the same surface, which is why it has two families of lines.
static const int Strings = 36;
static const float Height = 0.95;

// How far each ring turns against the other, in radians: the bass and a drop pull it tighter.
float hbTwist()
{
    return 0.62 + sin(Time * 0.045) * 0.22 + Bass * 0.38 + DropPulse * 0.35;
}

// The whole figure turns slowly, and each beat gives it a small push.
float hbTurn()
{
    return Time * 0.06 + (BeatCount - Beat) * 0.03;
}

// Projects a point on the figure (y up) to the screen (y down), keeping how near it is in z.
float3 hbProject(float3 w)
{
    float tilt = 0.32 + sin(Time * 0.031) * 0.14;
    float s, c;
    sincos(tilt, s, c);
    float3 v = float3(w.x, w.y * c - w.z * s, w.y * s + w.z * c);
    float distance = 4.2;
    float zoom = 2.35 * (1.0 + DropPulse * 0.05);
    return float3(float2(v.x, -v.y) * zoom / (distance - v.z) - float2(0.0, 0.09), v.z);
}

float3 hbRing(float angle, float y)
{
    float s, c;
    sincos(angle, s, c);
    return hbProject(float3(c, y, s));
}

// Distance from p to the segment a..b, and how far along it the closest point is.
float2 hbSegment(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float along = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
    return float2(length(p - a - ab * along), along);
}

float3 hbNeon(float t)
{
    return hsv2rgb(frac(0.48 + t * 0.4 + sin(Time * 0.017) * 0.05), 0.85, 1.0);
}

float3 Render(float2 uv, float2 p)
{
    float px = pixelSize();
    float twist = hbTwist();
    float turn = hbTurn();
    float sinceBeat = -log(max(Beat, 1e-4)) / 5.0;
    float other = saturate(0.18 + Mid * 0.5 + BuildUp * 0.6 + DropPulse * 0.5);
    float height = Height * (1.0 + Bass * 0.04);

    float3 colour = 0.0;
    [loop]
    for (int i = 0; i < Strings; i++)
    {
        float a = turn + i * (2.0 * PI / Strings);
        // Bands mirror round the ring, so the music keeps the figure's symmetry.
        float band = spectrum(0.03 + abs(frac(i / (float)Strings * 2.0) * 2.0 - 1.0) * 0.6);
        [unroll]
        for (int family = 0; family < 2; family++)
        {
            float lean = family == 0 ? twist : -twist;
            float3 top = hbRing(a + lean, height);
            float3 bottom = hbRing(a - lean, -height);
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
            float stroke = exp(-pow(d / (px * 1.2), 2.0)) + exp(-d / (px * 7.0)) * 0.1;
            float at = (along - sinceBeat * 1.6) * 7.0;
            float pulse = exp(-at * at) * Beat;
            float strength = (0.22 + band * 1.1 + pulse * 1.6) * depth * (family == 0 ? 1.0 : other);
            float3 tint = hbNeon(along * 0.85 + family * 0.18);
            colour += lerp(tint, float3(1.0, 0.95, 0.9), saturate(pulse * 0.5)) * stroke * strength;
        }
        // A bead of light where each string meets a ring.
        float3 topBead = hbRing(a + twist, height);
        float3 bottomBead = hbRing(a - twist, -height);
        float beads = exp(-pow(length(p - topBead.xy) / (px * 2.4), 2.0)) * (0.5 + 0.5 * smoothstep(-1.0, 1.0, topBead.z))
            + exp(-pow(length(p - bottomBead.xy) / (px * 2.4), 2.0)) * (0.5 + 0.5 * smoothstep(-1.0, 1.0, bottomBead.z));
        colour += float3(0.9, 0.95, 1.0) * beads * (0.5 + band + Beat * 0.6);
    }

    // Where the strings bunch together at the waist they would burn out, so ease the brightest parts down.
    colour = colour / (1.0 + max(max(colour.r, colour.g), colour.b) * 0.25);
    colour *= 0.8 + Intensity * 0.4;
    colour += float3(0.02, 0.03, 0.07) * exp(-dot(p, p) * 2.5) * (0.5 + Bass);
    return colour + previous(uv) * 0.25;
}
