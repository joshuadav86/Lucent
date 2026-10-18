// Name: Waveform Tunnel
// Flying down a twisting tunnel whose every ring is a moment of the waveform, rushing past in flowing colour.
// Family: Music-shaped
// Tags: Intense, Neon
// Added: 2026-10-16 23:54

// The waveform's recent history is kept in the top two pixel rows of the feedback buffer
// (224 pixels each, three rings per pixel in r, g and b), stored far too dim to see.
static const float TunnelSamples = 32.0;    // samples around half a ring (the other half is mirrored)
static const float TunnelGroupsPerLine = 7.0;
static const float TunnelRings = 42.0;      // 14 pixels groups x 3 channels
static const float TunnelRate = 12.0;       // rings per second
static const float TunnelStore = 0.004;
static const float TunnelSpacing = 0.24;    // world distance between rings
static const float TunnelRadius = 1.0;

// Smoothed waveform: keeps the body of the wave, drops the hiss.
float tunnelWave(float x)
{
    return waveform(x) * 0.3 + (waveform(x - 0.005) + waveform(x + 0.005)) * 0.22
         + (waveform(x - 0.011) + waveform(x + 0.011)) * 0.13;
}

// Where the waveform starts rising through zero, so successive rings line up into a steady shape.
float tunnelTrigger()
{
    float found = 0.0;
    float before = tunnelWave(0.02);
    [loop]
    for (int i = 1; i < 40; i++)
    {
        float x = 0.02 + i * 0.012;
        float now = tunnelWave(x);
        if (before < 0.0 && now >= 0.0)
        {
            found = x;
            break;
        }
        before = now;
    }
    return found;
}

// Values are stored as 0.3..0.9 of TunnelStore; anything else is not ours (e.g. right after a transition).
float3 tunnelEncode(float3 v)
{
    return (0.6 + 0.3 * clamp(v, -1.0, 1.0)) * TunnelStore;
}

bool tunnelValid(float3 stored)
{
    float3 t = stored / TunnelStore;
    return all(t > 0.27) && all(t < 0.93);
}

// The pixel holding ring row `ring` (three rings per pixel), at sample position `sample` (may be fractional).
float2 tunnelCell(float ring, float slot)
{
    float group = floor(ring / 3.0);
    float stripRow = floor(group / TunnelGroupsPerLine);
    float column = group - stripRow * TunnelGroupsPerLine;
    return float2(column * TunnelSamples + clamp(slot, 0.0, TunnelSamples - 1.0) + 0.5, stripRow + 0.5) / Resolution;
}

float tunnelChannel(float3 stored, float ring)
{
    float c = ring - floor(ring / 3.0) * 3.0;
    float3 pick = float3(step(c, 0.5), step(0.5, c) * step(c, 1.5), step(1.5, c));
    return dot(stored, pick);
}

// Stored ring value (-1..1) for a whole ring row at a sample position.
float tunnelRingValue(float ring, float slot)
{
    float3 stored = previous(tunnelCell(ring, slot));
    return tunnelValid(stored) ? (tunnelChannel(stored, ring) / TunnelStore - 0.6) / 0.3 : 0.0;
}

// Ring shape at a fractional ring row and angle position u (0..1).
float tunnelShape(float ring, float u)
{
    float r = clamp(ring, 0.0, TunnelRings - 1.001);
    float r0 = floor(r);
    float slot = u * TunnelSamples - 0.5;
    return lerp(tunnelRingValue(r0, slot), tunnelRingValue(r0 + 1.0, slot), r - r0);
}

// The tunnel's centre line wanders, so the flight curves.
float2 tunnelPath(float z)
{
    return float2(sin(z * 0.23) * 1.1 + sin(z * 0.11) * 0.6, cos(z * 0.17) * 0.8);
}

