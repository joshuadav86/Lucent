// Name: Wormhole Transit
// Hurtling down a winding wormhole whose walls are nested veils of streaming light, squeezing through glowing throats.
// Family: Fly-through
// Tags: Intense, Neon, Space

static const float ShellGrowth = 1.35;    // each veil is this much wider than the one inside it
static const float ThroatSpacing = 14.0;  // distance between the narrow throats

float2 wormPath(float z)
{
    return float2(sin(z * 0.11) * 1.6 + sin(z * 0.047) * 1.1, cos(z * 0.09) * 1.2);
}

// How tightly the wormhole squeezes at z: 1 in a throat, 0 between them.
float wormPinch(float z)
{
    float c = 0.5 + 0.5 * cos(z * (2.0 * PI / ThroatSpacing));
    return c * c * c * c;
}

float wormRadius(float z)
{
    return 1.15 - 0.65 * wormPinch(z);
}

// Colours of the wormhole: a half-circle of the colour wheel (cyan, electric blue, violet,
// magenta, rose) that slowly turns, drifting towards aqua and emerald or towards fire and gold.
float3 wormColour(float t)
{
    return hsv2rgb(0.48 + 0.5 * abs(frac(t) * 2.0 - 1.0) + 0.16 * sin(Time * 0.03), 0.8, 1.0);
}

// The light threads woven into one veil: thin bright helices that wander as they spiral.
// Neighbouring veils twist in opposite directions. halfWidth is the threads' half-width and
// spacing the gap between them, in world units; threads too fine to resolve fade to an even glow.
// Returns the threads' brightness (x) and which thread is nearest (y).
float2 wormThreads(float angle, float z, float spin, float count, float halfWidth, float spacing)
{
    float phase = angle / (2.0 * PI) * count + (z * 0.9 + Time * 0.4) * spin
                + 0.25 * sin(z * 0.37 + angle * 2.0 + Time * 0.5 + spin);
    float miss = abs(frac(phase) - 0.5) * spacing / halfWidth;
    float core = saturate(1.0 - miss);
    float sharp = core * core + exp(-miss * 0.3) * 0.06;
    float even = halfWidth / spacing;
    return float2(lerp(even, sharp, saturate(spacing / halfWidth * 0.25 - 0.5)), floor(phase));
}

float3 Render(float2 uv, float2 p)
{
    float zCam = Time * 1.1 + (BeatCount - Beat) * 0.25;
    float3 eye = float3(wormPath(zCam), zCam);
    float3 dir = cameraRay(eye, float3(wormPath(zCam + 2.5), zCam + 2.5), p, 0.8 - Beat * 0.08, Time * 0.07);

    float flow = Time * 0.05;
    // A blinding light hangs in the heart of each throat; the next two ahead light the veils.
    float zNear = ThroatSpacing * ceil(zCam / ThroatSpacing);
    float3 lampNear = float3(wormPath(zNear), zNear);
    float3 lampFar = float3(wormPath(zNear + ThroatSpacing), zNear + ThroatSpacing);
    float3 colour = 0.0;
    float transmit = 1.0;
    float travelled = 0.0;
    float layer = 0.0;
    int maxSteps = qualitySteps(64);
    float pixelAngle = 1.2 / (Resolution.y * 0.8);
    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float3 q = eye + dir * travelled;
        float2 rel = q.xy - wormPath(q.z);
        float radius = wormRadius(q.z);
        float shell = radius * pow(ShellGrowth, layer) * (1.0 + Bass * 0.06);
        float gap = shell - length(rel);
        if (gap < 0.003 + travelled * 0.002)
        {
            // Crossing a veil: add its glowing threads and a wash of aurora light, partly hiding
            // what lies beyond.
            float angle = atan2(rel.y, rel.x);
            float spin = frac(layer * 0.5) < 0.25 ? 1.0 : -1.0;
            float count = 7.0 + layer * 3.0;
            float2 threads = wormThreads(angle, q.z, spin, count, 0.008 + travelled * pixelAngle, 2.0 * PI * shell / count);
            float wash = 0.5 + 0.5 * sin(2.0 * angle + 0.6 * q.z * spin + Time * 0.7 + layer * 2.0
                                         + 1.5 * sin(q.z * 0.21 + angle + Time * 0.3));
            wash = wash * wash * wash;
            // Beads of light race along each thread towards us.
            float beads = pow(0.5 + 0.5 * sin(q.z * 2.2 + Time * 7.0 + threads.y * 2.4), 24.0) * threads.x;

            float band = abs(angle) / PI;
            float energy = spectrum(band);
            float throat = wormPinch(q.z);
            float hue = layer * 0.13 + band * 0.35 + q.z * 0.02 - flow;
            float3 tint = wormColour(hue);
            // After each beat a ring of light shoots ahead down the wormhole.
            float shock = glow(q.z - zCam - (1.0 - Beat) * 14.0, 1.5) * Beat;
            float fade = exp(-travelled * 0.06);
            float3 toNear = q - lampNear;
            float3 toFar = q - lampFar;
            float throatLit = 1.0 + 4.0 / (1.0 + dot(toNear, toNear) * 0.3) + 4.0 / (1.0 + dot(toFar, toFar) * 0.3);
            float3 light = tint * threads.x * (0.15 + energy * 1.0) * (0.6 + Bass * 0.6) * throatLit
                         + wormColour(hue + 0.3) * wash * 0.045 * sqrt(throatLit) * (0.6 + Bass * 0.8)
                         + lerp(tint, 1.0, 0.6) * beads * (0.5 + Treble * 2.0)
                         + lerp(tint, 1.0, 0.5) * shock * (threads.x + wash * 0.3);
            colour += transmit * light * fade;
            transmit *= 1.0 - threads.x * 0.5 - wash * 0.12;
            layer += 1.0;
            if (layer > 4.5 || transmit < 0.03)
            {
                break;
            }
        }
        travelled += max(gap * 0.8, 0.004);   // the veils slope as they pinch, so step with care
    }
    // The throat lights themselves, seen through the veils.
    float3 throatGlow = 0.0;
    [unroll]
    for (int k = 0; k < 2; k++)
    {
        float3 lamp = k == 0 ? lampNear : lampFar;
        float along = dot(lamp - eye, dir);
        float3 miss = lamp - eye - dir * max(along, 0.0);
        float miss2 = dot(miss, miss);
        float3 warm = lerp(float3(1.0, 0.8, 0.6), wormColour(lamp.z * 0.02 - flow + 0.5), 0.3);
        throatGlow += warm * step(0.0, along) * (0.03 / (miss2 + 0.03) + 0.004 / (miss2 + 0.004))
                    * exp(-along * 0.06) * (0.6 + Bass * 0.6 + Beat * 0.4);
    }
    colour += throatGlow;
    // Rays that are still between veils when we stop marching see the glow of the far throats.
    colour += transmit * wormColour(layer * 0.13 - flow) * (layer < 4.5 ? 0.12 : 0.03) * (1.0 - exp(-travelled * 0.08));

    // The far universe glimpsed between the veils: a swirl of nebula and stars.
    float3 sky = wormColour(flow + dir.x * 0.2) * (0.01 + 0.03 * noise(dir.xy * 4.0 + Time * 0.05));
    colour += transmit * sky;
    return colour + previous(uv) * 0.08;
}
