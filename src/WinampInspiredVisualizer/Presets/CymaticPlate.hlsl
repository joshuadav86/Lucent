// Name: Cymatic Plate
// Glowing sand gathers on the still lines of a vibrating plate; the song's spectrum picks the modes, so the figures morph with the music.
// Family: Music-shaped
// Tags: Colourful, Neon, Geometric

// One Chladni mode of a square plate: sand collects where this is zero.
float plateMode(float2 q, float n, float m)
{
    return cos(n * PI * q.x) * cos(m * PI * q.y) - cos(m * PI * q.x) * cos(n * PI * q.y);
}

float plateBand(float x)
{
    float s = spectrum(x) * 0.5 + (spectrum(x - 0.015) + spectrum(x + 0.015)) * 0.25;
    return s * s;
}

// The plate's vibration: modes weighted by bands of the spectrum, low modes for bass, high for treble.
float plateField(float2 q, float4 lowBands, float4 highBands)
{
    float f = 0.0;
    f += lowBands.x * plateMode(q, 1.0, 3.0);
    f += lowBands.y * plateMode(q, 2.0, 5.0);
    f += lowBands.z * plateMode(q, 1.0, 6.0);
    f += lowBands.w * plateMode(q, 3.0, 7.0);
    f += highBands.x * plateMode(q, 2.0, 9.0);
    f += highBands.y * plateMode(q, 5.0, 8.0);
    f += highBands.z * plateMode(q, 4.0, 11.0);
    f += highBands.w * plateMode(q, 7.0, 12.0);
    return f;
}

float plateHash(float2 cell)
{
    float3 p3 = frac(float3(cell.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float3 Render(float2 uv, float2 p)
{
    // Slowly turning, breathing view of the plate.
    float zoom = 0.85 + 0.1 * sin(Time * 0.05);
    float2 q = rotate(p, Time * 0.03) * zoom;

    // Band weights drift slowly between neighbouring modes, so the figure keeps evolving.
    float4 lowBands = float4(plateBand(0.03), plateBand(0.12), plateBand(0.22), plateBand(0.33));
    float4 highBands = float4(plateBand(0.45), plateBand(0.57), plateBand(0.7), plateBand(0.83));
    lowBands *= 1.0 + 0.5 * sin(Time * float4(0.11, 0.07, 0.09, 0.05) + float4(0.0, 1.0, 2.0, 3.0));
    highBands *= 1.0 + 0.5 * sin(Time * float4(0.06, 0.1, 0.08, 0.12) + float4(4.0, 5.0, 6.0, 7.0));
    // A quiet floor keeps a figure on the plate even in silence.
    lowBands += 0.03 * (1.2 + sin(Time * float4(0.13, 0.17, 0.19, 0.23)));
    float total = dot(lowBands + highBands, float4(1.0, 1.0, 1.0, 1.0)) + 1e-3;
    float f = plateField(q, lowBands, highBands) / total;

    // Distance to the nodal line in pixels.
    float width = max(fwidth(f), 1e-5);
    float pixels = abs(f) / width;

    // Sand: grains settle on the nodal lines; away from them they are shaken loose.
    float2 grainCell = floor(uv * Resolution / 1.5);
    float grain = plateHash(grainCell + floor(Time * 12.0) * step(2.5, pixels) * 7.0);
    float settled = exp(-abs(f) * (9.0 - Beat * 4.0));     // each beat shakes the sand a little looser
    float sand = step(1.0 - settled * 0.85, grain) * settled;

    float nodal = saturate(1.6 - pixels * 0.6);
    float halo = exp(-abs(f) * 6.0);

    // Colour flows outward from the middle of the plate.
    float radius = length(q);
    float3 hue = 0.5 + 0.5 * cos(2.0 * PI * (radius * 0.35 - Time * 0.05 + float3(0.0, 0.33, 0.67)));
    hue = lerp(hue * hue, hue, 0.4);

    float energy = 0.6 + Level * 0.9 + Beat * 0.3;
    float3 colour = lerp(hue, 1.0, 0.35) * nodal * 1.4 * energy;
    colour += hue * sand * 0.9 * energy;
    colour += hue * halo * 0.08;
    // The vibrating plate itself: a faint sheen where it moves most.
    colour += hue.bgr * 0.02 * smoothstep(0.2, 1.0, abs(f) * 2.0);
    return colour;
}
