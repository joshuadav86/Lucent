// Name: Hyperspace Rings
// Diving through an endless corridor of spinning hexagonal gates, each tuned to part of the spectrum.

static const float GateSpacing = 1.2;

float gate(float3 p, out float index)
{
    const float spacing = GateSpacing;
    index = floor(p.z / spacing + 0.5);
    float3 q = p;
    q.z -= index * spacing;
    float spin = index * 0.35 + Time * (0.25 + 0.1 * sin(index));
    q.xy = rotate(q.xy, spin);

    // A hexagonal frame with kaleidoscopic struts reaching inward.
    float frame = abs(polygon(q.xy, 6.0, 1.5)) - 0.05;
    float2 k = kaleidoscope(q.xy, 6.0);
    float struts = max(abs(k.y) - 0.025, abs(k.x - 1.25) - 0.22);
    float outline = min(frame, struts);
    float2 d = float2(outline, abs(q.z) - 0.04);
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}

float3 Render(float2 uv, float2 p)
{
    float z = Time * 2.4;
    float3 eye = float3(sin(Time * 0.4) * 0.25, cos(Time * 0.31) * 0.25, z);
    float3 direction = cameraRay(eye, float3(0.0, 0.0, z + 4.0), p, 1.1 - Beat * 0.15, Time * 0.1);

    const float farPlane = 30.0;
    float travelled = 0.0;
    float3 colour = 0.0;
    [loop]
    for (int i = 0; i < 100; i++)
    {
        float3 position = eye + direction * travelled;
        float index;
        float d = gate(position, index);

        float band = frac(index * 0.137);
        float energy = 0.3 + spectrum(band) * 3.0;
        float3 tint = lerp(neon(index * 0.06 + Time * 0.03), palette(index * 0.09), 0.35);
        colour += tint * energy * 0.0025 / (0.002 + abs(d)) * exp(-travelled * 0.09);

        if (abs(d) < 0.0008 || travelled > farPlane)
        {
            break;
        }
        // Never step further than the gap to the next gate.
        float gap = GateSpacing * 0.5 - abs(position.z - index * GateSpacing) + 0.05;
        travelled += max(min(abs(d), gap), 0.004);
    }

    colour *= 0.6;
    // Stars streaking past in the gaps.
    float2 streak = float2(atan2(p.y, p.x) * 40.0, 0.0);
    float star = step(0.985, hash(floor(streak))) * glow(frac(length(p) * 2.0 - Time * 3.0) - 0.5, 8.0);
    colour += star * 0.25 * (0.4 + Treble);
    return colour + previous(uv) * 0.18;
}