float3 Render(float2 uv, float2 p)
{
    float scroll = frac(Time * TunnelRate);

    // Memory pixels: the newest ring takes the live waveform; older rings move along when a new one is due.
    float2 pixel = floor(uv * Resolution);
    if (pixel.y < 2.0 && pixel.x < TunnelSamples * TunnelGroupsPerLine)
    {
        float shift = floor(Time * TunnelRate) - floor((Time - DeltaTime) * TunnelRate);
        float column = floor(pixel.x / TunnelSamples);
        float slot = pixel.x - column * TunnelSamples;
        float group = pixel.y * TunnelGroupsPerLine + column;
        float3 values = 0.0;
        float liveStart = group < 0.5 ? tunnelTrigger() : 0.0;
        [unroll]
        for (int c = 0; c < 3; c++)
        {
            float ring = group * 3.0 + c;
            float source = ring - shift;
            float value = source < 0.0 || ring < 0.5
                ? tunnelWave(liveStart + (slot + 0.5) / TunnelSamples * 0.42)
                : tunnelRingValue(source, slot);
            values[c] = value;
        }
        return tunnelEncode(values);
    }

    // Flying forward: rings are born far ahead (newest) and rush towards the camera.
    float speed = TunnelSpacing * TunnelRate;
    float camZ = Time * speed;
    float2 camOffset = tunnelPath(camZ);
    const float focal = 1.1;
    const float born = 9.5;      // how far ahead the newest ring appears

    // Find the depth of the wall seen by this pixel by fixed-point iteration, starting from a
    // straight tunnel aimed at the far end.
    float2 farCentre = (tunnelPath(camZ + born) - camOffset) * focal / born;
    float depth = clamp(focal * TunnelRadius / max(length(p - farCentre * 0.5), 1e-3), 0.15, 40.0);
    float shape = 0.0;
    [unroll]
    for (int k = 0; k < 5; k++)
    {
        float2 centre = (tunnelPath(camZ + depth) - camOffset) * focal / depth;
        float2 rel = p - centre;
        float twist = camZ * 0.3 + depth * 0.2;
        float angle = atan2(rel.y, rel.x) + twist;
        float u = abs(frac(angle / (2.0 * PI) + 0.5) * 2.0 - 1.0);
        float ring = (born - depth) / TunnelSpacing - scroll;
        shape = tunnelShape(ring, u);
        float wallRadius = TunnelRadius * (1.0 + shape * (0.2 + Bass * 0.06));
        depth = lerp(depth, clamp(focal * wallRadius / max(length(rel), 1e-3), 0.15, 40.0), 0.85);
    }

    float px = 2.0 / Resolution.y;
    float ringCoord = (born - depth) / TunnelSpacing - scroll;
    float ringId = floor(ringCoord + 0.5);
    float ringDistance = abs(ringCoord - ringId) * TunnelSpacing;
    // A pixel covers more depth the further away the wall is; far rings fade before they can shimmer.
    float footprint = px * depth * depth / (focal * TunnelRadius);
    float ringLine = saturate(1.0 - (ringDistance - 0.012) / (footprint * 1.3 + 0.002));
    float ringGlow = exp(-ringDistance / (0.03 + footprint));
    float clarity = saturate(TunnelSpacing / footprint / 3.0 - 0.5);
    ringLine *= clarity;
    ringGlow *= clarity;

    // Colour flows along the tunnel; each ring keeps its own hue as it travels.
    float hueT = ringId * 0.018 + Time * 0.04 + shape * 0.12;
    float3 hue = 0.5 + 0.5 * cos(2.0 * PI * (hueT + float3(0.0, 0.3, 0.6)));
    hue = hue * hue * 1.2;

    float fog = exp(-depth * 0.3);
    float fresh = smoothstep(born, born - 2.5, depth);     // rings fade in as they are born
    float3 wall = hue * (0.05 + 0.08 * (shape * 0.5 + 0.5)) * fog * fresh;
    float energy = 0.8 + Level * 1.2 + abs(shape) * 0.8;
    float3 colour = wall + (lerp(hue, 1.0, 0.35) * ringLine + hue * ringGlow * 0.35) * energy * fog * fresh;

    // A soft glow where the tunnel vanishes into the distance, in the colour of the newest ring.
    float3 farHue = 0.5 + 0.5 * cos(2.0 * PI * (-scroll * 0.018 + Time * 0.04 + float3(0.0, 0.3, 0.6)));
    colour += farHue * farHue * 0.3 * exp(-length(p - farCentre) * 4.0) * (0.4 + Beat * 0.4);

    // Fade into the two memory rows along the top edge, so they read as a soft edge, not a hard line.
    colour *= pixel.x < TunnelSamples * TunnelGroupsPerLine + 6.0 ? smoothstep(2.0, 8.0, pixel.y) : 1.0;
    return colour;
}
