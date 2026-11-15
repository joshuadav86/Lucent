// Name: Distance Bands
// One rounded shape and a pebble that circles it, drawn the way a map draws a hill: in bands of equal distance.
// Warm bands roll away from the outline and cool ones sink into it, one band a beat. Each corner of the shape is
// rounded by its own part of the spectrum, from the bass to the treble, and the whole thing swells with the music.
// Family: Minimal
// Tags: Minimal, Geometric, Colourful
// Added: 2026-11-15 20:41

// Distance to a box whose four corners are each rounded by their own amount. Negative inside.
float dbBox(float2 p, float2 size, float4 corners)
{
    float2 side = p.x > 0.0 ? corners.xy : corners.zw;
    float r = p.y > 0.0 ? side.y : side.x;
    float2 q = abs(p) - size + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

// How loud the music is around one place in the spectrum.
float dbBand(float at)
{
    return (spectrum(at - 0.03) + spectrum(at) + spectrum(at + 0.03)) / 3.0;
}

float dbShape(float2 p)
{
    float2 size = float2(0.66, 0.44) * (1.0 + 0.06 * Level + 0.04 * BeatSwell + 0.08 * DropPulse);
    float4 corners = 0.05 + 0.36 * saturate(float4(dbBand(0.06), dbBand(0.2), dbBand(0.38), dbBand(0.6)) * 1.5);
    float box = dbBox(rotate(p, 0.22 * sin(Time * 0.13)), size, min(corners, size.y));

    // The pebble circles the shape and melts into it when it comes close.
    float around = Time * 0.31 + BeatGlide * 0.2;
    float2 at = float2(cos(around) * 1.02, sin(around) * 0.62);
    float pebble = length(p - at) - (0.13 + 0.05 * Mid);
    float blend = saturate(0.5 + 0.5 * (pebble - box) / 0.22);
    return lerp(pebble, box, blend) - 0.22 * blend * (1.0 - blend);
}

float3 Render(float2 uv, float2 p)
{
    float d = dbShape(p);
    float flow = BeatGlide * PI + Time * 0.5;
    float edge = pixelSize();

    // Bands of equal distance: they travel away from the outline outside and into it inside.
    float wave = cos(abs(d) * 30.0 - flow);
    float band = smoothstep(-0.35, 0.35, wave);
    float3 outside = lerp(float3(1.5, 0.5, 0.06), float3(1.55, 1.15, 0.25), band);
    float3 inside = lerp(float3(0.08, 0.36, 1.25), float3(0.72, 1.08, 1.45), band);

    // The shape stands a little proud of the page: a shadow just outside it, a soft dip just inside.
    outside *= 0.5 + 0.5 * smoothstep(0.0, 0.16, d);
    inside *= 0.72 + 0.28 * smoothstep(0.0, 0.1, -d);
    float3 colour = lerp(inside, outside, smoothstep(-edge, edge, d));

    // The outline itself, in white.
    float width = 0.011 + 0.004 * BeatSwell;
    colour = lerp(colour, float3(1.15, 1.15, 1.1), smoothstep(width + edge, width - edge, abs(d)));

    // Far from the shape the bands fade into the page.
    colour *= 1.0 - 0.35 * smoothstep(0.6, 2.2, d);
    return colour * (0.9 + 0.12 * Treble) * (1.0 - dot(uv - 0.5, uv - 0.5) * 0.5);
}
